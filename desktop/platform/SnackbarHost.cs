namespace LiveCaptionsTranslator
{
    // Notifications of the core ("settings not saved", "speech failed", ...). The WPF app shows them in a snackbar;
    // here the window subscribes to `Shown` and draws them however it likes.
    public class SnackbarHost
    {
        // (title, message, type, timeout in seconds, closeButton). Raised on the UI thread.
        public static event Action<string, string, SnackbarType, int, bool>? Shown;

        // Safe to call from any thread.
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
