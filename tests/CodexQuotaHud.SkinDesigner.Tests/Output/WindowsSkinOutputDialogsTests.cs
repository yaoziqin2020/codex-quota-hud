using System.Windows;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.SkinDesigner.Tests.Preview;
using CodexQuotaHud.SkinDesigner.UI.Dialogs;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

[Collection(DesignerPreviewWpfCollection.Name)]
public sealed class WindowsSkinOutputDialogsTests
{
    [Fact]
    public void BuildExportDialogOptions_SplitsExchangeDirectoryFromLeafFileName()
    {
        var expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Codex Quota HUD Skins");
        var suggestedPath = Path.Combine(expectedDirectory, "Ocean Ring.cqskin");

        var options = WindowsSkinOutputDialogs.BuildExportDialogOptions(suggestedPath);

        Assert.Equal(expectedDirectory, options.InitialDirectory);
        Assert.Equal("Ocean Ring.cqskin", options.FileName);
        Assert.NotEqual(suggestedPath, options.FileName);
        Assert.False(Path.IsPathRooted(options.FileName));
    }

    [Fact]
    public void ChooseExportPath_KeepsNativePickerOnTheDesignerDispatcher()
    {
        const string suggested = "Ocean Ring.cqskin";
        var expectedSuggestion = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Codex Quota HUD Skins",
            suggested);

