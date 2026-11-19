// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The After-Action Report archive (the plugin's SrArchive.h, v0.6.14 / v0.7.1): every finished report appended to a plain text file, one per
// player faction, in the SAME folder and format as the RE_Kenshi plugin (%LOCALAPPDATA%\kenshi\PZAServiceRecord\reports\<faction>.txt), so one
// playthrough's stories land in one place whichever game wrote them. Header "==== Report N | save '...' | written YYYY-MM-DD HH:MM ====";
// Windows line ends; a cap in MB for the whole folder; report numbers carry on from the highest one already kept.
using System.Globalization;
using System.Text;

namespace PZA.ServiceRecord.Meitou;

public sealed class SrArchive
{
    public enum Result { Written = 0, Off = 1, Full = 2, Failed = 3 }

    public bool On = true;
    public double MaxBytes = 50.0 * 1024 * 1024;
    public string Dir = DefaultDir();
    public long WrittenCount, FullCount, FailedCount;
    public bool FullLogged;

    /// <summary>A file name from a faction name: Windows-illegal and control characters become '_', trimmed, at most 80 bytes.</summary>
    public static string SafeName(string input)
    {
        var s = new StringBuilder();
        foreach (char c in input)
        {
            string piece = c < 32 || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? "_" : c.ToString();
            if (Encoding.UTF8.GetByteCount(s.ToString() + piece) > 80) break;
            s.Append(piece);
        }
        string r = s.ToString().TrimEnd(' ', '.').TrimStart(' ');
        if (r.Length == 0) return "Unknown faction";
        // fuzz 2026-11-18: a faction called CON, NUL, COM1, LPT1... named a Windows device, not a file (the report went nowhere)
        string stem = r.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        bool device = stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3]);
        if (!device) return r;
        int dot = r.IndexOf('.');   // Windows matches the device before the first dot whatever follows: "NUL.txt" is NUL too
        return dot < 0 ? r + "_" : r[..dot].TrimEnd(' ') + "_" + r[dot..];
    }

    public static string DefaultDir()
    {
        string? v = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        return string.IsNullOrEmpty(v) ? "" : Path.Combine(v, "kenshi", "PZAServiceRecord", "reports");
    }

    static double FolderBytes(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.txt").Sum(f => (double)new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>header: one line; text: the report as shown, plain (no colour marks).</summary>
    public Result Append(string faction, string header, string text, out string path)
    {
        path = "";
        if (!On) return Result.Off;
        try
        {
            if (Dir.Length == 0) { ++FailedCount; return Result.Failed; }
            Directory.CreateDirectory(Dir);
            string block = header + "\r\n" + text + "\r\n\r\n";
            var o = new StringBuilder(block.Length + 64);   // the report uses '\n'; the file gets Windows line ends so Notepad shows it right
            for (int i = 0; i < block.Length; i++)
            {
                if (block[i] == '\n' && (i == 0 || block[i - 1] != '\r')) o.Append('\r');
                o.Append(block[i]);
            }
            byte[] bytes = Encoding.UTF8.GetBytes(o.ToString());
            if (MaxBytes > 0 && FolderBytes(Dir) + Encoding.UTF8.GetByteCount(block) > MaxBytes) { ++FullCount; return Result.Full; }
            path = Path.Combine(Dir, SafeName(faction) + ".txt");
            using (var f = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) f.Write(bytes);
            ++WrittenCount;
            return Result.Written;
        }
        catch
        {
            ++FailedCount;
            return Result.Failed;
        }
    }

    /// <summary>The highest report number already kept for this faction (0 without a file or a header).</summary>
    public static int HighestNumber(string dir, string faction)
    {
        if (dir.Length == 0) return 0;
        string p = Path.Combine(dir, SafeName(faction) + ".txt");
        int best = 0;
        try
        {
            if (!File.Exists(p)) return 0;
            foreach (var line in File.ReadLines(p, Encoding.UTF8))
            {
                const string Tag = "==== Report ";
                if (!line.StartsWith(Tag, StringComparison.Ordinal)) continue;
                int i = Tag.Length, digits = 0;
                long n = 0;
                while (i < line.Length && line[i] >= '0' && line[i] <= '9' && digits < 9) { n = n * 10 + (line[i] - '0'); i++; digits++; }
                if (digits > 0 && i + 1 < line.Length && line[i] == ' ' && line[i + 1] == '|' && n > best) best = (int)n;
            }
        }
        catch
        {
            return best;
        }
        return best;
    }

    public static string Header(int number, string save, bool updated, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        return string.Create(CultureInfo.InvariantCulture,
            $"==== Report {number} | save '{(save.Length == 0 ? "unknown" : save)}' | written {t:yyyy-MM-dd HH:mm}{(updated ? " | updated: a later fight joined it" : "")} ====");
    }
}
