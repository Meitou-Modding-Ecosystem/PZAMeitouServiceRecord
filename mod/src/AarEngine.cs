// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The After-Action Report engine and writer: AarCore.h's Engine and Engine::Write, line for line (see AarCore.cs for the parity rules).
// Style: the AAR style guide. Fixed order: header, what happened (timeline), mentioned in this report, the anchor / did the
// work, losses and friendly fire, sustain / improve, one closing line.
using static PZA.ServiceRecord.Meitou.Aar.Words;

namespace PZA.ServiceRecord.Meitou.Aar;

public sealed class Engine
{
    #region Settings and counters

    public double Gap = 30.0;
    public int MinHits = 8, MaxHits = 6000, MaxReports = 30, MaxAftermath = 40;
    public long BattlesClosed, BattlesTooSmall, HitsDropped, HitsNotOurs;
    public string Side = "";
    public bool Advice;
    public long ReportNumber;
    public double MergeWindow = 300.0;
    public long ReportsMerged, NamedByFaction, Assaults, OurAttacks;

    #endregion

    #region State

    readonly List<Hit> _hits = [];
    SortedDictionary<ulong, string> _names = [];
    List<Event> _pendingEv = [];
    readonly LinkedList<Report> _reports = new();
    double _last = -1e9;
    SortedDictionary<ulong, string> _factions = [];
    Context _place = new(), _prevPlace = new();
    bool _placeSet;
    List<Hit> _prevHits = [];
    SortedDictionary<ulong, string> _prevNames = [];
    List<Event> _prevEv = [];
    int _prevNumber;
    double _prevT1 = -1e9;
    readonly List<double> _prevMops = [];

    #endregion

    #region Feeding

    public void SetFaction(ulong id, string f)
    {
        if (id != 0 && f.Length > 0 && (_factions.Count < 4000 || _factions.ContainsKey(id))) _factions[id] = f;
    }

    public bool KnowsFaction(ulong id) => _factions.ContainsKey(id);

    public void SetPlace(string name, int kind, string owner)
    {
        if (_hits.Count == 0 || _placeSet) return;
        _place.Place = name;
        _place.PlaceKind = kind;
        _place.PlaceOwner = owner;
        if (kind != 0) _placeSet = true;
    }

    public bool PlaceSettled => _placeSet;

    public void OnHit(in Hit h, string aName, string vName)
    {
        if (h.Am == 0 && h.Vm == 0) { ++HitsNotOurs; return; }
        if (_hits.Count > 0 && h.T - _last > Gap) Close();
        if (_hits.Count >= MaxHits) { ++HitsDropped; return; }
        _hits.Add(h);
        _last = h.T;
        if (h.A != 0) _names[h.A] = aName;
        if (h.V != 0) _names[h.V] = vName;
    }

    public void OnEvent(string kind, double t, ulong a, ulong v)
    {
        if (kind != "kill" && kind != "diedOfWounds") return;
        if (_hits.Count > 0 && t - _last <= Gap) { _pendingEv.Add(new Event { Kind = kind, T = t, A = a, V = v }); return; }
        for (var node = _reports.Last; node is not null; node = node.Previous)
        {
            var r = node.Value;
            if (!r.Enemies.Contains(v)) continue;
            if (r.Aftermath.Count >= MaxAftermath) return;
            string vn = Name(r, v), an = Name(r, a);
            if (kind == "kill") r.Aftermath.Add(Mark('d') + When(t) + "  " + vn + " died; the kill goes to " + an + ".");
            else r.Aftermath.Add(Mark('d') + When(t) + "  " + vn + " died of wounds, long after the last blow (" + an + ").");
            return;
        }
    }

    public void Tick(double now) { if (_hits.Count > 0 && now - _last > Gap) Close(); }

    public IReadOnlyCollection<Report> Reports => _reports;

    public Report? LastReport => _reports.Last?.Value;

    public bool InBattle => _hits.Count > 0;

    public int LiveHits => _hits.Count;

    #endregion

    #region A new world

    /// <summary>A new world (new game or load, 0.1.5): the battle still open and the last one (kept to merge a follow-up) belong to the world
    /// that ended, and so do the factions known by id; all are dropped unwritten, so no fight runs on into the next save. The reports already
    /// written stay (their numbers go on). The faction map is replaced, not cleared: the written reports hold the old one. Returns the open
    /// battle's hits.</summary>
    public int DropOpen()
    {
        int n = _hits.Count;
        _hits.Clear();
        _names = [];
        _pendingEv = [];
        _factions = [];
        _last = -1e9;
        _place = new Context();
        _prevPlace = new Context();
        _placeSet = false;
        _prevHits = [];
        _prevNames = [];
        _prevEv = [];
        _prevNumber = 0;
        _prevT1 = -1e9;
        _prevMops.Clear();
        return n;
    }

    #endregion

    #region Closing a battle

    public void Close()
    {
        if (_hits.Count == 0) return;
        ++BattlesClosed;
        if (TryMerge()) return;
        if (_hits.Count >= MinHits)
        {
            var cx = _place.Copy();
            cx.Factions = _factions;
            var r = Write(_hits, _names, (int)++ReportNumber, _pendingEv, Side, Advice, null, cx);
            if (r.Fired.Count > 0 && r.Fired.Contains("NAMED BY FACTION")) ++NamedByFaction;
            if (r.Fired.Contains("OUR ASSAULT")) ++Assaults;
            if (r.Fired.Contains("OUR ATTACK")) ++OurAttacks;
            _prevPlace = cx;
            AddLateKills(r, _pendingEv);
            _reports.AddLast(r);
            while (_reports.Count > MaxReports) _reports.RemoveFirst();
            _prevHits = [.. _hits];
            _prevNames = new(_names);
            _prevEv = [.. _pendingEv];
            _prevNumber = r.Number;
            _prevT1 = r.T1;
            _prevMops.Clear();
        }
        else ++BattlesTooSmall;
        _hits.Clear();
        _names.Clear();
        _pendingEv.Clear();
        _place = new Context();
        _placeSet = false;
    }

