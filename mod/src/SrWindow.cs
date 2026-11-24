// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The Service Record window (Shift+R), the plugin's SrWindow.inl on PZA's Meitou UI: a Kenshi window (caption, close, minimize to the tiny
// button) with the pages Fighter (the record and the radar chart), Squad, Recent, Reports and Help, as tabs. Page texts come from SrPages and
// the report engine (proven equal to the plugin's). Long pages sit in a scrolling list (one line each, colours kept, wrapped here), so nothing is
// cut off. Squad sorting: one direct button per column (PZA UI rule: no cycling buttons). Every button has a hover text; Shift+R opens it,
// restores it from the tiny button, or closes it; it never fires while a text box has the keyboard (PzaUi.Typing). The texts are rebuilt every
// 2 s and after every click, as the plugin's.
using System.Diagnostics;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;
using PZA.MeitouUi;
using L = PZA.MeitouUi.PzaLayout;
using static PZA.ServiceRecord.Meitou.SrText;

namespace PZA.ServiceRecord.Meitou;

public static class SrWindow
{
    public const string Id = "pza.servicerecord";
    public static PzaWindow Window = null!;
    public static PzaTinyButton Tiny = null!;
    public static global::Meitou.Modding.ModSettingsGroup? Group;

    #region The squad, as the tick last saw it

    public sealed class Person
    {
        public ulong Key;
        public string Name = "";
        public Stats S = new();
        public bool Animal;
        public int Post;
    }

    static volatile List<Person> s_squad = [];

    /// <summary>Called by the tick (under SrSystem.Lock): every character of the player's with a record, by name (the plugin's Squad()).</summary>
    public static void TakeSquad(World world)
    {
        var list = new List<Person>();
        foreach (ulong key in SrSystem.Core.TrackedIds())
        {
            if (list.Count >= 512) break;
            if (SrBridge.Cold(world, SrBridge.IdOf(key)) is not { IsPlayer: true } c || SrSystem.Core.Find(key) is not { } s) continue;
            list.Add(new Person { Key = key, Name = c.Name, S = s.Clone(), Animal = global::Meitou.Simulation.Factions.SquadRegistry.IsAnimal(c),
                Post = SrSave.CommandOf(c.Stats)?.PostNumber ?? 0 });
        }
        s_squad = [.. list.OrderBy(p => p.Name, StringComparer.Ordinal)];
        // 0.1.4: the Fighter page's own character, record or not (a fresh one is in no list above): its name and post
        ulong want = s_fighter;
        s_shown = want != 0 && SrBridge.Cold(world, SrBridge.IdOf(want)) is { } f ? new Shown(want, f.Name, SrSave.CommandOf(f.Stats)?.PostNumber ?? 0) : null;
    }

    /// <summary>A new world (new game or load, 0.1.5; under SrSystem.Lock): the squad list and the Fighter page's character are ids of the
    /// world that ended, so they go; the pages are built again.</summary>
    public static void NewWorld()
    {
        s_squad = [];
        s_shown = null;
        s_fighter = 0;
        s_dirty = true;
    }

    /// <summary>The Fighter page's character as the tick last saw it (any character, with a record or not).</summary>
    sealed record Shown(ulong Key, string Name, int Post);

    static volatile Shown? s_shown;

    /// <summary>For the offline check: the Fighter page's character without a world.</summary>
    public static void SetShownForCheck(ulong key, string name, int post) => s_shown = new Shown(key, name, post);

    /// <summary>For the offline check: the Fighter page on <paramref name="key"/> (as STA does with a selected character).</summary>
    public static void SelectFighterForCheck(ulong key) { s_fighter = key; s_dirty = true; }

    /// <summary>For the offline check: the next bind puts the kept page back again (as a restart does).</summary>
    public static void ResetTabForCheck() { s_tabRestored = false; s_tabBroken = false; }

    /// <summary>For the offline check: a squad without a world.</summary>
    public static void SetSquadForCheck(List<Person> squad) => s_squad = [.. squad.OrderBy(p => p.Name, StringComparer.Ordinal)];

