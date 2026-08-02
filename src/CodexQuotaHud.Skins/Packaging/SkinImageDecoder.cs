using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Packaging;

public sealed record SkinDecodedImage(
    int PixelWidth,
    int PixelHeight,
    bool HasAlpha,
    BitmapSource Bitmap);

public static class SkinImageDecoder
{
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static SkinDecodedImage Decode(
        SkinAssetSlot slot,
        string relativePath,
        ReadOnlyMemory<byte> encoded) =>
        Decode(
            slot,
            relativePath,
            encoded,
            SkinPackageLimits.MaximumDecodedPixels);

    internal static SkinDecodedImage Decode(
        SkinAssetSlot slot,
        string relativePath,
        ReadOnlyMemory<byte> encoded,
        long remainingPixelBudget)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(remainingPixelBudget);

        var hasPngSignature = encoded.Span.StartsWith(PngSignature);
        var hasJpegSignature = encoded.Span is [0xFF, 0xD8, ..];
        if (slot == SkinAssetSlot.Decoration && !hasPngSignature)
        {
            throw Error(
                "image.decoration-format",
                "Decoration images must use PNG content.");
        }

        var expectsPng = relativePath.EndsWith(
            ".png",
            StringComparison.Ordinal);
        var expectsJpeg = relativePath.EndsWith(
                ".jpg",
                StringComparison.Ordinal) ||
            relativePath.EndsWith(
                ".jpeg",
                StringComparison.Ordinal);
        if ((!expectsPng && !expectsJpeg) ||
            (expectsPng && !hasPngSignature) ||
            (expectsJpeg && !hasJpegSignature))
        {
            throw Error(
                "image.signature",
                "Image content does not match its declared format.");
        }

        try
        {
            var content = encoded.ToArray();
            int expectedWidth;
            int expectedHeight;
            using (var headerStream = new MemoryStream(
                       content,
                       writable: false))
            {
                var headerDecoder = BitmapDecoder.Create(
                    headerStream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnDemand);
                ValidateCodec(headerDecoder, expectsPng, expectsJpeg);
                var headerFrame = GetFirstFrame(headerDecoder);
                ValidateDimensions(headerFrame);
                expectedWidth = headerFrame.PixelWidth;
                expectedHeight = headerFrame.PixelHeight;
                var pixels = checked(
                    (long)expectedWidth * expectedHeight);
                if (pixels > remainingPixelBudget)
                {
                    throw Error(
                        "image.pixel-budget",
                        "Decoded images exceed the supported pixel budget.");
                }
            }

            using var stream = new MemoryStream(
                content,
                writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            ValidateCodec(decoder, expectsPng, expectsJpeg);
            var frame = GetFirstFrame(decoder);
            ValidateDimensions(frame);
            if (frame.PixelWidth != expectedWidth ||
                frame.PixelHeight != expectedHeight)
            {
                throw Error(
                    "image.decode",
                    "Image dimensions changed during decoding.");
            }

            ForceCompleteDecode(frame);
            if (!frame.IsFrozen)
            {
                frame.Freeze();
            }

            return new SkinDecodedImage(
                frame.PixelWidth,
                frame.PixelHeight,
                HasAlpha(frame.Format),
                frame);
        }
        catch (FileFormatException exception)
        {
            throw Error(
                "image.decode",
                "Image content could not be decoded.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw Error(
                "image.decode",
                "Image content could not be decoded.",
                exception);
        }
    }

    private static void ValidateCodec(
        BitmapDecoder decoder,
        bool expectsPng,
        bool expectsJpeg)
    {
        if ((expectsPng && decoder is not PngBitmapDecoder) ||
            (expectsJpeg && decoder is not JpegBitmapDecoder))
        {
            throw Error(
                "image.signature",
                "Image content does not match its declared format.");
        }
    }

    private static BitmapFrame GetFirstFrame(BitmapDecoder decoder)
    {
        if (decoder.Frames.Count == 0)
        {
            throw Error(
                "image.decode",
                "Image content could not be decoded.");
        }

        return decoder.Frames[0];
    }

    private static void ValidateDimensions(BitmapSource frame)
    {
        if (frame.PixelWidth <= 0 ||
            frame.PixelHeight <= 0 ||
            frame.PixelWidth > SkinPackageLimits.MaximumImageDimension ||
            frame.PixelHeight > SkinPackageLimits.MaximumImageDimension)
        {
            throw Error(
                "image.dimension",
                "Image dimensions exceed the supported limit.");
        }
    }

    private static bool HasAlpha(PixelFormat format) =>
        format == PixelFormats.Bgra32 ||
        format == PixelFormats.Pbgra32 ||
        format == PixelFormats.Rgba64 ||
        format == PixelFormats.Prgba64 ||
        format == PixelFormats.Rgba128Float ||
        format == PixelFormats.Prgba128Float;

    private static void ForceCompleteDecode(BitmapSource bitmap)
    {
        var stride = checked(
            (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8);
        var scanline = new byte[stride];
        for (var row = 0; row < bitmap.PixelHeight; row++)
        {
            bitmap.CopyPixels(
                new Int32Rect(0, row, bitmap.PixelWidth, 1),
                scanline,
                stride,
                0);
        }
    }

    private static SkinImageValidationException Error(
        string code,
        string message,
        Exception? innerException = null) =>
        new(code, message, innerException);
}

internal sealed class SkinImageValidationException : IOException
{
    public SkinImageValidationException(
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