    /// <summary>A later fight with an enemy of the last report is the same action: that report is rewritten over both fights.</summary>
    bool TryMerge()
    {
        if (_reports.Count == 0 || _prevNumber <= 0 || _reports.Last!.Value.Number != _prevNumber || _hits[0].T - _prevT1 > MergeWindow) return false;
        var was = _reports.Last.Value.Enemies;
        bool same = false;
        for (int i = 0; i < _hits.Count && !same; i++) same = was.Contains(_hits[i].A) || was.Contains(_hits[i].V);
        if (!same || _prevHits.Count + _hits.Count > MaxHits) return false;

        _prevMops.Add(_hits[0].T);
        var all = new List<Hit>(_prevHits);
        all.AddRange(_hits);
        var nm = new SortedDictionary<ulong, string>(_prevNames);
        foreach (var (k, v) in _names) nm[k] = v;
        var ev = new List<Event>(_prevEv);
        ev.AddRange(_pendingEv);
        var cx = (_place.PlaceKind >= 0 ? _place : _prevPlace).Copy();
        cx.Factions = _factions;
        var r = Write(all, nm, _prevNumber, ev, Side, Advice, _prevMops, cx);
        _prevPlace = cx;
        _place = new Context();
        _placeSet = false;
        AddLateKills(r, ev);
        _reports.Last.Value = r;
        ++ReportsMerged;
        _prevHits = all;
        _prevNames = nm;
        _prevEv = ev;
        _prevT1 = r.T1;
        _hits.Clear();
        _names.Clear();
        _pendingEv.Clear();
        return true;
    }

    static void AddLateKills(Report r, List<Event> ev)
    {
        foreach (var e in ev)
            if (e.Kind == "kill" && r.Enemies.Contains(e.V) && e.T > r.T1 + 2.0)
                r.Aftermath.Add(Mark('d') + When(e.T) + "  " + Name(r, e.V) + " died; the kill goes to " + Name(r, e.A) + ".");
    }

    public static string Name(Report r, ulong i) => r.Names.TryGetValue(i, out var n) && n.Length > 0 ? n : "someone";

    #endregion

    #region The writer

    sealed class TL
    {
        public double T;
        public string Text = "";
        public char Col;
        public ulong Who;
    }

    static int Get(SortedDictionary<ulong, int> m, ulong k) { if (!m.TryGetValue(k, out int v)) m[k] = v = 0; return v; }

    static double Get(SortedDictionary<ulong, double> m, ulong k) { if (!m.TryGetValue(k, out double v)) m[k] = v = 0; return v; }

    static void Add(SortedDictionary<ulong, int> m, ulong k, int d) => m[k] = m.GetValueOrDefault(k) + d;

    static void Add(SortedDictionary<ulong, double> m, ulong k, double d) => m[k] = m.GetValueOrDefault(k) + d;

