// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The bridge to Meitou (the plugin's hooks, one for one):
//   hitByMeleeAttack / iShotYou  -> prefixes on CombatSystem.LandBlow / BoltHit / Execute: the victim's state just BEFORE the hit (and whether
//                                   the attacker counts it as an ally, read before the hit can change relations); the hit observer
//                                   (ICombatHitObserver, SrSystem) reads AFTER and feeds the core, as the plugin brackets the hit.
//   getStealthKOChance + knockout -> a postfix on CarrySystem.Strike: its own log entry says who tried and whether the knockout landed.
//   serialise / loadFromSerialise -> postfixes on SaveCapture.WriteBody and SavedCharacters.Read / Again (SrSave); since 0.1.4 also the
//                                   Military Manager's pza_post / pza_form / pza_sup / pza_hid, carried unchanged.
//   ~Character                   -> SrSystem forgets a character whose id no longer resolves (its record stays on its stats object).
// Members are looked up by name (internal types); a miss switches that part off with a line naming it, never a crash.
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Dialogue;

namespace PZA.ServiceRecord.Meitou;

public static class SrBridge
{
    #region Ids and readings

    /// <summary>The core's id for a character: slot + 1 and generation packed (0 stays "nobody", as a null Character* in the plugin).</summary>
    public static ulong Key(CharacterId id) => id.IsNone ? 0 : ((ulong)(uint)(id.Slot + 1) << 32) | (uint)id.Generation;

    public static CharacterId IdOf(ulong key) => key == 0 ? CharacterId.None : new((int)(key >> 32) - 1, (int)(uint)key);

    /// <summary>The plugin's ReadSnap: down = unconscious or dead; health = sum of (flesh - stun); minPct = the lowest part's health fraction;
    /// severed = arms / legs that are stumps or crushed.</summary>
    public static Snapshot Read(CharacterCold? c)
    {
        if (c?.Medical is not { } m || m.Parts.Count <= 0) return default;
        var s = new Snapshot { Dead = m.Dead };
        s.Down = m.Unconscious || s.Dead;
        float sum = 0, mn = 1e9f;
        int n = Math.Min(m.Parts.Count, 16);
        for (int i = 0; i < n; i++)
        {
            var p = m.Parts[i];
            sum += p.Flesh - p.Stun;
            if (p.Fraction < mn) mn = p.Fraction;
        }
        s.Health = sum;
        s.MinPct = mn > 1e8f ? 0 : mn;
        int sev = 0;
        for (int i = 0; i < n; i++)
            if (m.Parts[i].Limb is LimbState.Stump or LimbState.Crushed) sev++;
        s.Severed = Math.Min(sev, 4);
        s.Valid = true;
        return s;
    }

    /// <summary>The character's medical state and record of a resolvable id; null when it is gone.</summary>
    public static CharacterCold? Cold(World world, CharacterId id) =>
        !id.IsNone && world.Characters.TryResolveNext(id, out int slot) ? world.Characters.Cold(slot) : null;

    #endregion

    #region The hit bracket

    /// <summary>What a hit's prefix read, for the observer call of the same hit (Apply and the projectile flight are serial: one at a time).</summary>
    public sealed class Before
    {
        public int Victim = -1, Attacker = -1;
        public Snapshot Snap;
        public bool Friendly;
        public int VictimLeader = -1;
        public long Tick = -1;
    }

    [ThreadStatic] static Before? t_before;
    public static long BeforeMisses, AllyFaults, LeaderFaults, StealthTries, StealthLanded;

    /// <summary>The prefix's reading for this hit (victim slot and tick must match), taken once.</summary>
    public static Before? TakeBefore(int victimSlot, long tick)
    {
        var b = t_before;
        t_before = null;
        if (b is null || b.Victim != victimSlot || b.Tick != tick) { Interlocked.Increment(ref BeforeMisses); return null; }
        return b;
    }

    static void Bracket(World world, int attacker, int victim)
    {
        if (!SrSystem.Active || victim < 0) return;
        var table = world.Characters;
        var b = t_before ??= new Before();
        b.Victim = victim;
        b.Attacker = attacker;
        b.Tick = world.Tick;
        b.Snap = Read(table.Cold(victim));
        b.Friendly = attacker >= 0 && attacker != victim && IsAlly(world, attacker, victim);
        b.VictimLeader = IsLeader(world, victim);
    }

    /// <summary>The plugin's isAlly(attacker, victim): Meitou's "A regards B as friendly" (DialogueSystem.RegardsFriendly, the original's
    /// isAlly), else same or allied faction.</summary>
    static bool IsAlly(World world, int a, int v)
    {
        try
        {
            if (s_friendly is { } f && DialogueOf(world) is { } d)
            {
                var args = (object?[])s_friendlyArgs!.Clone();
                (args[0], args[1], args[2]) = (world, a, v);
                return f.Invoke(d, args) is true;
            }
            var (ac, vc) = (world.Characters.Cold(a), world.Characters.Cold(v));
            return ac is not null && vc is not null && ac.Faction == vc.Faction;
        }
        catch
        {
            Interlocked.Increment(ref AllyFaults);
            return false;
        }
    }

