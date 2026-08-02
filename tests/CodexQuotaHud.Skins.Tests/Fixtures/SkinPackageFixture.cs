using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Serialization;

namespace CodexQuotaHud.Skins.Tests.Fixtures;

public sealed class SkinPackageFixture : IDisposable
{
    private const string ThemeJson = """
        {"schemaVersion":1,"templateId":"free-decoration-ring","background":{"offsetX":0,"offsetY":0,"scale":1,"rotation":0,"opacity":1,"cropFocusX":0.5,"cropFocusY":0.5},"center":{"offsetX":0,"offsetY":0,"scale":1,"rotation":0,"opacity":1,"cropFocusX":0.5,"cropFocusY":0.5},"decoration":{"offsetX":0,"offsetY":0,"scale":1,"rotation":0,"opacity":1,"cropFocusX":0.5,"cropFocusY":0.5},"primaryRingColor":"#FF53DCF8","secondaryRingColor":"#FF9A68FF","baseBackgroundColor":"#FF0A1622","baseBackgroundOpacity":0.9,"ringDiameter":96,"ringThickness":8,"ringGap":6,"startAngle":270,"glowColor":"#FF24CFF2","glowIntensity":0.5,"numberTextSize":28,"labelTextSize":12,"textWeight":"semiBold","textPlacement":"numberAboveLabel","animation":{"rotationIntensity":0.25,"breathingIntensity":0.5,"glowIntensity":0.75,"floatingIntensity":1}}
        """;

    private static readonly byte[] OneByOnePngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/j//z8DAAj8Av6IXwbgAAAAAElFTkSuQmCC");

    private static readonly byte[] OneByOneJpegBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==");

    private static readonly Lazy<byte[]> MaximumPixelPngBytes = new(
        () => CreateGrayscalePng(
            SkinPackageLimits.MaximumImageDimension,
            SkinPackageLimits.MaximumImageDimension));

    private readonly string _outerDirectory;
    private int _packageNumber;
    private bool _disposed;