    public static Report Write(List<Hit> b, SortedDictionary<ulong, string> names, int number, List<Event> kills, string side, bool advice = false,
        List<double>? mopUps = null, Context? ctx = null)
    {
        var r = new Report { Names = new(names), T0 = b[0].T, T1 = b[^1].T, Number = number };

        // ---- sides: the game's player flag when known; else the name heuristic (old logs)
        var ours = new SortedSet<ulong>();
        var enemy = new SortedSet<ulong>();
        bool flags = false;
        foreach (var h in b)
        {
            if (h.Am >= 0 || h.Vm >= 0) flags = true;
            if (h.Am == 1) ours.Add(h.A);
            if (h.Vm == 1) ours.Add(h.V);
        }
        string stem = "";
        if (flags)
        {
            foreach (var h in b)
            {
                if (h.Ff) continue;
                if (ours.Contains(h.A) && h.V != 0 && !ours.Contains(h.V)) enemy.Add(h.V);
                if (ours.Contains(h.V) && h.A != 0 && !ours.Contains(h.A)) enemy.Add(h.A);
            }
            var cnt = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in enemy) { string k = Stem(Name(r, e)); cnt[k] = cnt.GetValueOrDefault(k) + 1; }
            int best = 0;
            foreach (var (k, v) in cnt) if (v > best) { best = v; stem = k; }
        }
        else
        {
            var byStem = new SortedDictionary<string, SortedSet<ulong>>(StringComparer.Ordinal);
            void Put(ulong id) { if (id == 0) return; string k = Stem(Name(r, id)); if (!byStem.TryGetValue(k, out var set)) byStem[k] = set = []; set.Add(id); }
            foreach (var h in b) { Put(h.A); Put(h.V); }
            int best = 0;
            foreach (var (k, v) in byStem) if (v.Count >= 3 && v.Count > best) { best = v.Count; stem = k; }
            foreach (var (k, v) in byStem) if (k == stem) enemy = [.. v];
            foreach (var h in b)
            {
                if (enemy.Contains(h.V) && h.A != 0 && !enemy.Contains(h.A)) ours.Add(h.A);
                if (enemy.Contains(h.A) && h.V != 0 && !enemy.Contains(h.V)) ours.Add(h.V);
            }
            for (int pass = 0; pass < 3; pass++)
                foreach (var h in b)
                {
                    if (!h.Ff || h.A == 0 || h.V == 0 || enemy.Contains(h.A) || enemy.Contains(h.V)) continue;
                    if (ours.Contains(h.A) || ours.Contains(h.V)) { ours.Add(h.A); ours.Add(h.V); }
                }
        }
        r.Enemies = [.. enemy];
        r.Ours = [.. ours];
        var order = new List<ulong>();
        foreach (var h in b)
            foreach (ulong x in (ReadOnlySpan<ulong>)[h.A, h.V])
                if (enemy.Contains(x) && !order.Contains(x)) order.Add(x);
        for (int i = 0; i < order.Count; i++)
        {
            string n = Name(r, order[i]);
            if (!Lower(n).Contains("leader", StringComparison.Ordinal)) r.Names[order[i]] = n + " #" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        {
            var same = new SortedDictionary<string, List<ulong>>(StringComparer.Ordinal);
            foreach (var o in ours) { string k = Name(r, o); if (!same.TryGetValue(k, out var l)) same[k] = l = []; l.Add(o); }
            foreach (var (k, v) in same)
                if (v.Count > 1)
                    for (int i = 0; i < v.Count; i++) r.Names[v[i]] = k + " (" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
        string one0 = stem.Length == 0 ? "enemy" : Lower(stem[(stem.LastIndexOf(' ') < 0 ? 0 : stem.LastIndexOf(' ') + 1)..]);
        string one = one0;
        string many = one == "enemy" ? "enemies" : PluralWord(one);
        string foes = stem.Length == 0 ? "enemies" : Plural(stem);
        string group = "";
        if (ctx?.Factions is { } factions && enemy.Count > 0)
        {
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            var fc = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in enemy)
            {
                kinds.Add(Stem(names.TryGetValue(e, out var n) ? n : ""));
                if (factions.TryGetValue(e, out var f) && f.Length > 0) fc[f] = fc.GetValueOrDefault(f) + 1;
            }
            string bestF = "";
            int bestN = 0;
            foreach (var (k, v) in fc) if (v > bestN) { bestN = v; bestF = k; }
            if (kinds.Count >= 2 && bestF.Length > 0 && bestN * 2 >= enemy.Count) group = bestF;
        }
        string oneW = one, manyW = many;
        if (group.Length > 0)
        {
            foes = group;
            r.Fired.Add("NAMED BY FACTION");
            (oneW, manyW) = FactionNoun(group);
        }
        one = oneW;
        many = manyW;
        uint s0 = unchecked((uint)(r.T0 * 7.0) * 2654435761u);
        int Var(int k) { s0 = unchecked(s0 * 1664525u + 1013904223u); return (int)((s0 >> 16) % (uint)k); }

        // ---- tallies
        var taken = new SortedDictionary<ulong, double>();
        var takenEnemy = new SortedDictionary<ulong, double>();
        var toEnemy = new SortedDictionary<ulong, double>();
        var ffDealt = new SortedDictionary<ulong, double>();
        var hitsByEnemy = new SortedDictionary<ulong, int>();
        var blocksEnemy = new SortedDictionary<ulong, int>();
        var kos = new SortedDictionary<ulong, int>();
        var limbs = new SortedDictionary<ulong, int>();
        var hitsOn = new SortedDictionary<ulong, int>();
        var blocks = new SortedDictionary<ulong, int>();
        var swings = new SortedDictionary<ulong, int>();
        var ffHits = new SortedDictionary<ulong, int>();
        var downsTheirs = new List<int>();
        var downsOurs = new List<int>();
        var ffList = new List<int>();
        var downed = new HashSet<ulong>();
        int zero = 0;
        for (int i = 0; i < b.Count; i++)
        {
            var h = b[i];
            float d = h.Drop();
            Add(taken, h.V, d);
            Add(hitsOn, h.V, 1);
            Add(swings, h.A, 1);
            if (enemy.Contains(h.A) && !h.Ff) { Add(takenEnemy, h.V, d); Add(hitsByEnemy, h.V, 1); if (d == 0) Add(blocksEnemy, h.V, 1); }
            if (d == 0) { Add(blocks, h.V, 1); ++zero; }
            if (h.Ff) { Add(ffDealt, h.A, d); Add(ffHits, h.A, 1); ffList.Add(i); }
            if (h.Downing() && !h.Ff) Add(kos, h.A, 1);
            if (h.Lb1 > h.Lb0 && !h.Ff) Add(limbs, h.A, h.Lb1 - h.Lb0);
            if (ours.Contains(h.A) && enemy.Contains(h.V)) Add(toEnemy, h.A, d);
            if (h.Downing() && enemy.Contains(h.V) && !h.Ff) downsTheirs.Add(i);
            if (h.Downing() && ours.Contains(h.V)) downsOurs.Add(i);
            if (h.Dn1 || h.Dd1) downed.Add(h.V);
        }
        int nEnemy = order.Count, nOurs = ours.Count;
        int enemyDown = order.Count(downed.Contains);
        int unaccounted = nEnemy - enemyDown;
        int oursDownFF = downsOurs.Count(i => b[i].Ff);
        int oursDown = downsOurs.Count, oursDownEnemy = oursDown - oursDownFF;
        bool skirmish = nEnemy < 10 && nOurs < 10;
        int result;
        if (nEnemy > 0 && enemyDown * 4 >= nEnemy * 3 && oursDownEnemy * 4 <= nOurs) result = 0;
        else if (nEnemy > 0 && enemyDown * 2 >= nEnemy && oursDownEnemy <= enemyDown) result = oursDownEnemy * 3 > enemyDown ? 2 : 1;
        else if (oursDownEnemy > enemyDown) result = 3;
        else result = 4;
        bool atTheirs = ctx is not null && ctx.PlaceKind == 2;
        bool weStruck = ctx is not null && ctx.PlaceKind == 0 && b.Count > 0 && ours.Contains(b[0].A) && !b[0].Ff;
        bool atOurs = ctx is not null && ctx.PlaceKind == 1;
        bool raid = nEnemy >= 6 && !atTheirs && !(weStruck && !atOurs);
        if (atTheirs) r.Fired.Add("OUR ASSAULT");
        else if (weStruck && nEnemy >= 6) r.Fired.Add("OUR ATTACK");
        string[] resultWord = [raid ? "raid broken" : "enemy broken", raid ? "raid beaten off" : "enemy beaten off", "a hard win", "we were overrun", "no clear result"];
        double ffTotal = 0;
        foreach (int i in ffList) ffTotal += b[i].Drop();
        int ffRanged = ffList.Count(i => b[i].Ranged);
        bool ffShots = ffList.Count > 0 && ffRanged * 2 >= ffList.Count;
        double total = 0;
        foreach (var v in toEnemy.Values) total += v;

        // ---- header
        var sb = new System.Text.StringBuilder();
        string sideUp = Upper(side.Length == 0 ? "Our side" : side);
        r.Title = sideUp + " " + Dash + " REPORT OF ACTION No. " + F0(number);
        if (GameClock.HoursAt(b.Count == 0 ? 0.0 : b[0].T, out double gh)) { r.GameH0 = gh; r.Title += " " + Dash + " DAY " + F0(GameClock.Day(gh)); }
        {
            string head;
            if (atTheirs) head = "Our assault on " + (ctx!.Place.Length == 0 ? "the " + foes : ctx.Place);
            else if (!raid && nEnemy >= 6) head = "Our attack on the " + foes;
            else if (raid) head = group.Length == 0 ? "The " + TitleCase(stem.Length == 0 ? "enemy" : stem) + " Raid" : "A raid by the " + group;
            else head = "A skirmish with " + (group.Length == 0 ? (stem.Length == 0 ? "enemies" : foes) : "the " + group);
            sb.Append(Mark('y') + head + "\n");
        }
        sb.Append("Contact " + Clock(r.T0) + " to " + Clock(r.T1) + " (" + Duration(r.T1 - r.T0) + ")  " + Dash + "  " + (stem.Length == 0 ? "Enemies" : foes) + " " + F0(nEnemy)
            + "  " + Dash + "  " + (side.Length == 0 ? "Ours" : side) + " " + F0(nOurs) + "\n");
        { string gs = GameSpan(r.T0, r.T1); if (gs.Length > 0) sb.Append("In game: " + gs + "\n"); }
        if (ctx is not null && ctx.Place.Length > 0)
            sb.Append("Where: " + ctx.Place + (ctx.PlaceKind == 1 ? " (ours)" : ctx.PlaceKind == 2 ? " (" + (ctx.PlaceOwner.Length == 0 ? "theirs" : ctx.PlaceOwner) + ")"
                : ctx.PlaceOwner.Length == 0 ? "" : " (" + ctx.PlaceOwner + ")") + "\n");
        {
            string res = "Result: " + resultWord[result] + ". " + F0(enemyDown) + " " + (enemyDown == 1 ? one : many) + " down";
            if (unaccounted > 0) res += ", " + F0(unaccounted) + " not accounted for (fled, or still standing off the field)";
            res += ". ";
            if (oursDown == 0) res += "None of ours down.";
            else res += F0(oursDown) + " of ours down" + (oursDownFF == oursDown ? ", by our own hand." : oursDownFF != 0 ? " (" + F0(oursDownFF) + " by our own hand)." : ".");
            sb.Append(res + "\n");
        }
        sb.Append("Blows: " + F0(b.Count) + " struck; " + F0(zero) + " drew no blood.\n");
        r.Fired.Add("HEADER");

        // ---- what happened
        var tl = new List<TL>();
        ulong openerA = 0, leaderA = 0, bloodA = 0;
        for (int i = 0; i < b.Count && bloodA == 0; i++) if (!b[i].Ff && ours.Contains(b[i].A) && enemy.Contains(b[i].V) && b[i].Drop() > 0) bloodA = b[i].A;
        Timeline.Contact(b, r, ours, enemy, many, tl);
        if (downsTheirs.Count > 0)
        {
            var f = b[downsTheirs[0]];
            openerA = f.A;
            string how = f.Ranged ? "a single shot" : IsAnimalName(Name(r, f.A)) ? "a charge" : f.H0 >= 699 ? "a single blow" : "a blow";
            tl.Add(new TL { T = f.T, Text = "First down. " + Name(r, f.A) + " drops " + Name(r, f.V) + " with " + how + " of " + F0(f.Drop()) + ".", Col = 'o', Who = f.A });
            r.Fired.Add("OPENING");
        }
        Timeline.FocusFire(b, r, ours, enemy, many, tl);
        Timeline.WallOfBlocks(b, r, ours, tl);
        Timeline.Multis(b, r, downsTheirs, many, tl);
        Timeline.Collapse(b, r, downsTheirs, many, raid, tl);
        foreach (int i in downsTheirs)
            if (b[i].Drop() < 5)
            {
                tl.Add(new TL { T = b[i].T, Text = Name(r, b[i].A) + " finishes " + Name(r, b[i].V) + " with a tap of " + F0(b[i].Drop()) + ".", Col = 'o', Who = b[i].A });
                r.Fired.Add("CHEEKY FINISHER");
            }
        leaderA = Timeline.Leaders(b, r, downsTheirs, bloodA, tl);
        Timeline.MopUps(b, r, ours, enemy, mopUps, tl);
        foreach (int i in downsOurs)
            if (!b[i].Ff)
            {
                var h = b[i];
                tl.Add(new TL { T = h.T, Text = Name(r, h.V) + (h.VLeader == 1 ? ", our squad leader," : "") + " goes down (" + Name(r, h.A) + ", " + F0(h.Drop()) + ").", Col = 'r', Who = h.V });
                r.Fired.Add("DOWN");
            }
        if (ffList.Count > 0)
        {
            double f1 = b[ffList[^1]].T;
            string what = ffShots
                ? (ffList.Count == 1 ? "One of our own shots lands in the melee (see Losses)." : "Our own shots start landing in the melee. " + Cap(Of(ffList.Count)) + " hit our people (see Losses).")
                : (ffList.Count == 1 ? "One blow lands on our own (see Losses)." : "The press turns on itself. " + Cap(Of(ffList.Count)) + " blows land on our own (see Losses).");
            tl.Add(new TL { T = b[ffList[0]].T, Text = "@" + When(f1) + "|" + what, Col = 'g' });
            r.Fired.Add("FRIENDLY FIRE");
        }
        Timeline.Deaths(r, enemy, kills, tl);
        {
            string endText = result <= 2 ? (unaccounted > 0 ? "The last of them is down or gone. The field is ours." : "The last of them is down. The field is ours.")
                : result == 3 ? "The fighting stops. The field is theirs." : "The fighting stops. Nobody holds the field.";
            double endT = r.T1;
            foreach (var k in kills) if (k.T <= r.T1 + 2.0 && k.T > endT) endT = k.T;
            tl.Add(new TL { T = endT + 0.001, Text = endText, Col = 'n' });
        }
        tl = [.. tl.OrderBy(x => x.T)];   // stable, as std::stable_sort
        {
            var keep = new List<TL>();
            foreach (var x in tl)
            {
                bool dup = false;
                if (x.Who != 0 && x.Col == 'b')
                    foreach (var k in keep) if (k.Who == x.Who && x.T - k.T <= 10) { dup = true; break; }
                if (!dup) keep.Add(x);
            }
            tl = keep;
        }
        if (skirmish && tl.Count > 6) tl = Timeline.Shorten(tl);
        sb.Append("\nWHAT HAPPENED\n");
        foreach (var x in tl)
        {
            string when = When(x.T), text = x.Text;
            if (text.Length > 0 && text[0] == '@')
            {
                int bar = text.IndexOf('|');
                string end = text[1..bar];
                text = text[(bar + 1)..];
                if (end != when) when += " to " + end;
            }
            sb.Append(Mark(x.Col) + when + " " + Dash + " " + text + "\n");
        }

        // ---- mentioned in this report
        {
            var sbl = new List<(int K, double D, ulong Who)>();
            foreach (var o in ours) if (Get(kos, o) != 0 || Get(toEnemy, o) > 0) sbl.Add((Get(kos, o), Get(toEnemy, o), o));
            sbl = [.. sbl.OrderByDescending(x => x.K).ThenByDescending(x => x.D).ThenByDescending(x => x.Who)];   // std::sort then reverse
            int limbTotal = 0;
            foreach (var v in limbs.Values) limbTotal += v;
            int top = skirmish ? 3 : 5;
            if (sbl.Count > 0)
            {
                sb.Append("\nMENTIONED IN THIS REPORT\n");
                for (int i = 0; i < sbl.Count && i < top; i++)
                {
                    var (k, d, who) = sbl[i];
                    string line = F0(i + 1) + ". " + Name(r, who) + ": " + F0(k) + " down, " + F0(d) + (i == 0 ? " damage" : "");
                    var why = new List<string>();
                    if (i == 0 && total > 0)
                    {
                        long per = (long)(100 * d / total + 0.5);
                        line += per >= 100 ? ", every point the " + many + " took" : ", " + Of(per) + " in every hundred points the " + many + " took";
                        why.Add("Carried the fight.");
                    }
                    int lw = Get(limbs, who);
                    if (lw > 0 && lw == limbTotal) line += limbTotal == 1 ? ", and the only limb taken" : ", and every limb taken";
                    else if (lw > 0) line += ", and " + Of(lw) + (lw == 1 ? " limb" : " limbs");
                    line += ".";
                    {
                        var cr = new List<string>();
                        if (who == bloodA) cr.Add("first blood");
                        if (who == openerA) cr.Add("the first down");
                        if (who == leaderA) cr.Add(cr.Count == 0 ? "dropped the leader" : "the leader");
                        if (cr.Count > 0) why.Add(Cap(JoinAnd(cr)) + ".");
                    }
                    if (IsAnimalName(Name(r, who)) && k >= 2)
                    {
                        string[] f = ["Feed it double.", "Somebody pat it.", "Give it the good feed."];
                        why.Add("A pack animal with " + Of(k) + " knockouts. " + f[Var(3)]);
                    }
                    for (int j = 0; j < why.Count && j < 2; j++) line += " " + why[j];
                    sb.Append(Mark(i == 0 ? 'y' : 'n') + line + "\n");
                }
                r.Fired.Add("MENTIONED");
                if (total > 0) r.Fired.Add("CARRIED THE BATTLE");
            }
        }
        const int AnchorMinBlows = 5;
        ulong tank = 0;
        double tv = 0;
        {
            var downOurs = new HashSet<ulong>(downsOurs.Select(i => b[i].V));
            foreach (var o in ours) if (!downOurs.Contains(o) && Get(takenEnemy, o) > tv) { tv = Get(takenEnemy, o); tank = o; }
            if (tank != 0 && Get(hitsByEnemy, tank) < AnchorMinBlows) { tank = 0; tv = 0; }
        }
        if (!skirmish)
        {
            string blk = "";
            if (tank != 0)
            {
                int hb = Get(hitsByEnemy, tank), bl = Get(blocksEnemy, tank);
                blk += Mark('b') + "The anchor: " + Name(r, tank) + " took " + Of(hb) + (hb == 1 ? " blow" : " blows") + " and " + F0(tv) + " damage, "
                    + (bl == 0 ? "blocked none" : "blocked " + Of(bl)) + ", and kept their feet.\n";
                r.Fired.Add("TOOK THE MOST PUNISHMENT");
            }
            ulong soft = 0;
            double sv = 0;
            foreach (var (k, v) in toEnemy.ToList()) if (Get(kos, k) == 0 && v > sv) { sv = v; soft = k; }
            if (soft != 0 && sv >= 0.04 * total) { blk += "Did the work, got no credit: " + Name(r, soft) + ", " + F0(sv) + " damage and not one knockout.\n"; r.Fired.Add("SOFTENED THEM UP"); }
            if (blk.Length > 0) sb.Append("\n" + blk);
        }

        // ---- losses and friendly fire
        sb.Append("\nLOSSES AND FRIENDLY FIRE\n");
        {
            var od = new List<string>();
            foreach (int i in downsOurs)
            {
                var h = b[i];
                od.Add(Name(r, h.V) + ", " + WhenAt(h.T) + (h.Ff ? (h.Ranged ? ", shot down by " : ", knocked out by ") + Name(r, h.A) : " (" + Name(r, h.A) + ")"));
            }
            sb.Append(Mark(od.Count == 0 ? 'n' : 'r') + "Ours down: " + (od.Count == 0 ? "none" : JoinAnd(od)) + ".\n");
        }
        if (ffList.Count > 0) sb.Append(Mark('g') + Losses.FriendlyFire(b, r, ffList, ffHits, ffTotal, ffShots, ffRanged) + "\n");
        if (!skirmish)
        {
            var onlyOwn = new List<string>();
            var never = new List<string>();
            foreach (var o in ours)
            {
                if (Get(toEnemy, o) > 0) continue;
                if (Get(ffDealt, o) > 0) onlyOwn.Add(Name(r, o));
                else if (Get(hitsOn, o) != 0 || Get(swings, o) != 0) never.Add(Name(r, o));
            }
            onlyOwn.Sort(StringComparer.Ordinal);
            never.Sort(StringComparer.Ordinal);
            if (onlyOwn.Count > 0) sb.Append(Mark('g') + "Struck only their own: " + JoinAnd(onlyOwn) + ".\n");
            if (never.Count > 0)
            {
                var v = never.Take(14).ToList();
                if (never.Count > 14) v.Add(F0(never.Count - 14) + " more");
                sb.Append(Mark('d') + "Never touched the enemy: " + JoinAnd(v) + ".\n");
            }
            if (onlyOwn.Count > 0 || never.Count > 0) r.Fired.Add("BARELY CONTRIBUTED");
        }
        r.Body = sb.ToString();
        sb.Clear();

        // ---- sustain / improve (written always, shown when asked for)
        if (!skirmish)
            sb.Append(Losses.Sustain(b, r, ours, downsTheirs, ffList, ffHits, ffShots, tank, leaderA, kos, one, many, nOurs, nEnemy, result, raid, oursDownEnemy));
        r.Advice = sb.ToString();
        sb.Clear();

        // ---- one closing line
        {
            string c;
            string nW = Cap(Of(nEnemy));
            string came = one == "bandit" && Lower(stem).Contains("hungry", StringComparison.Ordinal) ? " came hungry." : " came.";
            if (nEnemy == 1) c = result <= 2 ? "One came. One fell." : result == 3 ? "One came, and it was enough." : "One came. Nobody fell.";
            else if (result == 0) c = nW + came + " " + (unaccounted == 0 ? "None went home." : unaccounted == 1 ? "One is still out there." : Cap(Of(unaccounted)) + " are still out there.");
            else if (result == 1) { string[] v = ["They will be back. Fewer of them.", "Beaten off. Not beaten."]; c = v[Var(2)]; }
            else if (result == 2) { string[] v = ["It was closer than it looked.", "A win. Count the bandages."]; c = v[Var(2)]; }
            else if (result == 3) { string[] v = ["The field is theirs. For now.", "They ate well tonight."]; c = v[Var(2)]; }
            else { string[] v = ["Nobody won. Everybody bled.", "Both sides went home tired."]; c = v[Var(2)]; }
            sb.Append("\n" + Mark('d') + c + "\n");
        }
        r.Closing = sb.ToString();
        return r;
    }

