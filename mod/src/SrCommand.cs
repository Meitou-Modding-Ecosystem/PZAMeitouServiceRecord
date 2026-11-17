// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The Military Manager's chain of command: the plugin's SrCommand.h rule for rule. Posts, top down: Commander-in-Chief (one) > General >
// Captain > Sergeant > (no post). Every character has at most one post and one superior; a superior must hold a HIGHER post, so the links
// can never form a loop. Separate from the earned chevrons. A character is a key (a string). Ordinal order = the C++ std::map's byte order.
namespace PZA.ServiceRecord.Meitou;

public static class SrCommand
{
    public const int PostNone = 0, PostSergeant = 1, PostCaptain = 2, PostGeneral = 3, PostChief = 4, PostCount = 5;
    public const int MaxDepth = 8;

    public sealed class Entry
    {
        public int Post;
        public string Sup = "";
        public bool Form;

        public Entry Copy() => (Entry)MemberwiseClone();
    }

    public sealed class Counters
    {
        public long PostsSet, ChiefReplaced, MovedToNewChief, LinksSet, LinksCleared, LinksRefused, LinksDroppedByPost, Removed, Renamed, Loaded,
            LoadedBadPost, CapRefused;
    }

    public enum LinkResult { Ok = 0, Cleared, Self, Unknown, NotSenior, Cap, NoKey }

    public static string LinkWhy(LinkResult r) => r switch
    {
        LinkResult.Ok => "set",
        LinkResult.Cleared => "cleared",
        LinkResult.Self => "refused: cannot report to themselves",
        LinkResult.Unknown => "refused: that superior holds no post",
        LinkResult.NotSenior => "refused: the superior's post is not higher",
        LinkResult.Cap => "refused: the tree is full",
        _ => "refused: no id for this character",
    };

    public sealed class Line
    {
        public string Key = "";
        public int Depth, Post;
        public bool Away;
    }

    public sealed class Tree
    {
        public readonly Counters N = new();
        public int MaxEntries = 2048;
        readonly SortedDictionary<string, Entry> _m = new(StringComparer.Ordinal);
        readonly SortedDictionary<string, string> _alias = new(StringComparer.Ordinal);

        public int Size => _m.Count;

        public Entry? Find(string key) => _m.GetValueOrDefault(key);

        public int PostOf(string key) => Find(key)?.Post ?? PostNone;

        public bool LinkLive(string key)
        {
            var e = Find(key);
            if (e is null || e.Sup.Length == 0 || e.Sup == key) return false;
            var s = Find(e.Sup);
            return s is not null && s.Post > e.Post;
        }

        public string Chief() => _m.FirstOrDefault(kv => kv.Value.Post == PostChief).Key ?? "";

        Entry At(string key) { if (!_m.TryGetValue(key, out var e)) _m[key] = e = new Entry(); return e; }

        /// <summary>Gives key a post (PostNone takes it away); one Commander-in-Chief (the old one becomes a General under the new one, and its direct
        /// reports move); links the new post makes impossible are dropped.</summary>
        public bool SetPost(string key, int post, Action<string>? demoted = null)
        {
            if (key.Length == 0) return false;
            if (post < PostNone || post >= PostCount) post = PostNone;
            if (Find(key) is null)
            {
                if (post == PostNone) return true;
                if (_m.Count >= MaxEntries) { ++N.CapRefused; return false; }
            }
            if (post == PostChief)
            {
                for (int guard = 0; guard < 64; guard++)
                {
                    string old = _m.FirstOrDefault(kv => kv.Value.Post == PostChief && kv.Key != key).Key ?? "";
                    if (old.Length == 0) break;
                    _m[old].Post = PostGeneral;
                    ++N.ChiefReplaced;
                    demoted?.Invoke(old);
                    foreach (var (k, e) in _m)
                        if (e.Sup == old && k != key) { e.Sup = key; ++N.MovedToNewChief; }
                    _m[old].Sup = key;
                }
            }
            var me = At(key);
            me.Post = post;
            ++N.PostsSet;
            if (me.Sup.Length > 0 && Find(me.Sup) is { } s && s.Post <= post) { me.Sup = ""; ++N.LinksDroppedByPost; }
            foreach (var (k, e) in _m)
                if (e.Sup == key && k != key && e.Post >= post) { e.Sup = ""; ++N.LinksDroppedByPost; }
            Prune(key);
            foreach (var k in _m.Where(kv => kv.Value.Post == PostNone && kv.Value.Sup.Length == 0 && !kv.Value.Form).Select(kv => kv.Key).ToList()) _m.Remove(k);
            return true;
        }

