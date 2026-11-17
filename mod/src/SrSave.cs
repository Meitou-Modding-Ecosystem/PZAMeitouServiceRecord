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
        if (str.TryGetValue("pza_biggest_on", out var on)) s.BiggestOn = on;
        Interlocked.Increment(ref Loads);
        return s;
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
        str["pza_biggest_on"] = s.BiggestOn;
    }

    #endregion
}
