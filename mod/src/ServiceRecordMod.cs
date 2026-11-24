// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The code mod's entry (meitou.json "entry"). Port of the RE_Kenshi plugin PZA Service Record 0.12.0 to MeitouClient 0.20.1, feature for
// feature (the port plan). This build: the stats (rules core, hits, stealth knockouts, saving with the character in the plugin's own keys,
// Kill Counter import, the STA lines, the logs), the After-Action Reports and their archive, ranks, the radar chart and the Shift+R window.
// The Military Manager follows in 0.2.0 (until then the posts a save has are shown and kept).
using System.Reflection;
using Meitou.Localisation;
using Meitou.Modding;
using Meitou.Simulation;

namespace PZA.ServiceRecord.Meitou;

public sealed class ServiceRecordMod : IMod
{
    public void Initialize(ModContext context)
    {
        SrLog.Sink = context.Log;
        SrWindow.Group = context.AddSettings(SrSystem.Settings);
        // 0.1.4: Meitou keeps only the Settings page's own changes; the window's places, opacity, tiny form, page, sort and Advice the mod changes
        // are kept in its own file (PZA's Meitou UI PzaSettings.Keep, pza.servicerecord.settings.json in the mod's folder), as SlowMo and the
        // Director's Kit do
        var kept = PZA.MeitouUi.PzaSettings.Keep(SrWindow.Group, context.Folder);
        var s = SrSystem.Settings;
        string version = typeof(ServiceRecordMod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        SrLog.Line($"v{version} - PZA Service Record (combat stats) for Meitou, by PZA");
        foreach (var line in kept) SrLog.Line(line);
        // the language: the RE_Kenshi plugin's own lang\{code}.txt (same keys), beside this mod in the plugin's folder
        foreach (var line in SrSta.Text.Init(context.Folder, Loc.Language?.Code ?? "en", s.Language, "lang/{code}.txt", "lang/meitou.{code}.txt")) SrLog.Line(line);
        if (!s.Enabled) { SrLog.Line("Enabled is off - inactive"); return; }

        Configure(s);
        PZA.ServiceRecord.Meitou.Aar.GameClock.KeepLatest = true;   // 0.1.4: events read the tick's exact game time (GameClock.KeepLatest)
        SrWindow.Group.Changed += Changed;   // 0.1.4: the rules and the reports follow the Settings page at once
        SrReports.LoadPlurals(context.Folder);
        SrSystem.Settings = s;
        foreach (var line in SrBridge.Install(context.Harmony)) SrLog.Line(line);
        SrLog.Line(SrSta.Install(context.Harmony));
        foreach (var line in SrSaveName.Install(context.Harmony)) SrLog.Line(line);
        context.AddSystem("pza.servicerecord", _ => new SrSystem(), SystemAnchor.End);
        PZA.MeitouUi.PzaUi.Log = l => SrLog.Line(l);
        foreach (var line in SrKeys.Install(context.Harmony)) SrLog.Line(line);
        SrWindow.Setup();
        PZA.MeitouUi.PzaUi.Install(context);   // the Service Record window, the tiny button, the player's selection (STA lines)
        SrSystem.Active = true;
    }

    /// <summary>Settings whose values are copied into the rules or the reports (the others are read where they are used, at once).</summary>
    static readonly HashSet<string> s_copied = new(StringComparer.Ordinal)
    {
        nameof(SrSettings.FreshSeconds), nameof(SrSettings.FreshMinPercent), nameof(SrSettings.SettleSeconds), nameof(SrSettings.KillCreditSeconds),
        nameof(SrSettings.ImportKillCounter), nameof(SrSettings.RecentAll), nameof(SrSettings.ReportAdvice), nameof(SrSettings.GameDayOffset),
        nameof(SrSettings.ReportArchive), nameof(SrSettings.ReportArchiveMaxMB),
    };

    /// <summary>A setting changed (the Settings page, or the window itself): the rules and reports take it now; Enabled and Language say a
    /// restart applies them. The window's own places and states change silently.</summary>
    public static void Changed(global::Meitou.Modding.ModSetting setting)
    {
        try
        {
            if (setting.Key is nameof(SrSettings.Enabled) or nameof(SrSettings.Language))
            {
                SrLog.Line($"setting {setting.Key}={setting.Text}: applies after a restart");
                return;
            }
            if (!s_copied.Contains(setting.Key)) return;
            bool advice = setting.Key == nameof(SrSettings.ReportAdvice);
            lock (SrSystem.Lock)
            {
                if (advice) SrReports.Aar.Advice = SrSystem.Settings.ReportAdvice;   // the window's Advice button: nothing else to apply
                else Configure(SrSystem.Settings);
            }
            if (!advice) SrLog.Line($"setting {setting.Key}={setting.Text}: applied now");
        }
        catch (Exception e)
        {
            SrLog.Line($"setting {setting.Key}: could not be applied ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>The settings into the rules (the plugin's LoadSettings), logged as the plugin's "settings:" line.</summary>
    public static void Configure(SrSettings s)
    {
        var c = SrSystem.Core.Cfg;
        c.FreshWindow = s.FreshSeconds;
        c.FreshMinPct = s.FreshMinPercent / 100.0f;
        c.SettleWindow = s.SettleSeconds;
        c.KillCreditWindow = s.KillCreditSeconds;
        SrSave.ImportKc = s.ImportKillCounter;
        SrReports.Configure(s);
        SrSystem.Core.Event = SrSystem.OnEvent;
        SrLog.Line(FormattableString.Invariant(
            $"settings: Enabled={(s.Enabled ? 1 : 0)} ShowLines={s.ShowLines} EventLog={(s.EventLog ? 1 : 0)} HitLogFirst={s.HitLogFirst} FreshSeconds={c.FreshWindow:0.0} FreshMinPercent={c.FreshMinPct * 100:0} SettleSeconds={c.SettleWindow:0.0} KillCreditSeconds={c.KillCreditWindow:0} ImportKillCounter={(s.ImportKillCounter ? 1 : 0)}"));
    }
}