        public LinkResult SetSuperior(string key, string supKey)
        {
            if (key.Length == 0) { ++N.LinksRefused; return LinkResult.NoKey; }
            if (supKey.Length == 0)
            {
                if (_m.TryGetValue(key, out var e) && e.Sup.Length > 0) { e.Sup = ""; ++N.LinksCleared; Prune(key); }
                return LinkResult.Cleared;
            }
            if (supKey == key) { ++N.LinksRefused; return LinkResult.Self; }
            var s = Find(supKey);
            if (s is null || s.Post == PostNone) { ++N.LinksRefused; return LinkResult.Unknown; }
            if (s.Post <= PostOf(key)) { ++N.LinksRefused; return LinkResult.NotSenior; }
            if (Find(key) is null && _m.Count >= MaxEntries) { ++N.CapRefused; return LinkResult.Cap; }
            At(key).Sup = supKey;
            ++N.LinksSet;
            return LinkResult.Ok;
        }

        public void SetForm(string key, bool on) { if (_m.TryGetValue(key, out var e)) e.Form = on; }

        public void Remove(string key)
        {
            if (_m.Remove(key)) ++N.Removed;
            if (_m.Count == 0) _alias.Clear();
        }

        public void Load(string key, int post, string sup, bool form)
        {
            if (key.Length == 0) return;
            if (post < PostNone || post >= PostCount) { post = PostNone; ++N.LoadedBadPost; }
            string s = _alias.TryGetValue(sup, out var a) ? a : sup;
            if (s == key) s = "";
            if (post == PostNone && s.Length == 0 && !form) { _m.Remove(key); return; }
            if (Find(key) is null && _m.Count >= MaxEntries) { ++N.CapRefused; return; }
            var e = At(key);
            e.Post = post;
            e.Sup = s;
            e.Form = form;
            ++N.Loaded;
        }

        public void RenameKey(string oldKey, string newKey)
        {
            if (oldKey.Length == 0 || newKey.Length == 0 || oldKey == newKey) return;
            if (_alias.Count < 4096) _alias[oldKey] = newKey;
            if (_m.TryGetValue(oldKey, out var own))
            {
                if (!_m.ContainsKey(newKey)) _m[newKey] = own;
                _m.Remove(oldKey);
            }
            foreach (var e in _m.Values) if (e.Sup == oldKey) e.Sup = newKey;
            ++N.Renamed;
        }

        public void Clear() { _m.Clear(); _alias.Clear(); }

        /// <summary>A copy of every entry (the Military Manager's undo steps).</summary>
        public SortedDictionary<string, Entry> Snapshot() => new(_m.ToDictionary(kv => kv.Key, kv => kv.Value.Copy()), StringComparer.Ordinal);

        public void Restore(SortedDictionary<string, Entry> copy)
        {
            _m.Clear();
            foreach (var (k, e) in copy) _m[k] = e.Copy();
        }

        public IEnumerable<KeyValuePair<string, Entry>> All => _m;

        public List<string> Eligible(string key)
        {
            var o = new List<string>();
            int mine = PostOf(key);
            for (int p = PostChief; p > mine && p > PostNone; --p)
                foreach (var (k, e) in _m) if (e.Post == p && k != key) o.Add(k);
            return o;
        }