    /// <summary>For the offline check: the texts now (not waiting for the 2 s refresh).</summary>
    public static void Rebuild() { s_dirty = true; }

    public static IReadOnlyList<string> FighterLines => s_fighterLines;
    public static IReadOnlyList<string> SquadLines => s_squadLines;
    public static IReadOnlyList<string> ReportLines => s_reportLines;
    public static IReadOnlyList<string> HelpLines => s_helpLines;
    public static string RadarTexture => s_radar;
    public static bool IsTiny => s_tiny;

    #endregion

    #region State

    static ulong s_fighter;
    static int s_reportIdx = -1;
    static bool s_dirty = true, s_tiny;
    static readonly Stopwatch s_clock = Stopwatch.StartNew();
    static double s_lastBuild = -10;
    static List<string> s_fighterLines = [], s_squadLines = [], s_recentLines = [], s_reportLines = [], s_helpLines = [];
    static string s_radar = "", s_radarCap = "", s_radarTipAll = "";
    static readonly string[] s_axisTip = new string[SrRadar.Axes];
    static readonly bool[] s_axisKnown = new bool[SrRadar.Axes];
    static bool s_radarOn;
    static string s_normalHex = "#E9E1D2";

    static string T(string k) => SrStr.T(k);

    static string TS(string k, params string[] a) => SrStr.TS(k, a);

    #endregion

    #region Layout

    // Top: a page's top margin. PZA's Meitou UI 0.3.0 puts every tab page below its tab strip itself (the old 0.075 made a second gap).
    const double Top = 0.01, RadarX = 0.60, RadarY = 0.09, RadarW = 0.32;

    /// <summary>0.1.4 (the user's screenshot, 2026-11-21): the list was 0.55 wide and "First strike" / "Endurance" ran into it. The left
    /// labels end at the hexagon's side (about 0.644) and need about 0.11: the list now ends at 0.49 and the label boxes start after it.</summary>
    const double FighterListW = 0.47, LabelGap = 0.005;

    /// <summary>The window, in fractions of the screen (PZA UI rules: it scales with the screen).</summary>
    public static string LayoutXml()
    {
        double rh = RadarW * 1.267;   // the page (0.36 x 0.58 window, below its tab strip) is about 1.27 times as wide as high at 16:9: a square picture
        var fighter = new List<string>
        {
            L.List("lbFighter", 0.02, Top, FighterListW, 0.86 - Top),
            L.Portrait("rdImg", RadarX, RadarY, RadarW, rh),
            L.Text("rdCap", 0.56, RadarY + rh + 0.07, 0.43, 0.05, align: "Center"),
            L.Button("FBack", 0.02, 0.88, 0.30, 0.09), L.Button("FSta", 0.35, 0.88, 0.30, 0.09), L.Button("FNext", 0.68, 0.88, 0.30, 0.09),
        };
        for (int a = 0; a < SrRadar.Axes; a++)
        {
            var (x, y, w, h) = AxisLabelBox(a, rh);
            fighter.Add(L.Text("rdL" + a, x, y, w, h, align: a == 0 || a == 3 ? "Center" : a < 3 ? "Left" : "Right"));
        }
        var squad = new List<string> { L.List("lbSquad", 0.02, Top, 0.96, 0.74 - Top) };
        for (int k = 0; k < (int)SrPages.SortKey.Count; k++) squad.Add(L.Button("Sort" + k, 0.02 + 0.327 * (k % 3), k < 3 ? 0.77 : 0.875, 0.30, 0.085));
        // 0.1.1 (the user 2026-11-17: windows too big): 0.36 x 0.58 of the screen (was 0.40 x 0.64); the lists scroll
        return L.Doc(L.WindowMin("Root", "PZA SERVICE RECORD", 0.32, 0.14, 0.36, 0.58,
            L.Tabs("Tabs", 0.02, 0.02, 0.96, 0.96,
                ("TabFighter", [.. fighter]),
                ("TabSquad", [.. squad]),
                ("TabRecent", [L.List("lbRecent", 0.02, Top, 0.96, 0.96 - Top)]),
                ("TabReports", [L.List("lbReport", 0.02, Top, 0.96, 0.86 - Top), L.Button("ABack", 0.02, 0.88, 0.30, 0.09),
                    L.Button("AAdvice", 0.35, 0.88, 0.30, 0.09), L.Button("ANext", 0.68, 0.88, 0.30, 0.09)]),
                ("TabHelp", [L.List("lbHelp", 0.02, Top, 0.96, 0.96 - Top)]))));
    }

