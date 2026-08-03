using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Drafts;

public sealed class DraftRecoveryServiceTests
{
    private static readonly Guid DraftId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SkinId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    [Fact]
    public async Task NotifyMeaningfulChange_WritesOnlyAfterExactOneSecond()
    {
        var delay = new ManualDelay();
        var files = new RecordingFileOperations();
        await using var service = CreateService(files, delay);

        service.NotifyMeaningfulChange(Draft(1));
        await delay.WaitForRequestCountAsync(1);
        delay.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(files.SavedRevisions);

        delay.Advance(TimeSpan.FromMilliseconds(1));
        await files.WaitForSavedAsync(1);

        Assert.Equal([TimeSpan.FromSeconds(1)], delay.RequestedDurations);
        Assert.Equal([1L], files.SavedRevisions);
    }

    [Fact]
    public async Task ThreeChangesInsideDebounce_WriteOnlyLatestHigherRevision()
    {
        var delay = new ManualDelay();
        var files = new RecordingFileOperations();
        await using var service = CreateService(files, delay);

        service.NotifyMeaningfulChange(Draft(1));
        await delay.WaitForRequestCountAsync(1);
        delay.Advance(TimeSpan.FromMilliseconds(300));
        service.NotifyMeaningfulChange(Draft(2));
        await delay.WaitForRequestCountAsync(2);
        delay.Advance(TimeSpan.FromMilliseconds(300));
        service.NotifyMeaningfulChange(Draft(3));
        await delay.WaitForRequestCountAsync(3);
        delay.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(files.SavedRevisions);

        delay.Advance(TimeSpan.FromMilliseconds(1));
        await files.WaitForSavedAsync(3);

        Assert.Equal([3L], files.SavedRevisions);
    }

