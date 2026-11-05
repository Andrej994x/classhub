using SkiaSharp;

namespace ClassHub.UnitTests.Files;

/// <summary>Builds real images in memory (no binary fixtures in the repository).</summary>
public static class TestImages
{
    public static byte[] Png(int width, int height) => Encode(width, height, SKEncodedImageFormat.Png);

    public static byte[] Jpeg(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    /// <summary>
    /// A JPEG with an EXIF APP1 segment (Orientation tag) and a COM segment containing <paramref name="secret"/>,
    /// inserted right after the SOI marker, like a phone camera's metadata.
    /// </summary>
    public static byte[] JpegWithMetadata(int width, int height, ushort orientation, string secret)
    {
        var jpeg = Jpeg(width, height);
        byte[] tiff =
        [
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, // big-endian TIFF header, IFD at 8
            0x00, 0x01,                                                 // 1 entry
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,             // Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,    // value
            0x00, 0x00, 0x00, 0x00,                                     // no next IFD
        ];
        var exif = Segment(0xE1, [.. "Exif\0\0"u8.ToArray(), .. tiff]);
        var comment = Segment(0xFE, System.Text.Encoding.ASCII.GetBytes(secret));
        return [jpeg[0], jpeg[1], .. exif, .. comment, .. jpeg.AsSpan(2).ToArray()];
    }

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
            using var paint = new SKPaint { Color = SKColors.Gold };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint); // top-left quadrant marks the orientation
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
