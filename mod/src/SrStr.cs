// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The plugin's SrStrings.h helpers on the shared string table (SrSta.Text, PZA's Meitou UI): TS fills {1}..{9} in ONE pass as str::Format does
// (an argument is never read again for placeholders; a placeholder with no argument becomes empty), Num is "%.0f", PluralForm / TP pick the
// .one / .few / .many text by language, Colour turns ^a..^n into the pages' colour marks.
using System.Globalization;
using System.Text;

namespace PZA.ServiceRecord.Meitou;

public static class SrStr
{
    static readonly Dictionary<string, int> s_index = SrText.Table.Select((e, i) => (e.Key, i)).ToDictionary(x => x.Key, x => x.i, StringComparer.Ordinal);

    public static string T(string key) => SrSta.Text.T(key);

    public static string TS(string key, params string[] args) => Format(T(key), args);

    public static string Format(string fmt, IReadOnlyList<string> args)
    {
        var o = new StringBuilder(fmt.Length + 16);
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] == '{' && i + 2 < fmt.Length && fmt[i + 1] >= '1' && fmt[i + 1] <= '9' && fmt[i + 2] == '}')
            {
                int k = fmt[i + 1] - '1';
                if (k < args.Count && k < 5) o.Append(args[k]);
                i += 2;
                continue;
            }
            o.Append(fmt[i]);
        }
        return o.ToString();
    }

    public static string Num(double v) => Aar.Words.F0(v);

    /// <summary>0 = .one, 1 = .few, 2 = .many (the plugin's rules: Russian 1/21/31 one, 2-4/22-24 few, else many; zh / ja always many;
    /// English 1 one, else many).</summary>
    public static int PluralForm(double v, string lang)
    {
        double a = v < 0 ? -v : v;
        long n = (long)a;
        bool whole = n == a;
        if (lang == "ru")
        {
            if (!whole) return 1;
            if (n % 10 == 1 && n % 100 != 11) return 0;
            if (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 12 || n % 100 > 14)) return 1;
            return 2;
        }
        if (lang is "zh" or "ja") return 2;
        return whole && n == 1 ? 0 : 2;
    }

    /// <summary>TS(oneKey + form, number): oneKey is the .one key of a .one / .few / .many trio (consecutive in the list, as the plugin's ids).</summary>
    public static string TP(string oneKey, double v)
    {
        int form = PluralForm(v, SrSta.Text.Lang);
        string key = s_index.TryGetValue(oneKey, out int i) && i + form < SrText.Table.Length ? SrText.Table[i + form].Key : oneKey;
        return TS(key, Num(v));
    }

    /// <summary>The key n places after <paramref name="first"/> in the list (the plugin's consecutive ids: S_RANK_0 + rank, S_POST_0 + post).</summary>
    public static string After(string first, int n) => s_index.TryGetValue(first, out int i) && i + n >= 0 && i + n < SrText.Table.Length ? SrText.Table[i + n].Key : first;

    /// <summary>"^a" ... "^n" -> the colour mark + letter; "^^" -> '^'.</summary>
    public static string Colour(string s)
    {
        var o = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '^' && i + 1 < s.Length)
            {
                char c = s[i + 1];
                if (c == '^') { o.Append('^'); ++i; continue; }
                if ("agryobdn".Contains(c, StringComparison.Ordinal)) { o.Append(Aar.Words.ColourMark).Append(c); ++i; continue; }
            }
            o.Append(s[i]);
        }
        return o.ToString();
    }

    public static string Inv(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
}
