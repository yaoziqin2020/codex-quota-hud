using System.Collections.ObjectModel;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public sealed class DraftRecoveryService : IAsyncDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly DraftStore _store;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly HashSet<Task> _admittedOperations = [];

    private SkinDraftDocument? _latest;
    private long _lastSavedRevision = -1;
    private CancellationTokenSource? _pendingDelayCancellation;
    private Task _pendingDelayTask = Task.CompletedTask;
    private bool _disposing;
    private Task? _disposeTask;

    public DraftRecoveryService(
        DraftStore store,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _delay = delay ?? Task.Delay;
    }

    public event EventHandler<DraftPersistenceFailure>? SaveFailed;

    public void NotifyMeaningfulChange(SkinDraftDocument draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        CancellationTokenSource cancellation;
        TaskCompletionSource completion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (_latest is not null && draft.Revision <= _latest.Revision)
            {
                return;
            }

            _latest = Clone(draft);
            CancelPendingDelayLocked();
            cancellation = new CancellationTokenSource();
            completion = NewCompletion();
            _pendingDelayCancellation = cancellation;
            _pendingDelayTask = completion.Task;
            _admittedOperations.Add(completion.Task);
        }

        _ = CompleteDebounceAdmissionAsync(cancellation, completion);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task pendingDelay;
        TaskCompletionSource completion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            pendingDelay = CancelPendingDelayLocked();
            completion = NewCompletion();
            _admittedOperations.Add(completion.Task);
        }

        _ = CompleteFlushAdmissionAsync(
            pendingDelay,
            cancellationToken,
            completion);
        return completion.Task;
    }

    public async Task DiscardAsync(
        Guid draftId,
        long maximumRevision,
        CancellationToken cancellationToken = default)
    {
        Task pendingDelay;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            ThrowIfNewerRecoveryPending(draftId, maximumRevision);
            pendingDelay = CancelPendingDelayLocked();
            if (_latest?.DraftId == draftId &&
                _latest.Revision <= maximumRevision)
            {
                _latest = null;
            }
        }

        await AwaitSettledAsync(pendingDelay).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                ThrowIfNewerRecoveryPending(draftId, maximumRevision);
            }

            var removed = await _store.DiscardWorkingCopyAsync(
                draftId,
                maximumRevision,
                cancellationToken).ConfigureAwait(false);
            if (!removed)
            {
                throw new InvalidOperationException(
                    "draft.discard-rejected: The recovery is corrupt, newer, or could not be deleted safely.");
            }

            lock (_sync)
            {
                if (_latest is null)
                {
                    _lastSavedRevision = -1;
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void ThrowIfNewerRecoveryPending(Guid draftId, long maximumRevision)
    {
        if (_latest?.DraftId == draftId &&
            _latest.Revision > maximumRevision)
        {
            throw new InvalidOperationException(
                "draft.discard-rejected: A newer in-memory recovery is pending and was preserved.");
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposing = true;
            CancelPendingDelayLocked();
            completion = NewCompletion();
            _disposeTask = completion.Task;
        }

        _ = CompleteDisposeAsync(completion);
        return new ValueTask(completion.Task);
    }

    private async Task CompleteDebounceAdmissionAsync(
        CancellationTokenSource cancellation,
        TaskCompletionSource completion)
    {
        DraftPersistenceFailure? failure = null;
        Exception? error = null;
        try
        {
            try
            {
                await _delay(DebounceInterval, cancellation.Token)
                    .ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }

            failure = await SaveLatestAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            CompleteAdmission(completion, cancellation, error, failure);
        }
    }

    private async Task CompleteFlushAdmissionAsync(
        Task pendingDelay,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        DraftPersistenceFailure? failure = null;
        Exception? error = null;
        try
        {
            await AwaitSettledAsync(pendingDelay).ConfigureAwait(false);
            failure = await SaveLatestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            CompleteAdmission(completion, null, error, failure);
        }
    }

    private void CompleteAdmission(
        TaskCompletionSource completion,
        CancellationTokenSource? delayCancellation,
        Exception? error,
        DraftPersistenceFailure? failure)
    {
        lock (_sync)
        {
            _admittedOperations.Remove(completion.Task);
            if (delayCancellation is not null &&
                ReferenceEquals(_pendingDelayCancellation, delayCancellation))
            {
                _pendingDelayCancellation = null;
                _pendingDelayTask = Task.CompletedTask;
            }
        }

        delayCancellation?.Dispose();
        Complete(completion, error);
        if (failure is not null)
        {
            RaiseSaveFailed(failure);
        }
    }

    private async Task<DraftPersistenceFailure?> SaveLatestAsync(
        CancellationToken cancellationToken)
    {
        DraftPersistenceFailure? failure = null;
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SkinDraftDocument? snapshot;
            lock (_sync)
            {
                snapshot = _latest;
                if (snapshot is null || snapshot.Revision <= _lastSavedRevision)
                {
                    return null;
                }

                snapshot = Clone(snapshot);
            }

            try
            {
                await _store.SaveRecoveryAsync(snapshot, cancellationToken)
                    .ConfigureAwait(false);
                lock (_sync)
                {
                    _lastSavedRevision = Math.Max(
                        _lastSavedRevision,
                        snapshot.Revision);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure = CreateFailure(snapshot, exception);
            }
        }
        finally
        {
            _writeGate.Release();
        }

        return failure;
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        DraftPersistenceFailure? failure = null;
        Exception? error = null;
        try
        {
            await AwaitAllAdmittedOperationsAsync().ConfigureAwait(false);
            failure = await SaveLatestAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            lock (_sync)
            {
                _pendingDelayCancellation?.Dispose();
                _pendingDelayCancellation = null;
                _pendingDelayTask = Task.CompletedTask;
            }

            _writeGate.Dispose();
        }

        Complete(completion, error);
        if (failure is not null)
        {
            RaiseSaveFailed(failure);
        }
    }

    private async Task AwaitAllAdmittedOperationsAsync()
    {
        while (true)
        {
            Task[] admitted;
            lock (_sync)
            {
                admitted = _admittedOperations.ToArray();
            }

            if (admitted.Length == 0)
            {
                return;
            }

            foreach (var operation in admitted)
            {
                await AwaitSettledAsync(operation).ConfigureAwait(false);
            }
        }
    }

    private Task CancelPendingDelayLocked()
    {
        _pendingDelayCancellation?.Cancel();
        return _pendingDelayTask;
    }

    private static async Task AwaitSettledAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The original caller observes an admitted operation's failure.
        }
    }

    private static void Complete(
        TaskCompletionSource completion,
        Exception? error)
    {
        if (error is OperationCanceledException canceled)
        {
            completion.TrySetCanceled(canceled.CancellationToken);
        }
        else if (error is not null)
        {
            completion.TrySetException(error);
        }
        else
        {
            completion.TrySetResult();
        }
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void RaiseSaveFailed(DraftPersistenceFailure failure)
    {
        try
        {
            SaveFailed?.Invoke(this, failure);
        }
        catch
        {
            // Persistence and lifecycle completion cannot depend on an observer.
        }
    }

    private static DraftPersistenceFailure CreateFailure(
        SkinDraftDocument snapshot,
        Exception exception)
    {
        var (code, message) = exception is DraftPersistenceException persistence
            ? (persistence.ErrorCode, persistence.SafeMessage)
            : (
                "draft.save-failed",
                "The recovery draft could not be saved and remains pending.");
        return new DraftPersistenceFailure(
            snapshot.DraftId,
            "recovery.json",
            code,
            message);
    }

    private static SkinDraftDocument Clone(SkinDraftDocument draft) =>
        draft with
        {
            Assets = new ReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>(
                draft.Assets.ToDictionary(pair => pair.Key, pair => pair.Value))
        };
}
