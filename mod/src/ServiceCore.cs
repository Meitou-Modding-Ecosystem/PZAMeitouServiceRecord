// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// ServiceCore: hit attribution and per-character stats. A rule-for-rule port of the RE_Kenshi plugin's src/ServiceCore.h / .cpp (0.12.0):
// the same rules, the same order, the same caps, the same counters, so the same fight gives the same numbers in both games. No Meitou code
// here: the mod feeds it victim snapshots taken before and after each hit and a probe of the characters it tracks; tools/check drives it
// offline (the C++ tests/test_core.cpp cases, and a differential replay against the C++ core).
// Ids are plain numbers: the mod uses a character's slot and generation packed into one (CharacterKey), the C++ a Character*.
using System.Globalization;
using System.Text;

namespace PZA.ServiceRecord.Meitou;

/// <summary>Victim kind for combat breakdown (v0.13.0 / 0.1.8): flesh humanoids, skeletons (robots), animals.</summary>
public static class TargetKind
{
    public const int Unknown = 0, Human = 1, Robot = 2, Animal = 3;
}

/// <summary>One enslavement spell (v0.13.0 / 0.1.8): EndDay == 0 means still enslaved.</summary>
public readonly record struct SlaveryStint(string Faction, int StartDay, int EndDay);

/// <summary>What we read from a character at one instant.</summary>
public struct Snapshot
{
    /// <summary>False = could not read (no medical state).</summary>
    public bool Valid;
    /// <summary>Unconscious OR dead.</summary>
    public bool Down;
    /// <summary>Dead.</summary>
    public bool Dead;
    /// <summary>Sum over body parts of (flesh - stun): what a hit removes.</summary>
    public float Health;
    /// <summary>Lowest body-part health fraction.</summary>
    public float MinPct;
    /// <summary>Arms / legs that are stumps or crushed.</summary>
    public int Severed;
    /// <summary>v0.13.0 / 0.1.8: sum of (flesh - stun) per body-part bucket (0 Head, 1 Chest, 2 Stomach, 3 Arms, 4 Legs).</summary>
    public float Part0, Part1, Part2, Part3, Part4;

    public readonly float Part(int i) => i switch { 0 => Part0, 1 => Part1, 2 => Part2, 3 => Part3, 4 => Part4, _ => 0 };

    public void SetPart(int i, float v)
    {
        switch (i)
        {
            case 0: Part0 = v; break;
            case 1: Part1 = v; break;
            case 2: Part2 = v; break;
            case 3: Part3 = v; break;
            case 4: Part4 = v; break;
        }
    }
}

/// <summary>A character's record (saved with the character).</summary>
public sealed class Stats
{
    public double Dealt, Taken;
    public float Biggest;
    /// <summary>Victim name of the biggest hit.</summary>
    public string BiggestOn = "";
    public int OneHitKO, OneHitKill, KosSuffered, TimesKO, LimbsLost;
    /// <summary>Kill Counter's three tracks, counted by Kill Counter's own rules (v0.2.0).</summary>
    public int Kills, KosDealt, LimbsRemoved;
    /// <summary>Kill Counter's saved numbers were added once.</summary>
    public bool KcImported;
    /// <summary>Friendly fire (v0.3.3): hits this character landed on its own side; never in dealt, KOs, kills or limbs.</summary>
    public int FfHits, FfKOs, FfLimbs;
    public double FfDamage;
    /// <summary>Battles with a report this character took part in (v0.6.8), and the record when fight counting began.</summary>
    public int Fights;
    public double BDealt;
    public int BKills, BKOs, BLimbs, B1KO, B1Kill;
    /// <summary>Turret combat (v0.13.0 / 0.1.8): tracked separately so turret gunners do not inflate combat totals, Merit, Rank or radar.</summary>
    public double TurretDealt;
    public int TurretKills, TurretKOs, TurretLimbs;
    /// <summary>Combat damage dealt by body-part bucket (v0.13.0 / 0.1.8).</summary>
    public double DmgHead, DmgChest, DmgStomach, DmgArms, DmgLegs;
    /// <summary>Combat damage dealt and combat kills split by target kind (v0.13.0 / 0.1.8).</summary>
    public double DmgHuman, DmgRobot, DmgAnimal;
    public int KillsHuman, KillsRobot, KillsAnimal;
    /// <summary>Slavery history (v0.13.0 / 0.1.8): semicolon-separated "&lt;Faction&gt;|&lt;startDay&gt;|&lt;endDay&gt;" (endDay 0 = still enslaved).</summary>
    public string Slavery = "";

