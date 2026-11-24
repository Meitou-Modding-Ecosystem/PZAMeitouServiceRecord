// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The text of the Service Record window's pages (the plugin's SrPages.h), built from the stats only: Fighter, Squad, Command and Recent.
// Every visible text comes from the string table (fighter.* / squad.* / recent.* / cmd.* keys): whole lines with {n} placeholders.
using static PZA.ServiceRecord.Meitou.SrText;

namespace PZA.ServiceRecord.Meitou;

public static class SrPages
{
    static string N(double v) => SrStr.Num(v);

    static string T(string k) => SrStr.T(k);

    static string TS(string k, params string[] a) => SrStr.TS(k, a);

    public static double MeritOf(Stats s) => SrRanks.Merit(s);

    public static int RankOf(Stats s) => SrRanks.RankIndex(MeritOf(s));

    public static string Chevrons(int rank) => SrRanks.ChevronText(SrRanks.Ladder[Math.Clamp(rank, 0, SrRanks.Ladder.Length - 1)].Chevrons, 16);

    public static string RankName(int rank) => T(SrStr.After(S_RANK_0, rank >= 0 && rank < SrRanks.Ladder.Length ? rank : 0));

    public static string PostName(int post) => T(SrStr.After(S_POST_0, post >= 0 && post < SrCommand.PostCount ? post : 0));

    /// <summary>Fighter page: everything we know about one character, grouped.</summary>
    public static string FighterText(string name, Stats s, bool hasStats, int post = 0)
    {
        string t = (name.Length == 0 ? T(S_F_NO_NAME) : name) + "\n";
        if (post > SrCommand.PostNone && post < SrCommand.PostCount) t += TS(S_F_POST, PostName(post)) + "\n";
        t += "\n";
        if (!hasStats) return t + T(S_F_NO_COMBAT);
        {
            double merit = MeritOf(s);
            int r = SrRanks.RankIndex(merit);
            t += TS(S_F_RANK, RankName(r)) + "\n";
            t += TS(S_F_MERIT, N(Math.Floor(merit))) + "\n";
            t += (r + 1 < SrRanks.Ladder.Length ? TS(S_F_NEXT_RANK, N(SrRanks.Ladder[r + 1].MinMerit)) : T(S_F_TOP_RANK)) + "\n\n";
        }
        t += T(S_F_OFFENCE) + "\n";
        t += TS(S_F_KILLS, N(s.Kills)) + "\n";
        t += TS(S_F_KOS, N(s.KosDealt)) + "\n";
        t += TS(S_F_LIMBS, N(s.LimbsRemoved)) + "\n";
        t += TS(S_F_1HKO, N(s.OneHitKO)) + "\n";
        t += TS(S_F_1HKILL, N(s.OneHitKill)) + "\n";
        t += TS(S_F_DEALT, N(s.Dealt)) + "\n";
        t += TS(S_F_BIGGEST, N(s.Biggest)) + "\n";
        if (s.BiggestOn.Length > 0) t += TS(S_F_BIGGEST_ON, s.BiggestOn) + "\n";
        if (s.HasPartDmg)
        {
            t += "\n" + T(S_F_BY_PART) + "\n";
            t += TS(S_F_PART_HEAD, N(s.DmgHead)) + "\n";
            t += TS(S_F_PART_CHEST, N(s.DmgChest)) + "\n";
            t += TS(S_F_PART_STOMACH, N(s.DmgStomach)) + "\n";
            t += TS(S_F_PART_ARMS, N(s.DmgArms)) + "\n";
            t += TS(S_F_PART_LEGS, N(s.DmgLegs)) + "\n";
        }
        if (s.HasTargetBreakdown)
        {
            t += "\n" + T(S_F_BY_TARGET) + "\n";
            if (s.DmgHuman > 0 || s.KillsHuman > 0) t += TS(S_F_TGT_HUMAN, ServiceCore.Short(s.DmgHuman), N(s.KillsHuman)) + "\n";
            if (s.DmgRobot > 0 || s.KillsRobot > 0) t += TS(S_F_TGT_ROBOT, ServiceCore.Short(s.DmgRobot), N(s.KillsRobot)) + "\n";
            if (s.DmgAnimal > 0 || s.KillsAnimal > 0) t += TS(S_F_TGT_ANIMAL, ServiceCore.Short(s.DmgAnimal), N(s.KillsAnimal)) + "\n";
        }
        if (s.HasTurrets)
        {
            t += "\n" + T(S_F_TURRETS) + "\n";
            t += TS(S_F_KILLS, N(s.TurretKills)) + "\n";
            t += TS(S_F_KOS, N(s.TurretKOs)) + "\n";
            t += TS(S_F_LIMBS, N(s.TurretLimbs)) + "\n";
            t += TS(S_F_DEALT, N(s.TurretDealt)) + "\n";
        }
        t += "\n" + T(S_F_DEFENCE) + "\n";
        t += TS(S_F_TAKEN, N(s.Taken)) + "\n";
        t += TS(S_F_TIMESKO, SrStr.TP(S_TIMES_ONE, s.TimesKO)) + "\n";
        t += TS(S_F_BY_ENEMY, N(s.KosSuffered)) + "\n";
        t += TS(S_F_LIMBS_LOST, N(s.LimbsLost)) + "\n";
        t += "\n" + TS(S_F_FIGHTS, N(s.Fights)) + "\n";
        if (s.Slavery.Length > 0)
        {
            var stints = ServiceCore.ParseSlavery(s.Slavery);
            if (stints.Count > 0)
            {
                t += "\n" + T(S_F_SLAVERY) + "\n";
                foreach (var st in stints)
                {
                    if (st.EndDay == 0) t += TS(S_F_SLAVE_SINCE, st.Faction, N(st.StartDay)) + "\n";
                    else if (st.EndDay == st.StartDay) t += TS(S_F_SLAVE_DAY, st.Faction, N(st.StartDay)) + "\n";
                    else t += TS(S_F_SLAVE_SPAN, st.Faction, N(st.StartDay), N(st.EndDay)) + "\n";
                }
            }
        }
        if (s.FfHits != 0)
        {
            t += "\n" + T(S_F_FF) + "\n";
            t += TS(S_F_FF_HITS, N(s.FfHits)) + "\n";
            t += TS(S_F_FF_KOS, N(s.FfKOs), N(s.FfLimbs)) + "\n";
            t += TS(S_F_FF_DAMAGE, N(s.FfDamage)) + "\n";
        }
        if (s.KcImported) t += "\n" + T(S_F_KC) + "\n";
        return t;
    }

