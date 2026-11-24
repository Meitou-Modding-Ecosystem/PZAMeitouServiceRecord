// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// A character's record lives on the character's own CharacterStats object (a ConditionalWeakTable), so it follows the character wherever Meitou
// keeps it (loaded, stored in an unloaded squad, saved), with no id mapping. It is saved in the character's STATS record under the RE_Kenshi
// plugin's own keys (pza_* floats, pza_biggest_on string): the same save format, so a save carries its history between the two games.
// Meitou keeps only the stats it knows when it reads a STATS record (CharacterStats.ReadSave), so two patches carry ours: SavedCharacters.Read / Again
// (reading a saved character) and SaveCapture.WriteBody (writing one). Rules of the plugin's LoadHook / SerHook, kept as they are.
using System.Runtime.CompilerServices;
using Meitou.Simulation.Bodies;

namespace PZA.ServiceRecord.Meitou;

public static class SrSave
{
    static readonly ConditionalWeakTable<CharacterStats, Stats> s_records = new();
    static readonly object s_lock = new();

    /// <summary>Counters of the plugin's save / load bridge (its "saves ... loads ... kcImports" counters line).</summary>
    public static long Saves, SavesSkippedEmpty, SaveNoRecord, Loads, KcImports, KcSkipped, BaselinesSet;

    /// <summary>Imports Kill Counter's values once per character (setting ImportKillCounter).</summary>
    public static bool ImportKc = true;

    /// <summary>The record on <paramref name="stats"/>; null when it has none.</summary>
    public static Stats? Of(CharacterStats? stats)
    {
        if (stats is null) return null;
        lock (s_lock) return s_records.TryGetValue(stats, out var s) ? s : null;
    }

    /// <summary>Hangs <paramref name="record"/> on <paramref name="stats"/> (the core's own object, so both always agree).</summary>
    public static void Attach(CharacterStats stats, Stats record)
    {
        lock (s_lock) s_records.AddOrUpdate(stats, record);
    }

    #region The chain of command keys (the plugin's Military Manager: pza_post, pza_form, pza_sup, pza_hid)

    /// <summary>A character's chain-of-command keys exactly as a save had them (the RE_Kenshi plugin's v0.8.0 Military Manager writes them). The
    /// Meitou version has no Military Manager yet (0.2.0): it shows the post and carries all four keys back into every save of the character,
    /// value for value, so a save the RE_Kenshi plugin wrote keeps them (0.1.4). Meitou reuses a loaded character's STATS record when it saves, but
    /// a character it writes into a fresh record (moved to another squad) would lose them.</summary>
    public sealed class Command
    {
        public float? Post, Form;
        public string? Sup, Hid;

        /// <summary>The post as the plugin reads it (pza_post above 0.5 and below 100, rounded); 0 = none.</summary>
        public int PostNumber => Post is float f && f > 0.5f && f < 100.0f ? (int)(f + 0.5f) : 0;
    }

    static readonly ConditionalWeakTable<CharacterStats, Command> s_commands = new();

    /// <summary>The kept chain-of-command keys on <paramref name="stats"/>; null when the save had none.</summary>
    public static Command? CommandOf(CharacterStats? stats)
    {
        if (stats is null) return null;
        lock (s_lock) return s_commands.TryGetValue(stats, out var c) ? c : null;
    }

    public static void AttachCommand(CharacterStats stats, Command command)
    {
        lock (s_lock) s_commands.AddOrUpdate(stats, command);
    }

    /// <summary>Counters: characters read with chain-of-command keys, and saves that carried them back.</summary>
    public static long CommandsRead, CommandsWritten;

    /// <summary>The four keys from a saved STATS record, exactly as they are (null: none of them).</summary>
    public static Command? ReadCommand(IReadOnlyDictionary<string, float> f, IReadOnlyDictionary<string, string> str)
    {
        var c = new Command();
        if (f.TryGetValue("pza_post", out float v)) c.Post = v;
        if (f.TryGetValue("pza_form", out v)) c.Form = v;
        if (str.TryGetValue("pza_sup", out var t)) c.Sup = t;
        if (str.TryGetValue("pza_hid", out t)) c.Hid = t;
        return c.Post is null && c.Form is null && c.Sup is null && c.Hid is null ? null : c;
    }

