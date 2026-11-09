using System.IO.Compression;
using ProjectIGI.Core.Numerics;

namespace ProjectIGI.TerrainPrototype;

/// <summary>
/// A shaded picture of a model's detailed (level 0) collision mesh from above one corner: enough to tell a
/// pagoda from a barracks before choosing it for the compound. Untextured, so it shows shape only.
/// </summary>
internal static class ModelPreview
{
    public static void Render(IReadOnlyList<(Vector3d A, Vector3d B, Vector3d C)> triangles, string path, double azimuthDegrees = 35,
        int width = 900, int height = 600, IReadOnlyList<Vector3d>? facing = null)
    {
        double az = azimuthDegrees * Math.PI / 180, el = 30 * Math.PI / 180;
        Vector3d right = new(Math.Cos(az), -Math.Sin(az), 0);
        Vector3d forward = new(Math.Sin(az) * Math.Cos(el), Math.Cos(az) * Math.Cos(el), -Math.Sin(el));
        Vector3d up = Vector3d.Cross(right, forward);
        Vector3d light = new Vector3d(-0.4, -0.6, 0.7) / new Vector3d(-0.4, -0.6, 0.7).Length;

        (double X, double Y, double D) Project(Vector3d p) => (Vector3d.Dot(p, right), Vector3d.Dot(p, up), Vector3d.Dot(p, forward));
        var projected = triangles.Select((t, i) => (P: (Project(t.A), Project(t.B), Project(t.C)), T: t, I: i)).ToList();
        double minX = projected.Min(t => Math.Min(t.P.Item1.X, Math.Min(t.P.Item2.X, t.P.Item3.X)));
        double maxX = projected.Max(t => Math.Max(t.P.Item1.X, Math.Max(t.P.Item2.X, t.P.Item3.X)));
        double minY = projected.Min(t => Math.Min(t.P.Item1.Y, Math.Min(t.P.Item2.Y, t.P.Item3.Y)));
        double maxY = projected.Max(t => Math.Max(t.P.Item1.Y, Math.Max(t.P.Item2.Y, t.P.Item3.Y)));
        double scale = Math.Min((width - 20) / (maxX - minX), (height - 20) / (maxY - minY));
        double[] depth = new double[width * height];
        Array.Fill(depth, double.MaxValue);
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < rgb.Length; i += 3)
        {
            (rgb[i], rgb[i + 1], rgb[i + 2]) = ((byte)170, (byte)200, (byte)230);
        }

        foreach (var (p, t, i) in projected)
        {
            Vector3d n = Vector3d.Cross(t.C - t.A, t.B - t.A);
            if (n.Length < 1e-9)
            {
                continue;
            }

            n /= n.Length;
            if (facing is not null)
            {
                // A drawn mesh is one-sided: a face whose normal points away from the eye is not seen.
                if (Vector3d.Dot(facing[i], forward) >= 0)
                {
                    continue;
                }

                n = facing[i];
            }

            double shade = 0.35 + (0.65 * Math.Abs(Vector3d.Dot(n, light)));
            (byte r, byte g, byte b) colour = Math.Abs(n.Z) > 0.7
                ? ((byte)(200 * shade), (byte)(190 * shade), (byte)(160 * shade))
                : ((byte)(150 * shade), (byte)(150 * shade), (byte)(155 * shade));
            (double X, double Y)[] s = [.. new[] { p.Item1, p.Item2, p.Item3 }
                .Select(q => (10 + ((q.X - minX) * scale), height - 10 - ((q.Y - minY) * scale)))];
            double[] d = [p.Item1.D, p.Item2.D, p.Item3.D];
            double area = ((s[1].X - s[0].X) * (s[2].Y - s[0].Y)) - ((s[2].X - s[0].X) * (s[1].Y - s[0].Y));
            if (Math.Abs(area) < 1e-9)
            {
                continue;
            }

            int x0 = Math.Max(0, (int)Math.Floor(s.Min(q => q.X))), x1 = Math.Min(width - 1, (int)Math.Ceiling(s.Max(q => q.X)));
            int y0 = Math.Max(0, (int)Math.Floor(s.Min(q => q.Y))), y1 = Math.Min(height - 1, (int)Math.Ceiling(s.Max(q => q.Y)));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double w0 = (((s[1].X - px) * (s[2].Y - py)) - ((s[2].X - px) * (s[1].Y - py))) / area;
                    double w1 = (((s[2].X - px) * (s[0].Y - py)) - ((s[0].X - px) * (s[2].Y - py))) / area;
                    double w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                    {
                        continue;
                    }

                    double z = (w0 * d[0]) + (w1 * d[1]) + (w2 * d[2]);
                    int index = (y * width) + x;
                    if (z < depth[index])
                    {
                        depth[index] = z;
                        (rgb[index * 3], rgb[(index * 3) + 1], rgb[(index * 3) + 2]) = colour;
                    }
                }
            }
        }

        WritePng(path, width, height, rgb);
    }

    private static void WritePng(string path, int width, int height, byte[] rgb)
    {
        using FileStream file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 2;
        Chunk(file, "IHDR", header);
        using MemoryStream raw = new();
        using (ZLibStream zlib = new(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgb, y * width * 3, width * 3);
            }
        }

        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);
        byte[] body = [.. type.Select(c => (byte)c), .. data];
        stream.Write(body);
        byte[] crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(body));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
