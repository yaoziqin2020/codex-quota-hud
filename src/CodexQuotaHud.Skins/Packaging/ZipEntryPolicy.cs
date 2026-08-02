using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Packaging;

internal static class ZipEntryPolicy
{
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint LocalHeaderSignature = 0x04034b50;
    private const ushort EncryptionFlags = 0x2041;
    private const ushort DataDescriptorFlag = 0x0008;
    private const ushort Utf8NameFlag = 0x0800;
    private const ushort StoredCompression = 0;
    private const ushort DeflateCompression = 8;
    private const ushort Zip64ExtraFieldId = 0x0001;
    private const int MaximumEndRecordBytes = ushort.MaxValue + 22 + 20;
    private const int DosDirectoryAttribute = 0x10;
    private const int DosDeviceAttribute = 0x40;
    private const int WindowsReparseAttribute = 0x400;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixRegularFile = 0x8000;
    private const string SyntheticRoot = @"C:\cqskin-validation-root";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly string[] ForbiddenExtensions =
        [".exe", ".dll", ".xaml", ".js", ".ps1"];

    public static ZipPreflight Preflight(
        Stream package,
        long packageLength,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endRecord = ReadEndRecord(
            package,
            packageLength,
            cancellationToken);

        if (endRecord.IsZip64)
        {
            throw Violation(
                "archive.zip64.unsupported",
                "$archive",
                "ZIP64 skin archives are not supported.");
        }

        if (endRecord.TotalEntries > SkinPackageLimits.MaximumEntries)
        {
            throw Violation(
                "archive.entry-count",
                "$archive",
                "The archive contains too many entries.");
        }

        var entries = ReadCentralDirectory(
            package,
            endRecord,
            cancellationToken);
        return new ZipPreflight(entries);
    }

    public static SafeZipCatalog BindArchive(
        ZipPreflight preflight,
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var archiveEntries = archive.Entries;
        if (archiveEntries.Count != preflight.Entries.Count)
        {
            throw new InvalidDataException(
                "ZIP entry metadata is inconsistent.");
        }

        var safeEntries = new List<SafeZipEntry>(preflight.Entries.Count);
        for (var index = 0; index < preflight.Entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = preflight.Entries[index];
            var archiveEntry = archiveEntries[index];
            if (!string.Equals(
                    archiveEntry.FullName,
                    raw.CentralName,
                    StringComparison.Ordinal) ||
                archiveEntry.Length != raw.DeclaredUncompressedSize ||
                archiveEntry.CompressedLength != raw.CompressedSize)
            {
                throw Violation(
                    "archive.header.mismatch",
                    $"$archive.entries[{index}]",
                    "ZIP entry headers are inconsistent.");
            }

            safeEntries.Add(new SafeZipEntry(index, raw));
        }

        return new SafeZipCatalog(safeEntries);
    }