    public SkinPackageFixture()
    {
        _outerDirectory = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud.SkinPackageTests",
            Guid.NewGuid().ToString("N"));
        RootDirectory = Path.Combine(_outerDirectory, "root");
        PotentialEscapePath = Path.Combine(_outerDirectory, "escape.png");
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }

    public string PotentialEscapePath { get; }

    public static byte[] OneByOnePng => [.. OneByOnePngBytes];

    public static byte[] OneByOneJpeg => [.. OneByOneJpegBytes];

    public static byte[] MaximumPixelPng => [.. MaximumPixelPngBytes.Value];

    public string CreateValidPackage(params SkinAssetSlot[] slots)
    {
        var assets = slots.Select(DefaultAsset).ToArray();
        return CreatePackage(assets);
    }

    public string CreateDuplicateSlotPackage() => CreatePackage(
        [
            new FixtureAsset(
                SkinAssetSlot.Background,
                "assets/background.png",
                OneByOnePng),
            new FixtureAsset(
                SkinAssetSlot.Background,
                "assets/background.jpg",
                OneByOneJpeg)
        ]);

    public string CreateOversizedPackageFile()
    {
        var packagePath = Path.Combine(
            RootDirectory,
            $"package-{++_packageNumber}.cqskin");
        using var stream = new FileStream(
            packagePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: false);
        WriteEntry(
            archive,
            "payload.bin",
            [],
            repeatedByteCount: SkinPackageLimits.MaximumPackageBytes + 1,
            compressionLevel: CompressionLevel.NoCompression);
        return packagePath;
    }

    public string CreatePackage(
        IReadOnlyList<FixtureAsset>? assets = null,
        IReadOnlyList<FixtureEntry>? additionalEntries = null,
        Func<SkinManifest, SkinManifest>? transformManifest = null)
    {
        assets ??= [];
        additionalEntries ??= [];

        var packagePath = Path.Combine(
            RootDirectory,
            $"package-{++_packageNumber}.cqskin");
        var manifest = new SkinManifest(
            SchemaVersion: 1,
            SkinId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DisplayName: "Ocean",
            Author: "Alice",
            PackageVersion: SemanticVersion.Parse("1.2.3"),
            Description: "Ocean ring",
            TemplateId: SkinPackageLimits.FreeDecorationRingTemplateId,
            MinimumHudVersion: SemanticVersion.Parse("1.1.1"),
            OriginSkinId: null,
            Assets: assets.Select(
                asset => new SkinAssetReference(
                    asset.Slot,
                    asset.Path,
                    Convert.ToHexString(SHA256.HashData(asset.Content))
                        .ToLowerInvariant()))
                .ToArray());
        manifest = transformManifest?.Invoke(manifest) ?? manifest;

        using var stream = new FileStream(
            packagePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: false);

        WriteEntry(
            archive,
            SkinPackageLimits.ManifestFileName,
            SkinJsonCodec.WriteManifest(manifest));
        WriteEntry(
            archive,
            SkinPackageLimits.ThemeFileName,
            System.Text.Encoding.UTF8.GetBytes(ThemeJson));

        foreach (var asset in assets)
        {
            WriteEntry(
                archive,
                asset.Path,
                asset.Content,
                compressionLevel: asset.CompressionLevel);
        }

        foreach (var entry in additionalEntries)
        {
            WriteEntry(
                archive,
                entry.Name,
                entry.Content ?? [],
                entry.ExternalAttributes,
                entry.RepeatedByteCount,
                entry.CompressionLevel);
        }

        return packagePath;
    }

    public void MarkEntryEncrypted(string packagePath, string entryName) =>
        PatchEntryHeaders(
            packagePath,
            entryName,
            encrypted: true,
            compressionMethod: null);

    public void SetEntryCompressionMethod(
        string packagePath,
        string entryName,
        ushort compressionMethod) =>
        PatchEntryHeaders(
            packagePath,
            entryName,
            encrypted: false,
            compressionMethod);

    public static byte[] CreateGrayscalePng(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 0;
        WritePngChunk(png, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(
                   compressed,
                   CompressionLevel.Fastest,
                   leaveOpen: true))
        {
            var scanline = new byte[checked(width + 1)];
            for (var row = 0; row < height; row++)
            {
                zlib.Write(scanline);
            }
        }

        WritePngChunk(png, "IDAT", compressed.ToArray());
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    public void AssertNoEscape() =>
        Assert.False(
            File.Exists(PotentialEscapePath),
            "Archive validation wrote outside its unique temporary root.");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AssertNoEscape();
        if (Directory.Exists(_outerDirectory))
        {
            Directory.Delete(_outerDirectory, recursive: true);
        }
    }

    public sealed record FixtureAsset(
        SkinAssetSlot Slot,
        string Path,
        byte[] Content,
        CompressionLevel CompressionLevel = CompressionLevel.Optimal);

    public sealed record FixtureEntry(
        string Name,
        byte[]? Content = null,
        int? ExternalAttributes = null,
        long RepeatedByteCount = 0,
        CompressionLevel CompressionLevel = CompressionLevel.Optimal);

    private static FixtureAsset DefaultAsset(SkinAssetSlot slot) => slot switch
    {
        SkinAssetSlot.Background => new FixtureAsset(
            slot,
            "assets/background.png",
            OneByOnePng),
        SkinAssetSlot.Center => new FixtureAsset(
            slot,
            "assets/center.jpg",
            OneByOneJpeg),
        SkinAssetSlot.Decoration => new FixtureAsset(
            slot,
            "assets/decoration.png",
            OneByOnePng),
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        byte[] content,
        int? externalAttributes = null,
        long repeatedByteCount = 0,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(name, compressionLevel);
        if (externalAttributes is { } attributes)
        {
            entry.ExternalAttributes = attributes;
        }

        using var entryStream = entry.Open();
        entryStream.Write(content);
        if (repeatedByteCount > 0)
        {
            var buffer = new byte[64 * 1024];
            while (repeatedByteCount > 0)
            {
                var writeCount = (int)Math.Min(buffer.Length, repeatedByteCount);
                entryStream.Write(buffer, 0, writeCount);
                repeatedByteCount -= writeCount;
            }
        }
    }

    private static void PatchEntryHeaders(
        string packagePath,
        string entryName,
        bool encrypted,
        ushort? compressionMethod)
    {
        var bytes = File.ReadAllBytes(packagePath);
        var endOfCentralDirectory = FindSignatureFromEnd(bytes, 0x06054b50);
        Assert.True(endOfCentralDirectory >= 0, "ZIP EOCD was not found.");
        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(endOfCentralDirectory + 10, 2));
        var centralOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(endOfCentralDirectory + 16, 4)));
        var position = centralOffset;
        var patched = false;

        for (var index = 0; index < entryCount; index++)
        {
            Assert.Equal(
                0x02014b50u,
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4)));
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(position + 28, 2));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(position + 30, 2));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(position + 32, 2));
            var name = Encoding.UTF8.GetString(
                bytes,
                position + 46,
                nameLength);
            if (name == entryName)
            {
                var localOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(position + 42, 4)));
                Assert.Equal(
                    0x04034b50u,
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(localOffset, 4)));

                if (encrypted)
                {
                    SetFlag(bytes, position + 8, 0x0001);
                    SetFlag(bytes, localOffset + 6, 0x0001);
                }

                if (compressionMethod is { } method)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(position + 10, 2),
                        method);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localOffset + 8, 2),
                        method);
                }

                patched = true;
            }

            position += 46 + nameLength + extraLength + commentLength;
        }

        Assert.True(patched, $"ZIP entry '{entryName}' was not found.");
        File.WriteAllBytes(packagePath, bytes);
    }

    private static int FindSignatureFromEnd(byte[] bytes, uint signature)
    {
        for (var index = bytes.Length - sizeof(uint); index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(index, sizeof(uint))) == signature)
            {
                return index;
            }
        }

        return -1;
    }

    private static void SetFlag(byte[] bytes, int offset, ushort flag)
    {
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(offset, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(offset, 2),
            (ushort)(flags | flag));
    }

    private static void WritePngChunk(
        Stream destination,
        string chunkType,
        byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        destination.Write(length);

        var type = Encoding.ASCII.GetBytes(chunkType);
        destination.Write(type);
        destination.Write(data);

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(
            checksum,
            ComputePngCrc(type, data));
        destination.Write(checksum);
    }

    private static uint ComputePngCrc(byte[] type, byte[] data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0
                    ? 0xEDB88320u ^ (crc >> 1)
                    : crc >> 1;
            }
        }

        return ~crc;
    }
}