        RunSta(() =>
        {
            var owner = new Window();
            var service = new RecordingDesignerDialogService("ok");
            var dialogs = new WindowsSkinOutputDialogs(
                () => owner,
                service,
                (actualOwner, actualSuggested) =>
                {
                    Assert.Same(owner, actualOwner);
                    Assert.True(owner.Dispatcher.CheckAccess());
                    Assert.Equal(expectedSuggestion, actualSuggested);
                    return actualSuggested;
                });

            var selected = dialogs.ChooseExportPath(suggested);

            Assert.Equal(expectedSuggestion, selected);
            Assert.Empty(service.Calls);
        });
    }

    [Theory]
    [InlineData("replace", true)]
    [InlineData("cancel", false)]
    public void ConfirmExportReplace_MapsThemedActionsToExistingBoolean(
        string response,
        bool expected)
    {
        RunSta(() =>
        {
            var owner = new Window();
            var service = new RecordingDesignerDialogService(response);
            var dialogs = new WindowsSkinOutputDialogs(() => owner, service);

            var actual = dialogs.ConfirmExportReplace(@"C:\exports\Ocean Ring.cqskin");

            Assert.Equal(expected, actual);
            var call = Assert.Single(service.Calls);
            Assert.Same(owner, call.Owner);
            Assert.Equal("Export skin package", call.Request.Title);
            Assert.Equal(
                "Replace the existing package 'Ocean Ring.cqskin'?",
                call.Request.Message);
            Assert.Equal(DesignerDialogIcon.Warning, call.Request.Icon);
            Assert.Collection(
                call.Request.Actions,
                action => AssertAction(action, "replace", "Replace"),
                action => AssertAction(action, "cancel", "Cancel", true, true));
        });
    }

    [Theory]
    [InlineData("replace", SkinCollisionDecision.Replace)]
    [InlineData("keep-copy", SkinCollisionDecision.KeepCopy)]
    [InlineData("cancel", SkinCollisionDecision.Cancel)]
    public void ChooseApplyCollision_MapsThemedActionsToExistingDecision(
        string response,
        SkinCollisionDecision expected)
    {
        RunSta(() =>
        {
            using var root = new TemporaryRoot();
            var owner = new Window();
            var service = new RecordingDesignerDialogService(response);
            var dialogs = new WindowsSkinOutputDialogs(() => owner, service);

            var actual = dialogs.ChooseApplyCollision(CreatePreview(root));

            Assert.Equal(expected, actual);
            var call = Assert.Single(service.Calls);
            Assert.Same(owner, call.Owner);
            Assert.Equal("Apply skin", call.Request.Title);
            Assert.Equal(
                "A skin with this ID is already installed.\n\nYes: Replace   No: Keep a copy   Cancel: Stop",
                call.Request.Message);
            Assert.Equal(DesignerDialogIcon.Question, call.Request.Icon);
            Assert.Collection(
                call.Request.Actions,
                action => AssertAction(action, "replace", "Replace"),
                action => AssertAction(action, "keep-copy", "Keep a copy"),
                action => AssertAction(action, "cancel", "Stop", true, true));
        });
    }

    [Fact]
    public void ShowResult_UsesThemedOkActionForSuccessfulOutput()
    {
        RunSta(() =>
        {
            var owner = new Window();
            var service = new RecordingDesignerDialogService("ok");
            var dialogs = new WindowsSkinOutputDialogs(() => owner, service);
            var result = new DesignerOutputResult(
                DesignerOutputDisposition.Exported,
                null,
                @"C:\exports\Ocean Ring.cqskin",
                [],
                "Skin package exported.");

            dialogs.ShowResult(result);

            var call = Assert.Single(service.Calls);
            Assert.Same(owner, call.Owner);
            Assert.Equal("Skin Designer", call.Request.Title);
            Assert.Equal("Skin package exported.", call.Request.Message);
            Assert.Equal(DesignerDialogIcon.Information, call.Request.Icon);
            var action = Assert.Single(call.Request.Actions);
            AssertAction(action, "ok", "OK", true, true);
        });
    }

    [Fact]
    public void ShowResult_UsesWarningIconForCommittedOutputWithCleanupError()
    {
        RunSta(() =>
        {
            var service = new RecordingDesignerDialogService("ok");
            var dialogs = new WindowsSkinOutputDialogs(() => null, service);
            var result = new DesignerOutputResult(
                DesignerOutputDisposition.AppliedLive,
                null,
                null,
                [new SkinValidationError(
                    "apply.cleanup-failed",
                    "$operation",
                    "Cleanup failed.")],
                "Installed, but cleanup failed.");

            dialogs.ShowResult(result);

            var call = Assert.Single(service.Calls);
            Assert.Null(call.Owner);
            Assert.Equal(DesignerDialogIcon.Warning, call.Request.Icon);
        });
    }

    private static SkinInstallPreview CreatePreview(TemporaryRoot root)
    {
        var packagePath = Path.Combine(root.Path, "preview.cqskin");
        var request = new DraftPackageBuilder(OutputTestFixture.HudVersion).Build(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());
        Assert.True(request.IsValid);
        Assert.True(new SkinPackageWriter().WriteFile(
            packagePath,
            request.Value!,
            overwrite: false,
            CancellationToken.None).IsValid);
        var inspected = new SkinPackageInstaller(
            root.Paths,
            OutputTestFixture.HudVersion).Inspect(
                packagePath,
                OutputTestFixture.HudVersion,
                CancellationToken.None);
        return Assert.IsType<SkinInstallPreview>(inspected.Value);
    }

    private static void AssertAction(
        DesignerDialogAction action,
        string id,
        string label,
        bool isDefault = false,
        bool isCancel = false)
    {
        Assert.Equal(id, action.Id);
        Assert.Equal(label, action.Label);
        Assert.Equal(isDefault, action.IsDefault);
        Assert.Equal(isCancel, action.IsCancel);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private sealed class RecordingDesignerDialogService(
        params string[] responses)
        : IDesignerDialogService
    {
        private readonly Queue<string> _responses = new(responses);

        public List<DesignerDialogCall> Calls { get; } = [];

        public string Show(Window? owner, DesignerDialogRequest request)
        {
            Calls.Add(new DesignerDialogCall(owner, request));
            return _responses.Dequeue();
        }
    }

    private sealed record DesignerDialogCall(
        Window? Owner,
        DesignerDialogRequest Request);

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task15-dialogs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Paths = new SkinStoragePaths(Path);
        }

        public string Path { get; }

        public SkinStoragePaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
