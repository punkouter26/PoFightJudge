using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PoFightJudge.TestSupport;

/// <summary>A real, decodable PNG of any size, so a test has a portrait to upload without an imaging library.</summary>
public static class TestPng
{
    /// <summary>A solid black 8-bit RGB image.</summary>
    public static byte[] Solid(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk(png, "IHDR", header);

        // Every scanline is one filter byte and three bytes a pixel; all zero is "no filter, black".
        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(new byte[(1 + (width * 3)) * height]);
        }

        Chunk(png, "IDAT", pixels.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(MemoryStream png, string type, byte[] data)
    {
        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);

        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(body));
        png.Write(number);
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
