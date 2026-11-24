// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The Settings > Mods group (MeitouClient docs/modding.md "Settings"): the RE_Kenshi plugin's PZAServiceRecord.ini, the same names, defaults and
// meanings. The page shows them in English for now (the strings for the page come with the window's translation pass).
using Meitou.Modding;

namespace PZA.ServiceRecord.Meitou;

public sealed class SrSettings
{
    [Setting("Enabled", Hint = "Off: the mod does nothing (a restart applies it).")]
    public bool Enabled { get; set; } = true;

    [Setting("STA window lines", Hint = "Lines in each character's STA window: 0 none, 1 compact (3 lines), 2 all 12 lines.", Min = 0, Max = 2, Step = 1)]
    public int ShowLines { get; set; } = 1;

    [Setting("Fresh seconds", Hint = "A fresh victim (for one-hit KOs and kills): no hit from anyone for this long.", Min = 1, Max = 60, Step = 1,
        SubGroup = "Rules")]
    public int FreshSeconds { get; set; } = 10;

    [Setting("Fresh minimum %", Hint = "...and every body part at this % of its health or more.", Min = 50, Max = 100, Step = 1, SubGroup = "Rules")]
    public int FreshMinPercent { get; set; } = 95;

    [Setting("Settle seconds", Hint = "A downing is judged after this long: dead by then = kill, alive = knockout.", Min = 1, Max = 10, Step = 1,
        SubGroup = "Rules")]
    public int SettleSeconds { get; set; } = 2;

    [Setting("Kill credit seconds", Hint = "A death this long after the last hit is 'died of wounds', not a kill (0 = no limit).", Min = 0,
        Max = 3600, Step = 30, SubGroup = "Rules")]
    public int KillCreditSeconds { get; set; } = 600;

    [Setting("Import Kill Counter", Hint = "Once per character, add Kill Counter's saved kills, KOs and limbs removed from the save being loaded.",
        SubGroup = "Rules")]
    public bool ImportKillCounter { get; set; } = true;

    [Setting("Counter log seconds", Hint = "A counters line in the log every this many seconds (0 = off).", Min = 0, Max = 600, Step = 10,
        SubGroup = "Log")]
    public int CounterLogSeconds { get; set; } = 60;

    [Setting("Event log", Hint = "One log line per counted event.", SubGroup = "Log")]
    public bool EventLog { get; set; } = true;

    [Setting("Hit log first", Hint = "Full detail in the log for the first this many hits of a session.", Min = 0, Max = 500, Step = 10,
        SubGroup = "Log")]
    public int HitLogFirst { get; set; } = 30;

    [Setting("Hit log", Hint = "One 'aar hit' line per hit (the After-Action Reports' corpus).", SubGroup = "Log")]
    public bool HitLog { get; set; } = true;

    [Setting("Recent: all fights", Hint = "The Recent page lists every loaded fight, not only those of your people.", SubGroup = "Reports")]
    public bool RecentAll { get; set; }

    [Setting("Report advice", Hint = "The reports' SUSTAIN / IMPROVE section.", SubGroup = "Reports")]
    public bool ReportAdvice { get; set; }

    // 0.1.4: 0 (the plugin's is 1). Meitou's clock already counts its start day: with 1 every report said Day 2 under a HUD saying Day 1.
    [Setting("Game day offset", Hint = "Added to the Day in reports; 0 = the Day on Meitou's own clock.", Min = -1, Max = 3, Step = 1,
        SubGroup = "Reports")]
    public int GameDayOffset { get; set; }

    [Setting("Report archive", Hint = "Keep every After-Action Report in a text file, one per faction.", SubGroup = "Reports")]
    public bool ReportArchive { get; set; } = true;

    [Setting("Report archive max MB", Hint = "The cap for the whole archive folder (0 = no cap).", Min = 0, Max = 500, Step = 10,
        SubGroup = "Reports")]
    public int ReportArchiveMaxMB { get; set; } = 50;

    [Setting("Language", Hint = "auto = the game's own; en = always English.", Options = ["auto", "en", "ru", "zh", "ja"])]
    public string Language { get; set; } = "auto";

    [Setting("Window open at start", Hint = "The Service Record window (Shift+R) opens when a game starts.", SubGroup = "Window")]
    public bool WindowStartOpen { get; set; }

    [Setting("Window X", Hint = "Where the window was left (a fraction of the screen; drag the window to move it).", Min = -1, Max = 1, Step = 0.001, SubGroup = "Window")]
    public double WindowX { get; set; }

    [Setting("Window Y", Hint = "Where the window was left (a fraction of the screen).", Min = -1, Max = 1, Step = 0.001, SubGroup = "Window")]
    public double WindowY { get; set; }

    [Setting("Tiny button X", Hint = "The tiny button's place (drag it to move it).", Min = 0, Max = 1, Step = 0.001, SubGroup = "Window")]
    public double TinyX { get; set; } = 0.012;

    [Setting("Tiny button Y", Hint = "The tiny button's place.", Min = 0, Max = 1, Step = 0.001, SubGroup = "Window")]
    public double TinyY { get; set; } = 0.09;

    [Setting("Tiny button opacity", Hint = "0.35 (faint) to 1; the mouse wheel over the tiny button changes it.", Min = 0.35, Max = 1, Step = 0.05, SubGroup = "Window")]
    public double TinyOpacity { get; set; } = 1;

    [Setting("Shrunk to the tiny button", Hint = "The window was left as its tiny button.", SubGroup = "Window")]
    public bool Collapsed { get; set; }

    [Setting("Squad page sort", Hint = "0 kills, 1 knockouts, 2 damage dealt, 3 biggest hit, 4 damage taken, 5 friendly fire.", Min = 0, Max = 5, Step = 1, SubGroup = "Window")]
    public int SortBy { get; set; }

    [Setting("Window page", Hint = "The page the window was left on: 0 Fighter, 1 Squad, 2 Recent, 3 Reports, 4 Help.", Min = 0, Max = 4, Step = 1, SubGroup = "Window")]
    public int Tab { get; set; }

    // 0.1.4: "Military Manager (beta)" and "Portrait insignia" are gone until the Military Manager is ported (0.2.0): neither did anything here.
}
