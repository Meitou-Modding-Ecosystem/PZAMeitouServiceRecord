// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The tick system (registered at SystemAnchor.End): the plugin's hit hooks and main-loop work.
//   HitResolved   once per landed hit (melee, projectile, execution): the prefix's "before", the victim's state now ("after"), into the core.
//   SlowWorld     once per tick: game time (ticks / 30 = the plugin's game seconds at 1x), characters gone away forgotten, the core's frame
//                 (settles, kill credit, the KO poll), records hung on stats objects, the counters line.
// The core is shared with the window (host thread), so every use of it takes s_lock. Display only: nothing here changes the world.
using System.Diagnostics;
using System.Globalization;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace PZA.ServiceRecord.Meitou;

public sealed class SrSystem : ICombatHitObserver
{
    #region State

    public static readonly ServiceCore Core = new();
    public static readonly object Lock = new();
    public static SrSettings Settings = new();
    public static bool Active;

    /// <summary>The plugin's "b" counters (bridge side), for the counters line.</summary>
    public static long HitLines, EventLines, UnloadForgets, Attached, Replaced, UiLines;

    static World? s_world;
    static double s_now;
    static string s_lastCounterLine = "";
    static readonly Stopwatch s_real = Stopwatch.StartNew();
    static double s_nextCounterLog;

    /// <summary>Game seconds at 1x, the plugin's g_now.</summary>
    public static double Now => s_now;

    public const double TicksPerSecond = 30;

    #endregion

    #region Hits

    public void HitResolved(World world, in CombatHit hit)
    {
        if (!Active) return;
        FollowWorld(world);
        var b = SrBridge.TakeBefore(hit.Victim.Slot, world.Tick);
        var before = b?.Snap ?? default;
        var victimCold = SrBridge.Cold(world, hit.Victim);
        var after = SrBridge.Read(victimCold);
        var attackerCold = SrBridge.Cold(world, hit.Attacker);
        string vname = victimCold?.Name ?? "?";
        ulong a = SrBridge.Key(hit.Attacker), v = SrBridge.Key(hit.Victim);
        bool ranged = hit.Source == CombatHitSource.Projectile;
        bool friendly = b?.Friendly ?? false;
        double now = world.Tick / TicksPerSecond;
        if (now > s_now) s_now = now;

        lock (Lock)
        {
            EnsureLoaded(a, attackerCold);
            EnsureLoaded(v, victimCold);
            long down0 = Core.N.Downings, fresh0 = Core.N.FreshDownings;
            Core.OnHit(a, v, before, after, s_now, vname, ranged, friendly);
            Hang(a, attackerCold);
            Hang(v, victimCold);
            int purpose = ranged ? (hit.Intended ? 1 : 0) : -1;   // the plugin: iShotYou's "on purpose" flag; Meitou: the shot's own target
            SrReports.OnHit(world, attackerCold, victimCold, a, v, before, after, ranged, friendly, b?.VictimLeader ?? -1, purpose,
                attackerCold?.Name ?? "", vname, s_now);
            LogHit(world, hit, b, before, after, attackerCold?.Name ?? "-", vname, ranged, friendly, down0, fresh0);
        }
    }

    /// <summary>The plugin's hit log: the "aar hit" corpus line for every hit (HitLog), full detail for the first HitLogFirst and every downing.</summary>
    static void LogHit(World world, in CombatHit hit, SrBridge.Before? b, Snapshot before, Snapshot after, string aname, string vname, bool ranged,
        bool friendly, long down0, long fresh0)
    {
        int am = Mine(world, hit.Attacker), vm = Mine(world, hit.Victim);
        if (Settings.HitLog)
            SrLog.Line(string.Create(CultureInfo.InvariantCulture,
                $"aar hit t={s_now:0.00} a={SrBridge.Key(hit.Attacker):x}|{aname} v={SrBridge.Key(hit.Victim):x}|{vname} h={before.Health:0.0}>{after.Health:0.0} " +
                $"dn={B(before.Down)}>{B(after.Down)} dd={B(before.Dead)}>{B(after.Dead)} lb={before.Severed}>{after.Severed} r={B(ranged)} ff={B(friendly)} " +
                $"vl={b?.VictimLeader ?? -1} op={(ranged ? (hit.Intended ? 1 : 0) : -1)} am={am} vm={vm}"));
        bool downing = Core.N.Downings != down0;
        if (HitLines >= Settings.HitLogFirst && !(downing && Settings.EventLog)) return;
        HitLines++;
        string what = ranged ? "ranged" : hit.Source == CombatHitSource.Execution ? "execution" : "melee";
        string tail = downing ? "  DOWNING" : "";
        tail += friendly ? "  FRIENDLY FIRE" : Core.N.FreshDownings != fresh0 ? " (fresh victim: one-hit candidate)" : "";
        SrLog.Line(string.Create(CultureInfo.InvariantCulture,
            $"{what}: {aname} -> {vname}  health {before.Health:0.0} -> {after.Health:0.0} (drop {before.Health - after.Health:0.0})  " +
            $"minPct {before.MinPct:0.00} -> {after.MinPct:0.00}  down {B(before.Down)}->{B(after.Down)} dead {B(before.Dead)}->{B(after.Dead)} " +
            $"limbs {before.Severed}->{after.Severed}{tail}{(b is null ? "  (no before reading)" : "")}"));
    }