    public Stats Clone() => (Stats)MemberwiseClone();

    public void AddPartDmg(int i, double d)
    {
        switch (i)
        {
            case 0: DmgHead += d; break;
            case 1: DmgChest += d; break;
            case 2: DmgStomach += d; break;
            case 3: DmgArms += d; break;
            case 4: DmgLegs += d; break;
        }
    }

    public bool HasTurrets => TurretDealt > 0 || TurretKills > 0 || TurretKOs > 0 || TurretLimbs > 0;
    public bool HasPartDmg => DmgHead > 0 || DmgChest > 0 || DmgStomach > 0 || DmgArms > 0 || DmgLegs > 0;
    public bool HasTargetBreakdown => DmgHuman > 0 || DmgRobot > 0 || DmgAnimal > 0 || KillsHuman > 0 || KillsRobot > 0 || KillsAnimal > 0;

    /// <summary>The baseline = the record as it is now (a save from before fight counting).</summary>
    public void SetBaselineToNow() { BDealt = Dealt; BKills = Kills; BKOs = KosDealt; BLimbs = LimbsRemoved; B1KO = OneHitKO; B1Kill = OneHitKill; }

    public bool Empty => Dealt == 0 && Taken == 0 && Biggest == 0 && OneHitKO == 0 && OneHitKill == 0 && KosSuffered == 0 && TimesKO == 0 && LimbsLost == 0
                         && Kills == 0 && KosDealt == 0 && LimbsRemoved == 0 && !KcImported && FfHits == 0 && Fights == 0
                         && !HasTurrets && !HasPartDmg && !HasTargetBreakdown && Slavery.Length == 0;
}

public sealed class CoreConfig
{
    /// <summary>s: no hit from anyone this long before = fresh victim.</summary>
    public double FreshWindow = 10.0;
    /// <summary>Every body part at least this healthy = fresh victim.</summary>
    public float FreshMinPct = 0.95f;
    /// <summary>s: a downing is judged after this long (dead = kill, alive = KO).</summary>
    public double SettleWindow = 2.0;
    /// <summary>s: how often tracked characters are probed for KOs from any cause.</summary>
    public double PollEvery = 0.25;
    /// <summary>s (game time): a death this long after the last hit is "died of wounds", not a kill (0 = no limit).</summary>
    public double KillCreditWindow = 600.0;
    public int MaxStats = 4096, MaxVictims = 4096, MaxPending = 256, MaxTrack = 4096;
}

/// <summary>Every rule increments one of these; the mod logs them.</summary>
public sealed class Counters
{
    public long Hits, HitsRanged, Unreadable, SelfHits, CorpseHits, DamageEvents, ZeroDrop, HealDuringHit;
    public long FreshHits, Downings, FreshDownings;
    public long OneHitKO, OneHitKill, KoSuffered, SettledByHit, SettledUnloaded;
    public long TimesKO, DownThenDied, LimbsLost, BiggestUpdates;
    public long UnloadClears, StatsCapRefused, VictimsCapRefused, PendingCapRefused, VictimsPruned, ProbeMiss;
    public long Kills, KillsNoCredit, KosDealt, KosUndone, StealthKOs, LimbsRemoved, TrackCapRefused, TrackDropped;
    public long BadReadings, CorpseCredits;
    public long FirstSeenDown, DiedOfWounds;
    public long FriendlyHits;
    public long HitsTurret, TurretKills, TurretKOs, TurretLimbs, SlaveryStints;

    public void Reset()
    {
        foreach (var f in typeof(Counters).GetFields()) f.SetValue(this, 0L);
    }

