// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The After-Action Reports in the game (the plugin's AfterHit / NoteFactionsAndPlace / OnEvent / main-loop report work, one for one):
//   every hit goes to the engine with the game's own flags (whose side, the victim leads its squad, a shot aimed or a stray); each fighter's
//   faction once; the battle's place (the town one of ours stands in, and whose it is); the game clock sampled once a second; kills and
//   deaths of wounds for the aftermath; the Recent page's lines; when a report is written: a fight for each of ours in it, the report into the
//   log ("AAR| ..."), and into the archive. Everything here runs under SrSystem.Lock.
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Population;
using PZA.ServiceRecord.Meitou.Aar;

namespace PZA.ServiceRecord.Meitou;

public static class SrReports
{
    #region State

    public static readonly Engine Aar = new();
    public static readonly SrArchive Archive = new();
    public static readonly LinkedList<string> Ring = new();
    public static long RecentSkipped, FightsCounted, FightsNotMine, PlaceReads, FactionReads;
    public static bool RecentAll;
    static long s_placeLogs, s_written, s_merged;
    static int s_fightReport = -1;
    static readonly HashSet<ulong> s_fightCounted = [];
    static string s_saveName = "";

    /// <summary>The save last loaded or written (the archive header's "save 'X'"; the plugin reads Kenshi's SaveManager).</summary>
    public static string SaveName { get => Volatile.Read(ref s_saveName); set => Volatile.Write(ref s_saveName, value); }

    #endregion

    public static void Configure(SrSettings s)
    {
        Aar.Advice = s.ReportAdvice;
        GameClock.DayOffset = s.GameDayOffset;
        Archive.On = s.ReportArchive;
        Archive.MaxBytes = s.ReportArchiveMaxMB > 0 ? s.ReportArchiveMaxMB * 1024.0 * 1024.0 : 0;
        RecentAll = s.RecentAll;
    }

    #region Hits

    /// <summary>One hit (the plugin's AfterHit, report half).</summary>
    public static void OnHit(World world, CharacterCold? attacker, CharacterCold? victim, ulong a, ulong v, Snapshot before, Snapshot after, bool ranged,
        bool friendly, int vLeader, int purpose, string aName, string vName, double now)
    {
        var x = Hit.New();
        x.T = now;
        x.A = a;
        x.V = v;
        x.H0 = before.Health;
        x.H1 = after.Health;
        x.Dn0 = before.Down;
        x.Dn1 = after.Down;
        x.Dd0 = before.Dead;
        x.Dd1 = after.Dead;
        x.Lb0 = before.Severed;
        x.Lb1 = after.Severed;
        x.Ranged = ranged;
        x.Ff = friendly;
        x.VLeader = (sbyte)vLeader;
        x.Purpose = (sbyte)purpose;
        if (a != 0) x.Am = (sbyte)(attacker is null ? -1 : attacker.IsPlayer ? 1 : 0);
        if (v != 0) x.Vm = (sbyte)(victim is null ? -1 : victim.IsPlayer ? 1 : 0);
        if (Aar.Side.Length == 0)
        {
            var mine = x.Am == 1 ? attacker : x.Vm == 1 ? victim : null;
            if (mine is not null && FactionName(world, mine.Faction) is { Length: > 0 } fn)
            {
                Aar.Side = fn;
                SrLog.Line($"AAR: our side is '{fn}' (from the game)");
                SeedReportNumber();
            }
        }
        if (!before.Valid || !after.Valid) return;
        Aar.OnHit(x, a != 0 ? aName : "", vName);
        NoteFactionsAndPlace(world, attacker, victim, a, v, x);
    }

