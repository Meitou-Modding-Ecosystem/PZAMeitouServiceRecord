// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The radar chart as a picture: Meitou's window engine draws no lines (the plugin used MyGUI's PolygonalSkin), but an ImageBox shows any
// image file ("file:<path>", decoded by stb_image). So the chart is drawn here, anti-aliased, in the plugin's geometry and colours (three
// web rings at 1/3, 2/3 and 1, six spokes, the fighter's shape; axis 0 straight up, then clockwise), and saved as an uncompressed TGA.
// Meitou keeps a texture by its name, so each distinct chart gets its own file (a hash of its values); at most MaxFiles are made in a
// session, then the last chart stays (logged once). Files live in %TEMP%\PZAServiceRecord\radar and are replaced each session.
using System.Text;

namespace PZA.ServiceRecord.Meitou;

public static class SrRadarImage
{
    public const int Size = 256, MaxFiles = 300;
    static readonly Dictionary<string, string> s_made = new(StringComparer.Ordinal);
    static string s_dir = "";
    static bool s_capLogged;
    public static long Made, Failed;

    /// <summary>The image property value for a chart ("file:<path>"), drawing it when new; "" when it cannot be made.</summary>
    public static string For(double[] values, bool[] known)
    {
        string key = string.Join(",", values.Select(v => v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))) + "|" + string.Join("", known.Select(k => k ? '1' : '0'));
        if (s_made.TryGetValue(key, out var path)) return "file:" + path;
        if (s_made.Count >= MaxFiles)
        {
            if (!s_capLogged) { s_capLogged = true; SrLog.Line($"radar chart: {MaxFiles} charts drawn this session - the last one stays until a restart"); }
            return s_made.Count > 0 ? "file:" + s_made.Values.Last() : "";
        }
        try
        {
            if (s_dir.Length == 0)
            {
                s_dir = Path.Combine(Path.GetTempPath(), "PZAServiceRecord", "radar");
                if (Directory.Exists(s_dir)) foreach (var f in Directory.EnumerateFiles(s_dir, "*.tga")) File.Delete(f);
                Directory.CreateDirectory(s_dir);
            }
            string name = Fnv(key).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
            path = Path.Combine(s_dir, "radar_" + name + ".tga");
            File.WriteAllBytes(path, Tga(Draw(values, known)));
            s_made[key] = path;
            Made++;
            return "file:" + path;
        }
        catch (Exception e)
        {
            if (Failed++ < 3) SrLog.Line("radar chart: the picture could not be written (" + e.Message + ")");
            return "";
        }
    }

    /// <summary>A 64-bit FNV-1a hash of the chart's values (a file name; not security).</summary>
    static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    /// <summary>The vertex of axis a at fraction t, in picture pixels (the plugin's srradar::Point, R = 0.40 of the picture).</summary>
    public static (double X, double Y) Vertex(int a, double t) => SrRadar.Point(a, t, Size / 2.0, Size / 2.0, Size * 0.40);

    static byte[] Draw(double[] values, bool[] known)
    {
        var px = new float[Size * Size * 4];   // straight RGBA, 0..1
        var web = (R: 0.62f, G: 0.56f, B: 0.46f, A: 0.55f);
        var shape = (R: 0.93f, G: 0.60f, B: 0.22f, A: 1.0f);
        var segs = new List<(double X0, double Y0, double X1, double Y1, float W, (float R, float G, float B, float A) C)>();
        for (int r = 1; r <= 3; r++)
            for (int a = 0; a < SrRadar.Axes; a++)
            {
                var p = Vertex(a, r / 3.0);
                var q = Vertex((a + 1) % SrRadar.Axes, r / 3.0);
                segs.Add((p.X, p.Y, q.X, q.Y, 1.2f, web));
            }
        for (int a = 0; a < SrRadar.Axes; a++)
        {
            var p = Vertex(a, 1.0);
            segs.Add((Size / 2.0, Size / 2.0, p.X, p.Y, 1.2f, web));
        }
        for (int a = 0; a < SrRadar.Axes; a++)
        {
            var p = Vertex(a, values[a]);
            var q = Vertex((a + 1) % SrRadar.Axes, values[(a + 1) % SrRadar.Axes]);
            segs.Add((p.X, p.Y, q.X, q.Y, 3.0f, shape));
        }
        foreach (var s in segs) Stroke(px, s.X0, s.Y0, s.X1, s.Y1, s.W, s.C);
        var o = new byte[Size * Size * 4];
        for (int i = 0; i < px.Length; i++) o[i] = (byte)Math.Clamp((int)(px[i] * 255 + 0.5f), 0, 255);
        return o;
    }

    /// <summary>An anti-aliased line of width w: coverage from the distance to the segment, composited over what is there.</summary>
    static void Stroke(float[] px, double x0, double y0, double x1, double y1, float w, (float R, float G, float B, float A) c)
    {
        double half = w / 2.0;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - half - 1)), maxX = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(x0, x1) + half + 1));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - half - 1)), maxY = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(y0, y1) + half + 1));
        double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double cx = x + 0.5, cy = y + 0.5;
                double t = len2 > 0 ? Math.Clamp(((cx - x0) * dx + (cy - y0) * dy) / len2, 0, 1) : 0;
                double ex = cx - (x0 + t * dx), ey = cy - (y0 + t * dy);
                float cov = (float)Math.Clamp(half + 0.5 - Math.Sqrt(ex * ex + ey * ey), 0, 1) * c.A;
                if (cov <= 0) continue;
                int i = (y * Size + x) * 4;
                float a0 = px[i + 3], a1 = cov + a0 * (1 - cov);
                if (a1 <= 0) continue;
                px[i] = (c.R * cov + px[i] * a0 * (1 - cov)) / a1;
                px[i + 1] = (c.G * cov + px[i + 1] * a0 * (1 - cov)) / a1;
                px[i + 2] = (c.B * cov + px[i + 2] * a0 * (1 - cov)) / a1;
                px[i + 3] = a1;
            }
    }

    /// <summary>An uncompressed 32-bit TGA, top-left origin, from RGBA bytes.</summary>
    public static byte[] Tga(byte[] rgba)
    {
        var o = new byte[18 + rgba.Length];
        o[2] = 2;                                           // uncompressed true colour
        o[12] = Size & 0xFF; o[13] = Size >> 8; o[14] = Size & 0xFF; o[15] = Size >> 8;
        o[16] = 32;
        o[17] = 0x28;                                       // 8 alpha bits, top-left origin
        for (int i = 0; i < rgba.Length; i += 4)
        {
            o[18 + i] = rgba[i + 2];
            o[18 + i + 1] = rgba[i + 1];
            o[18 + i + 2] = rgba[i];
            o[18 + i + 3] = rgba[i + 3];
        }
        return o;
    }
}