    private static EndRecord ReadEndRecord(
        Stream package,
        long packageLength,
        CancellationToken cancellationToken)
    {
        var tailLength = checked((int)Math.Min(
            packageLength,
            MaximumEndRecordBytes));
        var tail = new byte[tailLength];
        var tailOffset = packageLength - tailLength;
        ReadExactlyAt(package, tailOffset, tail, cancellationToken);

        var endOffset = -1;
        for (var index = tail.Length - 22; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

        var absoluteEndOffset = checked(tailOffset + endOffset);
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

        var hasZip64Locator = endOffset >= 20 &&
            BinaryPrimitives.ReadUInt32LittleEndian(
                tail.AsSpan(endOffset - 20, 4)) ==
            Zip64LocatorSignature;
        var isZip64 = hasZip64Locator ||
            diskEntries == ushort.MaxValue ||
            totalEntries == ushort.MaxValue ||
            centralSize == uint.MaxValue ||
            centralOffset == uint.MaxValue;

        if (!isZip64 &&
            (diskNumber != 0 ||
                centralDisk != 0 ||
                diskEntries != totalEntries ||
                (long)centralOffset + centralSize != absoluteEndOffset))
        {
            throw new InvalidDataException(
                "ZIP end-of-central-directory metadata is inconsistent.");
        }

        return new EndRecord(
            totalEntries,
            centralOffset,
            centralSize,
            isZip64);
    }

    private static IReadOnlyList<RawZipEntry> ReadCentralDirectory(
        Stream package,
        EndRecord endRecord,
        CancellationToken cancellationToken)
    {
        if (endRecord.TotalEntries > SkinPackageLimits.MaximumEntries)
        {
            throw Violation(
                "archive.entry-count",
                "$archive",
                "The archive contains too many entries.");
        }

        var result = new List<RawZipEntry>(endRecord.TotalEntries);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranges = new List<EntryRange>(endRecord.TotalEntries);
        var position = (long)endRecord.CentralOffset;
        var centralEnd = checked(position + endRecord.CentralSize);
        var centralHeader = new byte[46];
        var localHeader = new byte[30];
        long declaredExtractedBytes = 0;

        for (var index = 0; index < endRecord.TotalEntries; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = $"$archive.entries[{index}]";
            if (position > centralEnd - centralHeader.Length)
            {
                throw new InvalidDataException(
                    "ZIP central directory is truncated.");
            }

            ReadExactlyAt(
                package,
                position,
                centralHeader,
                cancellationToken);
            var centralSpan = centralHeader.AsSpan();
            if (BinaryPrimitives.ReadUInt32LittleEndian(centralSpan) !=
                CentralDirectorySignature)
            {
                throw new InvalidDataException(
                    "ZIP central directory is malformed.");
            }

            var centralFlags = BinaryPrimitives.ReadUInt16LittleEndian(
                centralSpan.Slice(8, 2));
            var centralCompression = BinaryPrimitives.ReadUInt16LittleEndian(
                centralSpan.Slice(10, 2));
            var centralCrc = BinaryPrimitives.ReadUInt32LittleEndian(
                centralSpan.Slice(16, 4));
            var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                centralSpan.Slice(20, 4));
            var uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                centralSpan.Slice(24, 4));
            var centralNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                centralSpan.Slice(28, 2));
            var centralExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                centralSpan.Slice(30, 2));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                centralSpan.Slice(32, 2));
            var diskStart = BinaryPrimitives.ReadUInt16LittleEndian(
                centralSpan.Slice(34, 2));
            var externalAttributes = BinaryPrimitives.ReadUInt32LittleEndian(
                centralSpan.Slice(38, 4));
            var localOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                centralSpan.Slice(42, 4));
            var centralVariableLength = checked(
                (long)centralNameLength +
                centralExtraLength +
                commentLength);
            if (position + 46 + centralVariableLength > centralEnd)
            {
                throw new InvalidDataException(
                    "ZIP central directory fields exceed their boundary.");
            }

            var centralNameBytes = new byte[centralNameLength];
            ReadExactlyAt(
                package,
                position + 46,
                centralNameBytes,
                cancellationToken);
            var centralExtra = new byte[centralExtraLength];
            ReadExactlyAt(
                package,
                position + 46 + centralNameLength,
                centralExtra,
                cancellationToken);

            if (diskStart != 0 ||
                compressedSize == uint.MaxValue ||
                uncompressedSize == uint.MaxValue ||
                localOffset == uint.MaxValue ||
                ContainsZip64Extra(centralExtra))
            {
                throw Violation(
                    "archive.zip64.unsupported",
                    location,
                    "ZIP64 skin archives are not supported.");
            }

            if (localOffset > endRecord.CentralOffset - localHeader.Length)
            {
                throw new InvalidDataException(
                    "ZIP local header lies outside the data area.");
            }

            ReadExactlyAt(
                package,
                localOffset,
                localHeader,
                cancellationToken);
            var localSpan = localHeader.AsSpan();
            if (BinaryPrimitives.ReadUInt32LittleEndian(localSpan) !=
                LocalHeaderSignature)
            {
                throw new InvalidDataException(
                    "ZIP local header is malformed.");
            }

            var localFlags = BinaryPrimitives.ReadUInt16LittleEndian(
                localSpan.Slice(6, 2));
            var localCompression = BinaryPrimitives.ReadUInt16LittleEndian(
                localSpan.Slice(8, 2));
            var localCrc = BinaryPrimitives.ReadUInt32LittleEndian(
                localSpan.Slice(14, 4));
            var localCompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                localSpan.Slice(18, 4));
            var localUncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                localSpan.Slice(22, 4));
            var localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                localSpan.Slice(26, 2));
            var localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                localSpan.Slice(28, 2));
            var dataOffset = checked(
                (long)localOffset +
                localHeader.Length +
                localNameLength +
                localExtraLength);
            var dataEnd = checked(dataOffset + compressedSize);
            if (dataOffset > endRecord.CentralOffset ||
                dataEnd > endRecord.CentralOffset)
            {
                throw new InvalidDataException(
                    "ZIP entry data exceeds its boundary.");
            }

            var localNameBytes = new byte[localNameLength];
            ReadExactlyAt(
                package,
                (long)localOffset + localHeader.Length,
                localNameBytes,
                cancellationToken);
            var localExtra = new byte[localExtraLength];
            ReadExactlyAt(
                package,
                (long)localOffset + localHeader.Length + localNameLength,
                localExtra,
                cancellationToken);

            if (localCompressedSize == uint.MaxValue ||
                localUncompressedSize == uint.MaxValue ||
                ContainsZip64Extra(localExtra))
            {
                throw Violation(
                    "archive.zip64.unsupported",
                    location,
                    "ZIP64 skin archives are not supported.");
            }

            if (((centralFlags | localFlags) & EncryptionFlags) != 0)
            {
                throw Violation(
                    "archive.entry.encrypted",
                    location,
                    "Encrypted archive entries are not supported.");
            }

            if (!IsSupportedCompression(centralCompression) ||
                !IsSupportedCompression(localCompression))
            {
                throw Violation(
                    "archive.compression.unsupported",
                    location,
                    "The archive uses an unsupported compression method.");
            }

            if (((centralFlags | localFlags) & DataDescriptorFlag) != 0)
            {
                throw Violation(
                    "archive.data-descriptor.unsupported",
                    location,
                    "ZIP data descriptors are not supported.");
            }

            if ((centralFlags & Utf8NameFlag) !=
                    (localFlags & Utf8NameFlag))
            {
                throw Violation(
                    "archive.name.mismatch",
                    location,
                    "Central and local ZIP entry names do not match.");
            }

            var centralName = DecodeName(
                centralNameBytes,
                (centralFlags & Utf8NameFlag) != 0,
                location);
            var localName = DecodeName(
                localNameBytes,
                (localFlags & Utf8NameFlag) != 0,
                location);
            var normalizedCentralName = NormalizePath(
                centralName,
                location);
            var normalizedLocalName = NormalizePath(
                localName,
                location);
            if (!centralNameBytes.AsSpan().SequenceEqual(localNameBytes) ||
                !string.Equals(
                    centralName,
                    localName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    normalizedCentralName,
                    normalizedLocalName,
                    StringComparison.Ordinal))
            {
                throw Violation(
                    "archive.name.mismatch",
                    location,
                    "Central and local ZIP entry names do not match.");
            }

            if (centralFlags != localFlags ||
                centralCompression != localCompression ||
                centralCrc != localCrc ||
                compressedSize != localCompressedSize ||
                uncompressedSize != localUncompressedSize)
            {
                throw Violation(
                    "archive.header.mismatch",
                    location,
                    "Central and local ZIP entry headers do not match.");
            }

            if (!IsRegular(centralName, externalAttributes))
            {
                throw Violation(
                    "archive.entry.not-regular",
                    location,
                    "Only regular files are allowed in a skin archive.");
            }

            if (!names.Add(normalizedCentralName))
            {
                throw Violation(
                    "archive.path.duplicate",
                    location,
                    "Archive entry names must be unique.");
            }

            if (ForbiddenExtensions.Any(extension =>
                    normalizedCentralName.EndsWith(
                        extension,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw Violation(
                    "archive.file.forbidden",
                    location,
                    "The archive contains a forbidden file type.");
            }

            declaredExtractedBytes = checked(
                declaredExtractedBytes + uncompressedSize);
            if (declaredExtractedBytes >
                SkinPackageLimits.MaximumExtractedBytes)
            {
                throw Violation(
                    "archive.extracted-size",
                    "$archive",
                    "The archive expands beyond the supported limit.");
            }

            if (IsImagePath(normalizedCentralName) &&
                uncompressedSize > SkinPackageLimits.MaximumImageBytes)
            {
                throw Violation(
                    "archive.entry-size",
                    location,
                    "An image entry exceeds the supported size.");
            }

            result.Add(new RawZipEntry(
                centralName,
                normalizedCentralName,
                dataOffset,
                compressedSize,
                uncompressedSize,
                centralCompression,
                centralCrc));
            ranges.Add(new EntryRange(localOffset, dataEnd));
            position = checked(position + 46 + centralVariableLength);
        }

        if (position != centralEnd)
        {
            throw new InvalidDataException(
                "ZIP central directory size is inconsistent.");
        }

        var orderedRanges = ranges.OrderBy(range => range.Start).ToArray();
        for (var index = 1; index < orderedRanges.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (orderedRanges[index - 1].End > orderedRanges[index].Start)
            {
                throw new InvalidDataException(
                    "ZIP entry ranges overlap.");
            }
        }

        return result;
    }

    private static string DecodeName(
        byte[] bytes,
        bool isUtf8,
        string location)
    {
        if (!isUtf8 && bytes.Any(value => value >= 0x80))
        {
            throw Violation(
                "archive.name.encoding",
                location,
                "Legacy non-ASCII ZIP entry names are not supported.");
        }

        try
        {
            return isUtf8
                ? StrictUtf8.GetString(bytes)
                : Encoding.ASCII.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new PackageValidationException(
                "archive.name.encoding",
                location,
                "The ZIP entry name encoding is invalid.",
                exception);
        }
    }

    private static string NormalizePath(string name, string location)
    {
        if (name.Contains('\\'))
        {
            throw Violation(
                "archive.path.separator",
                location,
                "Archive paths must use forward slashes.");
        }

        if (name.StartsWith("/", StringComparison.Ordinal) ||
            name.StartsWith("//", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(name))
        {
            throw Violation(
                "archive.path.absolute",
                location,
                "Absolute archive paths are not allowed.");
        }

        var normalized = name.Normalize(NormalizationForm.FormC);
        var segments = normalized.Split('/');
        if (segments.Any(segment => segment is "." or "..") ||
            normalized.IndexOf('\0') >= 0)
        {
            throw Violation(
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
            throw Violation(
                "archive.path.traversal",
                location,
                "Archive paths must remain within the package root.");
        }

        return normalized;
    }

    private static bool ContainsZip64Extra(byte[] extra)
    {
        var position = 0;
        while (position < extra.Length)
        {
            if (extra.Length - position < 4)
            {
                throw new InvalidDataException(
                    "ZIP extra fields are malformed.");
            }

            var identifier = BinaryPrimitives.ReadUInt16LittleEndian(
                extra.AsSpan(position, 2));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(
                extra.AsSpan(position + 2, 2));
            position += 4;
            if (length > extra.Length - position)
            {
                throw new InvalidDataException(
                    "ZIP extra fields exceed their boundary.");
            }

            if (identifier == Zip64ExtraFieldId)
            {
                return true;
            }

            position += length;
        }

        return false;
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

    private static void ReadExactlyAt(
        Stream stream,
        long position,
        Span<byte> buffer,
        CancellationToken cancellationToken)
    {
        stream.Position = position;
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

    private static PackageValidationException Violation(
        string code,
        string location,
        string message) =>
        new(code, location, message);

    private sealed record EndRecord(
        ushort TotalEntries,
        uint CentralOffset,
        uint CentralSize,
        bool IsZip64);

    private sealed record EntryRange(long Start, long End);
}

internal sealed record ZipPreflight(IReadOnlyList<RawZipEntry> Entries);

internal sealed record RawZipEntry(
    string CentralName,
    string NormalizedName,
    long DataOffset,
    long CompressedSize,
    long DeclaredUncompressedSize,
    ushort CompressionMethod,
    uint Crc32);

internal sealed record SafeZipEntry(int Index, RawZipEntry Raw)
{
    public string NormalizedName => Raw.NormalizedName;

    public long DeclaredUncompressedSize => Raw.DeclaredUncompressedSize;

    public uint ExpectedCrc32 => Raw.Crc32;

    public Stream OpenDataStream(Stream package)
    {
        var segment = new ZipSegmentReadStream(
            package,
            Raw.DataOffset,
            Raw.CompressedSize);
        return Raw.CompressionMethod switch
        {
            0 => segment,
            8 => new DeflateStream(
                segment,
                CompressionMode.Decompress,
                leaveOpen: false),
            _ => throw new InvalidDataException(
                "ZIP compression method was not preflighted.")
        };
    }
}

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

internal sealed class ZipSegmentReadStream : Stream
{
    private readonly Stream _package;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public ZipSegmentReadStream(Stream package, long start, long length)
    {
        _package = package;
        _start = start;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException(
                "The buffer range is invalid.",
                nameof(count));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = _length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, remaining);
        _package.Position = checked(_start + _position);
        var read = _package.Read(buffer[..count]);
        _position += read;
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