    /// <summary>The kept keys back into a STATS record, each one that was read, unchanged.</summary>
    public static void WriteCommand(IDictionary<string, float> f, IDictionary<string, string> str, Command c)
    {
        if (c.Post is float p) f["pza_post"] = p;
        if (c.Form is float m) f["pza_form"] = m;
        if (c.Sup is { } s) str["pza_sup"] = s;
        if (c.Hid is { } h) str["pza_hid"] = h;
    }

    #endregion

    #region Reading (the plugin's LoadHook)

    /// <summary>Reads our keys from a saved STATS record into a record (null: nothing of ours, and no Kill Counter import). Logs as the plugin.</summary>
    public static Stats? Read(IReadOnlyDictionary<string, float> f, IReadOnlyDictionary<string, string> str, string name)
    {
        var s = new Stats();
        bool any = false;
        bool R(string k, out float v) => f.TryGetValue(k, out v);
        static int I(float v) => (int)(v + 0.5f);

        if (R("pza_dealt", out float v)) { s.Dealt = v; any = true; }
        if (R("pza_taken", out v)) { s.Taken = v; any = true; }
        if (R("pza_biggest", out v)) { s.Biggest = v; any = true; }
        if (R("pza_1hit_ko", out v)) { s.OneHitKO = I(v); any = true; }
        if (R("pza_1hit_kill", out v)) { s.OneHitKill = I(v); any = true; }
        if (R("pza_kos_suffered", out v)) { s.KosSuffered = I(v); any = true; }
        if (R("pza_times_ko", out v)) { s.TimesKO = I(v); any = true; }
        if (R("pza_limbs_lost", out v)) { s.LimbsLost = I(v); any = true; }
        if (R("pza_kills", out v)) { s.Kills = I(v); any = true; }
        if (R("pza_kos_dealt", out v)) { s.KosDealt = I(v); any = true; }
        if (R("pza_limbs_removed", out v)) { s.LimbsRemoved = I(v); any = true; }
        if (R("pza_kc_imported", out v) && v > 0.5f) { s.KcImported = true; any = true; }
        if (R("pza_ff_hits", out v)) { s.FfHits = I(v); any = true; }
        if (R("pza_ff_kos", out v)) { s.FfKOs = I(v); any = true; }
        if (R("pza_ff_limbs", out v)) { s.FfLimbs = I(v); any = true; }
        if (R("pza_ff_dmg", out v)) { s.FfDamage = v; any = true; }
        if (R("pza_turret_dealt", out v)) { s.TurretDealt = v; any = true; }
        if (R("pza_turret_kills", out v)) { s.TurretKills = I(v); any = true; }
        if (R("pza_turret_kos", out v)) { s.TurretKOs = I(v); any = true; }
        if (R("pza_turret_limbs", out v)) { s.TurretLimbs = I(v); any = true; }
        if (R("pza_dmg_head", out v)) { s.DmgHead = v; any = true; }
        if (R("pza_dmg_chest", out v)) { s.DmgChest = v; any = true; }
        if (R("pza_dmg_stomach", out v)) { s.DmgStomach = v; any = true; }
        if (R("pza_dmg_arms", out v)) { s.DmgArms = v; any = true; }
        if (R("pza_dmg_legs", out v)) { s.DmgLegs = v; any = true; }
        if (R("pza_dmg_human", out v)) { s.DmgHuman = v; any = true; }
        if (R("pza_dmg_robot", out v)) { s.DmgRobot = v; any = true; }
        if (R("pza_dmg_animal", out v)) { s.DmgAnimal = v; any = true; }
        if (R("pza_kills_human", out v)) { s.KillsHuman = I(v); any = true; }
        if (R("pza_kills_robot", out v)) { s.KillsRobot = I(v); any = true; }
        if (R("pza_kills_animal", out v)) { s.KillsAnimal = I(v); any = true; }
        if (str.TryGetValue("pza_slavery", out var sl) && sl.Length > 0)
        {
            s.Slavery = sl;
            if (ServiceCore.SanitizeSlavery(sl).Length > 0) any = true;
        }

        // One-time import of Kill Counter's numbers from THIS save (its STATS floats "kills", "kos", "limbs").
        if (ImportKc && !s.KcImported)
        {
            bool hk = R("kills", out float k), hko = R("kos", out float ko), hl = R("limbs", out float lb);
            if (ServiceCore.ImportKillCounter(hk, k, hko, ko, hl, lb, s, out string why))
            {
                any = true;
                Interlocked.Increment(ref KcImports);
                SrLog.Line($"imported Kill Counter stats for {name}: kills {k:0}, KOs {ko:0}, limbs removed {lb:0} (once; saved with the character)");
            }
            else if (hk || hko || hl)
            {
                Interlocked.Increment(ref KcSkipped);
                SrLog.Line($"Kill Counter import skipped for {name}: {why} (kills {(hk ? "yes" : "no")}, kos {(hko ? "yes" : "no")}, limbs {(hl ? "yes" : "no")})");
            }
        }
        if (!any) return null;

        // v0.6.8: a save with fight counting reads its baseline; an older one starts counting now (its record = the baseline)
        if (R("pza_fb", out float fb) && fb > 0.5f)
        {
            if (R("pza_fights", out v)) s.Fights = I(v);
            if (R("pza_fb_dealt", out v)) s.BDealt = v;
            if (R("pza_fb_kills", out v)) s.BKills = I(v);
            if (R("pza_fb_kos", out v)) s.BKOs = I(v);
            if (R("pza_fb_limbs", out v)) s.BLimbs = I(v);
            if (R("pza_fb_1ko", out v)) s.B1KO = I(v);
            if (R("pza_fb_1kill", out v)) s.B1Kill = I(v);
        }
        else
        {
            s.SetBaselineToNow();
            Interlocked.Increment(ref BaselinesSet);
        }
        if (str.TryGetValue("pza_biggest_on", out var on)) s.BiggestOn = on.Length > 200 ? on[..200] : on;
        if (Sanitize(s) is { Length: > 0 } fixedWhat)
        {
            Interlocked.Increment(ref Corrected);
            SrLog.Line($"saved record of {name}: corrected out-of-range values ({fixedWhat}); a broken or edited save");
        }
        Interlocked.Increment(ref Loads);
        return s;
    }