    public enum SortKey { Kills = 0, Kos, Dealt, Biggest, Taken, Ff, Count }

    public static string SortName(int k) => T(SrStr.After(S_SORT_KILLS, k is >= 0 and < (int)SortKey.Count ? k : 0));

    public static double SortValue(Stats s, int k) => (SortKey)k switch
    {
        SortKey.Kos => s.KosDealt,
        SortKey.Dealt => s.Dealt,
        SortKey.Biggest => s.Biggest,
        SortKey.Taken => s.Taken,
        SortKey.Ff => s.FfHits,
        _ => s.Kills,
    };

    public sealed class Row
    {
        public string Name = "";
        public Stats S = new();
        public bool Animal;
        public int Post;
    }

    /// <summary>Squad page: one line per squad member, best first by the chosen column (stable, as std::stable_sort).</summary>
    public static string SquadText(List<Row> rows, int key, int maxRows)
    {
        if (key < 0 || key >= (int)SortKey.Count) key = (int)SortKey.Kills;
        string t = TS(S_SQ_TITLE, SortName(key)) + "\n";
        t += rows.Any(r => r.Animal) ? SrStr.Colour(T(S_SQ_ANIMALS)) + "\n\n" : "\n";
        if (rows.Count == 0) return t + T(S_SQ_EMPTY);
        if (key == (int)SortKey.Ff)
        {
            rows = [.. rows.Where(r => r.S.FfHits > 0)];
            if (rows.Count == 0) return t + T(S_SQ_NO_FF);
        }
        var sorted = rows.OrderByDescending(r => SortValue(r.S, key)).ThenByDescending(r => r.S.KosDealt).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        for (int i = 0; i < sorted.Count && i < maxRows; i++)
        {
            var s = sorted[i].S;
            string stats = TS(S_SQ_STATS, N(s.Kills), N(s.KosDealt), ServiceCore.Short(s.Dealt), N(s.Biggest), ServiceCore.Short(s.Taken));
            if (s.FfHits != 0) stats += TS(S_SQ_FF, N(s.FfHits));
            string name = sorted[i].Animal ? Aar.Words.Mark('a') + sorted[i].Name + Aar.Words.Mark('n') : sorted[i].Name;
            if (sorted[i].Post > SrCommand.PostNone && sorted[i].Post < SrCommand.PostCount) name = TS(S_CM_WITH_POST, name, PostName(sorted[i].Post));
            t += TS(S_SQ_ROW, N(i + 1), name, stats) + "\n";
        }
        if (sorted.Count > maxRows) t += TS(S_SQ_MORE, N(sorted.Count - maxRows)) + "\n";
        return t;
    }

