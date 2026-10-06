using System.IO;
using System.Windows;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class App : Application
    {
        App()
        {
            // Settings and history are stored next to the program, so do not depend on how it was started.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Translator.Setting?.Save();

            Task.Run(() => Translator.SyncLoop());
            Task.Run(() => Translator.TranslateLoop());
            Task.Run(() => Translator.DisplayLoop());
            Task.Run(() => Summarizer.SummaryLoop());
            Speaker.Start();
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            if (Translator.Window != null)
            {
                LiveCaptionsHandler.RestoreLiveCaptions(Translator.Window);
                LiveCaptionsHandler.KillLiveCaptions(Translator.Window);
            }
        }
    }
}
