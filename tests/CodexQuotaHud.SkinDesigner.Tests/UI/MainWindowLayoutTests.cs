using System.IO.Compression;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.SkinDesigner.Tests.Preview;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.UI;

[Collection(DesignerPreviewWpfCollection.Name)]
public sealed class MainWindowLayoutTests
{
    [Fact]
    public void RealWindow_LoadsDesignerSpecificIcon()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);

            Assert.NotNull(window.Icon);
            Assert.Contains(
                "DesignerIcon.ico",
                window.Icon.ToString(),
                StringComparison.OrdinalIgnoreCase);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void MainWindowButton_UsesDesignerTemplateWithPressedOffsetAndKeepsRaisedSurfaceWhenDisabled()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            var button = Assert.IsType<Button>(window.FindName("NewDraftButton"));

            window.AttachPreviewOwnerForTesting();
            button.ApplyTemplate();
            var templateRoot = Assert.IsType<Border>(
                button.Template.FindName("DesignerButtonBorder", button));
            var normalOffset = Assert.IsType<TranslateTransform>(
                templateRoot.RenderTransform);
            var pressedTrigger = Assert.Single(
                button.Template.Triggers.OfType<Trigger>(),
                trigger => trigger.Property == Button.IsPressedProperty &&
                    Equals(trigger.Value, true));
            var offsetSetter = Assert.Single(
                pressedTrigger.Setters.OfType<Setter>(),
                setter => setter.TargetName == "DesignerButtonBorder" &&
                    setter.Property == UIElement.RenderTransformProperty);
            var pressedOffset = Assert.IsType<TranslateTransform>(
                offsetSetter.Value);

            Assert.Equal(0, normalOffset.Y);
            Assert.Equal(1, pressedOffset.Y);

            window.IsEnabled = false;
            window.UpdateLayout();

            Assert.Same(
                window.FindResource("DesignerRaisedBrush"),
                templateRoot.Background);
            Assert.Equal(0.55, templateRoot.Opacity, precision: 2);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_ExposesDocumentCommandsAndFailedOpenPreservesCurrentSession()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var paths = new SkinStoragePaths(temporary.Path);
            var draft = CreateDraft();
            var dialog = new RecordingDialog();
            var service = new DesignerDocumentService(
                paths,
                new DraftStore(paths),
                new InstalledSkinCatalog(
                    paths,
                    SemanticVersion.Parse("1.1.1")),
                new CodexQuotaHud.Skins.Packaging.SkinPackageReader());
            var requests = new RecordingDocumentRequests
            {
                DraftId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
            };
            var window = new MainWindow(
                draft,
                new Dictionary<SkinAssetSlot, SkinAsset>(),
                paths,
                dialog,
                service,
                requests);
            var before = window.Editor.Current;
            foreach (var name in new[]
                     {
                         "NewDraftButton", "OpenDraftButton",
                         "EditInstalledButton", "ImportForEditingButton"
                     })
            {
                Assert.IsType<Button>(window.FindName(name));
            }

            Assert.IsType<Button>(window.FindName("OpenDraftButton")).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Same(before, window.Editor.Current);
            Assert.Contains(
                "not found",
                Assert.IsType<TextBlock>(window.FindName("DocumentStatusText")).Text,
                StringComparison.OrdinalIgnoreCase);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealHandle_ReappliesLayoutFromInjectedCurrentMonitorMetrics()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var paths = new SkinStoragePaths(temporary.Path);
            var dialog = new RecordingDialog();
            var service = new DesignerDocumentService(
                paths,
                new DraftStore(paths),
                new InstalledSkinCatalog(
                    paths,
                    SemanticVersion.Parse("1.1.1")),
                new CodexQuotaHud.Skins.Packaging.SkinPackageReader());
            var monitor = new RecordingMonitorSource(
                new DesignerMonitorMetrics(
                    new Rect(1600, 120, 1280, 720),
                    new DpiScale(1.5, 1.5)));
            var window = new MainWindow(
                CreateDraft(),
                new Dictionary<SkinAssetSlot, SkinAsset>(),
                paths,
                dialog,
                service,
                new RecordingDocumentRequests(),
                monitorWorkArea: monitor);
            window.AttachPreviewOwnerForTesting();
            monitor.Reset();

            var layout = window.ReapplyCurrentMonitorLayoutForTesting();

            Assert.Equal(1, monitor.CallCount);
            Assert.Same(window, monitor.LastWindow);
            Assert.NotEqual(IntPtr.Zero, monitor.LastHandle);
            Assert.Equal(layout.WindowBounds.Left, window.Left);
            Assert.True(window.Left >= 1600);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void DisplayEnvironmentChange_ReappliesMixedDpiWorkAreaAndPreviewRestoresAfterMinimize()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var paths = new SkinStoragePaths(temporary.Path);
            var dialog = new RecordingDialog();
            var service = new DesignerDocumentService(
                paths,
                new DraftStore(paths),
                new InstalledSkinCatalog(
                    paths,
                    SemanticVersion.Parse("1.1.1")),
                new CodexQuotaHud.Skins.Packaging.SkinPackageReader());
            var monitor = new RecordingMonitorSource(
                new DesignerMonitorMetrics(
                    new Rect(0, 0, 1280, 720),
                    new DpiScale(1, 1)));
            var window = new MainWindow(
                CreateDraft(),
                new Dictionary<SkinAssetSlot, SkinAsset>(),
                paths,
                dialog,
                service,
                new RecordingDocumentRequests(),
                monitorWorkArea: monitor);
            window.AttachPreviewOwnerForTesting();
            monitor.Metrics = new DesignerMonitorMetrics(
                new Rect(-960, 0, 960, 540),
                new DpiScale(2, 2));
            monitor.Reset();

            window.NotifyDisplayEnvironmentChangedForTesting();
            PumpUntil(() => monitor.CallCount > 0);

            Assert.True(window.Left < 0);
            Assert.True(window.Left >= -960);
            window.ShowPreviewForTesting();
            Assert.True(window.PreviewWindowForTesting.IsVisible);
            window.WindowState = WindowState.Minimized;
            Assert.False(window.PreviewWindowForTesting.IsVisible);
            window.WindowState = WindowState.Normal;
            PumpUntil(() => window.PreviewWindowForTesting.IsVisible);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void SystemDisplayCallback_AfterDispatcherShutdownDoesNotEscape()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            window.DisposeWithoutShowingForTesting();
            Dispatcher.CurrentDispatcher.BeginInvokeShutdown(
                DispatcherPriority.Send);
            var callback = Assert.IsAssignableFrom<System.Reflection.MethodInfo>(
                typeof(MainWindow).GetMethod(
                    "OnSystemDisplayChanged",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic));

            var exception = Record.Exception(() =>
                callback.Invoke(window, [null, EventArgs.Empty]));

            Assert.Null(exception);
        });
    }

    [Fact]
    public void SystemDisplayCallback_WhenDispatcherRejectsPostDoesNotEscape()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(
                temporary,
                out _,
                systemEventDispatcherPost: _ =>
                    throw new InvalidOperationException("Dispatcher rejected post."));
            var callback = Assert.IsAssignableFrom<System.Reflection.MethodInfo>(
                typeof(MainWindow).GetMethod(
                    "OnSystemDisplayChanged",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic));

            var exception = Record.Exception(() =>
                callback.Invoke(window, [null, EventArgs.Empty]));

            Assert.Null(exception);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_UsesExactSharedSliderBoundsAndHasNoThemeSelector()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            Assert.IsType<Expander>(window.FindName("QuotaRingsSection")).IsExpanded = true;
            Assert.IsType<Expander>(window.FindName("TextSection")).IsExpanded = true;
            window.ApplyTemplate();
            window.UpdateLayout();
            var sliders = Descendants<Slider>(window)
                .Where(slider => slider.Tag is string)
                .ToDictionary(slider => (string)slider.Tag);
            var expected = new Dictionary<string, (double Minimum, double Maximum)>
            {
                ["RingDiameter"] = (
                    SkinPackageLimits.MinimumRingDiameterDip,
                    SkinPackageLimits.MaximumRingDiameterDip),
                ["RingThickness"] = (
                    SkinPackageLimits.MinimumRingThicknessDip,
                    SkinPackageLimits.MaximumRingThicknessDip),
                ["RingGap"] = (
                    SkinPackageLimits.MinimumRingGapDip,
                    SkinPackageLimits.MaximumRingGapDip),
                ["StartAngle"] = (
                    SkinPackageLimits.MinimumStartAngleDegrees,
                    SkinPackageLimits.MaximumStartAngleDegrees),
                ["NumberTextSize"] = (
                    SkinPackageLimits.MinimumTextSizeDip,
                    SkinPackageLimits.MaximumTextSizeDip),
                ["LabelTextSize"] = (
                    SkinPackageLimits.MinimumTextSizeDip,
                    SkinPackageLimits.MaximumTextSizeDip),
                ["TextOffsetY"] = (
                    SkinPackageLimits.MinimumTextOffsetYDip,
                    SkinPackageLimits.MaximumTextOffsetYDip),
                ["TextLineGap"] = (
                    SkinPackageLimits.MinimumTextLineGapDip,
                    SkinPackageLimits.MaximumTextLineGapDip)
            };

            foreach (var pair in expected)
            {
                Assert.Equal(pair.Value.Minimum, sliders[pair.Key].Minimum);
                Assert.Equal(pair.Value.Maximum, sliders[pair.Key].Maximum);
            }

            Assert.Null(window.FindName("ThemeSelector"));
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_ExposesAccessibleSignedTextCompositionControls()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            window.AttachPreviewOwnerForTesting();
            Assert.IsType<Expander>(window.FindName("TextSection")).IsExpanded = true;
            window.ApplyTemplate();
            window.UpdateLayout();
            var taggedSliders = Descendants<Slider>(window)
                .Where(slider => slider.Tag is string)
                .ToArray();
            var tags = taggedSliders
                .Select(slider => (string)slider.Tag)
                .ToArray();
            var offset = Assert.Single(
                taggedSliders,
                slider => Equals(slider.Tag, "TextOffsetY"));
            var gap = Assert.Single(
                taggedSliders,
                slider => Equals(slider.Tag, "TextLineGap"));

            Assert.Equal(tags.Length, tags.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(-32, offset.Minimum);
            Assert.Equal(32, offset.Maximum);
            Assert.Equal(1, offset.TickFrequency);
            Assert.True(offset.IsSnapToTickEnabled);
            Assert.Equal("文字整体上下偏移", AutomationProperties.GetName(offset));
            Assert.Equal(-16, gap.Minimum);
            Assert.Equal(32, gap.Maximum);
            Assert.Equal(1, gap.TickFrequency);
            Assert.True(gap.IsSnapToTickEnabled);
            Assert.Equal("数字和时间行距", AutomationProperties.GetName(gap));
            Assert.Contains(
                Descendants<TextBlock>(window),
                text => text.Text == "文字整体上下");
            Assert.Contains(
                Descendants<TextBlock>(window),
                text => text.Text == "数字/时间间距");
            Assert.Equal("0 DIP", Assert.IsType<TextBlock>(
                window.FindName("TextOffsetYValueText")).Text);
            Assert.Equal("0 DIP", Assert.IsType<TextBlock>(
                window.FindName("TextLineGapValueText")).Text);

            offset.Value = 7;
            gap.Value = -5;

            Assert.Equal(7, window.Editor.Current.Theme.TextOffsetY);
            Assert.Equal(-5, window.Editor.Current.Theme.TextLineGap);
            Assert.Equal("+7 DIP", Assert.IsType<TextBlock>(
                window.FindName("TextOffsetYValueText")).Text);
            Assert.Equal("-5 DIP", Assert.IsType<TextBlock>(
                window.FindName("TextLineGapValueText")).Text);

            offset.Maximum = 40;
            offset.Value = 33;

            Assert.Equal(7, window.Editor.Current.Theme.TextOffsetY);
            Assert.Equal(7, offset.Value);
            Assert.NotNull(offset.ToolTip);
            Assert.Same(Brushes.OrangeRed, offset.BorderBrush);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RejectedSliderValue_RestoresDraftValueAndKeepsFieldError()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            Assert.IsType<Expander>(window.FindName("QuotaRingsSection")).IsExpanded = true;
            window.ApplyTemplate();
            window.UpdateLayout();
            var slider = Descendants<Slider>(window)
                .Single(control => Equals(control.Tag, "RingDiameter"));
            var before = window.Editor.Current.Theme.RingDiameter;
            slider.Maximum = 200;

            slider.Value = 160;

            Assert.Equal(before, window.Editor.Current.Theme.RingDiameter);
            Assert.Equal(before, slider.Value);
            Assert.NotNull(slider.ToolTip);
            Assert.Same(Brushes.OrangeRed, slider.BorderBrush);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_HasFourRowsIndependentEditorSixSectionsAndVisibleActions()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            window.AttachPreviewOwnerForTesting();
            window.Width = 960;
            window.Height = 540;
            var root = Assert.IsType<Grid>(window.FindName("DesignerRoot"));
            window.UpdateLayout();
            Assert.Equal(4, root.RowDefinitions.Count);
            var editor = Assert.IsType<ScrollViewer>(
                window.FindName("EditorScrollViewer"));
            var preview = Assert.IsType<Border>(window.FindName("PreviewStage"));
            Assert.Equal(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
            Assert.True(preview.ActualWidth >= 280);
            Assert.NotNull(window.FindName("SyntheticPreviewStrip"));
            Assert.NotNull(window.FindName("PrimaryActionBar"));
            foreach (var name in new[]
                     {
                         "BasicInformationSection", "ImagesSection",
                         "QuotaRingsSection", "ColorsEffectsSection",
                         "TextSection", "AnimationSection"
                     })
            {
                Assert.IsType<Expander>(window.FindName(name));
            }

            var save = Assert.IsType<Button>(window.FindName("SaveDraftButton"));
            var apply = Assert.IsType<Button>(window.FindName("ApplyToHudButton"));
            var export = Assert.IsType<Button>(window.FindName("ExportPackageButton"));
            AssertFullyRenderedWithin(save, root);
            AssertFullyRenderedWithin(apply, root);
            AssertFullyRenderedWithin(export, root);
            Assert.False(apply.IsEnabled);
            Assert.False(export.IsEnabled);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_OffersPresetsAndKeepsAdvancedDecorationMotionContextual()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            Assert.IsType<Expander>(window.FindName("AnimationSection"))
                .IsExpanded = true;
            window.ApplyTemplate();
            window.UpdateLayout();

            var advanced = Assert.IsType<Expander>(
                window.FindName("AdvancedAnimationSection"));
            var status = Assert.IsType<TextBlock>(
                window.FindName("AnimationPresetStatusText"));
            var hint = Assert.IsType<TextBlock>(
                window.FindName("DecorationAnimationHintText"));
            var rotation = Descendants<Slider>(window)
                .Single(control => Equals(control.Tag, "RotationIntensity"));
            var floating = Descendants<Slider>(window)
                .Single(control => Equals(control.Tag, "FloatingIntensity"));

            Assert.False(advanced.IsExpanded);
            Assert.Equal("柔和", status.Text);
            Assert.False(rotation.IsEnabled);
            Assert.False(floating.IsEnabled);
            Assert.Contains("透明装饰图", hint.Text);

            var revision = window.Editor.Current.Revision;
            Assert.IsType<Button>(window.FindName(
                    "AnimationNoticeablePresetButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(revision + 1, window.Editor.Current.Revision);
            Assert.Equal(
                new SkinAnimationSettings(0, .9, .9, 0),
                window.Editor.Current.Theme.Animation);
            Assert.Equal("明显", status.Text);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_EachPresetPreservesFourTimesThreeSecondsAcrossControlsDraftAndSave()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var paths = new SkinStoragePaths(temporary.Path);
            var store = new DraftStore(paths);
            var window = CreateWindow(
                temporary,
                out _,
                draftStore: store);
            window.AttachPreviewOwnerForTesting();
            Assert.IsType<Expander>(window.FindName("AnimationSection"))
                .IsExpanded = true;
            window.UpdateLayout();

            var speed = Assert.Single(
                Descendants<Slider>(window),
                control => Equals(control.Tag, "RefreshSpeedMultiplier"));
            var hold = Assert.Single(
                Descendants<Slider>(window),
                control => Equals(control.Tag, "RefreshHoldSeconds"));
            speed.Value = 4;
            hold.Value = 3;

            foreach (var buttonName in new[]
                     {
                         "AnimationStillPresetButton",
                         "AnimationGentlePresetButton",
                         "AnimationNoticeablePresetButton"
                     })
            {
                Assert.IsType<Button>(window.FindName(buttonName)).RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(
                    4,
                    window.Editor.Current.Theme.Animation.RefreshSpeedMultiplier);
                Assert.Equal(
                    3,
                    window.Editor.Current.Theme.Animation.RefreshHoldSeconds);
                Assert.Equal(4, speed.Value);
                Assert.Equal(3, hold.Value);
                Assert.StartsWith("4.0", Assert.IsType<TextBlock>(
                    window.FindName("RefreshSpeedValueText")).Text);
                Assert.StartsWith("3.0", Assert.IsType<TextBlock>(
                    window.FindName("RefreshHoldValueText")).Text);

                Assert.IsType<Button>(window.FindName("SaveDraftButton"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => window.SaveOperationForTesting.IsCompleted);
                Assert.True(window.SaveOperationForTesting.IsCompletedSuccessfully);
                var saved = store.LoadForOpen(window.Editor.Current.DraftId);
                Assert.NotNull(saved.Document);
                Assert.Equal(
                    4,
                    saved.Document.Theme.Animation.RefreshSpeedMultiplier);
                Assert.Equal(
                    3,
                    saved.Document.Theme.Animation.RefreshHoldSeconds);
            }

            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_ProvidesOneAccessibleRefreshControlForEachLivePreviewSetting()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            window.AttachPreviewOwnerForTesting();
            Assert.IsType<Expander>(window.FindName("AnimationSection"))
                .IsExpanded = true;
            window.ApplyTemplate();
            window.UpdateLayout();

            var speed = Assert.Single(
                Descendants<Slider>(window),
                control => Equals(control.Tag, "RefreshSpeedMultiplier"));
            var hold = Assert.Single(
                Descendants<Slider>(window),
                control => Equals(control.Tag, "RefreshHoldSeconds"));

            Assert.Equal(SkinPackageLimits.MinimumRefreshSpeedMultiplier,
                speed.Minimum);
            Assert.Equal(SkinPackageLimits.MaximumRefreshSpeedMultiplier,
                speed.Maximum);
            Assert.Equal(SkinPackageLimits.MinimumRefreshHoldSeconds,
                hold.Minimum);
            Assert.Equal(SkinPackageLimits.MaximumRefreshHoldSeconds,
                hold.Maximum);
            Assert.Equal(0.1, speed.SmallChange, precision: 6);
            Assert.Equal(0.1, hold.SmallChange, precision: 6);
            Assert.Equal(0.1, speed.TickFrequency, precision: 6);
            Assert.Equal(0.1, hold.TickFrequency, precision: 6);
            Assert.True(speed.IsSnapToTickEnabled);
            Assert.True(hold.IsSnapToTickEnabled);
            Assert.Equal("刷新速度", AutomationProperties.GetName(speed));
            Assert.Equal("加速延续", AutomationProperties.GetName(hold));
            Assert.Equal("2.0×", Assert.IsType<TextBlock>(
                window.FindName("RefreshSpeedValueText")).Text);
            Assert.Equal("1.5 秒", Assert.IsType<TextBlock>(
                window.FindName("RefreshHoldValueText")).Text);

            foreach (var value in new[] { 0d, 2d, 4d })
            {
                speed.Value = value;
                Assert.Equal(
                    value,
                    window.Editor.Current.Theme.Animation.RefreshSpeedMultiplier);
            }

            hold.Value = 3;
            Assert.Equal(3, window.Editor.Current.Theme.Animation.RefreshHoldSeconds);

            speed.Maximum = 5;
            speed.Value = 4.5;

            Assert.Equal(4, window.Editor.Current.Theme.Animation.RefreshSpeedMultiplier);
            Assert.Equal(4, speed.Value);
            Assert.NotNull(speed.ToolTip);
            Assert.Same(Brushes.OrangeRed, speed.BorderBrush);

            speed.Maximum = 4;
            speed.Value = 2.051;
            hold.Value = 1.551;

            Assert.Equal(2.1, speed.Value, precision: 6);
            Assert.Equal(2.1,
                window.Editor.Current.Theme.Animation.RefreshSpeedMultiplier,
                precision: 6);
            Assert.Equal("2.1×", Assert.IsType<TextBlock>(
                window.FindName("RefreshSpeedValueText")).Text);
            Assert.Equal(1.6, hold.Value, precision: 6);
            Assert.Equal(1.6,
                window.Editor.Current.Theme.Animation.RefreshHoldSeconds,
                precision: 6);
            Assert.Equal("1.6 秒", Assert.IsType<TextBlock>(
                window.FindName("RefreshHoldValueText")).Text);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_UsesTwoReadableSyntheticRowsAtMinimumWidth()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            window.AttachPreviewOwnerForTesting();
            window.Width = 600;
            window.Height = 720;
            window.UpdateLayout();

            var strip = Assert.IsType<Border>(
                window.FindName("SyntheticPreviewStrip"));
            var quotaRow = Assert.IsType<Grid>(
                window.FindName("SyntheticQuotaRow"));
            var stateRow = Assert.IsType<Grid>(
                window.FindName("SyntheticStateRow"));
            AssertFullyRenderedWithin(quotaRow, strip);
            AssertFullyRenderedWithin(stateRow, strip);

            var stateGroup = Assert.IsType<Border>(
                window.FindName("SyntheticStateGroup"));
            var dockGroup = Assert.IsType<Border>(
                window.FindName("SyntheticDockGroup"));
            AssertFullyRenderedWithin(stateGroup, stateRow);
            AssertFullyRenderedWithin(dockGroup, stateRow);
            Assert.True(
                dockGroup.TranslatePoint(new Point(), stateRow).X <=
                stateGroup.ActualWidth + 16);

            var presets = Assert.IsType<ComboBox>(
                window.FindName("FiveHourPresetBox"));
            Assert.Equal(84, presets.Width);
            Assert.Equal(
                84,
                Assert.IsType<ComboBox>(
                    window.FindName("WeeklyPresetBox")).Width);
            Assert.Equal(
                "左",
                Assert.IsType<Button>(
                    window.FindName("PreviewLeftButton")).Content);
            Assert.Equal(
                "右",
                Assert.IsType<Button>(
                    window.FindName("PreviewRightButton")).Content);
            Assert.Equal(
                "上",
                Assert.IsType<Button>(
                    window.FindName("PreviewTopButton")).Content);
            Assert.Equal(
                "下",
                Assert.IsType<Button>(
                    window.FindName("PreviewBottomButton")).Content);
            presets.SelectedItem = 68d;
            presets.IsDropDownOpen = true;
            window.UpdateLayout();
            var item = Assert.IsType<ComboBoxItem>(
                presets.ItemContainerGenerator.ContainerFromItem(68d));
            var expected = Color.FromRgb(0x0B, 0x12, 0x20);
            Assert.Equal(
                expected,
                Assert.IsType<SolidColorBrush>(item.Foreground).Color);
            var renderedText = VisualDescendants<TextBlock>(item).ToArray();
            Assert.NotEmpty(renderedText);
            Assert.All(renderedText, block => Assert.Equal(
                expected,
                Assert.IsType<SolidColorBrush>(block.Foreground).Color));
            presets.IsDropDownOpen = false;
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_ProvidesAccessibleNamesTabOrderAndLongTextContainment()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            window.AttachPreviewOwnerForTesting();
            window.Width = 960;
            window.Height = 540;
            window.UpdateLayout();
            var controls = Descendants<Control>(window)
                .Where(control =>
                    KeyboardNavigation.GetTabIndex(control) > 0 &&
                    KeyboardNavigation.GetTabIndex(control) < int.MaxValue &&
                    !string.IsNullOrWhiteSpace(
                        AutomationProperties.GetName(control)))
                .OrderBy(KeyboardNavigation.GetTabIndex)
                .ToArray();

            Assert.All(controls, control =>
                Assert.False(string.IsNullOrWhiteSpace(
                    AutomationProperties.GetName(control))));
            var tabIndexes = controls.Select(KeyboardNavigation.GetTabIndex).ToArray();
            Assert.Equal(Enumerable.Range(1, 64), tabIndexes);
            Assert.Equal(
                "图片变换目标",
                AutomationProperties.GetName(controls[15]));
            Assert.Equal(
                "额度环直径",
                AutomationProperties.GetName(controls[23]));
            Assert.Equal(
                "额度显示模式",
                AutomationProperties.GetName(controls[48]));
            Assert.Equal(
                "保存草稿",
                AutomationProperties.GetName(controls[61]));
            var projectName = Assert.IsType<TextBox>(
                window.FindName("ProjectNameTextBox"));
            var displayName = Assert.IsType<TextBox>(
                window.FindName("DisplayNameTextBox"));
            Assert.True(projectName.Focus());
            Assert.True(projectName.IsKeyboardFocused);
            Assert.True(projectName.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(displayName, Keyboard.FocusedElement);

            var slider = Assert.IsType<Slider>(
                window.FindName("FiveHourPercentSlider"));
            Assert.True(slider.Focus());
            var before = slider.Value;
            slider.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice,
                Assert.IsAssignableFrom<PresentationSource>(
                    PresentationSource.FromVisual(window)),
                Environment.TickCount,
                Key.Right)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
            Assert.True(slider.Value > before);

            projectName.Text = string.Concat(Enumerable.Repeat("皮肤设计", 20));
            window.UpdateLayout();
            Assert.True(projectName.ActualWidth <=
                Assert.IsType<ScrollViewer>(
                    window.FindName("EditorScrollViewer")).ActualWidth);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealWindow_ProvidesVisibleFocusRingAndWcagContrastPairs()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            var projectName = Assert.IsType<TextBox>(
                window.FindName("ProjectNameTextBox"));
            var focusStyle = Assert.IsType<Style>(projectName.FocusVisualStyle);
            Assert.Same(
                window.FindResource("DesignerFocusVisualStyle"),
                focusStyle);
            var template = Assert.IsType<ControlTemplate>(
                focusStyle.Setters.OfType<Setter>()
                    .Single(setter => setter.Property == Control.TemplateProperty)
                    .Value);
            var focusBorder = Assert.IsType<Border>(template.LoadContent());
            Assert.Equal(new Thickness(2), focusBorder.BorderThickness);
            var accent = Assert.IsType<SolidColorBrush>(
                window.FindResource("DesignerAccentBrush"));

            var background = Assert.IsType<SolidColorBrush>(
                window.FindResource("DesignerBackgroundBrush"));
            var surface = Assert.IsType<SolidColorBrush>(
                window.FindResource("DesignerSurfaceBrush"));
            var raised = Assert.IsType<SolidColorBrush>(
                window.FindResource("DesignerRaisedBrush"));
            var text = Assert.IsType<SolidColorBrush>(
                window.FindResource("DesignerTextBrush"));
            var muted = Assert.IsType<SolidColorBrush>(
                window.FindResource("DesignerMutedTextBrush"));
            var accentText = Assert.IsType<SolidColorBrush>(
                window.FindResource("DesignerAccentTextBrush"));
            Assert.True(ContrastRatio(text.Color, background.Color) >= 4.5);
            Assert.True(ContrastRatio(text.Color, raised.Color) >= 4.5);
            Assert.True(ContrastRatio(muted.Color, surface.Color) >= 4.5);
            Assert.True(ContrastRatio(accentText.Color, accent.Color) >= 4.5);
            Assert.True(ContrastRatio(accent.Color, background.Color) >= 3);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Theory]
    [MemberData(nameof(DesignerLayoutPolicyTests.WorkAreas),
        MemberType = typeof(DesignerLayoutPolicyTests))]
    public void RealWindow_AppliesPolicyAtEveryWorkAreaAndDpi(
        Rect workArea,
        double scale)
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            var layout = window.ApplyLayoutForTesting(
                workArea,
                new DpiScale(scale, scale));

            Assert.Equal(layout.WindowBounds.Left, window.Left);
            Assert.Equal(layout.WindowBounds.Top, window.Top);
            Assert.Equal(layout.WindowBounds.Width, window.Width);
            Assert.Equal(layout.WindowBounds.Height, window.Height);
            Assert.True(window.EditorColumnWidthForTesting >= 320);
            Assert.True(window.PreviewColumnWidthForTesting >= 280);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void RealPreview_IsOwnedTaskbarHiddenAndDisposedOnlyWithDesigner()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);

            window.AttachPreviewOwnerForTesting();

            Assert.Same(window, window.PreviewWindowForTesting.Owner);
            Assert.False(window.PreviewWindowForTesting.ShowInTaskbar);
            Assert.False(window.PreviewDisposedForTesting);
            window.DisposeWithoutShowingForTesting();
            Assert.True(window.PreviewDisposedForTesting);
        });
    }

    [Fact]
    public void ExpandPreview_RecentersRealHudInsideLatestPreviewWorkArea()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out _);
            window.AttachPreviewOwnerForTesting();
            window.ShowPreviewForTesting();
            window.Width += 1;
            DrainDispatcher();

            var previewStage = Assert.IsType<Border>(
                window.FindName("PreviewStage"));
            var source = Assert.IsAssignableFrom<PresentationSource>(
                PresentationSource.FromVisual(previewStage));
            var origin = source.CompositionTarget!.TransformFromDevice.Transform(
                previewStage.PointToScreen(new Point()));
            var workArea = new Rect(
                origin.X,
                origin.Y,
                previewStage.ActualWidth,
                previewStage.ActualHeight);
            var hud = window.PreviewWindowForTesting;
            foreach (var edgeCommand in new[]
                     {
                         window.Synthetic.PreviewLeftEdgeCommand,
                         window.Synthetic.PreviewRightEdgeCommand,
                         window.Synthetic.PreviewTopEdgeCommand,
                         window.Synthetic.PreviewBottomEdgeCommand
                     })
            {
                edgeCommand.ExecuteAsync().GetAwaiter().GetResult();
                window.Synthetic.ExpandCommand.ExecuteAsync()
                    .GetAwaiter().GetResult();

                var hudWidth = hud.ActualWidth > 0 ? hud.ActualWidth : hud.Width;
                var hudHeight = hud.ActualHeight > 0 ? hud.ActualHeight : hud.Height;
                var expectedLeft = workArea.Left + ((workArea.Width - hudWidth) / 2);
                var expectedTop = workArea.Top + ((workArea.Height - hudHeight) / 2);
                Assert.InRange(hud.Left, expectedLeft - 0.5, expectedLeft + 0.5);
                Assert.InRange(hud.Top, expectedTop - 0.5, expectedTop + 0.5);
            }
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void Closing_TwoRequestsRunCoordinatorOnceAndFinalCloseIsAllowedOnce()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var window = CreateWindow(temporary, out var dialog);
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();
            window.Close();
            PumpUntil(() => closed);

            Assert.Equal(1, window.CloseCoordinatorRequestCountForTesting);
            Assert.Equal(0, dialog.ShowCount);
            Assert.True(window.FinalCloseAllowedForTesting);
            Assert.True(window.PreviewDisposedForTesting);
        });
    }

    [Fact]
    public void ConcurrentNamedSaveAndClosing_UsesOneGateAndKeepsWindowOpen()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var files = new BlockingNamedSaveFileOperations();
            var paths = new SkinStoragePaths(temporary.Path);
            var store = new DraftStore(paths, files);
            var window = CreateWindow(
                temporary,
                out var dialog,
                draftStore: store);
            Assert.True(window.Editor.BasicInformation
                .SetDisplayName("Blocked named save").Succeeded);
            window.AttachPreviewOwnerForTesting();

            Assert.IsType<Button>(window.FindName("SaveDraftButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => files.NamedWriteStarted);
            window.Close();

            var remainedOpen = window.IsLoaded;
            var dialogCount = dialog.ShowCount;
            var status = Assert.IsType<TextBlock>(
                window.FindName("DocumentStatusText")).Text;
            files.ReleaseNamedWrite();
            PumpUntil(() =>
                files.NamedWriteCompleted &&
                window.SaveOperationForTesting.IsCompleted);
            Assert.True(window.SaveOperationForTesting.IsCompletedSuccessfully);
            DrainDispatcher();
            window.DisposeWithoutShowingForTesting();

            Assert.True(remainedOpen);
            Assert.Equal(0, dialogCount);
            Assert.Contains(
                "operation",
                status,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("NewDraftButton")]
    [InlineData("OpenDraftButton")]
    [InlineData("EditInstalledButton")]
    [InlineData("ImportForEditingButton")]
    public void BlockedNamedSave_PreventsEveryDocumentReplacementUntilGateReleases(
        string buttonName)
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var paths = new SkinStoragePaths(temporary.Path);
            var files = new BlockingNamedSaveFileOperations();
            var requests = PrepareSuccessfulDocumentRequest(
                temporary,
                paths,
                buttonName);
            var documents = CreateDocumentService(paths);
            var replacement = new RecordingDesignerWindow();
            var window = CreateWindow(
                temporary,
                out _,
                draftStore: new DraftStore(paths, files),
                documents: documents,
                requests: requests,
                createReplacementWindow: _ => replacement);
            Assert.True(window.Editor.BasicInformation
                .SetDisplayName("Blocked document replacement").Succeeded);
            var current = window.Editor.Current;
            var assets = window.Editor.Assets;
            window.AttachPreviewOwnerForTesting();

            Assert.IsType<Button>(window.FindName("SaveDraftButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => files.NamedWriteStarted);
            Assert.IsType<Button>(window.FindName(buttonName))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DrainDispatcher();

            Assert.True(window.IsLoaded);
            Assert.False(window.PreviewDisposedForTesting);
            Assert.Same(current, window.Editor.Current);
            Assert.Same(assets, window.Editor.Assets);
            Assert.Equal(0, replacement.ShowCount);
            Assert.Contains(
                "operation",
                Assert.IsType<TextBlock>(
                    window.FindName("DocumentStatusText")).Text,
                StringComparison.OrdinalIgnoreCase);

            files.ReleaseNamedWrite();
            PumpUntil(() => window.SaveOperationForTesting.IsCompleted);
            Assert.True(window.SaveOperationForTesting.IsCompletedSuccessfully);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void UnsavedCancel_PreservesExactCurrentWindowSessionAndAssets()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var replacement = new RecordingDesignerWindow();
            var window = CreateWindow(
                temporary,
                out var dialog,
                UnsavedCloseChoice.Cancel,
                createReplacementWindow: _ => replacement);
            Assert.True(window.Editor.BasicInformation
                .SetDisplayName("Keep this exact document").Succeeded);
            var current = window.Editor.Current;
            var assets = window.Editor.Assets;
            window.AttachPreviewOwnerForTesting();

            Assert.IsType<Button>(window.FindName("NewDraftButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() =>
                window.DocumentOperationForTesting.IsCompleted &&
                window.IsEnabled);

            Assert.True(window.DocumentOperationForTesting.IsCompletedSuccessfully);
            Assert.True(window.IsLoaded);
            Assert.False(window.PreviewDisposedForTesting);
            Assert.Same(current, window.Editor.Current);
            Assert.Same(assets, window.Editor.Assets);
            Assert.Equal(1, dialog.ShowCount);
            Assert.Equal(0, replacement.ShowCount);
            Assert.Contains(
                "cancelled",
                Assert.IsType<TextBlock>(
                    window.FindName("DocumentStatusText")).Text,
                StringComparison.OrdinalIgnoreCase);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Theory]
    [InlineData(UnsavedCloseChoice.Save)]
    [InlineData(UnsavedCloseChoice.Discard)]
    public void UnsavedSaveOrDiscard_ReleasesGateBeforeSuccessfulReplacement(
        UnsavedCloseChoice choice)
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var paths = new SkinStoragePaths(temporary.Path);
            var store = new DraftStore(paths);
            var replacement = new RecordingDesignerWindow();
            var window = CreateWindow(
                temporary,
                out var dialog,
                choice,
                draftStore: store,
                createReplacementWindow: _ => replacement);
            Assert.True(window.Editor.BasicInformation
                .SetDisplayName("Replace after unsaved decision").Succeeded);
            store.SaveRecoveryAsync(window.Editor.Current)
                .GetAwaiter().GetResult();
            var project = new DraftProjectPaths(
                paths.DraftsRoot,
                window.Editor.Current.DraftId);
            window.AttachPreviewOwnerForTesting();

            Assert.IsType<Button>(window.FindName("NewDraftButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.DocumentOperationForTesting.IsCompleted);
            DrainDispatcher();

            Assert.True(window.DocumentOperationForTesting.IsCompletedSuccessfully);
            Assert.Equal(1, replacement.ShowCount);
            Assert.False(window.IsLoaded);
            Assert.True(window.PreviewDisposedForTesting);
            Assert.Equal(1, dialog.ShowCount);
            if (choice == UnsavedCloseChoice.Save)
            {
                Assert.True(File.Exists(project.NamedDraftPath));
            }
            else
            {
                Assert.False(File.Exists(project.RecoveryPath));
            }
        });
    }

    [Fact]
    public void SuccessfulReplacement_PromotesLoadedEditorAsOutputDialogOwner()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var owner = new DesignerWindowOwner();
            var window = CreateWindow(
                temporary,
                out _,
                outputWindowOwner: owner);
            window.AttachPreviewOwnerForTesting();
            Assert.Same(window, owner.Current);

            Assert.IsType<Button>(window.FindName("NewDraftButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => window.DocumentOperationForTesting.IsCompleted);
            DrainDispatcher();

            Assert.False(window.IsLoaded);
            var replacement = Assert.IsType<MainWindow>(owner.Current);
            Assert.NotSame(window, replacement);
            Assert.True(replacement.IsLoaded);
            replacement.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void UnsavedDiscardError_KeepsCurrentDocumentAndShowsActionableStatus()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var replacement = new RecordingDesignerWindow();
            var window = CreateWindow(
                temporary,
                out _,
                UnsavedCloseChoice.Discard,
                createReplacementWindow: _ => replacement);
            Assert.True(window.Editor.BasicInformation
                .SetDisplayName("Recovery must remain safe").Succeeded);
            var current = window.Editor.Current;
            var project = new DraftProjectPaths(
                new SkinStoragePaths(temporary.Path).DraftsRoot,
                current.DraftId);
            Directory.CreateDirectory(project.ProjectRoot);
            File.WriteAllBytes(project.RecoveryPath, "{broken"u8.ToArray());
            window.AttachPreviewOwnerForTesting();

            Assert.IsType<Button>(window.FindName("NewDraftButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() =>
                window.DocumentOperationForTesting.IsCompleted &&
                window.IsEnabled);

            Assert.True(window.DocumentOperationForTesting.IsCompletedSuccessfully);
            Assert.True(window.IsLoaded);
            Assert.Same(current, window.Editor.Current);
            Assert.Equal(0, replacement.ShowCount);
            var status = Assert.IsType<TextBlock>(
                window.FindName("DocumentStatusText")).Text;
            Assert.Contains("recovery", status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("corrupt", status, StringComparison.OrdinalIgnoreCase);
            window.DisposeWithoutShowingForTesting();
        });
    }

    [Fact]
    public void CorruptRecoveryDiscard_ShowsActionableWarningAndKeepsRealWindowOpen()
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var paths = new SkinStoragePaths(temporary.Path);
            var store = new DraftStore(paths);
            var recovery = new DraftRecoveryService(store);
            var window = CreateWindow(
                temporary,
                out _,
                UnsavedCloseChoice.Discard,
                draftStore: store,
                recovery: recovery);
            Assert.True(window.Editor.BasicInformation
                .SetDisplayName("Unsaved change").Succeeded);
            recovery.FlushAsync().GetAwaiter().GetResult();
            var project = new DraftProjectPaths(
                paths.DraftsRoot,
                window.Editor.Current.DraftId);
            var settled = DraftJsonCodec.Parse(
                File.ReadAllBytes(project.RecoveryPath));
            Assert.True(settled.IsValid);
            Assert.NotNull(settled.Value);
            Assert.Equal(window.Editor.Current.Revision, settled.Value.Revision);

            var corruptBytes = "{broken"u8.ToArray();
            File.WriteAllBytes(project.RecoveryPath, corruptBytes);
            Assert.Equal(corruptBytes, File.ReadAllBytes(project.RecoveryPath));
            window.AttachPreviewOwnerForTesting();

            window.Close();
            PumpUntil(() =>
                window.CloseOperationForTesting.IsCompleted &&
                window.IsEnabled);
            Assert.True(window.CloseOperationForTesting.IsCompletedSuccessfully);

            var remainedOpen = window.IsLoaded;
            var status = Assert.IsType<TextBlock>(
                window.FindName("DocumentStatusText")).Text;
            window.DisposeWithoutShowingForTesting();

            Assert.True(remainedOpen);
            Assert.Contains("recovery", status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("corrupt", status, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(UnsavedCloseChoice.Save)]
    [InlineData(UnsavedCloseChoice.Discard)]
    [InlineData(UnsavedCloseChoice.Cancel)]
    public void RealWindowClosing_ExecutesSaveDiscardCancelMatrix(
        UnsavedCloseChoice choice)
    {
        RunSta(() =>
        {
            using var temporary = new TemporaryDirectory();
            var paths = new SkinStoragePaths(temporary.Path);
            var store = new DraftStore(paths);
            var window = CreateWindow(temporary, out var dialog, choice);
            Assert.True(window.Editor.BasicInformation
                .SetDisplayName("Closing matrix").Succeeded);
            store.SaveRecoveryAsync(window.Editor.Current)
                .GetAwaiter().GetResult();
            var project = new DraftProjectPaths(
                paths.DraftsRoot,
                window.Editor.Current.DraftId);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.AttachPreviewOwnerForTesting();

            window.Close();
            PumpUntil(() => choice == UnsavedCloseChoice.Cancel
                ? dialog.ShowCount == 1 && window.IsEnabled
                : closed);

            var namedExists = File.Exists(project.NamedDraftPath);
            var recoveryExists = File.Exists(project.RecoveryPath);
            var remainedOpen = window.IsLoaded;
            if (choice == UnsavedCloseChoice.Cancel)
            {
                window.DisposeWithoutShowingForTesting();
            }

            Assert.Equal(1, dialog.ShowCount);
            switch (choice)
            {
                case UnsavedCloseChoice.Save:
                    Assert.True(closed);
                    Assert.True(namedExists);
                    break;
                case UnsavedCloseChoice.Discard:
                    Assert.True(closed);
                    Assert.False(recoveryExists);
                    break;
                default:
                    Assert.True(remainedOpen);
                    Assert.True(recoveryExists);
                    break;
            }
        });
    }

    private static MainWindow CreateWindow(
        TemporaryDirectory temporary,
        out RecordingDialog dialog,
        UnsavedCloseChoice choice = UnsavedCloseChoice.Cancel,
        DraftStore? draftStore = null,
        DraftRecoveryService? recovery = null,
        DesignerDocumentService? documents = null,
        IDesignerDocumentRequestSource? requests = null,
        Func<DesignerDocumentResult, IDesignerWindow>? createReplacementWindow = null,
        Action<Action>? systemEventDispatcherPost = null,
        DesignerWindowOwner? outputWindowOwner = null)
    {
        var paths = new SkinStoragePaths(temporary.Path);
        var draft = SkinDraftFactory.CreateNew(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            SemanticVersion.Parse("1.1.1"));
        dialog = new RecordingDialog(choice);
        documents ??= CreateDocumentService(paths);
        return new MainWindow(
            draft,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            paths,
            dialog,
            documents,
            requests ?? new RecordingDocumentRequests(),
            createReplacementWindow,
            draftStore: draftStore,
            recovery: recovery,
            systemEventDispatcherPost: systemEventDispatcherPost,
            outputWindowOwner: outputWindowOwner);
    }

    private static DesignerDocumentService CreateDocumentService(
        SkinStoragePaths paths) => new(
            paths,
            new DraftStore(paths),
            new InstalledSkinCatalog(
                paths,
                SemanticVersion.Parse("1.1.1")),
            new CodexQuotaHud.Skins.Packaging.SkinPackageReader());

    private static RecordingDocumentRequests PrepareSuccessfulDocumentRequest(
        TemporaryDirectory temporary,
        SkinStoragePaths paths,
        string buttonName)
    {
        if (buttonName == "OpenDraftButton")
        {
            var draft = SkinDraftFactory.CreateNew(
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                DateTimeOffset.Parse("2026-08-02T02:00:00Z"),
                SemanticVersion.Parse("1.1.1"));
            new DraftStore(paths).SaveNamedAsync(draft)
                .GetAwaiter().GetResult();
            return new RecordingDocumentRequests { DraftId = draft.DraftId };
        }

        if (buttonName == "EditInstalledButton")
        {
            var skinId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
            var package = BuildPackage(temporary, skinId);
            ExtractInstalled(package, paths, skinId);
            return new RecordingDocumentRequests
            {
                InstalledSelectionKey = $"custom:{skinId:D}"
            };
        }

        if (buttonName == "ImportForEditingButton")
        {
            return new RecordingDocumentRequests
            {
                PackagePath = BuildPackage(
                    temporary,
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"))
            };
        }

        return new RecordingDocumentRequests();
    }

    private static string BuildPackage(
        TemporaryDirectory temporary,
        Guid skinId)
    {
        var packagePath = System.IO.Path.Combine(
            temporary.Path,
            $"{skinId:N}.cqskin");
        var draft = SkinDraftFactory.CreateNew(
            Guid.NewGuid(),
            skinId,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            SemanticVersion.Parse("1.1.1"));
        var manifest = new SkinManifest(
            SkinPackageLimits.SchemaVersion,
            skinId,
            "Document operation fixture",
            "Fixture Author",
            SemanticVersion.Parse("1.2.3"),
            "Fixture description",
            draft.Theme.TemplateId,
            SemanticVersion.Parse("1.1.1"),
            OriginSkinId: null,
            Assets: []);
        var result = new CodexQuotaHud.Skins.Packaging.SkinPackageWriter()
            .WriteFile(
                packagePath,
                new CodexQuotaHud.Skins.Packaging.SkinPackageBuildRequest(
                    manifest,
                    draft.Theme,
                    new Dictionary<SkinAssetSlot, SkinAsset>()),
                overwrite: false,
                CancellationToken.None);
        Assert.True(
            result.IsValid,
            string.Join("; ", result.Errors.Select(error => error.Code)));
        return packagePath;
    }

    private static void ExtractInstalled(
        string packagePath,
        SkinStoragePaths paths,
        Guid skinId)
    {
        var destination = System.IO.Path.Combine(
            paths.InstalledSkinsRoot,
            skinId.ToString("D"));
        Directory.CreateDirectory(destination);
        using var package = ZipFile.OpenRead(packagePath);
        foreach (var entry in package.Entries)
        {
            var target = System.IO.Path.Combine(
                destination,
                entry.FullName.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }
    }

    private static SkinDraftDocument CreateDraft() =>
        SkinDraftFactory.CreateNew(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            SemanticVersion.Parse("1.1.1"));

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

    private static void PumpUntil(Func<bool> completed)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromSeconds(5),
            DispatcherPriority.Send,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        void Check()
        {
            if (completed())
            {
                frame.Continue = false;
                return;
            }

            Dispatcher.CurrentDispatcher.BeginInvoke(Check);
        }

        Dispatcher.CurrentDispatcher.BeginInvoke(Check);
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(completed(), "The close lifecycle did not complete.");
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void AssertFullyRenderedWithin(
        FrameworkElement element,
        FrameworkElement ancestor)
    {
        Assert.True(element.IsVisible);
        Assert.NotNull(PresentationSource.FromVisual(element));
        Assert.True(element.ActualWidth > 0);
        Assert.True(element.ActualHeight > 0);
        Assert.Null(element.Clip);
        var bounds = element.TransformToAncestor(ancestor).TransformBounds(
            new Rect(element.RenderSize));
        var viewport = new Rect(ancestor.RenderSize);
        const double tolerance = 0.5;
        Assert.True(bounds.Left >= viewport.Left - tolerance);
        Assert.True(bounds.Top >= viewport.Top - tolerance);
        Assert.True(bounds.Right <= viewport.Right + tolerance);
        Assert.True(bounds.Bottom <= viewport.Bottom + tolerance);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel)
            {
                var value = channel / 255d;
                return value <= 0.04045
                    ? value / 12.92
                    : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Linear(color.R)) +
                (0.7152 * Linear(color.G)) +
                (0.0722 * Linear(color.B));
        }

        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root)
                     .OfType<DependencyObject>())
        {
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

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in VisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class RecordingDialog(
        UnsavedCloseChoice choice = UnsavedCloseChoice.Cancel)
        : IUnsavedChangesDialog
    {
        public int ShowCount { get; private set; }

        public UnsavedCloseChoice Show(SkinDraftDocument draft)
        {
            ShowCount++;
            return choice;
        }
    }

    private sealed class BlockingNamedSaveFileOperations : IDraftFileOperations
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _namedWriteStarted;
        private int _namedWriteCompleted;

        public bool NamedWriteStarted =>
            Volatile.Read(ref _namedWriteStarted) != 0;

        public bool NamedWriteCompleted =>
            Volatile.Read(ref _namedWriteCompleted) != 0;

        public void ReleaseNamedWrite() => _release.TrySetResult();

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool FileExists(string path) => File.Exists(path);

        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

        public IEnumerable<string> EnumerateDirectories(string path) =>
            Directory.EnumerateDirectories(path);

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public async Task WriteAndFlushAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
        {
            if (Path.GetFileName(path).StartsWith(
                    ".draft.json.tmp-",
                    StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref _namedWriteStarted, 1);
                await _release.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            await File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken)
                .ConfigureAwait(false);
        }

        public void ReplaceFile(string sourcePath, string destinationPath)
        {
            File.Move(sourcePath, destinationPath, overwrite: true);
            if (string.Equals(
                    Path.GetFileName(destinationPath),
                    "draft.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref _namedWriteCompleted, 1);
            }
        }

        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class RecordingDocumentRequests : IDesignerDocumentRequestSource
    {
        public Guid? DraftId { get; init; }

        public string? InstalledSelectionKey { get; init; }

        public string? PackagePath { get; init; }

        public Guid? SelectDraftId(Window owner) => DraftId;

        public string? SelectInstalledSelectionKey(Window owner) =>
            InstalledSelectionKey;

        public string? SelectPackagePath(Window owner) => PackagePath;
    }

    private sealed class RecordingDesignerWindow : IDesignerWindow
    {
        public int ShowCount { get; private set; }

        public void Show() => ShowCount++;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingMonitorSource(DesignerMonitorMetrics metrics) :
        IDesignerMonitorWorkAreaSource
    {
        public DesignerMonitorMetrics Metrics { get; set; } = metrics;

        public int CallCount { get; private set; }

        public Window? LastWindow { get; private set; }

        public IntPtr LastHandle { get; private set; }

        public DesignerMonitorMetrics GetCurrent(Window window)
        {
            CallCount++;
            LastWindow = window;
            LastHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            return Metrics;
        }

        public void Reset()
        {
            CallCount = 0;
            LastWindow = null;
            LastHandle = IntPtr.Zero;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task14-window-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
