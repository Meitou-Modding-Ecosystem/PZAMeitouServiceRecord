// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The code mod's entry (meitou.json "entry"). Port of the RE_Kenshi plugin PZA Service Record 0.12.0 to MeitouClient 0.20.1, feature for
// feature (the port plan). This build: the stats (rules core, hits, stealth knockouts, saving with
// the character in the plugin's own keys, Kill Counter import, the STA lines, the logs). The windows, reports, ranks and the Military
// Manager follow.
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
        var s = SrSystem.Settings;
        string version = typeof(ServiceRecordMod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        SrLog.Line($"v{version} - PZA Service Record (combat stats) for Meitou, by PZA");
        // the language: the RE_Kenshi plugin's own lang\{code}.txt (same keys), beside this mod in the plugin's folder
        foreach (var line in SrSta.Text.Init(context.Folder, Loc.Language?.Code ?? "en", s.Language, "lang/{code}.txt", "lang/meitou.{code}.txt")) SrLog.Line(line);
        if (!s.Enabled) { SrLog.Line("Enabled is off - inactive"); return; }

        Configure(s);
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