        public List<string> Reports(string key) => [.. _m.Where(kv => kv.Value.Sup == key && LinkLive(kv.Key)).Select(kv => kv.Key)];

        public List<Line> Lines(IReadOnlyDictionary<string, string> names)
        {
            var o = new List<Line>();
            var roots = new List<string>();
            var away = new List<string>();
            foreach (var (k, e) in _m)
            {
                if (LinkLive(k)) continue;
                if (e.Post > PostNone) roots.Add(k);
                else if (e.Sup.Length > 0) away.Add(k);
            }
            var ord = new Order(this, names);
            roots.Sort(ord);
            away.Sort(ord);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in roots) Walk(r, 0, ord, seen, o);
            foreach (var a in away) o.Add(new Line { Key = a, Away = true });
            return o;
        }

        public (int Officers, int Live, int NotInForce) Counts()
        {
            int officers = 0, live = 0, dead = 0;
            foreach (var (k, e) in _m)
            {
                if (e.Post > PostNone) ++officers;
                if (e.Sup.Length > 0) { if (LinkLive(k)) ++live; else ++dead; }
            }
            return (officers, live, dead);
        }

        void Prune(string key)
        {
            if (_m.TryGetValue(key, out var e) && e.Post == PostNone && e.Sup.Length == 0 && !e.Form) _m.Remove(key);
        }

        sealed class Order(Tree t, IReadOnlyDictionary<string, string> names) : IComparer<string>
        {
            string NameOf(string k) => names.TryGetValue(k, out var n) ? n : k;

            public int Compare(string? a, string? b)
            {
                if (a == b) return 0;
                int pa = t.PostOf(a!), pb = t.PostOf(b!);
                if (pa != pb) return pa > pb ? -1 : 1;
                int c = string.CompareOrdinal(NameOf(a!), NameOf(b!));
                return c != 0 ? c : string.CompareOrdinal(a, b);
            }
        }

