using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexQuotaHud.App.UI.SkinManagement;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;
using CodexQuotaHud.Skins.Templates;
using CodexQuotaHud.Skins.Templates.FreeDecorationRing;

namespace CodexQuotaHud.App.Tests.UI.SkinManagement;

[Collection(WpfUiCollection.Name)]
public sealed class SkinImportPreviewWindowTests
{
    [Fact]
    public void CleanPreview_LabelsAuthorAndEffectiveCompatibilityAndRendersRealFixedIdleState() =>
        RunSta(() =>
        {
            using var fixture = PreviewFixture.Clean();
            var window = new SkinImportPreviewWindow(fixture.Preview);

            var renderer = Assert.IsAssignableFrom<CustomSkinRenderer>(
                window.PreviewHost.Content);
            renderer.Measure(new Size(132, 132));
            renderer.Arrange(new Rect(0, 0, 132, 132));
            renderer.ApplyTemplate();
            renderer.UpdateLayout();
            var text = Descendants<TextBlock>(renderer)
                .Select(block => block.Text)
                .ToArray();
            var arcs = Descendants<FreeDecorationRingArc>(renderer).ToArray();

            Assert.Equal("作者：Unverified author", window.AuthorLabel.Text);
            Assert.Contains("68%", text);
            Assert.Contains(arcs, arc => Math.Abs(arc.SweepAngle - (68 * 3.6)) < 0.01);
            Assert.Contains(arcs, arc => Math.Abs(arc.SweepAngle - (34 * 3.6)) < 0.01);
            Assert.True(renderer.HasActiveAnimations);
            Assert.Equal(4, renderer.DesiredFrameRate);
            Assert.Equal("安装", window.InstallButton.Content);
            Assert.Equal(Visibility.Collapsed, window.ReplaceButton.Visibility);
            Assert.Equal(Visibility.Collapsed, window.KeepCopyButton.Visibility);
            Assert.Equal("取消", window.CancelButton.Content);
            Assert.Contains("无可选图片", window.AssetSummaryText.Text);
            Assert.Contains("1.3.0", window.CompatibilityText.Text);
            Assert.DoesNotContain("1.1.1", window.CompatibilityText.Text);
            Assert.Equal(SkinCollisionDecision.Cancel, window.Decision);
        });

    [Fact]
    public void CleanPreview_InstallClickReturnsInternalPromoteDecision() =>
        RunSta(() =>
        {
            using var fixture = PreviewFixture.Clean();
            var window = new SkinImportPreviewWindow(fixture.Preview);

            window.InstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(SkinCollisionDecision.Replace, window.Decision);
            Assert.Equal("安装", window.InstallButton.Content);
        });

    [Fact]
    public void CollisionPreview_OffersExactlyReplaceKeepCopyAndCancel() =>
        RunSta(() =>
        {
            using var fixture = PreviewFixture.Collision(
                installedVersion: "1.0.0",
                incomingVersion: "2.0.0");
            var window = new SkinImportPreviewWindow(fixture.Preview);

            Assert.Equal(Visibility.Collapsed, window.InstallButton.Visibility);
            Assert.Equal("替换", window.ReplaceButton.Content);
            Assert.Equal(Visibility.Visible, window.ReplaceButton.Visibility);
            Assert.Equal("保留副本", window.KeepCopyButton.Content);
            Assert.Equal(Visibility.Visible, window.KeepCopyButton.Visibility);
            Assert.Equal("取消", window.CancelButton.Content);
            Assert.Equal(SkinCollisionDecision.Cancel, window.Decision);
        });

    [Fact]
    public void CollisionPreview_ClicksReturnExactReplaceKeepCopyAndCancelDecisions() =>
        RunSta(() =>
        {
            using var fixture = PreviewFixture.Collision(
                installedVersion: "1.0.0",
                incomingVersion: "2.0.0");

            var replace = new SkinImportPreviewWindow(fixture.Preview);
            replace.ReplaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SkinCollisionDecision.Replace, replace.Decision);

            var keepCopy = new SkinImportPreviewWindow(fixture.Preview);
            keepCopy.KeepCopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SkinCollisionDecision.KeepCopy, keepCopy.Decision);