    /// <summary>An axis label beside its vertex (the plugin's placement: top centred above, bottom centred below, the sides outside).</summary>
    static (double X, double Y, double W, double H) AxisLabelBox(int a, double rh)
    {
        var (vx, vy) = SrRadarImage.Vertex(a, 1.0);
        double fx = RadarX + vx / SrRadarImage.Size * RadarW, fy = RadarY + vy / SrRadarImage.Size * rh;
        const double w = 0.17, h = 0.05;
        // a label box takes the mouse (its hover text): the left ones stop short of the list (its scroll bar), the right ones at the page's edge
        double listEnd = 0.02 + FighterListW + LabelGap, left = fx - 0.005 - Math.Min(w, fx - 0.005 - listEnd), right = Math.Min(w, 0.995 - (fx + 0.005));
        return a switch
        {
            0 => (fx - w / 2, fy - h - 0.005, w, h),
            3 => (fx - w / 2, fy + 0.005, w, h),
            < 3 => (fx + 0.005, fy - h / 2, right, h),
            _ => (left, fy - h / 2, fx - 0.005 - left, h),
        };
    }

    #endregion

    #region Setup

    public static void Setup()
    {
        var s = SrSystem.Settings;
        Window = new PzaWindow(Id, LayoutXml)
        {
            Minimizable = true,
            Bind = Bind,
            Event = OnEvent,
            LoadPlace = () => new Vector2((float)s.WindowX, (float)s.WindowY),
            SavePlace = p => { Set(nameof(SrSettings.WindowX), Math.Round((double)p.X, 3)); Set(nameof(SrSettings.WindowY), Math.Round((double)p.Y, 3)); },
            Minimized = () => SetTiny(true, "the - button"),
            OpenChanged = open => { if (open) { SetTiny(false, "opened"); s_dirty = true; } },
        };
        PzaUi.Add(Window);
        Tiny = new PzaTinyButton("servicerecord")
        {
            Caption = () => T(S_BTN_MINI),
            Visible = () => s_tiny && !Window.IsOpen,
            Click = () => Window.Open(),
            OpenFull = () => Window.Open(),
            LoadPlace = () => new Vector2((float)s.TinyX, (float)s.TinyY),
            SavePlace = p => { Set(nameof(SrSettings.TinyX), Math.Round((double)p.X, 3)); Set(nameof(SrSettings.TinyY), Math.Round((double)p.Y, 3)); },
            LoadOpacity = () => s.TinyOpacity,
            SaveOpacity = a => Set(nameof(SrSettings.TinyOpacity), a),
        };
        PzaUi.Add(Tiny);
        s_tiny = s.Collapsed;
        if (s.WindowStartOpen && !s.Collapsed) Window.Open();
    }

    static void Set(string key, object value) => PzaSettings.Set(Group, SrSystem.Settings, key, value);

    static void SetTiny(bool tiny, string why)
    {
        if (tiny == s_tiny) return;
        s_tiny = tiny;
        Set(nameof(SrSettings.Collapsed), tiny);
        SrLog.Line($"window {(tiny ? "shrunk to the tiny button" : "full")} ({why}, saved)");
    }

    /// <summary>Shift+R (the plugin's rule): closed -> open; tiny -> full; open -> closed.</summary>
    public static void Hotkey()
    {
        if (PzaUi.Typing) { SrLog.Line("hotkey ignored: a text box has the keyboard"); return; }
        if (!Window.IsOpen) Window.Open();
        else Window.Close();
    }

    #endregion

    #region Bind (every frame) and the texts (every 2 s and after a click)

