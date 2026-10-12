using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // Starts and stops the background work of the core (translation, summaries, speech): the counterpart of the
    // startup code in the WPF App.xaml.cs. Any desktop window can call Start once at startup.
    public static class AppHost
    {
        private static readonly object startLock = new();
        private static bool started = false;

        public static void Start()
        {
            lock (startLock)
            {
                if (started)
                    return;
                started = true;
            }

            // Settings and history are kept in the user's data folder, so do not depend on how the program was started.
            Directory.SetCurrentDirectory(AppPaths.DataDirectory);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Shutdown();
            Translator.Setting?.Save();

            Task.Run(() => Translator.TranslateLoop());
            Task.Run(() => Translator.DisplayLoop());
            Task.Run(() => Summarizer.SummaryLoop());
            Task.Run(() => Refiner.RefineLoop());
            Speaker.Start();
        }

        // Stops capturing and speaking. Safe to call more than once.
        public static void Shutdown()
        {
            lock (startLock)
            {
                if (!started)
                    return;
                started = false;
            }
            try
            {
                ClassSession.Stop();
                CloudAsr.Stop();
                Speaker.StopAll();
            }
            catch (Exception)
            {
                // Shutting down: a failing stop has nothing left to affect.
            }
        }
    }
}
