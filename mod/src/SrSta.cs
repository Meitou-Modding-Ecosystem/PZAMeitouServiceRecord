// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The STA window lines (the plugin's UpdateStatsWindows / SetLine): a postfix on Meitou's StatsWindow.AddStatsContent adds our rows under the
// four attributes, in the attributes' own font and colour, each with its hover text and the "(PZA Service Record)" tag. ShowLines: 1 compact
// (three lines, a fourth for friendly fire once it happened), 2 all 12 (+2) lines, 0 none. The window shows the first selected character, as
// the game's own rows do. The panel has a fixed height: more rows than fit share its height.
using System.Globalization;
using System.Numerics;
using HarmonyLib;
using Meitou.Data.Ui;
using T = PZA.ServiceRecord.Meitou.SrText;

namespace PZA.ServiceRecord.Meitou;

public static class SrSta
{
    public static PZA.MeitouUi.PzaStrings Text = new(SrText.Table);
    static int s_faults;

    public static string Install(Harmony h)
    {
        var type = typeof(global::Meitou.Game.Ui.MyGuiScreenHost).Assembly.GetType("Meitou.Game.Ui.StatsWindow");
        var target = type is null ? null : AccessTools.Method(type, "AddStatsContent", [typeof(List<UiControl>)]);
        if (target is null) return "MISSING StatsWindow.AddStatsContent: no STA lines";
        try
        {
            h.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(SrSta), nameof(Postfix))));
        }
        catch (Exception e)
        {
            return $"MISSING StatsWindow.AddStatsContent: the patch failed ({e.GetType().Name}: {e.Message}); no STA lines";
        }
        return "patched StatsWindow.AddStatsContent (postfix: STA lines)";
    }

    static void Postfix(List<UiControl> controls)
    {
        if (!SrSystem.Active || SrSystem.Settings.ShowLines <= 0 || s_faults >= 3) return;
        try
        {
            Add(controls);
        }
        catch (Exception e)
        {
            // as the plugin: three faults switch the lines off for the session, never the game
            if (++s_faults >= 3) SrLog.Line("STA panel lines switched OFF for this session after 3 faults (" + e.Message + ")");
            else SrLog.Line($"STA panel lines: fault {s_faults} updating the stats window (ignored): {e.Message}");
        }
    }

    /// <summary>The rows for the record of the first selected character (an empty record when it has none: zeros, as the plugin shows).</summary>
    public static List<(string Key, string Label, string Value, string Tip)> Rows(Stats s, int mode)
    {
        static string Num(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        var r = new List<(string, string, string, string)>();
        void L(string key, string label, string value, string tip) => r.Add((key, Text.T(label), value, Text.T(tip)));
        if (mode == 1)
        {
            L("pza_sr_c_kkl", T.S_STA_C_KKL, ServiceCore.Triple(s.Kills, s.KosDealt, s.LimbsRemoved), T.S_STA_C_KKL_TIP);
            L("pza_sr_c_dmg", T.S_STA_C_DMG, ServiceCore.Short(s.Dealt) + "/" + ServiceCore.Short(s.Taken), T.S_STA_C_DMG_TIP);
            L("pza_sr_c_ko", T.S_STA_C_KO, ServiceCore.Short(s.TimesKO) + "/" + ServiceCore.Short(s.LimbsLost), T.S_STA_C_KO_TIP);
            if (s.FfHits != 0) L("pza_sr_c_ff", T.S_STA_C_FF, ServiceCore.Triple(s.FfHits, s.FfKOs, s.FfLimbs), T.S_STA_C_FF_TIP);
            return r;
        }
        L("pza_sr_kills", T.S_STA_KILLS, Num(s.Kills), T.S_STA_KILLS_TIP);
        L("pza_sr_kosdlt", T.S_STA_KOSDLT, Num(s.KosDealt), T.S_STA_KOSDLT_TIP);
        L("pza_sr_limbsrm", T.S_STA_LIMBSRM, Num(s.LimbsRemoved), T.S_STA_LIMBSRM_TIP);
        L("pza_sr_dealt", T.S_STA_DEALT, Num(s.Dealt), T.S_STA_DEALT_TIP);
        L("pza_sr_taken", T.S_STA_TAKEN, Num(s.Taken), T.S_STA_TAKEN_TIP);
        L("pza_sr_big", T.S_STA_BIG, Num(s.Biggest), T.S_STA_BIG_TIP);
        L("pza_sr_bigon", T.S_STA_BIGON, s.BiggestOn.Length == 0 ? "-" : s.BiggestOn, T.S_STA_BIGON_TIP);
        L("pza_sr_1hko", T.S_STA_1HKO, Num(s.OneHitKO), T.S_STA_1HKO_TIP);
        L("pza_sr_1hkill", T.S_STA_1HKILL, Num(s.OneHitKill), T.S_STA_1HKILL_TIP);
        L("pza_sr_kosuf", T.S_STA_KOSUF, Num(s.KosSuffered), T.S_STA_KOSUF_TIP);
        L("pza_sr_timesko", T.S_STA_TIMESKO, Num(s.TimesKO), T.S_STA_TIMESKO_TIP);
        L("pza_sr_limbs", T.S_STA_LIMBS, Num(s.LimbsLost), T.S_STA_LIMBS_TIP);
        L("pza_sr_ffhits", T.S_STA_FFHITS, Num(s.FfHits), T.S_STA_FFHITS_TIP);
        L("pza_sr_ffko", T.S_STA_FFKO, Num(s.FfKOs) + "/" + Num(s.FfLimbs), T.S_STA_FFKO_TIP);
        return r;
    }

    static void Add(List<UiControl> controls)
    {
        if (controls.FirstOrDefault(c => c.Name == "AttributesPanel") is not { } panel) return;
        var sel = PZA.MeitouUi.PzaUi.SelectedCharacters();
        Stats s;
        lock (SrSystem.Lock) s = (sel.Count > 0 ? SrSystem.Core.Find(SrBridge.Key(sel[0]))?.Clone() : null) ?? new Stats();
        var rows = Rows(s, SrSystem.Settings.ShowLines);
        if (rows.Count == 0) return;

        // the attributes' rows: 20 at the UI scale from the panel's top (StatsWindow.StatColumn), four of them
        float scale = MathF.Max(0.1f, controls.FirstOrDefault(c => c.Name == "AttributesPanel-label-0") is { } l0 ? l0.Bounds.Height / 20f : 1f);
        float top = panel.Bounds.Y + 4 * 20 * scale;
        float room = panel.Bounds.Y + panel.Bounds.Height - top;
        float row = MathF.Min(20 * scale, room / rows.Count);
        float valueWidth = MathF.Min(panel.Bounds.Width * 0.5f, 96 * scale);
        var clip = panel.Bounds.Intersect(panel.Clip ?? panel.Bounds);
        const string Font = "Kenshi_PaintedTextFont_Medium", Ink = "0.4 0.380392 0.356863";   // the attributes' own (StatsWindow.AddStat, column 0)
        string tag = Text.T(T.S_STA_TIP_TAG);
        for (int i = 0; i < rows.Count; i++)
        {
            var (key, label, value, tip) = rows[i];
            float y = top + i * row;
            var props = new Dictionary<string, string>
            {
                ["FontName"] = Font, ["TextColour"] = Ink, ["tooltip_type"] = "0", ["tooltip_text"] = tip + "\n\n" + tag,
            };
            var left = new Dictionary<string, string>(props) { ["TextAlign"] = "Left VCenter" };
            var right = new Dictionary<string, string>(props) { ["TextAlign"] = "Right VCenter" };
            controls.Add(new UiControl(key + "-label", "TextBox", new UiRect(panel.Bounds.X, y, panel.Bounds.Width - valueWidth - 4 * scale, row),
                label, "", "", true, left, Vector4.Zero, Pickable: true, Clip: clip));
            controls.Add(new UiControl(key + "-value", "TextBox", new UiRect(panel.Bounds.X + panel.Bounds.Width - valueWidth, y, valueWidth, row),
                value, "", "", true, right, Vector4.Zero, Pickable: true, Clip: clip));
        }
        SrSystem.UiLines++;
    }
}