    static void NoteFactionsAndPlace(World world, CharacterCold? attacker, CharacterCold? victim, ulong a, ulong v, in Hit x)
    {
        string theirFaction = "";
        var who = new[] { (attacker, a, x.Am), (victim, v, x.Vm) };
        foreach (var (c, id, side) in who)
        {
            if (c is null) continue;
            string fn = FactionName(world, c.Faction);
            if (!Aar.KnowsFaction(id) && fn.Length > 0) { Aar.SetFaction(id, fn); ++FactionReads; }
            if (side != 1 && theirFaction.Length == 0 && fn.Length > 0) theirFaction = fn;
        }
        if (Aar.PlaceSettled || !Aar.InBattle) return;
        ulong ours = x.Am == 1 ? a : x.Vm == 1 ? v : 0;
        if (ours == 0) return;
        ++PlaceReads;
        if (!TownOf(world, SrBridge.IdOf(ours), out string town, out string owner, out bool player)) { Aar.SetPlace("", 0, ""); return; }
        int kind = player ? 1 : theirFaction.Length > 0 && theirFaction == owner ? 2 : 3;
        Aar.SetPlace(town, kind, owner);
        if (s_placeLogs++ < 20)
            SrLog.Line($"AAR: battle place '{town}', owned by '{owner}' ({(kind == 1 ? "ours" : kind == 2 ? "THEIRS: our assault" : "someone else's")}; their side '{theirFaction}')");
    }

    #endregion

    #region The game's facts

    static PopulationData? DataOf(World world) => world.Systems.OfType<PopulationSystem>().FirstOrDefault()?.Data;

    public static string FactionName(World world, int f) =>
        DataOf(world) is { } d && f >= 0 && f < d.Factions.Count ? d.Factions[f].Name : "";

    /// <summary>The plugin's Character::getCurrentTownLocation: the nearest placed town (not a nest marker) or player outpost whose radius holds
    /// the character (Meitou's AiSystem.TownAt rule), its name and its owner's faction.</summary>
    public static bool TownOf(World world, CharacterId id, out string name, out string owner, out bool player)
    {
        name = owner = "";
        player = false;
        if (DataOf(world) is not { } data || !world.Characters.TryResolveNext(id, out int slot)) return false;
        var p = world.Characters.Previous[slot].Position;
        var at = new Vector2(p.X, p.Z);
        float bestD = float.MaxValue, bestR = 0;
        int bestSite = -1;
        object? bestTown = null;
        foreach (var site in data.Sites)
        {
            if (site.Town.Type is global::Meitou.Data.Gameplay.TownType.NestMarker or global::Meitou.Data.Gameplay.TownType.Null) continue;
            float d = Vector2.DistanceSquared(at, new Vector2(site.Position.X, site.Position.Z));
            if (d < bestD) (bestSite, bestTown, bestD, bestR) = (site.Index, null, d, site.Town.Radius);
        }
        foreach (var town in world.Buildings.Towns)
        {
            float d = Vector2.DistanceSquared(at, new Vector2(town.Position.X, town.Position.Z));
            if (d < bestD) (bestSite, bestTown, bestD, bestR) = (-1, town, d, town.Radius);
        }
        if (bestD > bestR * bestR) return false;
        int ownerFaction;
        if (bestTown is global::Meitou.Simulation.Construction.PlayerTown pt) { name = pt.Name; ownerFaction = pt.Owner; }
        else
        {
            name = data.Sites[bestSite].Town.Name;
            ownerFaction = bestSite < data.SiteFactions.Count ? data.SiteFactions[bestSite] : -1;
        }
        owner = FactionName(world, ownerFaction);
        player = ownerFaction >= 0 && ownerFaction == world.Player.Faction;
        return true;
    }

    #endregion

    #region Events, the Recent page

    /// <summary>The core's events: kills / deaths of wounds into the reports; the Recent page's lines (ours only unless RecentAll).</summary>
    public static void OnEvent(World world, string kind, ulong attacker, ulong victim, float value, double now)
    {
        Aar.OnEvent(kind, now, attacker, victim);
        bool want = kind is "kill" or "koDealt" or "limbRemoved" or "oneHitKO" or "oneHitKill" or "friendlyFire" or "diedOfWounds";
        if (!want) return;
        var ac = attacker != 0 ? SrBridge.Cold(world, SrBridge.IdOf(attacker)) : null;
        var vc = victim != 0 ? SrBridge.Cold(world, SrBridge.IdOf(victim)) : null;
        if (!RecentAll && !(ac?.IsPlayer ?? false) && !(vc?.IsPlayer ?? false)) { ++RecentSkipped; return; }
        string a = attacker != 0 ? ac?.Name ?? "?" : SrStr.T(SrText.S_RC_SOMEONE_A);
        string v = victim != 0 ? vc?.Name ?? "?" : SrStr.T(SrText.S_RC_SOMEONE_V);
        string line = kind switch
        {
            "kill" => SrStr.TS(SrText.S_RC_KILL, a, v),
            "koDealt" => SrStr.TS(SrText.S_RC_KO, a, v),
            "limbRemoved" => SrStr.TS(SrText.S_RC_LIMB, a, v),
            "oneHitKO" => SrStr.TS(SrText.S_RC_1HKO, a, v),
            "oneHitKill" => SrStr.TS(SrText.S_RC_1HKILL, a, v),
            "friendlyFire" => Words.Mark('g') + SrStr.TS(SrText.S_RC_FF, a, v, SrStr.Num(value)),
            _ => Words.Mark('d') + SrStr.TS(SrText.S_RC_WOUNDS, a, v),
        };
        RingPush(line, now);
    }

    static void RingPush(string line, double now)
    {
        string gs = GameStamp(now);
        Ring.AddLast("[" + Words.Clock(now) + (gs.Length == 0 ? "" : " | " + gs) + "]  " + line);
        while (Ring.Count > 80) Ring.RemoveFirst();
    }

    /// <summary>"Day 413, 07:40" (or "" when unknown), the plugin's aar::GameStamp.</summary>
    public static string GameStamp(double t) => GameClock.HoursAt(t, out double h) ? $"Day {GameClock.Day(h)}, {GameClock.HHMM(h)}" : "";

    #endregion

    #region Each tick

    /// <summary>The plugin's main-loop report work: the game clock, closing a battle, and what follows a report.</summary>
    public static void Tick(World world, double now)
    {
        if (world.GameHours >= 0) GameClock.Sample(now, world.GameHours);
        Aar.Tick(now);
        long written = Aar.BattlesClosed - Aar.BattlesTooSmall;
        if (written <= s_written || Aar.LastReport is not { } r) return;
        s_written = written;
        CountFights(world, r);
        string fired = string.Join(", ", r.Fired);
        bool wasMerged = Aar.ReportsMerged > s_merged;
        if (wasMerged)
        {
            s_merged = Aar.ReportsMerged;
            SrLog.Line($"AAR: a later fight joined report No. {r.Number} (mop-up), report rewritten ({Aar.ReportsMerged} merged this session; moments: {fired})");
        }
        else
            SrLog.Line($"AAR: battle closed, report written ({Aar.Reports.Count} reports this session; moments: {fired}; {Aar.HitsNotOurs} hits in fights without us skipped; {RecentSkipped} Recent-page events without us left out)");
        string t = Words.StripColour(r.Text(true));
        int n = 0;
        foreach (var line in t.Split('\n'))
        {
            if (n++ >= 80) break;
            if (line.Length > 0) SrLog.Line("AAR| " + line);
        }
        if (Archive.On) KeepInArchive(r, t, wasMerged);
    }

    static void CountFights(World world, Report r)
    {
        if (r.Number != s_fightReport) { s_fightReport = r.Number; s_fightCounted.Clear(); }
        foreach (ulong id in r.Ours)
        {
            if (id == 0 || !s_fightCounted.Add(id)) continue;
            if (SrBridge.Cold(world, SrBridge.IdOf(id)) is not { IsPlayer: true }) { ++FightsNotMine; continue; }
            if (SrSystem.Core.AddFight(id)) ++FightsCounted;
        }
    }

    static void KeepInArchive(Report r, string text, bool merged)
    {
        var res = Archive.Append(Aar.Side, SrArchive.Header(r.Number, SaveName, merged), text, out string path);
        if (res == SrArchive.Result.Written) SrLog.Line($"AAR: report No. {r.Number} kept in '{path}' ({Archive.WrittenCount} kept this session)");
        else if (res == SrArchive.Result.Full && !Archive.FullLogged)
        {
            Archive.FullLogged = true;
            SrLog.Line($"AAR: the report archive reached ReportArchiveMaxMB - no more reports kept this session (raise it in the settings, or move old files out of '{Archive.Dir}')");
        }
        else if (res == SrArchive.Result.Failed) SrLog.Line($"AAR: could not write report No. {r.Number} to the archive folder '{Archive.Dir}' ({Archive.FailedCount} failures)");
    }

    static void SeedReportNumber()
    {
        if (!Archive.On) return;
        int top = SrArchive.HighestNumber(Archive.Dir, Aar.Side);
        if (top > Aar.ReportNumber) Aar.ReportNumber = top;
        SrLog.Line($"AAR: report numbers carry on from the archive: {top} kept for '{Aar.Side}', the next new report is No. {Aar.ReportNumber + 1}");
    }

    #endregion

    #region Plurals file

    /// <summary>PZAServiceRecord_plurals.txt beside the mod: one "singular=plural" per line (the plugin's LoadPlurals).</summary>
    public static void LoadPlurals(string folder)
    {
        string p = Path.Combine(folder, "PZAServiceRecord_plurals.txt");
        if (!File.Exists(p)) return;
        int n = 0;
        foreach (var raw in File.ReadLines(p))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            Words.AddPlural(line[..eq].Trim(), line[(eq + 1)..].Trim());
            if (++n >= 500) break;
        }
        SrLog.Line($"plurals: {n} from {Path.GetFileName(p)}");
    }

    #endregion
}