    /// <summary>The counters line, word for word as the C++ prints it.</summary>
    public string Line() => string.Create(CultureInfo.InvariantCulture,
        $"hits {Hits} (ranged {HitsRanged}, unreadable {Unreadable}, self {SelfHits}, corpse {CorpseHits}) dmgEvents {DamageEvents} zeroDrop {ZeroDrop} healDuringHit {HealDuringHit} | " +
        $"fresh {FreshHits} downings {Downings} freshDownings {FreshDownings} | 1hitKO {OneHitKO} 1hitKill {OneHitKill} koSuffered {KoSuffered} settledByHit {SettledByHit} settledUnloaded {SettledUnloaded} | " +
        $"timesKO {TimesKO} downThenDied {DownThenDied} limbsLost {LimbsLost} biggestUpd {BiggestUpdates} | kills {Kills} (no credit {KillsNoCredit}) kosDealt {KosDealt} (undone {KosUndone}, stealth {StealthKOs}) limbsRemoved {LimbsRemoved} | " +
        $"unloadClears {UnloadClears} capRefused stats {StatsCapRefused} victims {VictimsCapRefused} pending {PendingCapRefused} track {TrackCapRefused} pruned {VictimsPruned} probeMiss {ProbeMiss} trackDropped {TrackDropped} | badReadings {BadReadings} corpseCredits {CorpseCredits} firstSeenDown {FirstSeenDown} diedOfWounds {DiedOfWounds} friendlyHits {FriendlyHits}" +
        $" | turretHits {HitsTurret} turretKills {TurretKills} turretKOs {TurretKOs} turretLimbs {TurretLimbs} slavery {SlaveryStints}");
}

/// <summary>Fills <c>snapshot</c> with the character's current state; false if it is gone.</summary>
public delegate bool ProbeFn(ulong who, out Snapshot snapshot);

/// <summary>Told about each counted event ("oneHitKO", "oneHitKill", "koSuffered", "timesKO", "biggest", ...), synchronously.</summary>
public delegate void EventFn(string kind, ulong attacker, ulong victim, float value);

public sealed class ServiceCore
{
    public CoreConfig Cfg = new();
    public readonly Counters N = new();
    public EventFn? Event;

    sealed class Victim
    {
        public double LastHit = -1e9;
        public bool Down;
        public double DownSince;
        public bool DownCounted;
        public bool Seen;
    }

    struct Pending
    {
        public ulong Attacker;
        public double T0;
        public bool Fresh;
        public bool Friendly;
    }

    sealed class Track
    {
        public ulong LastHitter, KoCreditor;
        public bool Done, LastTurret, KoTurret;
        public int TargetKind;
        public double LastHitTime;
    }

    // std::map in the C++: ordered by id, so every walk below visits in the same order as there
    readonly SortedDictionary<ulong, Stats> _stats = new();
    readonly SortedDictionary<ulong, Victim> _victims = new();
    readonly SortedDictionary<ulong, Pending> _pending = new();
    readonly SortedDictionary<ulong, Track> _track = new();
    double _lastPoll = -1e9, _lastPrune = -1e9, _now;

    void Ev(string k, ulong a, ulong v, float x) => Event?.Invoke(k, a, v, x);

    /// <summary>A character's stats; creates an entry (null when the cap is reached, or for id 0).</summary>
    public Stats? Get(ulong who)
    {
        if (who == 0) return null;
        if (_stats.TryGetValue(who, out var s)) return s;
        if (_stats.Count >= Cfg.MaxStats) { ++N.StatsCapRefused; return null; }
        s = new Stats();
        _stats[who] = s;
        return s;
    }

    public Stats? Find(ulong who) => _stats.GetValueOrDefault(who);

    /// <summary>One more battle (with a report) for this character (v0.6.8); false when it has no entry and none can be made.</summary>
    public bool AddFight(ulong who) { var s = Get(who); if (s is null) return false; ++s.Fights; return true; }

    /// <summary>From a save. The stats object itself is kept (the mod hangs it on the character's own stats).</summary>
    public void SetLoaded(ulong who, Stats s)
    {
        if (who == 0) return;
        if (!_stats.ContainsKey(who) && _stats.Count >= Cfg.MaxStats) { ++N.StatsCapRefused; return; }
        _stats[who] = s;
    }

    public void ClearAll() { _stats.Clear(); _victims.Clear(); _pending.Clear(); _track.Clear(); }

    public int StatsCount => _stats.Count;
    public int VictimCount => _victims.Count;
    public int PendingCount => _pending.Count;
    public int TrackCount => _track.Values.Count(t => !t.Done);

    /// <summary>Ids of everyone with stats (for the poll); a copy, so callers may erase.</summary>
    public List<ulong> TrackedIds() => [.. _stats.Keys];

    Track? TrackOf(ulong who)
    {
        if (_track.TryGetValue(who, out var t)) return t;
        if (_track.Count >= Cfg.MaxTrack) { ++N.TrackCapRefused; return null; }
        t = new Track();
        _track[who] = t;
        return t;
    }