    /// <summary>
    /// 0.1.6: DialogueSystem.RegardsFriendly found by name, not bound at build time. Meitou 0.22.2's nightlies gave it an optional
    /// <c>bool disguise = false</c>; the 0.20.1-built DLL asked for the exact (World, int, int) and the call failed when its caller was compiled,
    /// inside our prefix on the game's hit (RefCheck on 0.22.2-nightly.20261122.2, 2026-11-22). Any public RegardsFriendly(World, int, int, ...)
    /// whose further parameters are optional is used with their defaults; none found: the same-faction rule, said once in the log.
    /// </summary>
    static MethodInfo? s_friendly;

    static object?[]? s_friendlyArgs;

    /// <summary>What the ally test uses on this Meitou (for the log and the offline check).</summary>
    public static string AllyTest = "not looked up";

    public static void FindRegardsFriendly()
    {
        s_friendly = null;
        foreach (var m in typeof(DialogueSystem).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.Name == "RegardsFriendly" && m.ReturnType == typeof(bool)).OrderBy(m => m.GetParameters().Length))
        {
            var ps = m.GetParameters();
            if (ps.Length < 3 || ps[0].ParameterType != typeof(World) || ps[1].ParameterType != typeof(int) || ps[2].ParameterType != typeof(int)
                || ps.Skip(3).Any(p => !p.IsOptional))
                continue;
            s_friendlyArgs = [.. ps.Select(p => p.IsOptional ? p.DefaultValue : null)];
            s_friendly = m;
            AllyTest = "DialogueSystem.RegardsFriendly(" + string.Join(", ", ps.Select(p => p.ParameterType.Name + (p.IsOptional ? " = " + (p.DefaultValue ?? "null") : ""))) + ")";
            return;
        }
        AllyTest = "DialogueSystem.RegardsFriendly(World, int, int, ...) not found: friendly fire uses the same-faction rule";
    }

    static World? s_dialogueWorld;
    static DialogueSystem? s_dialogue;

    /// <summary>The world's dialogue system (World.SystemOf is internal: looked up once per world in its public system list).</summary>
    static DialogueSystem? DialogueOf(World world)
    {
        if (!ReferenceEquals(world, s_dialogueWorld)) (s_dialogueWorld, s_dialogue) = (world, world.Systems.OfType<DialogueSystem>().FirstOrDefault());
        return s_dialogue;
    }

    /// <summary>The plugin's getSquadLeader() == self, asked before the hit: 1 yes, 0 no, -1 unknown.</summary>
    static int IsLeader(World world, int slot)
    {
        try
        {
            var c = world.Characters.Cold(slot);
            if (c is null || world.Squads.Find(c.SquadId) is not { } squad) return 0;
            return squad.Leader == world.Characters.IdOf(slot) ? 1 : 0;
        }
        catch
        {
            Interlocked.Increment(ref LeaderFaults);
            return -1;
        }
    }

    static void LandBlowPrefix(World world, in Effect e) => Bracket(world, e.Source, e.Target);

    static void BoltHitPrefix(World world, int v, object[] __args)
    {
        int shooter = -1;
        if (__args.Length > 1 && __args[1] is { } p && s_shooter?.GetValue(p) is int s) shooter = s;
        Bracket(world, shooter, v);
    }

    static void ExecutePrefix(World world, int a, int d) => Bracket(world, a, d);

    #endregion

    #region Stealth knockouts

    static void StrikePostfix(World world, object ____knockouts)
    {
        if (!SrSystem.Active || s_entries?.GetValue(____knockouts) is not IReadOnlyList<global::Meitou.Simulation.Carrying.StealthKoEntry> list || list.Count == 0)
            return;
        var last = list[^1];
        if (last.Tick != world.Tick) return;
        Interlocked.Increment(ref StealthTries);
        if (!last.Success) return;
        Interlocked.Increment(ref StealthLanded);
        SrSystem.StealthKO(world, last.Attacker, last.Target);
    }

    #endregion

    #region Save and load

    /// <summary>A saved character was read: our keys go onto its stats object.</summary>
    static void ReadPostfix(object? __result)
    {
        if (__result is null || s_partsStats?.GetValue(__result) is not CharacterStats stats || s_partsSource?.GetValue(__result) is not SaveCharacter sc
            || sc.Stats is not { } rec)
            return;
        try
        {
            // 0.1.4: the Military Manager's keys, kept as they are (shown on the pages, written back by WriteBodyPostfix)
            if (SrSave.CommandOf(stats) is null && SrSave.ReadCommand(rec.Floats, rec.Strings) is { } command)
            {
                SrSave.AttachCommand(stats, command);
                Interlocked.Increment(ref SrSave.CommandsRead);
            }
            if (SrSave.Of(stats) is not null) return;
            if (SrSave.Read(rec.Floats, rec.Strings, sc.Name) is { } record) SrSave.Attach(stats, record);
        }
        catch (Exception e)
        {
            SrLog.Line("load: a saved record could not be read (" + e.GetType().Name + ": " + e.Message + ")");
        }
    }

    /// <summary>A character is being written: its record goes into its STATS record (nothing for one without stats, as the plugin).</summary>
    static void WriteBodyPostfix(SaveCharacter sc, CharacterStats? stats)
    {
        if (SrSave.CommandOf(stats) is { } command && sc.Stats is { } crec)
        {
            SrSave.WriteCommand(crec.Floats, crec.Strings, command);
            Interlocked.Increment(ref SrSave.CommandsWritten);
        }
        var s = SrSave.Of(stats);
        if (s is null || s.Empty) { Interlocked.Increment(ref SrSave.SavesSkippedEmpty); return; }
        if (sc.Stats is not { } rec) { Interlocked.Increment(ref SrSave.SaveNoRecord); return; }
        SrSave.Write(rec.Floats, rec.Strings, s);
        Interlocked.Increment(ref SrSave.Saves);
    }

    #endregion

    #region Install

    static FieldInfo? s_shooter;
    static PropertyInfo? s_entries, s_partsStats, s_partsSource;

    /// <summary>Resolves and patches; returns the lines to log (each part that is missing says so and stays off).</summary>
    public static List<string> Install(Harmony h)
    {
        var lines = new List<string>();
        var sim = typeof(World).Assembly;
        var combat = sim.GetType("Meitou.Simulation.Combat.CombatSystem");
        var carry = sim.GetType("Meitou.Simulation.Carrying.CarrySystem");
        var capture = sim.GetType("Meitou.Simulation.Saving.SaveCapture");
        var parts = sim.GetType("Meitou.Simulation.Saving.SavedParts");
        var characters = sim.GetType("Meitou.Simulation.Saving.SavedCharacters");   // Read / Again live here (0.20.1), returning a SavedParts
        s_shooter = sim.GetType("Meitou.Simulation.Combat.Projectile")?.GetField("Shooter");
        s_entries = sim.GetType("Meitou.Simulation.Carrying.KnockoutLog")?.GetProperty("Entries");
        s_partsStats = parts?.GetProperty("Stats");
        s_partsSource = parts?.GetProperty("Source");

        Patch(h, lines, combat, "LandBlow", nameof(LandBlowPrefix), prefix: true);
        Patch(h, lines, combat, "BoltHit", nameof(BoltHitPrefix), prefix: true);
        Patch(h, lines, combat, "Execute", nameof(ExecutePrefix), prefix: true);
        Patch(h, lines, carry, "Strike", nameof(StrikePostfix), prefix: false, needs: s_entries);
        Patch(h, lines, capture, "WriteBody", nameof(WriteBodyPostfix), prefix: false);
        Patch(h, lines, characters, "Read", nameof(ReadPostfix), prefix: false, needs: s_partsStats);
        Patch(h, lines, characters, "Again", nameof(ReadPostfix), prefix: false, needs: s_partsStats);
        if (s_shooter is null) lines.Add("Projectile.Shooter not found: ranged hits carry no attacker");
        FindRegardsFriendly();
        lines.Add("ally test: " + AllyTest);
        return lines;
    }

    static void Patch(Harmony h, List<string> lines, Type? type, string method, string ours, bool prefix, object? needs = null)
    {
        var target = type is null ? null : AccessTools.Method(type, method);
        bool needsMissing = needs is null && ours is nameof(StrikePostfix) or nameof(ReadPostfix);
        if (target is null || needsMissing)
        {
            string why = type is null ? "type not found" : target is null ? "method not found" : "a member it reads not found";
            lines.Add($"MISSING {type?.Name ?? "?"}.{method}: {why}; that part is off");
            return;
        }
        try
        {
            var hm = new HarmonyMethod(AccessTools.Method(typeof(SrBridge), ours));
            if (prefix) h.Patch(target, prefix: hm);
            else h.Patch(target, postfix: hm);
            lines.Add($"patched {type!.Name}.{method} ({(prefix ? "prefix" : "postfix")})");
        }
        catch (Exception e)
        {
            // a patch that cannot be applied switches only its own part off; the game and the other parts go on
            lines.Add($"MISSING {type!.Name}.{method}: the patch failed ({e.GetType().Name}: {e.Message}); that part is off");
        }
    }

    #endregion
}
