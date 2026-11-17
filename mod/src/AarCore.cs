// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// AarCore: the After-Action Reports, a rule-for-rule port of the RE_Kenshi plugin's src/AarCore.h (0.12.0): battles cut from the hit stream,
// written from rules (no LLM), the same moments, thresholds, wording, order and phrasing seed, so the same battle reads the same in both
// games. Parity details kept on purpose: C++ std::map / std::set walk in key order (ids as numbers, strings byte-wise: Ordinal here),
// std::sort + reverse on (knockouts, damage, id), ASCII-only upper / lower case, the seed's unsigned overflow, "%.0f" rounding half away from
// zero (the plugin's VS2010 CRT). tools/check replays the same battles through the C++ engine and this one and compares the reports.
using System.Globalization;
using System.Text;

namespace PZA.ServiceRecord.Meitou.Aar;

public struct Hit
{
    public double T;
    public ulong A, V;
    public float H0, H1;
    public bool Dn0, Dn1, Dd0, Dd1, Ranged, Ff;
    public int Lb0, Lb1;
    /// <summary>1 = the player's, 0 = not, -1 = unknown.</summary>
    public sbyte Am, Vm;
    /// <summary>The victim leads its own squad; -1 unknown.</summary>
    public sbyte VLeader;
    /// <summary>Ranged: 1 aimed at this victim, 0 a stray; -1 unknown / melee.</summary>
    public sbyte Purpose;

    public static Hit New() => new() { Am = -1, Vm = -1, VLeader = -1, Purpose = -1 };

    public readonly float Drop() { float d = H0 - H1; return d > 0 && d < 1e6f ? d : 0.0f; }

    public readonly bool Downing() => !Dn0 && Dn1;
}

public sealed class Event
{
    public string Kind = "";
    public double T;
    public ulong A, V;
}

public sealed class Report
{
    public double T0, T1;
    public int Number;
    public string Title = "", Body = "", Advice = "", Closing = "";
    public List<string> Aftermath = [];
    public SortedSet<ulong> Enemies = [];
    public SortedSet<ulong> Ours = [];
    public double GameH0 = -1;
    public SortedDictionary<ulong, string> Names = [];
    public List<string> Fired = [];

    public string Text(bool withAdvice = false)
    {
        string s = Title + "\n" + Body + (withAdvice ? Advice : "") + Closing;
        if (Aftermath.Count > 0)
        {
            s += "\nAFTERMATH\n";
            foreach (var a in Aftermath) s += a + "\n";
        }
        return s;
    }

    public Report Clone()
    {
        var r = (Report)MemberwiseClone();
        r.Aftermath = [.. Aftermath];
        r.Enemies = [.. Enemies];
        r.Ours = [.. Ours];
        r.Names = new(Names);
        r.Fired = [.. Fired];
        return r;
    }
}

public sealed class Context
{
    public IReadOnlyDictionary<ulong, string>? Factions;
    public string Place = "", PlaceOwner = "";
    /// <summary>-1 never read; 0 open ground; 1 ours; 2 an enemy's in this fight; 3 someone else's.</summary>
    public int PlaceKind = -1;

    public Context Copy() => (Context)MemberwiseClone();
}

/// <summary>The game's own clock samples (v0.6.11): session seconds against in-game total hours.</summary>
public static class GameClock
{
    static readonly LinkedList<(double T, double H)> s_s = new();
    static readonly List<(double T, double H)> s_index = [];
    static bool s_dirty;
    public static int DayOffset = 1;
    public static long Samples, Resets;

    public static void Clear() { s_s.Clear(); s_dirty = true; Samples = Resets = 0; }

    public static void Sample(double t, double hours)
    {
        if (double.IsNaN(t) || double.IsNaN(hours) || hours < 0 || hours > 1e8) return;
        if (s_s.Count > 0 && (t < s_s.Last!.Value.T || hours < s_s.Last.Value.H - 1e-6)) { s_s.Clear(); ++Resets; }
        if (s_s.Count > 0 && t - s_s.Last!.Value.T < 1.0) return;
        s_s.AddLast((t, hours));
        ++Samples;
        while (s_s.Count > 30000) s_s.RemoveFirst();
        s_dirty = true;
    }

    static List<(double T, double H)> Index()
    {
        if (!s_dirty) return s_index;
        s_index.Clear();
        s_index.AddRange(s_s);
        s_dirty = false;
        return s_index;
    }