    #endregion

    #region Timeline moments

    static class Timeline
    {
        public static void Contact(List<Hit> b, Report r, SortedSet<ulong> ours, SortedSet<ulong> enemy, string many, List<TL> tl)
        {
            var f = b[0];
            ulong lead = ours.Contains(f.V) ? f.V : ours.Contains(f.A) ? f.A : 0;
            string text = "Contact.";
            if (lead != 0)
            {
                var seen = new HashSet<ulong>();
                double second = r.T1;
                foreach (var h in b)
                    foreach (ulong x in (ReadOnlySpan<ulong>)[h.A, h.V])
                        if (ours.Contains(x) && x != lead && seen.Add(x) && h.T < second) second = h.T;
                var at = new HashSet<ulong>();
                var took = new List<float>();
                for (int i = 0; i < b.Count && b[i].T <= r.T0 + 6; i++) if (b[i].V == lead && enemy.Contains(b[i].A)) { at.Add(b[i].A); took.Add(b[i].Drop()); }
                string list = string.Join(", ", took.Take(6).Select(x => F0(x)));
                if (at.Count >= 2 && second - r.T0 >= 4)
                {
                    text += " " + Cap(Of(at.Count)) + " " + many + " find " + Engine.Name(r, lead) + " alone and go for them together: " + Of(took.Count) + " blows in six seconds (" + list + "). "
                        + Engine.Name(r, lead) + " gives no ground for " + Of((long)(second - r.T0 + 0.5)) + " seconds.";
                    r.Fired.Add("HOLDING THE LINE");
                }
                else if (at.Count >= 3) { text += " " + Cap(Of(at.Count)) + " " + many + " go for " + Engine.Name(r, lead) + " together (" + list + ")."; r.Fired.Add("FOCUS FIRE"); }
                else text += " The " + many + " reach " + Engine.Name(r, lead) + " first.";
            }
            tl.Add(new TL { T = r.T0, Text = text, Col = 'b', Who = lead });
        }