    Victim? VictimOf(ulong who)
    {
        if (_victims.TryGetValue(who, out var v)) return v;
        if (_victims.Count >= Cfg.MaxVictims) { ++N.VictimsCapRefused; return null; }
        v = new Victim();
        _victims[who] = v;
        return v;
    }

    /// <summary>A stealth knockout (assassination) landed: it never goes through a hit (Kill Counter's pairing).</summary>
    public void OnStealthKO(ulong attacker, ulong victimId)
    {
        if (attacker == 0 || victimId == 0 || attacker == victimId) return;
        ++N.StealthKOs;
        var a = Get(attacker);
        if (a is not null) { ++a.KosDealt; ++N.KosDealt; Ev("koDealt", attacker, victimId, 0); }
        var t = TrackOf(victimId);
        if (t is not null && t.Done) { ++N.CorpseCredits; return; }   // R2: a knockout on a corpse must not re-open its kill credit
        if (t is not null) { t.LastHitter = attacker; t.KoCreditor = attacker; t.LastTurret = false; t.KoTurret = false; t.TargetKind = TargetKind.Unknown; t.LastHitTime = _now; }
    }

    /// <summary>Slavery status poll for a character (v0.13.0 / 0.1.8): opens / transfers / closes stints by in-game Day.</summary>
    public void OnSlavery(ulong who, bool isSlave, string faction, int day)
    {
        if (who == 0) return;
        if (!isSlave)
        {
            if (!_stats.TryGetValue(who, out var existing) || existing.Slavery.Length == 0) return;
            RecordSlavery(existing, false, faction, day);
            return;
        }
        var s = Get(who);
        if (s is null) return;
        if (RecordSlavery(s, true, faction, day)) ++N.SlaveryStints;
    }

    /// <summary>Judges a downing. died = confirmed dead inside the settle window with no other hit in between; otherwise a knockout.</summary>
    void Settle(ulong victimId, Pending p, bool died)
    {
        if (died)
        {
            if (p.Fresh && p.Attacker != 0)
            {
                var a = Get(p.Attacker);
                if (a is not null) { ++a.OneHitKill; ++N.OneHitKill; Ev("oneHitKill", p.Attacker, victimId, 0); }
            }
            return;
        }
        var v = Get(victimId);
        if (!p.Friendly)   // knocked out by its own side: a time knocked out (below), not a KO suffered from an enemy
        {
            if (v is not null) ++v.KosSuffered;
            ++N.KoSuffered;
            Ev("koSuffered", p.Attacker, victimId, 0);
        }
        // A KO suffered is also a time knocked out, even if they got up inside the window.
        var vr = VictimOf(victimId);
        if (vr is not null && !vr.DownCounted)
        {
            // fuzz R1: a record made only now said "was up", so noteDown saw the same down spell as a new one. It is down from now on.
            if (!vr.Down) { vr.Down = true; vr.DownSince = p.T0; }
            vr.Seen = true;
            vr.DownCounted = true;
            if (v is not null) ++v.TimesKO;
            ++N.TimesKO;
            Ev("timesKO", p.Attacker, victimId, 0);
        }
        if (p.Fresh && p.Attacker != 0)
        {
            var a = Get(p.Attacker);
            if (a is not null) { ++a.OneHitKO; ++N.OneHitKO; Ev("oneHitKO", p.Attacker, victimId, 0); }
        }
    }

    /// <summary>Times knocked out, from any cause: counted once a down spell has lasted the settle window without death.</summary>
    void NoteDown(ulong who, Snapshot s, double now)
    {
        var v = VictimOf(who);
        if (v is null) return;
        if (!v.Seen)
        {   // already down at first sight is not a knockout we saw happen (2026-11-07 raid)
            v.Seen = true;
            if (s.Down) { v.Down = true; v.DownSince = now; v.DownCounted = true; ++N.FirstSeenDown; return; }
        }
        if (!s.Down) { v.Down = false; return; }
        if (!v.Down) { v.Down = true; v.DownSince = now; v.DownCounted = false; }
        if (v.DownCounted) return;
        if (s.Dead) { ++N.DownThenDied; v.DownCounted = true; return; }
        if (now - v.DownSince >= Cfg.SettleWindow)
        {
            var st = Get(who);
            if (st is not null) ++st.TimesKO;
            ++N.TimesKO;
            Ev("timesKO", 0, who, 0);
            v.DownCounted = true;
        }
    }