    public sealed class CmdPerson
    {
        public string Key = "", Name = "";
        public int Rank;
    }

    /// <summary>Command page: the selected character, then the tree.</summary>
    public static string CommandText(SrCommand.Tree tree, IReadOnlyList<CmdPerson> people, string selKey, string selName)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in people) { names[p.Key] = p.Name; ranks[p.Key] = p.Rank; }
        string t = T(S_CM_TITLE) + "\n" + SrStr.Colour(T(S_CM_NOTE)) + "\n\n";
        if (selKey.Length == 0) t += T(S_CM_NO_SEL) + "\n";
        else
        {
            var e = tree.Find(selKey);
            int post = e?.Post ?? 0;
            string ch = Chevrons(ranks.GetValueOrDefault(selKey));
            t += SrStr.Colour(TS(S_CM_SELECTED, selName)) + (ch.Length == 0 ? "" : "  " + ch) + "\n";
            t += TS(S_F_POST, PostName(post)) + "\n";
            string sup = T(S_CM_NOBODY);
            if (e is not null && e.Sup.Length > 0)
                sup = tree.LinkLive(selKey) && names.TryGetValue(e.Sup, out var sn) ? TS(S_CM_WITH_POST, sn, PostName(tree.PostOf(e.Sup))) : T(S_CM_AWAY);
            t += TS(S_CM_REPORTS_TO, sup) + "\n";
            var kids = tree.Reports(selKey);
            if (kids.Count > 0) t += TS(S_CM_LEADS, N(kids.Count)) + "\n";
        }
        t += "\n";
        int shown = 0;
        foreach (var l in tree.Lines(names))
        {
            if (shown >= 400) break;
            if (!names.TryGetValue(l.Key, out var who)) continue;
            ++shown;
            string row = string.Concat(Enumerable.Repeat("--", l.Depth)) + (l.Depth != 0 ? " " : "");
            string ch = Chevrons(ranks.GetValueOrDefault(l.Key));
            if (l.Key == selKey) who = Aar.Words.Mark('y') + who + Aar.Words.Mark('n');
            if (l.Away) row += SrStr.Colour(TS(S_CM_ROW_AWAY, who));
            else if (l.Post > SrCommand.PostNone) row += TS(S_CM_ROW, PostName(l.Post), who);
            else row += who;
            if (ch.Length > 0) row += "  " + ch;
            t += row + "\n";
        }
        if (shown == 0) t += T(S_CM_EMPTY) + "\n";
        int placed = people.Count(p => tree.Find(p.Key) is { } e && (e.Post > 0 || e.Sup.Length > 0));
        if (people.Count > placed) t += "\n" + TS(S_CM_UNPLACED, N(people.Count - placed)) + "\n";
        return t;
    }

    /// <summary>Recent page: this session's combat events in the order they happened.</summary>
    public static string RecentText(IReadOnlyCollection<string> ring)
    {
        string t = T(S_RC_TITLE) + "\n\n";
        if (ring.Count == 0) return t + T(S_RC_EMPTY);
        foreach (var r in ring) t += r + "\n";
        return t;
    }

    public static string Clock(double sec) => Aar.Words.Clock(sec);
}