        public static void FocusFire(List<Hit> b, Report r, SortedSet<ulong> ours, SortedSet<ulong> enemy, string many, List<TL> tl)
        {
            int n = 0;
            foreach (var o in ours)
            {
                if (n >= 2) break;
                if (tl.Count > 0 && o == tl[0].Who) continue;
                var hs = b.Where(h => h.V == o && enemy.Contains(h.A)).ToList();
                for (int k = 0; k < hs.Count; k++)
                {
                    var who = new HashSet<ulong>();
                    bool down = false;
                    for (int j = k; j < hs.Count && hs[j].T - hs[k].T <= 6; j++) { who.Add(hs[j].A); down = down || hs[j].Downing(); }
                    if (who.Count < 3) continue;
                    tl.Add(new TL
                    {
                        T = hs[k].T, Col = down ? 'r' : 'b', Who = o,
                        Text = Cap(Of(who.Count)) + " " + many + " gang up on " + Engine.Name(r, o) + (down ? ", who goes down." : "; " + Engine.Name(r, o) + " stays standing."),
                    });
                    r.Fired.Add("FOCUS FIRE");
                    ++n;
                    break;
                }
            }
        }

        public static void WallOfBlocks(List<Hit> b, Report r, SortedSet<ulong> ours, List<TL> tl)
        {
            ulong who = 0;
            int best = 0;
            double at = 0;
            foreach (var o in ours)
            {
                int run = 0;
                foreach (var h in b)
                {
                    if (h.V != o) continue;
                    run = h.Drop() == 0 ? run + 1 : 0;
                    if (run > best) { best = run; who = o; at = h.T; }
                }
            }
            if (best < 3) return;
            tl.Add(new TL { T = at, Text = Engine.Name(r, who) + " turns aside " + Of(best) + " blows in a row.", Col = 'b', Who = who });
            r.Fired.Add("WALL OF BLOCKS");
        }