    /// <summary>The tab control's name in the layout.</summary>
    const string TabsName = "Tabs";

    static bool s_tabRestored;

    /// <summary>0.1.4 (the plugin's Tab=): the window opens on the page it was left on. Meitou's screen host keeps a window's selected tab
    /// (GetTab / SetTab); it is put back on the first frame the window is bound, then every change is saved.</summary>
    static void KeepTab()
    {
        if (PzaUi.Host is not { } host) return;
        try
        {
            var s = SrSystem.Settings;
            if (!s_tabRestored)
            {
                s_tabRestored = true;
                if (s.Tab is > 0 and <= 4) host.SetTab(Id, TabsName, s.Tab);
                return;
            }
            int tab = host.GetTab(Id, TabsName);
            if (tab != s.Tab && tab is >= 0 and <= 4) Set(nameof(SrSettings.Tab), tab);
        }
        catch (Exception e)
        {
            s_tabRestored = true;
            SrLog.Line("window: the page is not remembered (" + e.GetType().Name + ")");
            s_tabBroken = true;
        }
    }

    static bool s_tabBroken;

    static void Bind(UiBindings b)
    {
        if (!s_tabBroken) KeepTab();
        double now = s_clock.Elapsed.TotalSeconds;
        if (s_byChars.Count > 0 && s_byChars.Any(l => PzaFit.RowFits(Window, l) is not null)) s_dirty = true;   // a list now drawn: real-font wrap
        if (s_dirty || now - s_lastBuild > 2.0)
        {
            s_dirty = false;
            s_lastBuild = now;
            BuildTexts();
        }
        PzaWindow.Enable(b, "FBack", "FSta", "FNext", "ABack", "AAdvice", "ANext", "Sort0", "Sort1", "Sort2", "Sort3", "Sort4", "Sort5");
        b.Text["Root"] = T(S_WIN_NAME);
        string[] tabs = [T(S_TAB_FIGHTER), T(S_TAB_SQUAD), T(S_TAB_RECENT), T(S_TAB_REPORTS), "?"];
        for (int i = 0; i < tabs.Length; i++) PzaWindow.TabText(b, "Tabs", i, tabs[i]);
        PzaWindow.Lines(b, "lbFighter", s_fighterLines);
        PzaWindow.Lines(b, "lbSquad", s_squadLines);
        PzaWindow.Lines(b, "lbRecent", s_recentLines);
        PzaWindow.Lines(b, "lbReport", s_reportLines);
        PzaWindow.Lines(b, "lbHelp", s_helpLines);
        b.Text["FBack"] = T(S_BTN_BACK);
        b.Text["FSta"] = T(S_BTN_STA);
        b.Text["FNext"] = T(S_BTN_NEXT);
        b.Text["ABack"] = T(S_BTN_BACK);
        b.Text["ANext"] = T(S_BTN_NEXT);
        b.Text["AAdvice"] = T(SrReports.Aar.Advice ? S_BTN_ADVICE_ON : S_BTN_ADVICE_OFF);
        for (int k = 0; k < (int)SrPages.SortKey.Count; k++)
        {
            string n = SrPages.SortName(k);
            b.Text["Sort" + k] = k == SrSystem.Settings.SortBy ? "[ " + n + " ]" : n;
            PzaWindow.Tooltip(b, "Sort" + k, T(S_TIP_SORT));
        }
        PzaWindow.Tooltip(b, "FBack", T(S_TIP_PREV_FIGHTER));
        PzaWindow.Tooltip(b, "FNext", T(S_TIP_NEXT_FIGHTER));
        PzaWindow.Tooltip(b, "FSta", T(S_TIP_STA));
        PzaWindow.Tooltip(b, "ABack", T(S_TIP_PREV_BATTLE));
        PzaWindow.Tooltip(b, "ANext", T(S_TIP_NEXT_BATTLE));
        PzaWindow.Tooltip(b, "AAdvice", T(S_TIP_ADVICE));
        BindRadar(b);
    }

