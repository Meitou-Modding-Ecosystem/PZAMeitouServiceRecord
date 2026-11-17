// PZA's Service Record for Meitou. Author: PZA. Licence: GPLv3 or later.
// The mod's log (ModContext.Log): every line starts "PZA's Service Record:", as the plugin's start "[PZAServiceRecord]".
namespace PZA.ServiceRecord.Meitou;

public static class SrLog
{
    public static Action<string> Sink = _ => { };

    public static void Line(string text) => Sink("PZA's Service Record: " + text);
}