        public static void Multis(List<Hit> b, Report r, List<int> downsTheirs, string many, List<TL> tl)
        {
            var g = new SortedDictionary<(ulong, long), List<int>>();
            foreach (int i in downsTheirs)
            {
                var key = (b[i].A, (long)Math.Floor(b[i].T * 10 + 0.5));
                if (!g.TryGetValue(key, out var l)) g[key] = l = [];
                l.Add(i);
            }
            foreach (var (_, l) in g)
            {
                if (l.Count < 2) continue;
                var h = b[l[0]];
                string word = IsAnimalName(Engine.Name(r, h.A)) ? "charge" : h.Ranged ? "volley" : "sweep";
                var nums = l.Select(j => F0(b[j].Drop())).ToList();
                tl.Add(new TL { T = h.T, Text = Engine.Name(r, h.A) + " drops " + Of(l.Count) + " " + many + " in one " + word + " (" + JoinAnd(nums) + ").", Col = 'o', Who = h.A });
                r.Fired.Add("TWO AT ONCE");
            }
            var ts = new SortedDictionary<ulong, List<double>>();
            foreach (int i in downsTheirs) { if (!ts.TryGetValue(b[i].A, out var l)) ts[b[i].A] = l = []; l.Add(b[i].T); }
            foreach (var (a, t) in ts)
                for (int k = 0; k + 2 < t.Count; k++)
                    if (t[k + 2] - t[k] <= 5)
                    {
                        tl.Add(new TL { T = t[k], Text = Engine.Name(r, a) + " drops three in " + Of((long)(t[k + 2] - t[k] + 0.5)) + " seconds.", Col = 'o', Who = a });
                        r.Fired.Add("HAT TRICK");
                        break;
                    }
        }

        public static void Collapse(List<Hit> b, Report r, List<int> downsTheirs, string many, bool raid, List<TL> tl)
        {
            if (downsTheirs.Count < 4) return;
            int best = 0;
            double at = 0;
            foreach (int k in downsTheirs)
            {
                int c = downsTheirs.Count(j => b[j].T >= b[k].T && b[j].T <= b[k].T + 15);
                if (c > best) { best = c; at = b[k].T; }
            }
            if (best < 4) return;
            var who = new SortedSet<string>(StringComparer.Ordinal);
            double last = at;
            foreach (int j in downsTheirs)
                if (b[j].T >= at && b[j].T <= at + 15) { who.Add(Engine.Name(r, b[j].A)); if (b[j].T > last) last = b[j].T; }
            tl.Add(new TL
            {
                T = at, Col = 'o',
                Text = "@" + When(last) + "|" + (raid ? "The raid breaks. " : "They break. ") + Cap(Of(best)) + " " + many + " fall in " + Seconds(last - at) + ": to " + JoinAnd([.. who]) + ".",
            });
            r.Fired.Add("THE COLLAPSE");
        }