    static int B(bool x) => x ? 1 : 0;

    /// <summary>1 = the player's, 0 = not, -1 = unknown (the plugin's IsMineRaw).</summary>
    public static int Mine(World world, CharacterId id)
    {
        if (SrBridge.Cold(world, id) is not { } c) return -1;
        return c.IsPlayer ? 1 : 0;   // the plugin's Character::isPlayerCharacter
    }

    /// <summary>A stealth knockout landed (the bridge's Strike postfix).</summary>
    public static void StealthKO(World world, CharacterId attacker, CharacterId victim)
    {
        FollowWorld(world);
        lock (Lock)
        {
            ulong a = SrBridge.Key(attacker), v = SrBridge.Key(victim);
            var ac = SrBridge.Cold(world, attacker);
            EnsureLoaded(a, ac);
            Core.OnStealthKO(a, v);
            Hang(a, ac);
        }
    }

    #endregion

    #region The world

    /// <summary>Worlds the system has started clean (the counters line's "worlds").</summary>
    public static long WorldsFollowed;

    /// <summary>
    /// Meitou builds a new world for every new game and load. The first call from a world not seen before starts it clean (0.1.5): the
    /// core's records, its clock and the reports' open fight belong to the world that ended. Records are keyed by slot and generation, so a
    /// record kept would hang on the new world's character in the same slot (the plugin drops a record left at an address that loads without
    /// one, its loadsCleared). The saved records come back with their characters (EnsureLoaded / Hang). Not IDisposable: Meitou disposes the
    /// old world's systems only after the new world is read and ready (findings F175).
    /// </summary>
    static void FollowWorld(World world)
    {
        if (ReferenceEquals(world, s_seen)) return;
        Follow(world, world.Tick);
        s_world = world;
    }

    /// <summary>The world last seen, by identity only (any object, so the fuzz harness can stand in for worlds).</summary>
    static object? s_seen;

    /// <summary>
    /// <see cref="FollowWorld"/>'s rule: a world (<paramref name="world"/>, at <paramref name="tick"/>) other than the last one seen clears the
    /// last one's state; the first world clears nothing.
    /// </summary>
    static void Follow(object world, long tick)
    {
        lock (Lock)
        {
            if (ReferenceEquals(world, s_seen)) return;
            bool first = s_seen is null;
            s_seen = world;
            if (first) return;

            int records = Core.StatsCount;
            Core.ClearAll();
            s_now = tick / TicksPerSecond;
            SrReports.NewWorld();
            SrWindow.NewWorld();
            WorldsFollowed++;
            SrLog.Line($"a new world (new game or load): {records} records of the last one dropped; the saved ones come back with their characters");
        }
    }

    /// <summary>For the fuzz harness: the world change without a running game (any object stands for a world).</summary>
    public static void FollowForCheck(object world, long tick) => Follow(world, tick);

    #endregion

    #region Records on characters

    /// <summary>A character the core has no entry for yet takes the record on its stats object (loaded from a save, or kept while it was away).</summary>
    static void EnsureLoaded(ulong key, CharacterCold? c)
    {
        if (key == 0 || c?.Stats is null || Core.Find(key) is not null) return;
        if (SrSave.Of(c.Stats) is { } record) Core.SetLoaded(key, record);
    }

    /// <summary>The core's record for a character hangs on its stats object, so it is saved and comes back with it. When both have one and they
    /// differ, a record from the save wins over an empty one the core made first.</summary>
    static void Hang(ulong key, CharacterCold? c)
    {
        if (key == 0 || c?.Stats is not { } stats || Core.Find(key) is not { } s) return;
        var on = SrSave.Of(stats);
        if (ReferenceEquals(on, s)) return;
        if (on is not null && s.Empty)
        {
            Core.SetLoaded(key, on);
            Replaced++;
            return;
        }
        SrSave.Attach(stats, s);
        Attached++;
    }

    #endregion

    #region The frame