            var cancel = new SkinImportPreviewWindow(fixture.Preview);
            cancel.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SkinCollisionDecision.Cancel, cancel.Decision);
        });

    [Fact]
    public void Preview_CloseAndEscapeReturnCancel() =>
        RunSta(() =>
        {
            using var fixture = PreviewFixture.Clean();
            var closed = new SkinImportPreviewWindow(fixture.Preview);

            closed.Close();

            Assert.Equal(SkinCollisionDecision.Cancel, closed.Decision);

            var escaped = new SkinImportPreviewWindow(fixture.Preview);
            var keyEvent = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                new TestPresentationSource(),
                Environment.TickCount,
                Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };

            escaped.RaiseEvent(keyEvent);

            Assert.True(keyEvent.Handled);
            Assert.Equal(SkinCollisionDecision.Cancel, escaped.Decision);
        });

    [Fact]
    public void DowngradePreview_DisablesInstallationAndStartsCancelled() =>
        RunSta(() =>
        {
            using var fixture = PreviewFixture.Collision(
                installedVersion: "2.0.0",
                incomingVersion: "1.0.0");
            var window = new SkinImportPreviewWindow(fixture.Preview);

            Assert.True(fixture.Preview.IsDowngrade);
            Assert.False(window.InstallButton.IsEnabled);
            Assert.False(window.ReplaceButton.IsEnabled);
            Assert.False(window.KeepCopyButton.IsEnabled);
            Assert.Equal(Visibility.Visible, window.DowngradeReason.Visibility);
            Assert.Equal(SkinCollisionDecision.Cancel, window.Decision);
        });

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
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
            throw failure;
        }
    }

    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;

        public override bool IsDisposed => false;

        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class PreviewFixture : IDisposable
    {
        private static readonly Guid SkinId =
            Guid.Parse("88888888-8888-8888-8888-888888888888");
        private static readonly SemanticVersion HudVersion =
            SemanticVersion.Parse("1.1.1");

        private PreviewFixture(string? installedVersion, string incomingVersion)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CodexQuotaHud.Task9.Preview",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            var paths = new SkinStoragePaths(Root);
            var installer = new SkinPackageInstaller(paths, HudVersion);
            if (installedVersion is not null)
            {
                var installedPath = WritePackage("installed.cqskin", installedVersion);
                var inspected = installer.Inspect(
                    installedPath,
                    HudVersion,
                    CancellationToken.None);
                Assert.True(inspected.IsValid, Format(inspected.Errors));
                var installed = installer.Install(
                    inspected.Value!,
                    SkinCollisionDecision.Replace,
                    CancellationToken.None);
                Assert.NotNull(installed.Installed);
            }

            var incomingPath = WritePackage("incoming.cqskin", incomingVersion);
            var preview = installer.Inspect(
                incomingPath,
                HudVersion,
                CancellationToken.None);
            Assert.True(preview.IsValid, Format(preview.Errors));
            Preview = preview.Value!;
        }

        public string Root { get; }

        public SkinInstallPreview Preview { get; }

        public static PreviewFixture Clean() => new(null, "1.0.0");

        public static PreviewFixture Collision(
            string installedVersion,
            string incomingVersion) =>
            new(installedVersion, incomingVersion);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private string WritePackage(string fileName, string version)
        {
            var destination = Path.Combine(Root, fileName);
            var identity = new SkinImageTransform(0, 0, 1, 0, 1, 0.5, 0.5);
            var result = new SkinPackageWriter().WriteFile(
                destination,
                new SkinPackageBuildRequest(
                    new SkinManifest(
                        1,
                        SkinId,
                        "Preview skin",
                        "Unverified author",
                        SemanticVersion.Parse(version),
                        "Safe preview fixture",
                        SkinPackageLimits.FreeDecorationRingTemplateId,
                        HudVersion,
                        null,
                        []),
                    new SkinTheme(
                        1,
                        SkinPackageLimits.FreeDecorationRingTemplateId,
                        identity,
                        identity,
                        identity,
                        "#FF53DCF8",
                        "#FF9A68FF",
                        "#FF0A1622",
                        0.9,
                        96,
                        8,
                        6,
                        270,
                        "#FF24CFF2",
                        0.5,
                        28,
                        12,
                        SkinTextWeight.SemiBold,
                        SkinTextPlacement.NumberAboveLabel,
                        new SkinAnimationSettings(0.25, 0.5, 0.75, 1)),
                    new Dictionary<SkinAssetSlot, SkinAsset>()),
                overwrite: false,
                CancellationToken.None);
            Assert.True(result.IsValid, Format(result.Errors));
            return destination;
        }

        private static string Format(IReadOnlyList<SkinValidationError> errors) =>
            string.Join(Environment.NewLine, errors.Select(error =>
                $"{error.Code} ({error.Location}): {error.Message}"));
    }
}