    [Fact]
    public async Task ChangeDuringActiveSave_SerializesSecondLatestWrite()
    {
        var delay = new ManualDelay();
        var firstSaveGate = NewSignal();
        var files = new RecordingFileOperations
        {
            SaveBehavior = async (draft, _) =>
            {
                if (draft.Revision == 1)
                {
                    await firstSaveGate.Task.ConfigureAwait(false);
                }
            }
        };
        await using var service = CreateService(files, delay);

        service.NotifyMeaningfulChange(Draft(1));
        await delay.WaitForRequestCountAsync(1);
        delay.Advance(TimeSpan.FromSeconds(1));
        await files.WaitForStartedAsync(1);
        service.NotifyMeaningfulChange(Draft(2));
        await delay.WaitForRequestCountAsync(2);
        delay.Advance(TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(2, files.StartedRevisions);

        firstSaveGate.SetResult();
        await files.WaitForSavedAsync(2);

        Assert.Equal([1L, 2L], files.SavedRevisions);
        Assert.Equal(1, files.MaximumConcurrentSaves);
    }

    [Fact]
    public async Task FlushAsync_CancelsTimerAndImmediatelyWritesLatest()
    {
        var delay = new ManualDelay();
        var files = new RecordingFileOperations();
        await using var service = CreateService(files, delay);
        service.NotifyMeaningfulChange(Draft(4));
        service.NotifyMeaningfulChange(Draft(5));

        await service.FlushAsync();
        delay.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal([5L], files.SavedRevisions);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForEverySupersededDebounceWorker()
    {
        var delay = new ManualDelay(honorCancellation: false);
        var oldestWorkerStateAtSave = 0;
        var files = new RecordingFileOperations
        {
            SaveBehavior = (_, _) =>
            {
                Volatile.Write(
                    ref oldestWorkerStateAtSave,
                    delay.IsContinuationCompleted(0) ? 1 : -1);
                return Task.CompletedTask;
            }
        };
        var service = CreateService(files, delay);
        service.NotifyMeaningfulChange(Draft(1));
        await delay.WaitForRequestCountAsync(1);
        service.NotifyMeaningfulChange(Draft(2));
        await delay.WaitForRequestCountAsync(2);

        var dispose = service.DisposeAsync().AsTask();
        delay.CompleteRequest(1);
        await delay.WaitForContinuationAsync(1);

        Assert.False(dispose.IsCompleted);
        Assert.Empty(files.SavedRevisions);

        delay.CompleteRequest(0);
        await dispose;

        Assert.Equal([2L], files.SavedRevisions);
        Assert.Equal(1, Volatile.Read(ref oldestWorkerStateAtSave));
    }

    [Fact]
    public async Task DisposeAsync_WaitsForConcurrentAdmittedFlushAndSavesLatestOnce()
    {
        var delay = new ManualDelay();
        var saveGate = NewSignal();
        var files = new RecordingFileOperations
        {
            SaveBehavior = async (_, _) =>
                await saveGate.Task.ConfigureAwait(false)
        };
        var service = CreateService(files, delay);
        service.NotifyMeaningfulChange(Draft(6));

        var flush = service.FlushAsync();
        await files.WaitForStartedAsync(6);
        var dispose = service.DisposeAsync().AsTask();

        Assert.False(dispose.IsCompleted);
        saveGate.SetResult();
        await Task.WhenAll(flush, dispose);

        Assert.Equal([6L], files.SavedRevisions);
        Assert.Equal(1, files.MaximumConcurrentSaves);
    }

    [Fact]
    public async Task DisposeAsync_FlushesLatestOnceAndIsIdempotent()
    {
        var delay = new ManualDelay();
        var files = new RecordingFileOperations();
        var service = CreateService(files, delay);
        service.NotifyMeaningfulChange(Draft(6));

        await service.DisposeAsync();
        await service.DisposeAsync();
        delay.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal([6L], files.SavedRevisions);
        Assert.Throws<ObjectDisposedException>(
            () => service.NotifyMeaningfulChange(Draft(7)));
        Assert.Equal([6L], files.SavedRevisions);
    }

    [Fact]
    public async Task SaveException_RaisesOneSafeFailureAndServiceRemainsUsable()
    {
        var delay = new ManualDelay();
        var attempts = 0;
        var files = new RecordingFileOperations
        {
            SaveBehavior = (_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new IOException("C:\\private\\raw-json"))
                    : Task.CompletedTask;
            }
        };
        await using var service = CreateService(files, delay);
        var failures = new List<DraftPersistenceFailure>();
        var failureRaised = NewSignal();
        service.SaveFailed += (_, failure) =>
        {
            failures.Add(failure);
            failureRaised.TrySetResult();
        };

        service.NotifyMeaningfulChange(Draft(7));
        await service.FlushAsync();
        service.NotifyMeaningfulChange(Draft(8));
        await service.FlushAsync();
        await failureRaised.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var failure = Assert.Single(failures);
        Assert.Equal(DraftId, failure.DraftId);
        Assert.Equal("recovery.json", failure.LeafName);
        Assert.Equal("draft.save-failed", failure.ErrorCode);
        Assert.DoesNotContain("private", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([8L], files.SavedRevisions);
    }

    [Fact]
    public async Task SaveFailed_HandlerCanSynchronouslyReenterFlush()
    {
        var attempts = 0;
        var files = new RecordingFileOperations
        {
            SaveBehavior = (_, _) => ++attempts == 1
                ? Task.FromException(new IOException("first write fails"))
                : Task.CompletedTask
        };
        await using var service = CreateService(files, new ManualDelay());
        var eventRaised = NewSignal();
        var reentrantCompleted = false;
        service.SaveFailed += (_, _) =>
        {
            var reentrant = service.FlushAsync();
            reentrantCompleted = reentrant.IsCompletedSuccessfully;
            eventRaised.TrySetResult();
        };

        service.NotifyMeaningfulChange(Draft(9));
        await service.FlushAsync();
        await eventRaised.Task;

        Assert.True(reentrantCompleted);
        Assert.Equal([9L], files.SavedRevisions);
    }

    [Fact]
    public async Task SaveFailed_HandlerCanSynchronouslyReenterDispose()
    {
        var attempts = 0;
        var files = new RecordingFileOperations
        {
            SaveBehavior = (_, _) => ++attempts == 1
                ? Task.FromException(new IOException("first write fails"))
                : Task.CompletedTask
        };
        var service = CreateService(files, new ManualDelay());
        var eventRaised = NewSignal();
        var reentrantCompleted = false;
        service.SaveFailed += (_, _) =>
        {
            var reentrant = service.DisposeAsync();
            reentrantCompleted = reentrant.IsCompletedSuccessfully;
            eventRaised.TrySetResult();
        };

        service.NotifyMeaningfulChange(Draft(10));
        await service.FlushAsync();
        await eventRaised.Task;
        await service.DisposeAsync();

        Assert.True(reentrantCompleted);
        Assert.Equal([10L], files.SavedRevisions);
    }

    [Fact]
    public async Task Discard_WhenNewerInMemoryRevisionIsPending_PreservesDebounceAndDiskEvidence()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud-Task14-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new SkinStoragePaths(root);
        var store = new DraftStore(paths);
        var delay = new ManualDelay();
        var service = new DraftRecoveryService(store, delay.DelayAsync);
        try
        {
            await store.SaveRecoveryAsync(Draft(1));
            var recoveryPath = new DraftProjectPaths(
                paths.DraftsRoot,
                DraftId).RecoveryPath;
            var evidence = await File.ReadAllBytesAsync(recoveryPath);
            service.NotifyMeaningfulChange(Draft(2));
            await delay.WaitForRequestCountAsync(1);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DiscardAsync(DraftId, maximumRevision: 1));

            Assert.False(delay.IsContinuationCompleted(0));
            Assert.Equal(evidence, await File.ReadAllBytesAsync(recoveryPath));
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static DraftRecoveryService CreateService(
        RecordingFileOperations files,
        ManualDelay delay) =>
        new(
            new DraftStore(
                new SkinStoragePaths(Path.Combine(
                    Path.GetTempPath(),
                    "CodexQuotaHud-Task12-recording-" + Guid.NewGuid().ToString("N"))),
                files),
            delay.DelayAsync);

    private static SkinDraftDocument Draft(long revision) =>
        SkinDraftFactory.CreateNew(
            DraftId,
            SkinId,
            CreatedAt,
            SemanticVersion.Parse("1.1.1")) with
        {
            Revision = revision,
            UpdatedAtUtc = CreatedAt.AddMinutes(revision)
        };

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingFileOperations : IDraftFileOperations
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, byte[]> _files =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, TaskCompletionSource> _started = [];
        private readonly Dictionary<long, TaskCompletionSource> _saved = [];
        private readonly List<long> _startedRevisions = [];
        private readonly List<long> _savedRevisions = [];
        private int _activeSaves;
        private int _maximumConcurrentSaves;

        public Func<SkinDraftDocument, CancellationToken, Task>? SaveBehavior { get; init; }

        public IReadOnlyList<long> StartedRevisions
        {
            get
            {
                lock (_sync)
                {
                    return _startedRevisions.ToArray();
                }
            }
        }

        public IReadOnlyList<long> SavedRevisions
        {
            get
            {
                lock (_sync)
                {
                    return _savedRevisions.ToArray();
                }
            }
        }

        public int MaximumConcurrentSaves
        {
            get
            {
                lock (_sync)
                {
                    return _maximumConcurrentSaves;
                }
            }
        }

        public void CreateDirectory(string path)
        {
        }

        public bool DirectoryExists(string path) => false;

        public bool FileExists(string path)
        {
            lock (_sync)
            {
                return _files.ContainsKey(path);
            }
        }

        public FileAttributes GetAttributes(string path) => FileAttributes.Normal;

        public IEnumerable<string> EnumerateDirectories(string path) => [];

        public byte[] ReadAllBytes(string path)
        {
            lock (_sync)
            {
                return _files[path].ToArray();
            }
        }

        public async Task WriteAndFlushAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
        {
            var parsed = DraftJsonCodec.Parse(bytes.Span);
            var draft = Assert.IsType<SkinDraftDocument>(parsed.Value);
            TaskCompletionSource started;
            lock (_sync)
            {
                _startedRevisions.Add(draft.Revision);
                started = SignalFor(_started, draft.Revision);
                _activeSaves++;
                _maximumConcurrentSaves = Math.Max(
                    _maximumConcurrentSaves,
                    _activeSaves);
            }

            started.TrySetResult();
            try
            {
                if (SaveBehavior is not null)
                {
                    await SaveBehavior(draft, cancellationToken).ConfigureAwait(false);
                }

                lock (_sync)
                {
                    _files[path] = bytes.ToArray();
                }
            }
            finally
            {
                lock (_sync)
                {
                    _activeSaves--;
                }
            }
        }

        public void ReplaceFile(string sourcePath, string destinationPath)
        {
            TaskCompletionSource saved;
            long revision;
            lock (_sync)
            {
                var bytes = _files[sourcePath];
                var parsed = DraftJsonCodec.Parse(bytes);
                var draft = Assert.IsType<SkinDraftDocument>(parsed.Value);
                revision = draft.Revision;
                _files[destinationPath] = bytes;
                _files.Remove(sourcePath);
                _savedRevisions.Add(revision);
                saved = SignalFor(_saved, revision);
            }

            saved.TrySetResult();
        }

        public void DeleteFile(string path)
        {
            lock (_sync)
            {
                _files.Remove(path);
            }
        }

        public Task WaitForStartedAsync(long revision)
        {
            lock (_sync)
            {
                return _startedRevisions.Contains(revision)
                    ? Task.CompletedTask
                    : SignalFor(_started, revision).Task;
            }
        }

        public Task WaitForSavedAsync(long revision)
        {
            lock (_sync)
            {
                return _savedRevisions.Contains(revision)
                    ? Task.CompletedTask
                    : SignalFor(_saved, revision).Task;
            }
        }

        private static TaskCompletionSource SignalFor(
            IDictionary<long, TaskCompletionSource> signals,
            long revision)
        {
            if (!signals.TryGetValue(revision, out var signal))
            {
                signal = NewSignal();
                signals.Add(revision, signal);
            }

            return signal;
        }
    }

    private sealed class ManualDelay
    {
        private readonly object _sync = new();
        private readonly List<Entry> _entries = [];
        private readonly bool _honorCancellation;
        private TaskCompletionSource _requestsChanged = NewSignal();
        private TimeSpan _elapsed;

        public ManualDelay(bool honorCancellation = true) =>
            _honorCancellation = honorCancellation;

        public IReadOnlyList<TimeSpan> RequestedDurations
        {
            get
            {
                lock (_sync)
                {
                    return _entries.Select(entry => entry.Duration).ToArray();
                }
            }
        }

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var completion = NewSignal();
                var continued = NewSignal();
                var registration = _honorCancellation
                    ? cancellationToken.Register(
                        () => completion.TrySetCanceled(cancellationToken))
                    : default;
                _entries.Add(new Entry(
                    duration,
                    _elapsed + duration,
                    completion,
                    continued,
                    registration));
                var changed = _requestsChanged;
                _requestsChanged = NewSignal();
                changed.TrySetResult();
                return ObserveContinuationAsync(completion.Task, continued);
            }
        }

