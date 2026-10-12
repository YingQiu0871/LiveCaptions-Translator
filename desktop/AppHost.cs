using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // Starts and stops the background work of the core (translation, summaries, speech). The WPF app does this in
    // App.xaml.cs; any desktop window can call it once at startup.
    public static class AppHost
    {
        private static readonly object startLock = new();
        private static bool started = false;

        public static bool IsStarted => started;

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
            }
        }
    }
}
