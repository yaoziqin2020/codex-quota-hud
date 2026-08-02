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
        ReadOnlyMemory<byte> encoded)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

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
            using var stream = new MemoryStream(
                encoded.ToArray(),
                writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if ((expectsPng && decoder is not PngBitmapDecoder) ||
                (expectsJpeg && decoder is not JpegBitmapDecoder))
            {
                throw Error(
                    "image.signature",
                    "Image content does not match its declared format.");
            }

            if (decoder.Frames.Count == 0)
            {
                throw Error(
                    "image.decode",
                    "Image content could not be decoded.");
            }

            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 ||
                frame.PixelHeight <= 0 ||
                frame.PixelWidth > SkinPackageLimits.MaximumImageDimension ||
                frame.PixelHeight > SkinPackageLimits.MaximumImageDimension)
            {
                throw Error(
                    "image.dimension",
                    "Image dimensions exceed the supported limit.");
            }

            _ = checked((long)frame.PixelWidth * frame.PixelHeight);
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