    public static bool HoursAt(double t, out double h)
    {
        h = 0;
        var s = Index();
        if (s.Count == 0 || double.IsNaN(t) || t < s[0].T - 1.0) return false;
        if (t >= s[^1].T)
        {
            if (t - s[^1].T > 120.0) return false;
            double slope = 0;
            if (s.Count >= 2)
            {
                var a = s[^2];
                var b = s[^1];
                if (b.T > a.T) slope = (b.H - a.H) / (b.T - a.T);
            }
            h = s[^1].H + (t - s[^1].T) * slope;
            return true;
        }
        int lo = 0, hi = s.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (s[mid].T <= t) lo = mid;
            else hi = mid;
        }
        double span = s[hi].T - s[lo].T;
        h = span > 0 ? s[lo].H + (s[hi].H - s[lo].H) * (t - s[lo].T) / span : s[lo].H;
        return true;
    }

    public static long Day(double hours) => (long)Math.Floor(hours / 24.0) + DayOffset;

    public static string HHMM(double hours)
    {
        long m = (long)Math.Floor(CFmod(hours, 24.0) * 60.0 + 1e-6);
        if (m < 0) m = 0;
        if (m >= 24 * 60) m = 24 * 60 - 1;
        return (m / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>C's fmod (the sign of the dividend), not IEEERemainder.</summary>
    static double CFmod(double a, double b) => a % b;
}

public static class Words
{
    public const char ColourMark = '\u0001';
    public const string Dash = "—";

    /// <summary>C's "%.0f" as the plugin's VS2010 CRT prints it (<see cref="CFixed"/>).</summary>
    public static string F0(double v) => CFixed(v, 0);

    /// <summary>C's "%.Nf" as the VS2010 CRT prints it: the number is first turned into 17 significant decimal digits, and THAT text is rounded
    /// half up (1.95 -> "1.9500000000000000" -> "2.0", where exact binary rounding gives "1.9"). A negative that rounds to zero keeps its sign
    /// ("-0"), as there. Found by the differential check (radar tooltips, 2026-11-16).</summary>
    public static string CFixed(double v, int decimals)
    {
        if (double.IsNaN(v)) return "nan";
        if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
        bool neg = v < 0 || (v == 0 && double.IsNegative(v));
        string e = Math.Abs(v).ToString("E16", CultureInfo.InvariantCulture);   // d.dddddddddddddddde+xxx: 17 significant digits
        int ep = e.IndexOf('E');
        string digits = e[0] + e.Substring(2, ep - 2);
        int exp = int.Parse(e[(ep + 1)..], CultureInfo.InvariantCulture);
        // the value is 0.digits x 10^(exp + 1); keep (exp + 1 + decimals) digits, rounding on the next one
        int keep = exp + 1 + decimals;
        var d = new List<int>();
        if (keep >= 0)
        {
            foreach (char c in digits.PadRight(Math.Max(keep, digits.Length), '0')) d.Add(c - '0');
            bool up = keep < d.Count && d[keep] >= 5;
            d = d.Take(keep).ToList();
            int lead = 0;
            if (up)
            {
                int i = d.Count - 1;
                while (i >= 0 && d[i] == 9) d[i--] = 0;
                if (i >= 0) d[i]++;
                else { d.Insert(0, 1); lead = 1; }
            }
            int intDigits = exp + 1 + lead;
            var sb = new StringBuilder();
            if (intDigits <= 0) sb.Append('0');
            else for (int i = 0; i < intDigits; i++) sb.Append(i < d.Count ? (char)('0' + d[i]) : '0');
            if (decimals > 0)
            {
                sb.Append('.');
                for (int k = 0; k < decimals; k++)
                {
                    int idx = intDigits + k;
                    sb.Append(idx >= 0 && idx < d.Count ? (char)('0' + d[idx]) : '0');
                }
            }
            return (neg ? "-" : "") + sb;
        }
        // |v| < 0.5 x 10^-decimals by more than a digit: rounds to zero
        return (neg ? "-" : "") + "0" + (decimals > 0 ? "." + new string('0', decimals) : "");
    }

    public static string Clock(double s)
    {
        if (!(s >= 0) || s > 1e7) s = 0;
        long x = (long)s;
        return x >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{x / 3600}:{(x / 60) % 60:00}:{x % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{x / 60}:{x % 60:00}");
    }

    public static string When(double t)
    {
        string c = Clock(t);
        return GameClock.HoursAt(t, out double h) ? c + " (" + GameClock.HHMM(h) + ")" : c;
    }

    public static string WhenAt(double t)
    {
        string c = Clock(t);
        return GameClock.HoursAt(t, out double h) ? c + " at " + GameClock.HHMM(h) : c;
    }

    public static string GameSpan(double t0, double t1)
    {
        if (!GameClock.HoursAt(t0, out double a) || !GameClock.HoursAt(t1, out double b)) return "";
        return GameClock.Day(a) == GameClock.Day(b)
            ? $"Day {GameClock.Day(a)}, {GameClock.HHMM(a)} to {GameClock.HHMM(b)}"
            : $"Day {GameClock.Day(a)}, {GameClock.HHMM(a)} to Day {GameClock.Day(b)}, {GameClock.HHMM(b)}";
    }

    #region Plurals

    static readonly Dictionary<string, string> s_plurals = new(StringComparer.Ordinal)
    {
        ["wolf"] = "wolves", ["thief"] = "thieves", ["knife"] = "knives", ["half"] = "halves", ["self"] = "selves", ["man"] = "men", ["woman"] = "women",
        ["fogman"] = "fogmen", ["fishman"] = "fishmen", ["watchman"] = "watchmen", ["swordsman"] = "swordsmen", ["nightman"] = "nightmen",
        ["shek"] = "shek", ["samurai"] = "samurai", ["hero"] = "heroes", ["child"] = "children", ["person"] = "people", ["foot"] = "feet",
        ["tooth"] = "teeth", ["mouse"] = "mice", ["ox"] = "oxen", ["sheep"] = "sheep", ["deer"] = "deer", ["fish"] = "fish", ["genin"] = "genin",
        ["chunin"] = "chunin", ["chuunin"] = "chuunin", ["jonin"] = "jonin", ["jounin"] = "jounin", ["ronin"] = "ronin", ["shinobi"] = "shinobi",
        ["staff"] = "staffs", ["chief"] = "chiefs", ["roof"] = "roofs", ["human"] = "humans", ["skin"] = "skins",
    };
    public static long PluralUses, PluralFromTable;

    public static void AddPlural(string singular, string plural)
    {
        if (singular.Length == 0 || plural.Length == 0 || singular.Length > 40 || plural.Length > 40) return;
        s_plurals[LowerAscii(singular)] = LowerAscii(plural);
    }

    /// <summary>C's tolower in the "C" locale: A-Z only.</summary>
    public static string LowerAscii(string s)
    {
        var b = s.ToCharArray();
        for (int i = 0; i < b.Length; i++) if (b[i] >= 'A' && b[i] <= 'Z') b[i] = (char)(b[i] - 'A' + 'a');
        return new string(b);
    }

    public static string Upper(string s)
    {
        var b = s.ToCharArray();
        for (int i = 0; i < b.Length; i++) if (b[i] >= 'a' && b[i] <= 'z') b[i] = (char)(b[i] - 'a' + 'A');
        return new string(b);
    }

    static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    public static string PluralWord(string w)
    {
        if (w.Length == 0) return w;
        string lw = LowerAscii(w);
        ++PluralUses;
        string o;
        if (s_plurals.TryGetValue(lw, out var t)) { o = t; ++PluralFromTable; }
        else if (lw.EndsWith('s') || lw.EndsWith('x') || lw.EndsWith('z') || lw.EndsWith("ch", StringComparison.Ordinal) || lw.EndsWith("sh", StringComparison.Ordinal))
            o = lw + "es";
        else if (lw.Length > 1 && lw[^1] == 'y' && !IsVowel(lw[^2])) o = lw[..^1] + "ies";
        else o = lw + "s";
        bool allUp = !w.Any(c => c >= 'a' && c <= 'z');
        if (allUp && w.Length > 1) o = Upper(o);
        else if (w[0] >= 'A' && w[0] <= 'Z' && o.Length > 0 && o[0] >= 'a' && o[0] <= 'z') o = (char)(o[0] - 'a' + 'A') + o[1..];
        return o;
    }

    public static string Plural(string name)
    {
        int sp = name.LastIndexOf(' ');
        return sp < 0 ? PluralWord(name) : name[..(sp + 1)] + PluralWord(name[(sp + 1)..]);
    }

    public static string CountNoun(double n, string singular) => n == 1 ? singular : Plural(singular);

    #endregion

    public static string Count(double n, string one, string many) => F0(n) + " " + (n == 1 ? one : many);

    public static string Lower(string s) => LowerAscii(s);

    public static bool IsAnimalName(string n)
    {
        string l = Lower(n);
        foreach (var w in (ReadOnlySpan<string>)["bull", "garru", "beak", "bonedog", "goat", "dog", "crab", "leviathan", "spider", "gorillo", "beast", "raptor", "teddy bear"])
            if (l.Contains(w, StringComparison.Ordinal)) return true;
        return false;
    }

    public static string Stem(string n) { int p = n.IndexOf(" leader", StringComparison.Ordinal); return p < 0 ? n : n[..p]; }

    public static char ColourOf(string t) => t switch
    {
        "FRIENDLY FIRE" => 'g',
        "DOWN" => 'r',
        "THE LEADER FALLS" or "CARRIED THE BATTLE" => 'y',
        "OPENING" or "TWO AT ONCE" or "HAT TRICK" or "THE COLLAPSE" or "CHEEKY FINISHER" => 'o',
        "HOLDING THE LINE" or "WALL OF BLOCKS" or "TOOK THE MOST PUNISHMENT" or "FOCUS FIRE" => 'b',
        "BARELY CONTRIBUTED" => 'd',
        _ => 'n',
    };

    public static string? ColourHex(char c) => c switch
    {
        'g' => "#6FD46F", 'r' => "#E05A4F", 'y' => "#E8C25A", 'o' => "#E8964A", 'b' => "#8FB3D9", 'd' => "#9A9A9A", 'a' => "#B79BE0", _ => null,
    };

    public static string StripColour(string s)
    {
        var o = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ColourMark) { ++i; continue; }
            o.Append(s[i]);
        }
        return o.ToString();
    }

    /// <summary>For MyGUI: every '#' doubled, each marked line in its colour and reset to <paramref name="normalHex"/> at its end.</summary>
    public static string ToMyGui(string s, string normalHex)
    {
        var o = new StringBuilder(s.Length + 64);
        bool coloured = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ColourMark && i + 1 < s.Length)
            {
                string? h = ColourHex(s[++i]);
                if (h is not null) { o.Append(h); coloured = true; }
                else if (coloured) { o.Append(normalHex); coloured = false; }
                continue;
            }
            if (c == '\n' && coloured) { o.Append(normalHex); coloured = false; }
            if (c == '#') o.Append("##");
            else o.Append(c);
        }
        if (coloured) o.Append(normalHex);
        return o.ToString();
    }

    static readonly string[] s_w =
    [
        "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen",
        "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];
    static readonly string[] s_tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    public static string Of(long n)
    {
        if (n >= 0 && n <= 20) return s_w[n];
        if (n > 20 && n < 100) return s_tens[n / 10] + (n % 10 != 0 ? "-" + s_w[n % 10] : "");
        return F0(n);
    }

    public static string Cap(string s) => s.Length == 0 ? s : (s[0] >= 'a' && s[0] <= 'z' ? (char)(s[0] - 'a' + 'A') + s[1..] : s);

    public static string TitleCase(string s)
    {
        var b = s.ToCharArray();
        bool up = true;
        for (int i = 0; i < b.Length; i++)
        {
            if (up && b[i] >= 'a' && b[i] <= 'z') b[i] = (char)(b[i] - 'a' + 'A');
            up = b[i] == ' ' || b[i] == '-';
        }
        return new string(b);
    }

    public static string Duration(double s)
    {
        long x = (long)(s + 0.5);
        return x >= 60 ? F0(x / 60) + " min " + F0(x % 60) + " s" : F0(x) + " s";
    }

    public static string JoinAnd(IReadOnlyList<string> v)
    {
        var o = new StringBuilder();
        for (int i = 0; i < v.Count; i++) o.Append(i == 0 ? "" : i + 1 == v.Count ? " and " : ", ").Append(v[i]);
        return o.ToString();
    }

    public static string Mark(char c) => new string(ColourMark, 1) + c;

    public static string Seconds(double s)
    {
        long x = (long)(s + 0.5);
        return x <= 0 ? "under a second" : x == 1 ? "one second" : Of(x) + " seconds";
    }
}