    /// <summary>A hit landed: <paramref name="before"/> just before the game applied it, <paramref name="after"/> just after. attacker 0 = unknown.
    /// friendly = the attacker counted the victim as an ally at the moment of the hit. turret = mounted turret shot (tracked separately).</summary>
    public void OnHit(ulong attacker, ulong victimId, Snapshot before, Snapshot after, double now, string victimName, bool ranged, bool friendly = false,
        bool turret = false, int targetKind = TargetKind.Unknown)
    {
        ++N.Hits;
        if (now > _now) _now = now;
        if (ranged) ++N.HitsRanged;
        if (turret) ++N.HitsTurret;
        if (victimId == 0) return;
        if (attacker == victimId) { ++N.SelfHits; attacker = 0; }
        if (!before.Valid || !after.Valid) { ++N.Unreadable; return; }
        if (before.Dead) { ++N.CorpseHits; return; }

        var v = VictimOf(victimId);
        if (v is not null && !v.Seen)
        {   // first sight is this hit: if it was already down before it, that down spell isn't one we saw start
            v.Seen = true;
            if (before.Down) { v.Down = true; v.DownSince = now; v.DownCounted = true; ++N.FirstSeenDown; }
        }

        // A second hit on a victim whose downing is still being judged: the knockout stands, and the death can no longer be one hit's.
        if (_pending.Remove(victimId, out var pend))
        {
            ++N.SettledByHit;
            Settle(victimId, pend, false);
        }

        bool fresh = v is not null && !before.Down && before.MinPct >= Cfg.FreshMinPct && now - v.LastHit >= Cfg.FreshWindow;
        if (fresh) ++N.FreshHits;

        float drop = before.Health - after.Health;
        if (float.IsNaN(drop) || drop > 1e6f || drop < -1e6f) { ++N.BadReadings; drop = 0; }   // R3
        if (drop < 0) { ++N.HealDuringHit; drop = 0; }
        if (drop == 0) ++N.ZeroDrop; else ++N.DamageEvents;

        var vs = Get(victimId);
        int lost = after.Severed - before.Severed;
        if (lost > 4 || before.Severed < 0 || after.Severed < 0) { ++N.BadReadings; lost = 0; }   // R4: a body has 4 limbs
        if (vs is not null)
        {
            vs.Taken += drop;
            if (lost > 0) { vs.LimbsLost += lost; N.LimbsLost += lost; }
        }
        // v0.3.3 friendly fire: recorded on the attacker as friendly fire, then the hit carries no attacker
        bool ffDowning = false;
        if (friendly && attacker != 0)
        {
            ++N.FriendlyHits;
            var fs = Get(attacker);
            if (fs is not null)
            {
                ++fs.FfHits; fs.FfDamage += drop;
                if (lost > 0) fs.FfLimbs += lost;
                if (!before.Down && after.Down) { ++fs.FfKOs; ffDowning = true; }
            }
            Ev("friendlyFire", attacker, victimId, drop);
            attacker = 0;
        }
        // v0.13.0 / 0.1.8: turret hits stay separate from personal combat (never inflate dealt, kills, Merit, Rank or radar)
        if (attacker != 0 && turret)
        {
            var aS = Get(attacker);
            if (aS is not null) aS.TurretDealt += drop;
            if (lost > 0 && aS is not null) { aS.TurretLimbs += lost; N.TurretLimbs += lost; Ev("limbRemoved", attacker, victimId, lost); }
            var t = TrackOf(victimId);
            if (t is not null && !t.Done) { t.LastHitter = attacker; t.LastHitTime = now; t.LastTurret = true; t.TargetKind = targetKind; }
            if (!before.Down && after.Down)
            {
                if (aS is not null) { ++aS.TurretKOs; ++N.TurretKOs; Ev("koDealt", attacker, victimId, 0); }
                if (t is not null) { t.KoCreditor = attacker; t.KoTurret = true; }
            }
        }
        else if (attacker != 0)
        {
            // Kill Counter's three tracks
            var aS = Get(attacker);
            if (lost > 0 && aS is not null) { aS.LimbsRemoved += lost; N.LimbsRemoved += lost; Ev("limbRemoved", attacker, victimId, lost); }
            var t = TrackOf(victimId);
            if (t is not null && !t.Done) { t.LastHitter = attacker; t.LastHitTime = now; t.LastTurret = false; t.TargetKind = targetKind; }
            if (!before.Down && after.Down)
            {
                if (aS is not null) { ++aS.KosDealt; ++N.KosDealt; Ev("koDealt", attacker, victimId, 0); }
                if (t is not null) { t.KoCreditor = attacker; t.KoTurret = false; }
            }
            if (aS is not null)
            {
                aS.Dealt += drop;
                if (drop > aS.Biggest) { aS.Biggest = drop; aS.BiggestOn = victimName; ++N.BiggestUpdates; Ev("biggest", attacker, victimId, drop); }
                for (int i = 0; i < 5; i++)
                {
                    float pd = before.Part(i) - after.Part(i);
                    if (!float.IsNaN(pd) && pd > 0 && pd < 1e6f) aS.AddPartDmg(i, pd);
                }
                if (drop > 0)
                {
                    if (targetKind == TargetKind.Human) aS.DmgHuman += drop;
                    else if (targetKind == TargetKind.Robot) aS.DmgRobot += drop;
                    else if (targetKind == TargetKind.Animal) aS.DmgAnimal += drop;
                }
            }
        }

        if (!before.Down && after.Down)
        {
            ++N.Downings;
            if (fresh) ++N.FreshDownings;
            if (_pending.Count >= Cfg.MaxPending) ++N.PendingCapRefused;
            else _pending[victimId] = new Pending { Attacker = turret ? 0 : attacker, T0 = now, Fresh = fresh, Friendly = ffDowning };
        }

        if (v is not null) v.LastHit = now;
        NoteDown(victimId, after, now);
    }