    static void BindRadar(UiBindings b)
    {
        if (!s_radarOn || s_radar.Length == 0)
        {
            for (int a = 0; a < SrRadar.Axes; a++) b.Text["rdL" + a] = "";
            b.Text["rdCap"] = "";
            return;
        }
        PzaWindow.Props(b, "rdImg", ("ImageTexture", s_radar), ("tooltip_type", "0"), ("tooltip_text", s_radarTipAll));
        for (int a = 0; a < SrRadar.Axes; a++)
        {
            string label = SrRadar.AxisLabel(a);
            b.Text["rdL" + a] = s_axisKnown[a] ? label : "#8C8C8C" + label + " -";
            PzaWindow.Tooltip(b, "rdL" + a, s_axisTip[a] ?? "");
        }
        b.Text["rdCap"] = s_radarCap;
    }

    static void BuildTexts()
    {
        var squad = s_squad;
        s_fighterLines = Wrap(FighterText(squad), "lbFighter", FighterListW);
        s_squadLines = Wrap(SrPages.SquadText([.. squad.Select(p => new SrPages.Row { Name = p.Name, S = p.S, Animal = p.Animal, Post = p.Post })], SrSystem.Settings.SortBy, 300), "lbSquad", 0.96);
        lock (SrSystem.Lock)
        {
            s_recentLines = Wrap(SrPages.RecentText([.. SrReports.Ring]), "lbRecent", 0.96);
            s_reportLines = Wrap(ReportText(), "lbReport", 0.96);
        }
        s_helpLines = Wrap(HelpText(), "lbHelp", 0.96);   // every build: wrapped again once the list's real width is known
    }

    /// <summary>The plugin's help: fifteen parts, colour words kept.</summary>
    static string HelpText()
    {
        // the user, 2026-11-17: help still spoke of the RE_Kenshi plugin. Chain of command and language are Meitou's own here (meitou.help.*).
        // 0.1.4: Fighter, Squad, kept reports and times too (they named the plugin's STA screen, its Sort button and PZAServiceRecord.ini)
        string[] parts = [S_HELP_INTRO, S_MEITOU_HELP_FIGHTER, S_HELP_CHART, S_HELP_RANKS, S_MEITOU_HELP_SQUAD, S_MEITOU_HELP_COMMAND, S_HELP_RECENT,
            S_HELP_REPORTS, S_MEITOU_HELP_ARCHIVE, S_MEITOU_HELP_TIMES, S_HELP_NAMES, S_HELP_COLOURS, S_HELP_CREDIT, S_HELP_KEYS, S_MEITOU_HELP_LANGUAGE];
        return SrStr.Colour(string.Join("\n\n", parts.Select(T)));
    }

    static string ReportText()
    {
        var rs = SrReports.Aar.Reports.ToList();
        if (rs.Count == 0) return T(S_RP_TITLE) + "\n\n" + T(S_RP_EMPTY) + (SrReports.Aar.InBattle ? "\n\n" + T(S_RP_FIGHTING) : "");
        int n = rs.Count;
        int i = s_reportIdx < 0 || s_reportIdx >= n ? n - 1 : s_reportIdx;
        return TS(S_RP_COUNTER, SrStr.Num(i + 1), SrStr.Num(n)) + "\n\n" + rs[i].Text(SrReports.Aar.Advice);
    }

    /// <summary>The Fighter page: the chosen one of ours (the STA character first), the record, and the radar against the rest of the squad.</summary>
    static string FighterText(List<Person> squad)
    {
        var sel = PzaUi.SelectedCharacters();
        ulong sta = sel.Count > 0 ? SrBridge.Key(sel[0]) : 0;
        if (s_fighter == 0 && sta != 0) s_fighter = sta;
        if (s_fighter == 0 && squad.Count > 0) s_fighter = squad[0].Key;
        var me = squad.FirstOrDefault(p => p.Key == s_fighter);
        Stats? st;
        lock (SrSystem.Lock) st = SrSystem.Core.Find(s_fighter)?.Clone();
        s_radarOn = false;
        if (s_fighter == 0) return T(S_F_NO_FIGHTER);
        var shown = s_shown is { } x && x.Key == s_fighter ? x : null;   // 0.1.4: a character with no record yet has its name too
        string name = me?.Name ?? shown?.Name ?? "";
        int post = me?.Post ?? shown?.Post ?? 0;
        if (st is null) return SrPages.FighterText(name, new Stats(), false, post);
        BuildRadar(squad, st, name);
        return SrPages.FighterText(name, st, true, post);
    }