        void Walk(string key, int depth, Order ord, HashSet<string> seen, List<Line> o)
        {
            if (depth > MaxDepth || !seen.Add(key)) return;
            o.Add(new Line { Key = key, Depth = depth, Post = PostOf(key) });
            var kids = Reports(key);
            kids.Sort(ord);
            foreach (var k in kids) Walk(k, depth + 1, ord, seen, o);
        }
    }

    public static List<string> EligibleForAll(Tree t, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return [];
        int top = 0;
        for (int i = 1; i < keys.Count; i++) if (t.PostOf(keys[i]) > t.PostOf(keys[top])) top = i;
        return [.. t.Eligible(keys[top]).Where(k => !keys.Contains(k))];
    }

    #region Auto-Organize (0.12.0)

    public sealed class OrgPerson
    {
        public string Key = "";
        public double Merit;
        public bool Animal, InArmy = true;
    }

    public sealed class OrgResult
    {
        public string Chief = "";
        public bool ChiefKept;
        public int Generals, Captains, Sergeants, Privates, Animals, LeftOut, Refused;
    }

    sealed class ByMerit : IComparer<OrgPerson>
    {
        public int Compare(OrgPerson? a, OrgPerson? b)
        {
            if (a!.Merit != b!.Merit) return a.Merit > b.Merit ? -1 : 1;
            return string.CompareOrdinal(a.Key, b.Key);
        }
    }

    static int CeilDiv(int a, int b) => b > 0 ? (a + b - 1) / b : 0;

    /// <summary>One press fills the whole tree from Merit (the plugin's rules: the chief stays, at most cap Generals, cap Captains per General,
    /// cap Sergeants per Captain, a level only when the one above cannot hold the rest; privates and animals spread over the lowest officers).</summary>
    public static OrgResult AutoOrganize(Tree t, IReadOnlyList<OrgPerson> people, int cap)
    {
        var r = new OrgResult();
        cap = Math.Clamp(cap, 1, 50);
        var hum = new List<OrgPerson>();
        var ani = new List<OrgPerson>();
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        string oldChief = t.Chief();
        bool chiefHere = false;
        foreach (var p in people)
        {
            if (p.Key.Length == 0 || !seen.Add(p.Key)) continue;
            if (p.Key == oldChief && !p.Animal) { chiefHere = true; continue; }
            if (!p.InArmy) { ++r.LeftOut; continue; }
            if (double.IsNaN(p.Merit)) continue;
            (p.Animal ? ani : hum).Add(p);
        }
        hum = [.. hum.OrderBy(p => p, new ByMerit())];
        ani = [.. ani.OrderBy(p => p, new ByMerit())];
        foreach (var k in seen)
        {
            if (chiefHere && k == oldChief) continue;
            t.SetSuperior(k, "");
            t.SetPost(k, PostNone);
        }
        int at = 0;
        if (chiefHere) { r.Chief = oldChief; r.ChiefKept = true; }
        else if (hum.Count > 0)
        {
            r.Chief = hum[0].Key;
            at = 1;
            if (!t.SetPost(r.Chief, PostChief)) { ++r.Refused; r.Chief = ""; }
        }
        if (r.Chief.Length == 0) return r;
        int h = hum.Count - at, a = ani.Count;
        int g = h < cap ? h : cap, c = 0, s = 0;
        int rest = h - g + a;
        if (g > 0 && rest > g * cap)
        {
            c = CeilDiv(rest, cap + 1);
            if (c > g * cap) c = g * cap;
            if (c > h - g) c = h - g;
            rest -= c;
            if (c > 0 && rest > c * cap)
            {
                s = CeilDiv(rest, cap + 1);
                if (s > c * cap) s = c * cap;
                if (s > h - g - c) s = h - g - c;
            }
        }
        var gens = new List<string>();
        var caps = new List<string>();
        var sergs = new List<string>();
        var kids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < g; i++, at++)
        {
            string k = hum[at].Key;
            if (!t.SetPost(k, PostGeneral) || t.SetSuperior(k, r.Chief) != LinkResult.Ok) { ++r.Refused; continue; }
            gens.Add(k);
            ++r.Generals;
        }
        for (int i = 0; i < c && gens.Count > 0; i++, at++)
        {
            string k = hum[at].Key, sup = gens[i % gens.Count];
            if (!t.SetPost(k, PostCaptain) || t.SetSuperior(k, sup) != LinkResult.Ok) { ++r.Refused; continue; }
            caps.Add(k);
            kids[sup] = kids.GetValueOrDefault(sup) + 1;
            ++r.Captains;
        }
        for (int i = 0; i < s && caps.Count > 0; i++, at++)
        {
            string k = hum[at].Key, sup = caps[i % caps.Count];
            if (!t.SetPost(k, PostSergeant) || t.SetSuperior(k, sup) != LinkResult.Ok) { ++r.Refused; continue; }
            sergs.Add(k);
            kids[sup] = kids.GetValueOrDefault(sup) + 1;
            ++r.Sergeants;
        }
        var low = new List<string>(sergs);
        low.AddRange(caps.Where(x => !kids.ContainsKey(x)));
        low.AddRange(gens.Where(x => !kids.ContainsKey(x)));
        if (low.Count == 0) low.Add(r.Chief);
        int li = 0;
        for (; at < hum.Count; at++, li++)
        {
            if (t.SetSuperior(hum[at].Key, low[li % low.Count]) == LinkResult.Ok) ++r.Privates;
            else ++r.Refused;
        }
        li = 0;
        for (int i = 0; i < ani.Count; i++, li++)
        {
            if (t.SetSuperior(ani[i].Key, low[li % low.Count]) == LinkResult.Ok) ++r.Animals;
            else ++r.Refused;
        }
        return r;
    }

    #endregion
}
