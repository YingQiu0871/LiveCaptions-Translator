namespace LiveCaptionsTranslator.utils
{
    // Runs an action on the UI thread, whichever UI toolkit the platform uses (WPF on Windows, Avalonia elsewhere).
    public static class UiThread
    {
        public static void Post(Action action)
        {
#if WINDOWS
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(action);
#else
            Avalonia.Threading.Dispatcher.UIThread.Post(action);
#endif
        }
    }
}