    static void BuildRadar(List<Person> squad, Stats st, string myName)
    {
        var others = squad.Where(p => p.Key != s_fighter).ToList();
        var pool = others.Select(p => SrRadar.Of(p.S)).ToList();
        var me = SrRadar.Of(st);
        var val = new double[SrRadar.Axes];
        SrRadar.Score(pool, me, val);
        var best = new double[SrRadar.Axes];
        var who = new int[SrRadar.Axes];
        SrRadar.Best(pool, me, best, who);
        double[] career = [st.Kills, st.KosDealt, st.LimbsRemoved, st.Dealt, 0, (double)st.OneHitKO + st.OneHitKill];
        string fb = me.PerFight ? SrStr.TP(S_RD_FIGHTS_ONE, me.Fights) : T(S_RD_NO_FIGHTS);
        string all = TS(S_RD_TIP_HEAD, myName, fb) + "\n";
        string[] careerKeys = [S_CAREER_0, SrStr.After(S_CAREER_0, 1), SrStr.After(S_CAREER_0, 2), SrStr.After(S_CAREER_0, 3), SrStr.After(S_CAREER_0, 4), SrStr.After(S_CAREER_0, 5)];
        for (int a = 0; a < SrRadar.Axes; a++)
        {
            string bestWho = who[a] < 0 ? T(S_RD_THIS_FIGHTER) : others[who[a]].Name;
            string t = me.Known[a] ? TS(S_RD_TIP_VALUE, SrRadar.AxisLabel(a), SrRadar.WithUnit(a, me.PerFight, me.V[a])) + "\n" : TS(S_RD_TIP_NODATA, SrRadar.AxisLabel(a)) + "\n";
            t += TS(S_RD_TIP_BEST, SrRadar.Num(a, best[a]), bestWho) + "\n";
            t += a == 4 ? TS(S_RD_TIP_ENDURANCE, SrRadar.Num(3, st.Taken), SrStr.TP(S_TIMES_ONE, st.TimesKO)) : TS(careerKeys[a], SrRadar.Num(3, career[a]));
            s_axisTip[a] = t;
            s_axisKnown[a] = me.Known[a];
            all += !me.Known[a] ? TS(S_RD_TIP_LINE, SrRadar.AxisLabel(a), T(S_RD_NO_DATA)) + "\n"
                : TS(who[a] < 0 ? S_RD_TIP_LINE_BEST : S_RD_TIP_LINE, SrRadar.AxisLabel(a), SrRadar.Num(a, me.V[a])) + "\n";
        }
        s_radarTipAll = all + T(S_RD_TIP_FOOT);
        s_radarCap = me.PerFight ? TS(S_RD_CAP_FIGHT, SrStr.Num(me.Fights)) : T(S_RD_CAP_CAREER);
        s_radar = SrRadarImage.For(val, me.Known);
        s_radarOn = s_radar.Length > 0;
    }

    #endregion

    #region Text into list lines

    /// <summary>Characters that fit a list of <paramref name="widthFraction"/> of the window, for Kenshi's standard font at this screen's scale.</summary>
    static int CharsPerLine(double widthFraction)
    {
        double px = PzaUi.Width * 0.40 * 0.96 * widthFraction;
        double scale = PzaHud.Scale(PzaUi.Height);
        return Math.Max(24, (int)(px / (7.6 * scale)));
    }

    /// <summary>The page as list lines, wrapped to <paramref name="list"/>'s rows in the real font (PZA's Meitou UI); before the list was first
    /// drawn, by characters for <paramref name="fallbackFraction"/> of the window.</summary>
    public static List<string> Wrap(string text, string list, double fallbackFraction)
    {
        var fits = Window is null ? null : PzaFit.RowFits(Window, list);
        if (fits is null) s_byChars.Add(list); else s_byChars.Remove(list);
        int chars = CharsPerLine(fallbackFraction);
        return Wrap(text, fits is null ? s => Aar.Words.StripColour(s).Length <= chars : s => fits(Aar.Words.StripColour(s)));
    }

