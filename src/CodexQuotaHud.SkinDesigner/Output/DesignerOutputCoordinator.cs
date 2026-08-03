using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Output;

public sealed class DesignerOutputCoordinator : INotifyPropertyChanged, IDisposable
{
    private readonly Func<SkinDraftDocument> _draft;
    private readonly Func<IReadOnlyDictionary<SkinAssetSlot, SkinAsset>> _assets;
    private readonly Func<
        SkinDraftDocument,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset>,
        CancellationToken,
        Task<DesignerOutputResult>> _apply;
    private readonly Func<
        SkinDraftDocument,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset>,
        string,
        bool,
        CancellationToken,
        Task<DesignerOutputResult>> _export;
    private readonly ISkinOutputDialogs _dialogs;
    private readonly Func<Action, Task> _dispatch;
    private int _busy;
    private int _disposed;
    private string? _errorMessage;
    private DesignerOutputResult? _lastResult;

    public DesignerOutputCoordinator(
        Func<SkinDraftDocument> draft,
        Func<IReadOnlyDictionary<SkinAssetSlot, SkinAsset>> assets,
        SkinApplyService apply,
        SkinExportService export,
        ISkinOutputDialogs dialogs)
        : this(
            draft,
            assets,
            apply.ApplyAsync,
            export.ExportAsync,
            dialogs,
            dispatch: null)
    {
    }

    internal DesignerOutputCoordinator(
        Func<SkinDraftDocument> draft,
        Func<IReadOnlyDictionary<SkinAssetSlot, SkinAsset>> assets,
        Func<
            SkinDraftDocument,
            IReadOnlyDictionary<SkinAssetSlot, SkinAsset>,
            CancellationToken,
            Task<DesignerOutputResult>> apply,
        Func<
            SkinDraftDocument,
            IReadOnlyDictionary<SkinAssetSlot, SkinAsset>,
            string,
            bool,
            CancellationToken,
            Task<DesignerOutputResult>> export,
        ISkinOutputDialogs dialogs,
        Func<Action, Task>? dispatch = null)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _export = export ?? throw new ArgumentNullException(nameof(export));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatch = dispatch ?? CreateContextDispatcher(SynchronizationContext.Current);
        ApplyCommand = new AsyncRelayCommand(ExecuteApplyAsync, CanRun);
        ExportCommand = new AsyncRelayCommand(ExecuteExportAsync, CanRun);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand ApplyCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (string.Equals(_errorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public DesignerOutputResult? LastResult
    {
        get => _lastResult;
        private set
        {
            if (Equals(_lastResult, value))
            {
                return;
            }

            _lastResult = value;
            OnPropertyChanged();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ApplyCommand.Dispose();
        ExportCommand.Dispose();
    }

    internal static string SuggestExportFileName(SkinDraftDocument draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder();
        foreach (var rune in draft.DisplayName.EnumerateRunes().Take(
                     SkinPackageLimits.MaximumDisplayNameScalars))
        {
            var replacement = rune.Value <= char.MaxValue &&
                invalid.Contains((char)rune.Value)
                ? "_"
                : rune.ToString();
            builder.Append(replacement);
        }

        var leaf = builder.ToString().Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(leaf))
        {
            leaf = draft.SkinId.ToString("D").ToLowerInvariant();
        }

        return leaf + ".cqskin";
    }

    private bool CanRun() =>
        Volatile.Read(ref _disposed) == 0 && !IsBusy;

    private Task ExecuteApplyAsync(CancellationToken cancellationToken) =>
        RunAsync(
            token => _apply(_draft(), _assets(), token),
            cancellationToken);

    private Task ExecuteExportAsync(CancellationToken cancellationToken) =>
        RunAsync(ExportCurrentAsync, cancellationToken);

    private async Task<DesignerOutputResult> ExportCurrentAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var draft = _draft();
        var destination = _dialogs.ChooseExportPath(
            SuggestExportFileName(draft));
        if (destination is null)
        {
            return Cancelled("Export path selection cancelled.");
        }

        var fullPath = Path.GetFullPath(destination);
        var overwrite = false;
        if (File.Exists(fullPath))
        {
            if (!_dialogs.ConfirmExportReplace(fullPath))
            {
                return Cancelled("Export replacement cancelled.");
            }

            overwrite = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _export(
            draft,
            _assets(),
            fullPath,
            overwrite,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(
        Func<CancellationToken, Task<DesignerOutputResult>> operation,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            OnPropertyChanged(nameof(IsBusy));
            ApplyCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            DesignerOutputResult result;
            try
            {
                result = await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = Cancelled("Output operation cancelled.");
            }
            catch
            {
                result = new DesignerOutputResult(
                    DesignerOutputDisposition.Failed,
                    null,
                    null,
                    [new SkinValidationError(
                        "output.failed",
                        "$output",
                        "The output operation failed.")],
                    "The output operation failed.");
            }

            try
            {
                await _dispatch(() => Present(result)).ConfigureAwait(false);
            }
            catch
            {
                RecordResultWithoutPresentation(result);
            }
        }
        finally
        {
            RestoreAvailability();
        }
    }

    private void Present(DesignerOutputResult result)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            RestoreAvailability();
            return;
        }

        LastResult = result;
        ErrorMessage = ResultErrorMessage(result);
        try
        {
            _dialogs.ShowResult(result);
        }
        catch
        {
            // Dialog shutdown/failure cannot change the completed operation.
        }
    }

    private void RecordResultWithoutPresentation(DesignerOutputResult result)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _lastResult = result;
        _errorMessage = ResultErrorMessage(result);
    }

    private static string? ResultErrorMessage(DesignerOutputResult result) =>
        result.Disposition is
            DesignerOutputDisposition.Failed or
            DesignerOutputDisposition.InstalledNotActivated
                ? result.Message
                : null;

    private void RestoreAvailability()
    {
        if (Interlocked.Exchange(ref _busy, 0) == 0)
        {
            return;
        }

        OnPropertyChanged(nameof(IsBusy));
        ApplyCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private static Func<Action, Task> CreateContextDispatcher(
        SynchronizationContext? context) => action =>
    {
        if (context is null ||
            ReferenceEquals(context, SynchronizationContext.Current))
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            context.Post(
                _ =>
                {
                    try
                    {
                        action();
                        completion.SetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                },
                null);
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }

        return completion.Task;
    };

    private static DesignerOutputResult Cancelled(string message) =>
        new(
            DesignerOutputDisposition.Cancelled,
            null,
            null,
            [],
            message);

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        var handlers = PropertyChanged;
        if (handlers is null)
        {
            return;
        }

        var arguments = new PropertyChangedEventArgs(propertyName);
        foreach (PropertyChangedEventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, arguments);
            }
            catch
            {
                // UI observers are advisory and cannot own command lifetime.
            }
        }
    }
}
