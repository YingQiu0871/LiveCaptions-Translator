using System.IO;
namespace LiveCaptionsTranslator.utils
{
    // Absolute locations of the settings and history files. Never rely on the current directory:
    // file dialogs (e.g. uploading slides) change it, and the settings would then be saved elsewhere.
    //
    // They live in the user's own folder (%LOCALAPPDATA%\LiveCaptionsTranslator), not next to the program:
    // installing a new version replaces the program folder, and settings kept there could be lost.
    public static class AppPaths
    {
        private const string SETTING_FILE = "setting.json";
        private const string HISTORY_FILE = "translation_history.db";

        private static readonly Lazy<string> dataDirectory = new(FindDataDirectory);

        public static string DataDirectory => dataDirectory.Value;
        public static string SettingFile => Path.Combine(DataDirectory, SETTING_FILE);
        public static string HistoryDb => Path.Combine(DataDirectory, HISTORY_FILE);

        private static string FindDataDirectory()
        {
            string programDir = AppContext.BaseDirectory;
            string userDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveCaptionsTranslator");
            // For tests (e.g. the desktop self-test): keep everything in a folder of their own.
            string? overrideDir = Environment.GetEnvironmentVariable("LCT_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
                userDir = Path.GetFullPath(overrideDir);
            try
            {
                Directory.CreateDirectory(userDir);
            }
            catch (Exception)
            {
                return programDir;
            }

            // Files from installs that kept them next to the program: copy them over once.
            foreach (string name in new[] { SETTING_FILE, HISTORY_FILE })
            {
                string target = Path.Combine(userDir, name);
                string source = Path.Combine(programDir, name);
                try
                {
                    if (!File.Exists(target) && File.Exists(source) &&
                        !string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                        File.Copy(source, target);
                }
                catch (Exception)
                {
                    // Best effort: a file that cannot be copied is simply not migrated.
                }
            }
            return userDir;
        }
    }
}
