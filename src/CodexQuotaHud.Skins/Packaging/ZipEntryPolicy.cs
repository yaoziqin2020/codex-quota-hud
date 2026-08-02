using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Packaging;

internal static class ZipEntryPolicy
{
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint LocalHeaderSignature = 0x04034b50;
    private const ushort EncryptionFlags = 0x2041;
    private const ushort StoredCompression = 0;
    private const ushort DeflateCompression = 8;
    private const int MaximumEndRecordBytes = ushort.MaxValue + 22;
    private const int DosDirectoryAttribute = 0x10;
    private const int DosDeviceAttribute = 0x40;
    private const int WindowsReparseAttribute = 0x400;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixRegularFile = 0x8000;
    private const string SyntheticRoot = @"C:\cqskin-validation-root";

    private static readonly string[] ForbiddenExtensions =
        [".exe", ".dll", ".xaml", ".js", ".ps1"];

    public static SkinValidationResult<SafeZipCatalog> Validate(
        Stream package,
        long packageLength,
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = archive.Entries.ToArray();
        if (entries.Length > SkinPackageLimits.MaximumEntries)
        {
            return Invalid(
                "archive.entry-count",
                "$archive",
                "The archive contains too many entries.");
        }

        var metadata = ReadCentralDirectory(
            package,
            packageLength,
            cancellationToken);
        if (metadata.Count != entries.Length)
        {
            throw new InvalidDataException(
                "ZIP entry metadata is inconsistent.");
        }

        var safeEntries = new List<SafeZipEntry>(entries.Length);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredExtractedBytes = 0;

        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var entryMetadata = metadata[index];
            var location = $"$archive.entries[{index}]";

            if (((entryMetadata.CentralFlags | entryMetadata.LocalFlags) &
                    EncryptionFlags) != 0)
            {
                return Invalid(
                    "archive.entry.encrypted",
                    location,
                    "Encrypted archive entries are not supported.");
            }

            if (!IsSupportedCompression(entryMetadata.CentralCompression) ||
                !IsSupportedCompression(entryMetadata.LocalCompression) ||
                entryMetadata.CentralCompression !=
                    entryMetadata.LocalCompression)
            {
                return Invalid(
                    "archive.compression.unsupported",
                    location,
                    "The archive uses an unsupported compression method.");
            }

            if (!IsRegular(entry.FullName, entryMetadata.ExternalAttributes))
            {
                return Invalid(
                    "archive.entry.not-regular",
                    location,
                    "Only regular files are allowed in a skin archive.");
            }

            var pathResult = NormalizePath(entry.FullName, location);
            if (!pathResult.IsValid)
            {
                return new SkinValidationResult<SafeZipCatalog>(
                    null,
                    pathResult.Errors);
            }

            var normalizedName = pathResult.Value!;
            if (!names.Add(normalizedName))
            {
                return Invalid(
                    "archive.path.duplicate",
                    location,
                    "Archive entry names must be unique.");
            }

            if (ForbiddenExtensions.Any(extension =>
                    normalizedName.EndsWith(
                        extension,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return Invalid(
                    "archive.file.forbidden",
                    location,
                    "The archive contains a forbidden file type.");
            }

            declaredExtractedBytes = checked(
                declaredExtractedBytes + entry.Length);
            if (declaredExtractedBytes >
                SkinPackageLimits.MaximumExtractedBytes)
            {
                return Invalid(
                    "archive.extracted-size",
                    "$archive",
                    "The archive expands beyond the supported limit.");
            }

            if (IsImagePath(normalizedName) &&
                entry.Length > SkinPackageLimits.MaximumImageBytes)
            {
                return Invalid(
                    "archive.entry-size",
                    location,
                    "An image entry exceeds the supported size.");
            }

            safeEntries.Add(new SafeZipEntry(
                index,
                normalizedName,
                entry));
        }

        return new SkinValidationResult<SafeZipCatalog>(
            new SafeZipCatalog(safeEntries),
            []);
    }

    private static IReadOnlyList<CentralEntryMetadata> ReadCentralDirectory(
        Stream package,
        long packageLength,
        CancellationToken cancellationToken)
    {
        var tailLength = checked((int)Math.Min(
            packageLength,
            MaximumEndRecordBytes));
        var tail = new byte[tailLength];
        package.Position = packageLength - tailLength;
        ReadExactly(package, tail, cancellationToken);

        var endOffset = -1;
        for (var index = tail.Length - 22; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    tail.AsSpan(index, 4)) !=
                EndOfCentralDirectorySignature)
            {
                continue;
            }

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(index + 20, 2));
            if (index + 22 + commentLength == tail.Length)
            {
                endOffset = index;
                break;
            }
        }

        if (endOffset < 0)
        {
            throw new InvalidDataException(
                "ZIP end-of-central-directory record is missing.");
        }

        var endRecord = tail.AsSpan(endOffset, 22);
        var diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(
            endRecord.Slice(4, 2));
        var centralDisk = BinaryPrimitives.ReadUInt16LittleEndian(
            endRecord.Slice(6, 2));
        var diskEntries = BinaryPrimitives.ReadUInt16LittleEndian(
            endRecord.Slice(8, 2));
        var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(
            endRecord.Slice(10, 2));
        var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(
            endRecord.Slice(12, 4));
        var centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            endRecord.Slice(16, 4));
        if (diskNumber != 0 ||
            centralDisk != 0 ||
            diskEntries != totalEntries ||
            totalEntries == ushort.MaxValue ||
            centralSize == uint.MaxValue ||
            centralOffset == uint.MaxValue ||
            (long)centralOffset + centralSize > packageLength)
        {
            throw new InvalidDataException(
                "Multi-disk and ZIP64 archives are not supported.");
        }

        var result = new List<CentralEntryMetadata>(totalEntries);
        var position = (long)centralOffset;
        var centralHeaderBytes = new byte[46];
        var localHeaderBytes = new byte[30];
        for (var index = 0; index < totalEntries; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var centralHeader = centralHeaderBytes.AsSpan();
            package.Position = position;
            ReadExactly(package, centralHeader, cancellationToken);
            if (BinaryPrimitives.ReadUInt32LittleEndian(centralHeader) !=
                CentralDirectorySignature)
            {
                throw new InvalidDataException(
                    "ZIP central directory is malformed.");
            }

            var centralFlags = BinaryPrimitives.ReadUInt16LittleEndian(
                centralHeader.Slice(8, 2));
            var centralCompression = BinaryPrimitives.ReadUInt16LittleEndian(
                centralHeader.Slice(10, 2));
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                centralHeader.Slice(28, 2));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                centralHeader.Slice(30, 2));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                centralHeader.Slice(32, 2));
            var diskStart = BinaryPrimitives.ReadUInt16LittleEndian(
                centralHeader.Slice(34, 2));
            var externalAttributes = BinaryPrimitives.ReadUInt32LittleEndian(
                centralHeader.Slice(38, 4));
            var localOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                centralHeader.Slice(42, 4));
            if (diskStart != 0 || localOffset == uint.MaxValue)
            {
                throw new InvalidDataException(
                    "ZIP entry metadata is unsupported.");
            }

            var localHeader = localHeaderBytes.AsSpan();
            package.Position = localOffset;
            ReadExactly(package, localHeader, cancellationToken);
            if (BinaryPrimitives.ReadUInt32LittleEndian(localHeader) !=
                LocalHeaderSignature)
            {
                throw new InvalidDataException(
                    "ZIP local header is malformed.");
            }

            result.Add(new CentralEntryMetadata(
                centralFlags,
                centralCompression,
                BinaryPrimitives.ReadUInt16LittleEndian(
                    localHeader.Slice(6, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    localHeader.Slice(8, 2)),
                externalAttributes));

            position = checked(
                position + 46 + nameLength + extraLength + commentLength);
        }

        if (position != (long)centralOffset + centralSize)
        {
            throw new InvalidDataException(
                "ZIP central directory size is inconsistent.");
        }

        return result;
    }

    private static SkinValidationResult<string> NormalizePath(
        string name,
        string location)
    {
        if (name.Contains('\\'))
        {
            return InvalidPath(
                "archive.path.separator",
                location,
                "Archive paths must use forward slashes.");
        }

        if (name.StartsWith("/", StringComparison.Ordinal) ||
            name.StartsWith("//", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(name))
        {
            return InvalidPath(
                "archive.path.absolute",
                location,
                "Absolute archive paths are not allowed.");
        }

        var normalized = name.Normalize(NormalizationForm.FormC);
        var segments = normalized.Split('/');
        if (segments.Any(segment => segment is "." or "..") ||
            normalized.IndexOf('\0') >= 0)
        {
            return InvalidPath(
                "archive.path.traversal",
                location,
                "Archive paths must remain within the package root.");
        }

        var combined = Path.GetFullPath(Path.Combine(
            SyntheticRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = SyntheticRoot + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return InvalidPath(
                "archive.path.traversal",
                location,
                "Archive paths must remain within the package root.");
        }

        return new SkinValidationResult<string>(normalized, []);
    }

    private static bool IsRegular(string name, uint externalAttributes)
    {
        if (name.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var dosAttributes = (int)(externalAttributes & 0xFFFF);
        if ((dosAttributes & (DosDirectoryAttribute |
                DosDeviceAttribute |
                WindowsReparseAttribute)) != 0)
        {
            return false;
        }

        var unixType = (int)((externalAttributes >> 16) & UnixFileTypeMask);
        return unixType == 0 || unixType == UnixRegularFile;
    }

    private static bool IsSupportedCompression(ushort method) =>
        method is StoredCompression or DeflateCompression;

    private static bool IsImagePath(string path) =>
        path.StartsWith(
            SkinPackageLimits.AssetsDirectoryName,
            StringComparison.OrdinalIgnoreCase) &&
        (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

    private static void ReadExactly(
        Stream stream,
        Span<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer[totalRead..]);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                throw new InvalidDataException(
                    "ZIP metadata ended unexpectedly.");
            }

            totalRead += read;
        }
    }

    private static SkinValidationResult<SafeZipCatalog> Invalid(
        string code,
        string location,
        string message) =>
        new(
            null,
            [new SkinValidationError(code, location, message)]);

    private static SkinValidationResult<string> InvalidPath(
        string code,
        string location,
        string message) =>
        new(
            null,
            [new SkinValidationError(code, location, message)]);

    private sealed record CentralEntryMetadata(
        ushort CentralFlags,
        ushort CentralCompression,
        ushort LocalFlags,
        ushort LocalCompression,
        uint ExternalAttributes);
}

internal sealed record SafeZipEntry(
    int Index,
    string NormalizedName,
    ZipArchiveEntry Entry);

internal sealed class SafeZipCatalog
{
    private readonly Dictionary<string, SafeZipEntry> _byName;

    public SafeZipCatalog(IReadOnlyList<SafeZipEntry> entries)
    {
        Entries = entries;
        _byName = entries.ToDictionary(
            entry => entry.NormalizedName,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<SafeZipEntry> Entries { get; }

    public bool TryGet(string name, out SafeZipEntry entry) =>
        _byName.TryGetValue(name, out entry!);
}