        public static ulong Leaders(List<Hit> b, Report r, List<int> downsTheirs, ulong bloodA, List<TL> tl)
        {
            var leads = new List<int>();
            var done = new HashSet<ulong>();
            foreach (int i in downsTheirs)
            {
                var h = b[i];
                bool lead = h.VLeader == 1 || (h.VLeader < 0 && Lower(Engine.Name(r, h.V)).Contains("leader", StringComparison.Ordinal));
                if (!lead || !done.Add(h.V)) continue;
                leads.Add(i);
            }
            if (leads.Count == 0) return 0;
            var f = b[leads[0]];
            string vn = Engine.Name(r, f.V);
            string text = "The leader falls. " + Engine.Name(r, f.A) + " drops " + (Lower(vn).Contains("leader", StringComparison.Ordinal) ? "the " + vn : vn + ", who led them") + ".";
            if (f.A == bloodA) text += " " + Engine.Name(r, f.A) + " opened the fight, and " + Engine.Name(r, f.A) + " ends their leader.";
            tl.Add(new TL { T = f.T, Text = text, Col = 'y', Who = f.A });
            r.Fired.Add("THE LEADER FALLS");
            if (leads.Count > 1)
            {
                var next = new List<string>();
                for (int k = 1; k < leads.Count; k++)
                {
                    string vk = Engine.Name(r, b[leads[k]].V);
                    int hp = vk.IndexOf(" #", StringComparison.Ordinal);
                    next.Add((hp < 0 ? vk : vk[(hp + 1)..]) + " to " + Engine.Name(r, b[leads[k]].A) + " (" + WhenAt(b[leads[k]].T) + ")");
                }
                tl.Add(new TL
                {
                    T = b[leads[1]].T, Col = 'y',
                    Text = "@" + When(b[leads[^1]].T) + "|" + (leads.Count == 2 ? "The next in line takes over, and falls: " : "Each one who takes over falls in turn: ") + JoinAnd(next) + ".",
                });
                r.Fired.Add("SUCCESSION");
            }
            return f.A;
        }

        public static void MopUps(List<Hit> b, Report r, SortedSet<ulong> ours, SortedSet<ulong> enemy, List<double>? mopUps, List<TL> tl)
        {
            for (int m = 0; mopUps is not null && m < mopUps.Count; m++)
            {
                double mopUpAt = mopUps[m], mopEnd = m + 1 < mopUps.Count ? mopUps[m + 1] : 1e30;
                var back = new HashSet<ulong>();
                var downedBefore = new HashSet<ulong>();
                ulong first = 0;
                foreach (var h in b)
                {
                    if (h.T < mopUpAt) { if (h.Dn1 || h.Dd1) downedBefore.Add(h.V); continue; }
                    if (h.T >= mopEnd) break;
                    if (enemy.Contains(h.A)) { back.Add(h.A); if (ours.Contains(h.V) && first == 0) first = h.V; }
                    if (enemy.Contains(h.V)) { back.Add(h.V); if (ours.Contains(h.A) && first == 0) first = h.A; }
                }
                int fallen = back.Count(downedBefore.Contains);
                int nb = back.Count;
                string who = first != 0 ? Engine.Name(r, first) : "us";
                string text = "Mop-up. ";
                if (nb <= 1) text += (fallen == 1 ? "One of the fallen gets back up and goes for " : "One of them comes back and goes for ") + who + ".";
                else if (fallen == nb) text += Cap(Of(nb)) + " of the fallen get back up and go for " + who + ".";
                else text += Cap(Of(nb)) + " of them come back and go for " + who + ".";
                tl.Add(new TL { T = mopUpAt, Text = text, Col = 'd' });
                r.Fired.Add("MOP-UP");
            }
        }

        public static void Deaths(Report r, SortedSet<ulong> enemy, List<Event> kills, List<TL> tl)
        {
            var inn = kills.Where(k => k.Kind == "kill" && enemy.Contains(k.V) && k.T <= r.T1 + 2.0).ToList();
            if (inn.Count == 0) return;
            var by = new SortedDictionary<ulong, List<string>>();
            foreach (var e in inn)
            {
                string vn = Engine.Name(r, e.V);
                int h = vn.IndexOf(" #", StringComparison.Ordinal);
                if (!by.TryGetValue(e.A, out var l)) by[e.A] = l = [];
                l.Add(h < 0 ? vn : vn[(h + 1)..]);
            }
            var parts = by.Select(kv => JoinAnd(kv.Value) + " to " + Engine.Name(r, kv.Key)).ToList();
            tl.Add(new TL
            {
                T = inn[0].T, Col = 'd',
                Text = "@" + When(inn[^1].T) + "|" + (inn.Count == 1 ? "One of the fallen dies: " : Cap(Of(inn.Count)) + " of the fallen die: ") + JoinAnd(parts) + ".",
            });
            r.Fired.Add("DEATHS");
        }

        /// <summary>A skirmish keeps six lines: the leader, ours down and friendly fire first, then contact, first down and the end, then the rest.</summary>
        public static List<TL> Shorten(List<TL> tl)
        {
            var pri = new int[tl.Count];
            for (int i = 0; i < tl.Count; i++)
            {
                char c = tl[i].Col;
                pri[i] = 3;
                if (c is 'y' or 'r' or 'g' || tl[i].Text.StartsWith("Mop-up.", StringComparison.Ordinal)) pri[i] = 0;
                else if (i == 0 || i + 1 == tl.Count || tl[i].Text.StartsWith("First down.", StringComparison.Ordinal)) pri[i] = 1;
            }
            var keepIt = new bool[tl.Count];
            int n = 0;
            for (int p = 0; p <= 3 && n < 6; p++)
                for (int i = 0; i < tl.Count && n < 6; i++)
                    if (pri[i] == p && !keepIt[i]) { keepIt[i] = true; ++n; }
            return [.. tl.Where((_, i) => keepIt[i])];
        }
    }

    #endregion

    #region Losses and advice