    /// <summary>Once per frame: settles pending downings and polls tracked characters.</summary>
    public void OnFrame(double now, ProbeFn probe)
    {
        if (now > _now) _now = now;
        // 1. Judge pending downings.
        foreach (ulong id in _pending.Keys.ToList())
        {
            if (!_pending.TryGetValue(id, out var p)) continue;
            if (!probe(id, out var s) || !s.Valid) { ++N.SettledUnloaded; _pending.Remove(id); continue; }
            if (s.Dead) { _pending.Remove(id); Settle(id, p, true); continue; }
            if (now - p.T0 >= Cfg.SettleWindow) { _pending.Remove(id); Settle(id, p, false); continue; }
        }

        // 1b. Kill credit: a tracked victim that is now dead goes to its last hitter; gone = forget it.
        foreach (ulong victimId in _track.Keys.ToList())
        {
            if (!_track.TryGetValue(victimId, out var tr)) continue;
            if (!probe(victimId, out var s) || !s.Valid) { ++N.TrackDropped; _track.Remove(victimId); continue; }
            if (!s.Dead || tr.Done) continue;
            tr.Done = true;   // kept (closed) until the corpse unloads, so a late knockout can't re-credit it (R2)
            if (tr.LastHitter == 0) { ++N.KillsNoCredit; continue; }
            // a death long after the last hit is "died of wounds", not that hitter's kill
            if (Cfg.KillCreditWindow > 0 && now - tr.LastHitTime > Cfg.KillCreditWindow)
            { ++N.DiedOfWounds; Ev("diedOfWounds", tr.LastHitter, victimId, (float)(now - tr.LastHitTime)); continue; }
            var k = Get(tr.LastHitter);
            if (k is not null)
            {
                if (tr.LastTurret) { ++k.TurretKills; ++N.TurretKills; }
                else
                {
                    ++k.Kills; ++N.Kills;
                    if (tr.TargetKind == TargetKind.Human) ++k.KillsHuman;
                    else if (tr.TargetKind == TargetKind.Robot) ++k.KillsRobot;
                    else if (tr.TargetKind == TargetKind.Animal) ++k.KillsAnimal;
                }
                Ev("kill", tr.LastHitter, victimId, 0);
            }
            // the same character knocked it out and killed it while down: one event, a kill (Kill Counter's KO undo)
            if (k is not null && tr.LastHitter == tr.KoCreditor && s.Down)
            {
                if (tr.KoTurret && k.TurretKOs > 0) { --k.TurretKOs; ++N.KosUndone; }
                else if (!tr.KoTurret && k.KosDealt > 0) { --k.KosDealt; ++N.KosUndone; }
            }
        }

        // 2. Poll tracked characters for knockouts from any cause.
        if (now - _lastPoll >= Cfg.PollEvery)
        {
            _lastPoll = now;
            foreach (ulong id in TrackedIds())
            {
                if (!probe(id, out var s) || !s.Valid) { ++N.ProbeMiss; continue; }
                NoteDown(id, s, now);
            }
        }

        // 3. Prune victim records with no stats entry (only happens at the stats cap) once stale.
        if (now - _lastPrune >= 5.0)
        {
            _lastPrune = now;
            foreach (var (id, v) in _victims.ToList())
            {
                if (!_stats.ContainsKey(id) && !v.Down && now - v.LastHit > Cfg.FreshWindow * 3) { ++N.VictimsPruned; _victims.Remove(id); }
            }
        }
    }