        public void Advance(TimeSpan duration)
        {
            Entry[] due;
            lock (_sync)
            {
                _elapsed += duration;
                due = _entries
                    .Where(entry => entry.Due <= _elapsed)
                    .ToArray();
            }

            foreach (var entry in due)
            {
                entry.Registration.Dispose();
                entry.Completion.TrySetResult();
            }
        }

        public void CompleteRequest(int index)
        {
            Entry entry;
            lock (_sync)
            {
                entry = _entries[index];
            }

            entry.Registration.Dispose();
            entry.Completion.TrySetResult();
        }

        public Task WaitForContinuationAsync(int index)
        {
            lock (_sync)
            {
                return _entries[index].Continued.Task;
            }
        }

        public bool IsContinuationCompleted(int index)
        {
            lock (_sync)
            {
                return _entries[index].Continued.Task.IsCompleted;
            }
        }

        public async Task WaitForRequestCountAsync(int count)
        {
            while (true)
            {
                Task changed;
                lock (_sync)
                {
                    if (_entries.Count >= count)
                    {
                        return;
                    }

                    changed = _requestsChanged.Task;
                }

                await changed.ConfigureAwait(false);
            }
        }

        private static async Task ObserveContinuationAsync(
            Task completion,
            TaskCompletionSource continued)
        {
            try
            {
                await completion.ConfigureAwait(false);
            }
            finally
            {
                continued.TrySetResult();
            }
        }

        private sealed record Entry(
            TimeSpan Duration,
            TimeSpan Due,
            TaskCompletionSource Completion,
            TaskCompletionSource Continued,
            CancellationTokenRegistration Registration);
    }
}