    static class Losses
    {
        public static string FriendlyFire(List<Hit> b, Report r, List<int> ffList, SortedDictionary<ulong, int> ffHits, double ffTotal, bool ffShots, int ffRanged)
        {
            string f = "Friendly fire: " + Of(ffList.Count) + (ffList.Count == 1 ? " blow, " : " blows, ") + F0(ffTotal) + " damage";
            if (ffShots) f += ffRanged == ffList.Count ? (ffList.Count == 1 ? ", a shot into our own melee." : ", all of it shot into our own melee.") : ", " + Of(ffRanged) + " of them shots into our own melee.";
            else f += ".";
            ulong worst = 0;
            int wh = 0;
            foreach (var (k, v) in ffHits) if (v > wh) { wh = v; worst = k; }
            bool worstKO = ffList.Any(i => b[i].A == worst && b[i].Downing());
            var used = new HashSet<int>();
            if (worst != 0 && (wh >= 3 || worstKO))
            {
                var v = new List<string>();
                bool allShot = true;
                for (int i = 0; i < ffList.Count; i++)
                {
                    var h = b[ffList[i]];
                    if (h.A != worst) continue;
                    v.Add(Engine.Name(r, h.V) + " " + F0(h.Drop()) + (h.Downing() ? ", out" : ""));
                    used.Add(i);
                    allShot = allShot && h.Ranged;
                }
                f += " " + Engine.Name(r, worst) + (allShot ? ", shooting into the melee," : "") + " the worst offender: " + Of(wh) + " of ours " + (allShot ? "hit" : "struck") + " (" + JoinAnd(v) + ").";
            }
            for (int i = 0; i < ffList.Count; i++)
            {
                if (used.Contains(i)) continue;
                var hi = b[ffList[i]];
                for (int j = i + 1; j < ffList.Count; j++)
                {
                    var hj = b[ffList[j]];
                    if (used.Contains(j) || hj.A != hi.V || hj.V != hi.A) continue;
                    f += " " + Engine.Name(r, hi.A) + " and " + Engine.Name(r, hi.V) + (hi.Ranged && hj.Ranged ? " shot each other (" : " traded blows (") + F0(hi.Drop()) + " and " + F0(hj.Drop()) + ").";
                    used.Add(i);
                    used.Add(j);
                    break;
                }
            }
            for (int i = 0; i < ffList.Count; i++)
            {
                if (used.Contains(i)) continue;
                var h = b[ffList[i]];
                if (h.Ranged) f += " " + Engine.Name(r, h.A) + "'s " + (h.Purpose == 0 ? "stray shot" : "shot") + " caught " + Engine.Name(r, h.V) + " for " + F0(h.Drop()) + (h.Downing() ? " and dropped them" : "") + ".";
                else f += " " + Engine.Name(r, h.A) + " caught " + Engine.Name(r, h.V) + " for " + F0(h.Drop()) + (h.Downing() ? " and knocked them out" : "") + ".";
            }
            return f;
        }

        public static string Sustain(List<Hit> b, Report r, SortedSet<ulong> ours, List<int> downsTheirs, List<int> ffList, SortedDictionary<ulong, int> ffHits, bool ffShots,
            ulong tank, ulong leaderA, SortedDictionary<ulong, int> kos, string one, string many, int nOurs, int nEnemy, int result, bool raid, int oursDownEnemy)
        {
            var s = new System.Text.StringBuilder("\nSUSTAIN / IMPROVE\n");
            double breakT = downsTheirs.Count >= 4 ? b[downsTheirs[downsTheirs.Count * 3 / 4]].T : r.T1;
            string expected = nOurs >= 2 * nEnemy ? "a " + (one == "bandit" ? "rabble" : "fight") + " we outnumbered " + F0(nOurs) + " to " + F0(nEnemy) + ", broken quickly"
                : nEnemy >= 2 * nOurs ? "a hard fight, " + F0(nEnemy) + " of them to " + F0(nOurs) + " of us" : "an even fight";
            bool quick = nOurs >= 2 * nEnemy;
            string when = breakT - r.T0 < 60 ? "inside a minute" : "in " + Of((long)((breakT - r.T0) / 60 + 1)) + " minutes";
            string happened = result <= 1 ? (quick ? "that, " + when : "we broke them " + when)
                : result == 2 ? "a win, harder than it should have been" : result == 3 ? "we were overrun" : "neither side broke";
            bool lateFF = ffList.Count > 0 && b[ffList[0]].T > breakT - 5;
            if (lateFF) happened += ", then a mess at the end";
            s.Append("Expected: " + expected + ". Happened: " + happened + ".\n");
            var sus = new List<string>();
            if (tank != 0) sus.Add(Engine.Name(r, tank) + " forward as the anchor");
            if (leaderA != 0) sus.Add(Engine.Name(r, leaderA) + " on the leader");
            foreach (var o in ours)
                if (IsAnimalName(Engine.Name(r, o)) && kos.GetValueOrDefault(o) > 0) { sus.Add("the beasts in the line"); r.Fired.Add("THE BEASTS"); break; }
            if (sus.Count > 0) s.Append("Sustain: " + JoinAnd(sus) + ".\n");
            var imp = new List<string>();
            int ffKO = ffList.Count(i => b[i].Downing());
            string kn = ffKO != 0 ? " and " + (ffKO == 1 ? "a knockout" : Of(ffKO) + " knockouts") : "";
            if (ffShots && ffList.Count >= 2)
                imp.Add("Our ranged kept firing once it was hand to hand: " + Of(ffList.Count) + " shots" + kn + " went into our own. Stand the turret crews and archers down when the enemy reaches our line.");
            else if (ffList.Count >= 3)
            {
                string x = Cap(Of(ffList.Count)) + " blows" + kn + " went to our own" + (lateFF ? " once the " + (raid ? "raid" : "enemy") + " was beaten" : "") + ".";
                if (nOurs >= 2 * nEnemy) x = F0(nOurs) + " blades on " + F0(nEnemy) + " " + many + " left too many swinging in too little space. " + x;
                ulong worst = 0;
                int wh = 0;
                foreach (var (k, v) in ffHits) if (v > wh) { wh = v; worst = k; }
                x += " Pull the extra blades back when the enemy breaks" + (wh >= 3 ? ", and keep " + Engine.Name(r, worst) + " out of the press." : ".");
                imp.Add(x);
            }
            if (oursDownEnemy > 0) imp.Add(Cap(Of(oursDownEnemy)) + " of ours went down to the enemy; look at who was left alone.");
            s.Append("Improve: " + (imp.Count == 0 ? "nothing worth a line." : imp[0]) + "\n");
            for (int i = 1; i < imp.Count; i++) s.Append(imp[i] + "\n");
            return s.ToString();
        }
    }

    #endregion
}
