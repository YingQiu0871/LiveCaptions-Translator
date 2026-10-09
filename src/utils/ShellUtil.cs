using System.Diagnostics;

namespace LiveCaptionsTranslator.utils
{
    public static class ShellUtil
    {
        // Opens a URL (or any shell target) with its default handler.
        public static void Open(string target)
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }
}
