// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The loaded save's name for the report archive's header (the plugin reads Kenshi's SaveManager::getCurrentGame()): Meitou keeps no public
// "current save", so the folder of the last load (GameSaves.BeginQuickLoad / BeginImport, its ReadingFolder) or save (QuickSaveNow) is kept;
// the save's name is that folder's name. Display only.
using HarmonyLib;
using Meitou.Simulation.Saving;

namespace PZA.ServiceRecord.Meitou;

public static class SrSaveName
{
    public static List<string> Install(Harmony h)
    {
        var lines = new List<string>();
        var saves = typeof(global::Meitou.Game.Ui.MyGuiScreenHost).Assembly.GetType("Meitou.Game.Host.GameSaves");
        foreach (var (method, ours) in new[] { ("BeginQuickLoad", nameof(LoadPostfix)), ("BeginImport", nameof(LoadPostfix)), ("QuickSaveNow", nameof(SavePostfix)) })
        {
            var target = saves is null ? null : AccessTools.Method(saves, method);
            if (target is null) { lines.Add($"MISSING GameSaves.{method}: the archive header names no save"); continue; }
            try
            {
                h.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(SrSaveName), ours)));
                lines.Add($"patched GameSaves.{method} (postfix: the save's name)");
            }
            catch (Exception e)
            {
                lines.Add($"MISSING GameSaves.{method}: the patch failed ({e.GetType().Name}); the archive header names no save");
            }
        }
        return lines;
    }

    static void LoadPostfix(string? ___ReadingFolder) => Note(___ReadingFolder);

    static void SavePostfix(string? destination) => Note(destination ?? QuickSave.Folder());

    static void Note(string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        string name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        if (name.Length > 0 && name != SrReports.SaveName)
        {
            SrReports.SaveName = name;
            SrLog.Line($"save: '{name}' (the report archive's header)");
        }
    }
}
