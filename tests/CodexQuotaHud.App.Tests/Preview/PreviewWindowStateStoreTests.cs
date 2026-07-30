using CodexQuotaHud.App.Preview;

namespace CodexQuotaHud.App.Tests.Preview;

public sealed class PreviewWindowStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "CodexQuotaHud-PreviewWindow",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultPathAndState_ArePreviewSpecific()
    {
        var store = new PreviewWindowStateStore(
            @"C:\Users\Test\AppData\Local");

        Assert.Equal(
            @"C:\Users\Test\AppData\Local\CodexQuotaHud\preview-window.json",
            store.StatePath);
        Assert.Equal(380, PreviewWindowState.Default.Width);
        Assert.Equal(650, PreviewWindowState.Default.Height);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOnlyPreviewGeometry()
    {
        var store = new PreviewWindowStateStore(_root);
        var expected = new PreviewWindowState(120, 80, 440, 720);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.False(File.Exists(Path.Combine(
            _root, "CodexQuotaHud", "settings.json")));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""{"Left":0,"Top":0,"Width":339,"Height":650}""")]
    [InlineData("""{"Left":0,"Top":0,"Width":380,"Height":519}""")]
    public void Load_InvalidDataFallsBackToDefault(string json)
    {
        var store = new PreviewWindowStateStore(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(store.StatePath)!);
        File.WriteAllText(store.StatePath, json);

        Assert.Equal(PreviewWindowState.Default, store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