    /// <summary>The character went away: forget it (the C++ calls this from the destructor hook).</summary>
    public void OnUnload(ulong who)
    {
        bool any = false;
        if (_stats.Remove(who)) any = true;
        if (_victims.Remove(who)) any = true;
        if (_pending.Remove(who)) any = true;
        if (_track.Remove(who)) any = true;
        // A pending downing credited to this attacker must not recreate its entry later.
        foreach (var id in _pending.Keys.ToList())
        {
            var p = _pending[id];
            if (p.Attacker == who) { p.Attacker = 0; _pending[id] = p; any = true; }
        }
        foreach (var t in _track.Values)
        {
            if (t.LastHitter == who) { t.LastHitter = 0; any = true; }
            if (t.KoCreditor == who) { t.KoCreditor = 0; any = true; }
        }
        if (any) ++N.UnloadClears;
    }

    /// <summary>One-time import of Kill Counter's saved numbers (STATS floats "kills", "kos", "limbs"): all three present, whole numbers from 0
    /// to 1,000,000. True (and KcImported set) when added; otherwise <paramref name="why"/> says what was wrong and <paramref name="s"/> is
    /// unchanged.</summary>
    public static bool ImportKillCounter(bool hk, float kills, bool hko, float kos, bool hl, float limbs, Stats s, out string why)
    {
        if (s.KcImported) { why = "already imported"; return false; }
        if (!hk && !hko && !hl) { why = "no Kill Counter values"; return false; }
        if (!(hk && hko && hl)) { why = "only some Kill Counter values present (another mod's keys?)"; return false; }
        foreach (float x in (ReadOnlySpan<float>)[kills, kos, limbs])
        {
            if (!(x >= 0.0f && x <= 1000000.0f)) { why = "a value is negative, too large or not a number"; return false; }
            if (MathF.Abs(x - MathF.Floor(x + 0.5f)) > 0.01f) { why = "a value is not a whole number"; return false; }
        }
        s.Kills += (int)MathF.Floor(kills + 0.5f);
        s.KosDealt += (int)MathF.Floor(kos + 0.5f);
        s.LimbsRemoved += (int)MathF.Floor(limbs + 0.5f);
        s.KcImported = true;
        why = "imported";
        return true;
    }

    /// <summary>Short numbers for the compact STA lines (v0.3.0): 999, 1.2k, 12k, 1.2M. Rounds as the plugin's VS2010 printf does (half away
    /// from zero).</summary>
    public static string Short(double v)
    {
        if (!(v > 0)) v = 0;   // NaN, negative and -0
        static string F(double x, int d) => Aar.Words.CFixed(x, d);   // the plugin's VS2010 printf
        if (v < 1000.0) return F(v, 0);
        if (v < 9950.0) return F(v / 1000.0, 1) + "k";
        if (v < 999500.0) return F(v / 1000.0, 0) + "k";
        if (v < 9.95e6) return F(v / 1e6, 1) + "M";
        return F(v / 1e6 > 999 ? 999.0 : v / 1e6, 0) + "M";
    }

    public static string Triple(double a, double b, double c) => Short(a) + "/" + Short(b) + "/" + Short(c);

    #region Slavery history (v0.13.0 / 0.1.8, byte-for-byte with C++)

    public static string SanitizeSlaveryFaction(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Unknown";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var clean = new List<byte>(Math.Min(bytes.Length, 48));
        foreach (byte c in bytes)
        {
            if (c < 0x20 || c == 0x7F || c == (byte)'|' || c == (byte)';') continue;
            clean.Add(c);
        }
        int a = 0;
        while (a < clean.Count && clean[a] == (byte)' ') a++;
        int b = clean.Count;
        while (b > a && clean[b - 1] == (byte)' ') b--;
        int len = b - a;
        if (len <= 0) return "Unknown";
        if (len > 48)
        {
            int cut = 48;
            while (cut > 0 && (clean[a + cut] & 0xC0) == 0x80) cut--;
            while (cut > 0 && clean[a + cut - 1] == (byte)' ') cut--;
            len = cut;
        }
        if (len <= 0) return "Unknown";
        return Encoding.UTF8.GetString(clean.GetRange(a, len).ToArray());
    }

