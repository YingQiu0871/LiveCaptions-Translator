using System.IO;

namespace LiveCaptionsTranslator.utils
{
    // Absolute locations of the settings and history files. Never rely on the current directory:
    // file dialogs (e.g. uploading slides) change it, and the settings would then be saved elsewhere.
    public static class AppPaths
    {
        private static readonly Lazy<string> dataDirectory = new(FindDataDirectory);

        public static string DataDirectory => dataDirectory.Value;
        public static string SettingFile => Path.Combine(DataDirectory, "setting.json");
        public static string HistoryDb => Path.Combine(DataDirectory, "translation_history.db");

        // Next to the program if it can write there, otherwise a per-user folder.
        private static string FindDataDirectory()
        {
            string programDir = AppContext.BaseDirectory;
            try
            {
                string probe = Path.Combine(programDir, ".write-test");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return programDir;
            }
            catch (Exception)
            {
                string userDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveCaptionsTranslator");
                Directory.CreateDirectory(userDir);
                return userDir;
            }
        }
    }
}