    /// <summary>Lists last wrapped by characters (before they were drawn): wrapped again in the real font as soon as their rows are laid out.</summary>
    static readonly HashSet<string> s_byChars = new(StringComparer.Ordinal);

    /// <summary>Wrapped at <paramref name="chars"/> characters (the check's fixed case).</summary>
    public static List<string> Wrap(string text, int chars) => Wrap(text, s => Aar.Words.StripColour(s).Length <= chars);

    /// <summary>The page as list lines: wrapped at spaces where <paramref name="fits"/> says a line is too long, each line in its colour (MyGUI
    /// tags, '#' doubled), empty lines kept as a space. A word longer than a line is cut.</summary>
    static List<string> Wrap(string text, Func<string, bool> fits)
    {
        var o = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            char colour = 'n';
            string line = raw;
            if (line.Length >= 2 && line[0] == Aar.Words.ColourMark) { colour = line[1]; line = line[2..]; }
            var pieces = new List<string>();
            int indent = 0;   // a continuation line starts with two spaces; a break must come after them, or the word is cut
            while (!fits(line) && line.Length > indent + 1)
            {
                int cut = -1;
                for (int i = line.IndexOf(' ', indent + 1); i > indent && fits(line[..i]); i = line.IndexOf(' ', i + 1)) cut = i;
                if (cut <= indent)
                {
                    cut = indent + 1;
                    while (cut < line.Length - 1 && fits(line[..(cut + 1)])) cut++;
                }
                pieces.Add(line[..cut]);
                line = "  " + line[cut..].TrimStart();
                indent = 2;
            }
            pieces.Add(line);
            foreach (var p in pieces) o.Add(Aar.Words.ToMyGui(Aar.Words.Mark(colour) + (p.Length == 0 ? " " : p), s_normalHex));
        }
        return o;
    }

    #endregion

    #region Events

    static void OnEvent(global::Meitou.Game.Ui.MyGuiEvent e)
    {
        var s = SrSystem.Settings;
        switch (e.Action)
        {
            case "FBack": Step(-1); break;
            case "FNext": Step(1); break;
            case "FSta":
                if (PzaUi.SelectedCharacters() is { Count: > 0 } sel) s_fighter = SrBridge.Key(sel[0]);
                break;
            case "ABack": StepReport(-1); break;
            case "ANext": StepReport(1); break;
            case "AAdvice":
                lock (SrSystem.Lock) SrReports.Aar.Advice = !SrReports.Aar.Advice;
                Set(nameof(SrSettings.ReportAdvice), SrReports.Aar.Advice);
                SrLog.Line($"report advice {(SrReports.Aar.Advice ? "on" : "off")} (saved)");
                break;
            default:
                if (e.Action.StartsWith("Sort", StringComparison.Ordinal) && int.TryParse(e.Action[4..], out int k) && k is >= 0 and < (int)SrPages.SortKey.Count)
                    Set(nameof(SrSettings.SortBy), k);
                else return;
                break;
        }
        s_dirty = true;
    }

    static void Step(int d)
    {
        var ids = s_squad.Select(p => p.Key).ToList();
        if (ids.Count == 0) return;
        int at = ids.IndexOf(s_fighter);
        int n = ids.Count;
        at = at < 0 ? (d > 0 ? 0 : n - 1) : ((at + d) % n + n) % n;
        s_fighter = ids[at];
    }

    static void StepReport(int d)
    {
        int n;
        lock (SrSystem.Lock) n = SrReports.Aar.Reports.Count;
        if (n == 0) return;
        int i = s_reportIdx < 0 || s_reportIdx >= n ? n - 1 : s_reportIdx;
        s_reportIdx = ((i + d) % n + n) % n;
    }

    #endregion
}
