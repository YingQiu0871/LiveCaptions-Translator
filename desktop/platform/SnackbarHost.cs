namespace LiveCaptionsTranslator
{
    // Notifications of the core ("settings not saved", "speech failed", ...), with the same Show signature as the
    // WPF snackbar host. The window subscribes to `Shown` and draws them; without a subscriber they go to stderr.
    public class SnackbarHost
    {
        // (title, message, type, timeout in seconds, closeButton). Raised on the UI thread.
        public static event Action<string, string, SnackbarType, int, bool>? Shown;

        // Whether a window is listening, so that a toast is really drawn rather than written to stderr.
        public static bool CanShow => Shown != null;

        // Safe to call from any thread. `width` is unused here; it keeps the signature of the WPF host.
        public static void Show(string title = "", string message = "", SnackbarType type = SnackbarType.Info,
                                int width = 500, int timeout = 1, bool closeButton = false)
        {
            utils.UiThread.Post(() =>
            {
                try
                {
                    var handler = Shown;
                    if (handler != null)
                        handler(title, message, type, timeout, closeButton);
                    else
                        Console.Error.WriteLine($"{title} {message}".Trim());
                }
                catch (Exception)
                {
                    // A notification must never break the caller.
                }
            });
        }
    }

    public enum SnackbarType
    {
        Warning,
        Error,
        Success,
        Info
    }
}
