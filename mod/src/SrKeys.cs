// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The hotkeys, as PZA's SlowMo for Meitou does its Shift+L: a postfix on InputBindings.Resolve(TickInput) sees the control tick's keys; a fresh
// press of the letter with Shift held (no Ctrl / Alt; never while typing) is ours. Plain R (stop / move up) and M (the map) stay Meitou's:
// prefixes on GameHotkeys.Key and PlayerInterface.WindowHotkey skip the game's handler only for Shift + our letter.
//   Shift+R   the Service Record window
//   Shift+M   the Military Manager (beta, only when its setting is on)
using HarmonyLib;
using Meitou.Game.Host.Input;

namespace PZA.ServiceRecord.Meitou;

public static class SrKeys
{
    static volatile bool s_shift;
    public static long Fired, Blocked;

    public static List<string> Install(Harmony h)
    {
        var lines = new List<string>();
        var game = typeof(global::Meitou.Game.Ui.MyGuiScreenHost).Assembly;
        Try(h, lines, AccessTools.Method(typeof(InputBindings), "Resolve", [typeof(TickInput)]), nameof(ResolvePostfix), false, "InputBindings.Resolve (Shift+R)");
        var hotkeys = game.GetType("Meitou.Game.Ui.GameHotkeys");
        Try(h, lines, hotkeys is null ? null : AccessTools.Method(hotkeys, "Key", [typeof(Key), typeof(bool)]), nameof(KeyPrefix), true, "GameHotkeys.Key (plain R stays Meitou's)");
        var pi = game.GetType("Meitou.Game.PlayerInterface");
        Try(h, lines, pi is null ? null : AccessTools.Method(pi, "WindowHotkey", [typeof(Key)]), nameof(WindowHotkeyPrefix), true, "PlayerInterface.WindowHotkey (plain M stays the map)");
        return lines;
    }

    static void Try(Harmony h, List<string> lines, System.Reflection.MethodInfo? target, string ours, bool prefix, string what)
    {
        if (target is null) { lines.Add("MISSING " + what + ": that key part is off"); return; }
        try
        {
            var hm = new HarmonyMethod(AccessTools.Method(typeof(SrKeys), ours));
            if (prefix) h.Patch(target, prefix: hm);
            else h.Patch(target, postfix: hm);
            lines.Add("patched " + what);
        }
        catch (Exception e)
        {
            lines.Add($"MISSING {what}: the patch failed ({e.GetType().Name})");
        }
    }

    static bool Ours(Key key, bool shift) => shift && (key == Key.R || key == Key.M && SrSystem.Settings.MilitaryManagerBeta);

    static void ResolvePostfix(TickInput input)
    {
        try
        {
            bool shift = input.IsDown(Key.LeftShift) || input.IsDown(Key.RightShift);
            bool ctrlAlt = input.IsDown(Key.LeftControl) || input.IsDown(Key.RightControl) || input.IsDown(Key.LeftAlt) || input.IsDown(Key.RightAlt);
            s_shift = shift && !ctrlAlt;
            if (!SrSystem.Active || !s_shift || PZA.MeitouUi.PzaUi.Typing) return;
            if (input.WasPressed(Key.R)) { Fired++; SrWindow.Hotkey(); }
        }
        catch (Exception e)
        {
            SrLog.Line("keys: " + e.Message);
        }
    }

    static bool KeyPrefix(Key key, bool shift)
    {
        if (!Ours(key, shift)) return true;
        Blocked++;
        return false;
    }

    static bool WindowHotkeyPrefix(Key key)
    {
        if (!Ours(key, s_shift)) return true;
        Blocked++;
        return false;
    }
}