    /// <summary>Records loaded with an out-of-range value corrected (fuzz 2026-11-18).</summary>
    public static long Corrected;

    /// <summary>A save is outside data: NaN, infinite, negative or huge values (a broken or hand-edited save, another tool) become 0 or the nearest
    /// sane value, so no report prints NaN and no count runs backwards. The mod itself never writes such values. Returns what was corrected.</summary>
    public static string Sanitize(Stats s)
    {
        var what = new List<string>();
        double D(double v, string n) { if (double.IsFinite(v) && v >= 0 && v <= 1e12) return v; what.Add(n); return 0; }
        int C(int v, string n) { if (v >= 0 && v <= 100_000_000) return v; what.Add(n); return v < 0 ? 0 : 100_000_000; }
        s.Dealt = D(s.Dealt, "dealt"); s.Taken = D(s.Taken, "taken"); s.Biggest = (float)D(s.Biggest, "biggest"); s.FfDamage = D(s.FfDamage, "ff damage");
        s.OneHitKO = C(s.OneHitKO, "1-hit KOs"); s.OneHitKill = C(s.OneHitKill, "1-hit kills"); s.KosSuffered = C(s.KosSuffered, "KOs suffered");
        s.TimesKO = C(s.TimesKO, "times KO"); s.LimbsLost = C(s.LimbsLost, "limbs lost"); s.Kills = C(s.Kills, "kills"); s.KosDealt = C(s.KosDealt, "KOs dealt");
        s.LimbsRemoved = C(s.LimbsRemoved, "limbs removed"); s.FfHits = C(s.FfHits, "ff hits"); s.FfKOs = C(s.FfKOs, "ff KOs"); s.FfLimbs = C(s.FfLimbs, "ff limbs");
        s.Fights = C(s.Fights, "fights");
        s.TurretDealt = D(s.TurretDealt, "turret dealt");
        s.TurretKills = C(s.TurretKills, "turret kills"); s.TurretKOs = C(s.TurretKOs, "turret KOs"); s.TurretLimbs = C(s.TurretLimbs, "turret limbs");
        s.DmgHead = D(s.DmgHead, "part head"); s.DmgChest = D(s.DmgChest, "part chest"); s.DmgStomach = D(s.DmgStomach, "part stomach");
        s.DmgArms = D(s.DmgArms, "part arms"); s.DmgLegs = D(s.DmgLegs, "part legs");
        s.DmgHuman = D(s.DmgHuman, "target human"); s.DmgRobot = D(s.DmgRobot, "target robot"); s.DmgAnimal = D(s.DmgAnimal, "target animal");
        s.KillsHuman = C(s.KillsHuman, "kills human"); s.KillsRobot = C(s.KillsRobot, "kills robot"); s.KillsAnimal = C(s.KillsAnimal, "kills animal");
        string cleanSlavery = ServiceCore.SanitizeSlavery(s.Slavery);
        if (cleanSlavery != s.Slavery) { s.Slavery = cleanSlavery; what.Add("slavery"); }
        if (s.Biggest > s.Dealt) { s.Biggest = (float)s.Dealt; what.Add("biggest above dealt"); }
        // the fight baseline is part of the record: never above it, never negative
        double bd = Math.Min(D(s.BDealt, "baseline"), s.Dealt);
        int B(int v, int max) => Math.Clamp(v, 0, max);
        int[] before = [s.BKills, s.BKOs, s.BLimbs, s.B1KO, s.B1Kill];
        (s.BKills, s.BKOs, s.BLimbs, s.B1KO, s.B1Kill) = (B(s.BKills, s.Kills), B(s.BKOs, s.KosDealt), B(s.BLimbs, s.LimbsRemoved), B(s.B1KO, s.OneHitKO), B(s.B1Kill, s.OneHitKill));
        if (bd != s.BDealt || !before.SequenceEqual([s.BKills, s.BKOs, s.BLimbs, s.B1KO, s.B1Kill])) what.Add("fight baseline");
        s.BDealt = bd;
        return string.Join(", ", what.Distinct());
    }