    static bool ParseSlaveryInt(ReadOnlySpan<char> s, out int value)
    {
        value = 0;
        if (s.Length == 0 || s.Length > 8) return false;
        long v = 0;
        foreach (char c in s)
        {
            if (c < '0' || c > '9') return false;
            v = v * 10 + (c - '0');
            if (v > 10_000_000) return false;
        }
        value = (int)v;
        return true;
    }

    public static List<SlaveryStint> ParseSlavery(string? raw)
    {
        var list = new List<SlaveryStint>();
        if (string.IsNullOrEmpty(raw)) return list;
        int p = 0, scanned = 0;
        while (p < raw.Length && scanned < 256)
        {
            ++scanned;
            int q = raw.IndexOf(';', p);
            if (q < 0) q = raw.Length;
            var item = raw.AsSpan(p, q - p);
            p = q + 1;
            int bar1 = item.IndexOf('|');
            if (bar1 < 0) continue;
            var rest = item[(bar1 + 1)..];
            int bar2Rel = rest.IndexOf('|');
            if (bar2Rel < 0 || rest[(bar2Rel + 1)..].IndexOf('|') >= 0) continue;
            if (!ParseSlaveryInt(rest[..bar2Rel], out int sd)) continue;
            if (!ParseSlaveryInt(rest[(bar2Rel + 1)..], out int ed)) continue;
            if (sd < 1 || (ed != 0 && ed < sd)) continue;
            if (list.Count >= 20) list.RemoveAt(0);
            list.Add(new SlaveryStint(SanitizeSlaveryFaction(item[..bar1].ToString()), sd, ed));
        }
        return list;
    }

    public static string FormatSlavery(IReadOnlyList<SlaveryStint> list)
    {
        var parts = new List<string>();
        int start = list.Count > 20 ? list.Count - 20 : 0;
        for (int i = start; i < list.Count; i++)
        {
            var st = list[i];
            if (st.StartDay < 1 || st.StartDay > 10_000_000) continue;
            if (st.EndDay < 0 || st.EndDay > 10_000_000 || (st.EndDay != 0 && st.EndDay < st.StartDay)) continue;
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{SanitizeSlaveryFaction(st.Faction)}|{st.StartDay}|{st.EndDay}"));
        }
        while (parts.Count > 0)
        {
            int bytes = 0;
            for (int i = 0; i < parts.Count; i++) bytes += Encoding.UTF8.GetByteCount(parts[i]) + (i > 0 ? 1 : 0);
            if (bytes <= 400) break;
            parts.RemoveAt(0);
        }
        return string.Join(';', parts);
    }

    public static string SanitizeSlavery(string? raw) => FormatSlavery(ParseSlavery(raw));

    public static bool RecordSlavery(Stats s, bool isSlave, string? faction, int day)
    {
        if (day < 1) day = 1;
        if (day > 10_000_000) day = 10_000_000;
        var list = s.Slavery.Length > 0 ? ParseSlavery(s.Slavery) : [];
        bool opened = false;
        if (!isSlave)
        {
            if (list.Count > 0 && list[^1].EndDay == 0)
            {
                var last = list[^1];
                list[^1] = last with { EndDay = day < last.StartDay ? last.StartDay : day };
                s.Slavery = FormatSlavery(list);
            }
            return false;
        }
        string fn = SanitizeSlaveryFaction(faction);
        if (list.Count == 0 || list[^1].EndDay != 0)
        {
            list.Add(new SlaveryStint(fn, day, 0));
            opened = true;
        }
        else
        {
            var cur = list[^1];
            if (cur.Faction == "Unknown" && fn != "Unknown") list[^1] = cur with { Faction = fn };
            else if (fn != "Unknown" && fn != cur.Faction)
            {
                list[^1] = cur with { EndDay = day < cur.StartDay ? cur.StartDay : day };
                list.Add(new SlaveryStint(fn, day, 0));
                opened = true;
            }
        }
        s.Slavery = FormatSlavery(list);
        return opened;
    }

    #endregion
}