    public void SlowWorld(World world)
    {
        if (!Active) return;
        FollowWorld(world);
        double now = world.Tick / TicksPerSecond;
        if (now > s_now) s_now = now;
        lock (Lock)
        {
            ForgetGone(world);
            Core.OnFrame(s_now, (ulong who, out Snapshot s) => Probe(world, who, out s));
            foreach (ulong key in Core.TrackedIds()) Hang(key, SrBridge.Cold(world, SrBridge.IdOf(key)));
            SrReports.Tick(world, s_now);
            if (world.Tick % 60 == 0) SrWindow.TakeSquad(world);   // every 2 s, for the window's pages
        }
        LogCounters(false);
    }

    static bool Probe(World world, ulong who, out Snapshot s)
    {
        s = SrBridge.Read(SrBridge.Cold(world, SrBridge.IdOf(who)));
        return s.Valid;
    }

    /// <summary>The plugin's destructor hook: a character whose id no longer resolves went away (unloaded, despawned, a dead body cleaned up).
    /// The core forgets it; its record stays on its stats object.</summary>
    static void ForgetGone(World world)
    {
        foreach (ulong key in Core.TrackedIds())
        {
            if (SrBridge.Cold(world, SrBridge.IdOf(key)) is not null) continue;
            Core.OnUnload(key);
            UnloadForgets++;
        }
    }

    /// <summary>The counters line, when it changed and every CounterLogSeconds (real time), as the plugin.</summary>
    public static void LogCounters(bool force)
    {
        double real = s_real.Elapsed.TotalSeconds;
        if (!force && (Settings.CounterLogSeconds <= 0 || real < s_nextCounterLog)) return;
        s_nextCounterLog = real + Settings.CounterLogSeconds;
        string line;
        lock (Lock)
        {
            line = Core.N.Line() + string.Create(CultureInfo.InvariantCulture,
                $" | forgets {UnloadForgets} saves {SrSave.Saves} (skippedEmpty {SrSave.SavesSkippedEmpty} noRecord {SrSave.SaveNoRecord}) loads {SrSave.Loads} " +
                $"attached {Attached} replaced {Replaced} uiLines {UiLines} | kcImports {SrSave.KcImports} kcSkipped {SrSave.KcSkipped} " +
                $"stealthTries {SrBridge.StealthTries} landed {SrBridge.StealthLanded} beforeMisses {SrBridge.BeforeMisses} allyFaults {SrBridge.AllyFaults} " +
                $"leaderFaults {SrBridge.LeaderFaults} baselines {SrSave.BaselinesSet} | maps stats {Core.StatsCount} victims {Core.VictimCount} " +
                $"pending {Core.PendingCount} track {Core.TrackCount} | commandsRead {SrSave.CommandsRead} commandsWritten {SrSave.CommandsWritten}");
        }
        if (!force && line == s_lastCounterLine) return;
        s_lastCounterLine = line;
        SrLog.Line("counters: " + line);
        SrLog.Line(string.Create(CultureInfo.InvariantCulture, $"clock: game {s_now:0.0} s, real {real:0.0} s"));
    }

    #endregion

    #region Events

    /// <summary>The core's event callback: one log line per counted event (EventLog), in the plugin's words.</summary>
    public static void OnEvent(string kind, ulong attacker, ulong victim, float value)
    {
        if (s_world is not { } w) return;
        SrReports.OnEvent(w, kind, attacker, victim, value, s_now);   // the plugin: g_aar.onEvent and the Recent page, before the log
        bool corpus = Settings.HitLog && kind != "biggest";
        if (!corpus && !Settings.EventLog) return;
        string a = attacker != 0 ? SrBridge.Cold(w, SrBridge.IdOf(attacker))?.Name ?? "?" : "-";
        string v = victim != 0 ? SrBridge.Cold(w, SrBridge.IdOf(victim))?.Name ?? "?" : "-";
        // 0.1.4: the plugin's v0.4.1 corpus line, every judged event with ids (kills, KOs, limbs, wounds...), keys as the "aar hit" line's
        if (corpus) SrLog.Line(string.Create(CultureInfo.InvariantCulture, $"aar ev={kind} t={s_now:0.00} a={attacker:x}|{a} v={victim:x}|{v} x={value:0.0}"));
        if (!Settings.EventLog) return;
        EventLines++;
        string line = kind switch
        {
            "biggest" => string.Create(CultureInfo.InvariantCulture, $"event biggest: {a} hit {v} for {value:0.0}"),
            "limbRemoved" => string.Create(CultureInfo.InvariantCulture, $"event limbRemoved: {a} took {value:0} limb(s) off {v}"),
            "friendlyFire" => string.Create(CultureInfo.InvariantCulture, $"event friendlyFire: {a} hit ally {v} for {value:0.0}"),
            _ => $"event {kind}: attacker {a}, victim {v}",
        };
        SrLog.Line(line);
    }

    #endregion
}
