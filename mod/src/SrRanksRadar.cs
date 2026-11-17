// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// Earned chevrons (the plugin's SrRanks.h) and the radar chart's numbers ("combat profile", SrRadar.h), rule for rule.
// Merit = kills x 3 + KOs dealt x 2 + limbs removed x 2 + fights x 1 + damage dealt / 250. Cosmetic only. Radar: six axes from the saved
// record, per counted fight once fights are counted (else career totals), scaled against the best of the squad in the same mode.
namespace PZA.ServiceRecord.Meitou;

public static class SrRanks
{
    public readonly record struct Rank(int Chevrons, double MinMerit, string Name);

    public static readonly Rank[] Ladder =
    [
        new(0, 0.0, "Recruit"), new(1, 10.0, "Scrapper"), new(2, 30.0, "Blade"), new(3, 70.0, "Veteran"),
        new(4, 140.0, "Blooded"), new(5, 260.0, "Hardened"), new(6, 450.0, "Champion"), new(7, 750.0, "Legend"),
    ];

    public static double Merit(int kills, int kosDealt, int limbsRemoved, int fights, float dealt)
    {
        double m = 3.0 * Math.Max(kills, 0) + 2.0 * Math.Max(kosDealt, 0) + 2.0 * Math.Max(limbsRemoved, 0) + 1.0 * Math.Max(fights, 0)
                   + (dealt > 0 ? dealt / 250.0 : 0.0);
        return double.IsNaN(m) ? 0.0 : m;
    }

    /// <summary>The plugin passes dealt as a float (MeritIn.dealt).</summary>
    public static double Merit(Stats s) => Merit(s.Kills, s.KosDealt, s.LimbsRemoved, s.Fights, (float)s.Dealt);

    public static int RankIndex(double merit)
    {
        int r = 0;
        for (int i = 1; i < Ladder.Length; i++) if (merit >= Ladder[i].MinMerit) r = i;
        return r;
    }

    public static double ToNext(double merit)
    {
        int r = RankIndex(merit);
        return r + 1 < Ladder.Length ? Ladder[r + 1].MinMerit - merit : 0.0;
    }

    public static string ChevronText(int chevrons, int n = 32) => new('^', Math.Clamp(chevrons, 0, Math.Max(0, n - 1)));
}

public static class SrRadar
{
    public const int Axes = 6;

    public static string AxisName(int i) => i is >= 0 and < Axes ? new[] { "Lethality", "Takedowns", "Maiming", "Damage", "Endurance", "First strike" }[i] : "?";

    public static string AxisLabel(int i) => i is >= 0 and < Axes ? SrStr.T(AxisKey(i)) : "?";

    static string AxisKey(int i) => i switch
    {
        0 => SrText.S_AX_0, 1 => SrText.S_AX_1, 2 => SrText.S_AX_2, 3 => SrText.S_AX_3, 4 => SrText.S_AX_4, _ => SrText.S_AX_5,
    };

    public sealed class Raw
    {
        public double[] V = new double[Axes];
        public bool[] Known = new bool[Axes];
        public bool PerFight;
        public int Fights;
    }

    static double Pos(double x) => x > 0 && x < 1e12 ? x : 0;

    public static Raw Of(Stats s)
    {
        var r = new Raw { Fights = Math.Max(s.Fights, 0) };
        r.PerFight = r.Fights > 0;
        for (int a = 0; a < Axes; a++) r.Known[a] = true;
        r.Known[4] = s.Taken > 0 || s.TimesKO > 0;
        double f = r.PerFight ? r.Fights : 1.0;
        double kills = r.PerFight ? (double)s.Kills - s.BKills : s.Kills;
        double kos = r.PerFight ? (double)s.KosDealt - s.BKOs : s.KosDealt;
        double limbs = r.PerFight ? (double)s.LimbsRemoved - s.BLimbs : s.LimbsRemoved;
        double dealt = r.PerFight ? s.Dealt - s.BDealt : s.Dealt;
        double first = r.PerFight ? (double)(s.OneHitKO - s.B1KO) + (s.OneHitKill - s.B1Kill) : (double)s.OneHitKO + s.OneHitKill;
        r.V[0] = Pos(kills / f);
        r.V[1] = Pos(kos / f);
        r.V[2] = Pos(limbs / f);
        r.V[3] = Pos(dealt / f);
        r.V[4] = Pos(s.Taken / (1.0 + Math.Max(s.TimesKO, 0)));
        r.V[5] = Pos(first / f);
        return r;
    }

    /// <summary>Me scaled 0..1 per axis against the best of the pool in the same mode, with a floor; how many were compared.</summary>
    public static int Score(IReadOnlyList<Raw> pool, Raw me, double[] o, double floorFrac = 0.04)
    {
        var best = (double[])me.V.Clone();
        int n = 1;
        foreach (var p in pool)
        {
            if (p.PerFight != me.PerFight) continue;
            ++n;
            for (int a = 0; a < Axes; a++) if (p.Known[a] && p.V[a] > best[a]) best[a] = p.V[a];
        }
        for (int a = 0; a < Axes; a++)
        {
            double x = me.Known[a] && best[a] > 0 ? me.V[a] / best[a] : 0;
            if (!(x >= 0)) x = 0;
            if (x > 1) x = 1;
            o[a] = floorFrac + (1.0 - floorFrac) * x;
        }
        return n;
    }

    public static void Best(IReadOnlyList<Raw> pool, Raw me, double[] best, int[] who)
    {
        for (int a = 0; a < Axes; a++) { best[a] = me.V[a]; who[a] = -1; }
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].PerFight != me.PerFight) continue;
            for (int a = 0; a < Axes; a++) if (pool[i].Known[a] && pool[i].V[a] > best[a]) { best[a] = pool[i].V[a]; who[a] = i; }
        }
    }

    public static string Num(int axis, double v) => axis == 3 || axis == 4 || v >= 100 ? Aar.Words.F0(v) : Aar.Words.CFixed(v, 1);

    public static string WithUnit(int axis, bool perFight, double v)
    {
        if (axis is < 0 or >= Axes) return Num(axis, v);
        string[] pf = [SrText.S_UNIT_PF_0, SrText.S_UNIT_PF_1, SrText.S_UNIT_PF_2, SrText.S_UNIT_PF_3, SrText.S_UNIT_PF_4, SrText.S_UNIT_PF_5];
        string[] ca = [SrText.S_UNIT_CA_0, SrText.S_UNIT_CA_1, SrText.S_UNIT_CA_2, SrText.S_UNIT_CA_3, SrText.S_UNIT_CA_4, SrText.S_UNIT_CA_5];
        return SrStr.TS(perFight ? pf[axis] : ca[axis], Num(axis, v));
    }

    public static (double X, double Y) Point(int a, double t, double cx, double cy, double r)
    {
        double ang = (-90.0 + 60.0 * a) * 3.14159265358979 / 180.0;
        return (cx + Math.Cos(ang) * r * t, cy + Math.Sin(ang) * r * t);
    }
}
