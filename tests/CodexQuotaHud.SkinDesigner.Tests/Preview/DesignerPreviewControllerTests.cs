using System.Windows.Threading;
using System.Windows.Controls;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.SkinDesigner.Preview;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Templates.FreeDecorationRing;

namespace CodexQuotaHud.SkinDesigner.Tests.Preview;

[Collection(DesignerPreviewWpfCollection.Name)]
public sealed class DesignerPreviewControllerTests
{
    [Fact]
    public void Update_RendersCanonicalBackgroundJpegAndCenterPng()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            var controller = new DesignerPreviewController(composition);
            const string backgroundPath = "assets/background.jpg";
            const string centerPath = "assets/center.png";
            var draft = DraftPreviewDocumentBuilderTests
                .CreateDraftWithImagePaths(backgroundPath, centerPath);
            var assets = new Dictionary<SkinAssetSlot, SkinAsset>
            {
                [SkinAssetSlot.Background] = DraftPreviewDocumentBuilderTests
                    .CreateAsset(SkinAssetSlot.Background, backgroundPath),
                [SkinAssetSlot.Center] = DraftPreviewDocumentBuilderTests
                    .CreateAsset(SkinAssetSlot.Center, centerPath)
            };

            var result = controller.Update(draft, assets);

            Assert.True(result.IsValid,
                DraftPreviewDocumentBuilderTests.Format(result.Errors));
            var package = Assert.IsType<SkinPackageDocument>(result.Value);
            Assert.Collection(
                package.Manifest.Assets,
                background => Assert.Equal(backgroundPath, background.Path),
                center => Assert.Equal(centerPath, center.Path));
            var host = Assert.IsType<ContentControl>(
                composition.HudWindow.FindName("SkinHost"));
            Assert.IsType<FreeDecorationRingRenderer>(host.Content);
        });
    }

    [Fact]
    public void Update_UsesProductionRendererAndKeepsLastGoodOnInvalidDraft()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            var controller = new DesignerPreviewController(composition);
            var draft = DraftPreviewDocumentBuilderTests.CreateDraft();

            var valid = controller.Update(
                draft,
                new Dictionary<SkinAssetSlot, SkinAsset>());

            Assert.True(valid.IsValid,
                DraftPreviewDocumentBuilderTests.Format(valid.Errors));
            var host = Assert.IsType<ContentControl>(
                composition.HudWindow.FindName("SkinHost"));
            var renderer = Assert.IsType<FreeDecorationRingRenderer>(
                host.Content);

            var invalid = controller.Update(
                draft with
                {
                    Theme = draft.Theme with { RingThickness = 17 }
                },
                new Dictionary<SkinAssetSlot, SkinAsset>());

            Assert.False(invalid.IsValid);
            Assert.Same(renderer, host.Content);
        });
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
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesignerPreviewWpfCollection
{
    public const string Name = "Designer Preview WPF tests";
}