    #endregion

    #region Writing (the plugin's SerHook)

    /// <summary>Writes a record into a STATS record's floats and strings, key for key as the plugin.</summary>
    public static void Write(IDictionary<string, float> f, IDictionary<string, string> str, Stats s)
    {
        f["pza_dealt"] = (float)s.Dealt;
        f["pza_taken"] = (float)s.Taken;
        f["pza_biggest"] = s.Biggest;
        f["pza_1hit_ko"] = s.OneHitKO;
        f["pza_1hit_kill"] = s.OneHitKill;
        f["pza_kos_suffered"] = s.KosSuffered;
        f["pza_times_ko"] = s.TimesKO;
        f["pza_limbs_lost"] = s.LimbsLost;
        f["pza_kills"] = s.Kills;
        f["pza_kos_dealt"] = s.KosDealt;
        f["pza_limbs_removed"] = s.LimbsRemoved;
        if (s.KcImported) f["pza_kc_imported"] = 1.0f;

        // v0.6.8: fights + the record when fight counting began (the flag marks a save that has them)
        f["pza_fights"] = s.Fights;
        f["pza_fb"] = 1.0f;
        f["pza_fb_dealt"] = (float)s.BDealt;
        f["pza_fb_kills"] = s.BKills;
        f["pza_fb_kos"] = s.BKOs;
        f["pza_fb_limbs"] = s.BLimbs;
        f["pza_fb_1ko"] = s.B1KO;
        f["pza_fb_1kill"] = s.B1Kill;
        if (s.FfHits != 0)
        {
            f["pza_ff_hits"] = s.FfHits;
            f["pza_ff_kos"] = s.FfKOs;
            f["pza_ff_limbs"] = s.FfLimbs;
            f["pza_ff_dmg"] = (float)s.FfDamage;
        }
        if (s.HasTurrets)
        {
            f["pza_turret_dealt"] = (float)s.TurretDealt;
            f["pza_turret_kills"] = s.TurretKills;
            f["pza_turret_kos"] = s.TurretKOs;
            f["pza_turret_limbs"] = s.TurretLimbs;
        }
        if (s.HasPartDmg)
        {
            f["pza_dmg_head"] = (float)s.DmgHead;
            f["pza_dmg_chest"] = (float)s.DmgChest;
            f["pza_dmg_stomach"] = (float)s.DmgStomach;
            f["pza_dmg_arms"] = (float)s.DmgArms;
            f["pza_dmg_legs"] = (float)s.DmgLegs;
        }
        if (s.HasTargetBreakdown)
        {
            f["pza_dmg_human"] = (float)s.DmgHuman;
            f["pza_dmg_robot"] = (float)s.DmgRobot;
            f["pza_dmg_animal"] = (float)s.DmgAnimal;
            f["pza_kills_human"] = s.KillsHuman;
            f["pza_kills_robot"] = s.KillsRobot;
            f["pza_kills_animal"] = s.KillsAnimal;
        }
        str["pza_biggest_on"] = s.BiggestOn;
        if (s.Slavery.Length > 0) str["pza_slavery"] = s.Slavery;
    }

    #endregion
}
