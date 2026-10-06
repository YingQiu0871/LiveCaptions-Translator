using Wpf.Ui.Controls;

namespace LiveCaptionsTranslator
{
    class SnackbarHost
    {
        public static Snackbar? mainSnackbar;
        public static MainWindow? mainWindow => App.Current.MainWindow as MainWindow;

        // Safe to call from any thread: background loops (summaries, refinement) report errors here, and touching
        // the snackbar off the UI thread used to throw inside their error handling and stop those loops for good.
        public static void Show(string title = "", string message = "", SnackbarType type = SnackbarType.Info,
                                int width = 500, int timeout = 1, bool closeButton = false)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() => Show(title, message, type, width, timeout, closeButton));
                return;
            }
            try
            {
                ShowOnUiThread(title, message, type, width, timeout, closeButton);
            }
            catch (Exception)
            {
                // A notification must never break the caller.
            }
        }

        private static void ShowOnUiThread(string title, string message, SnackbarType type,
                                           int width, int timeout, bool closeButton)
        {
            ControlAppearance appearance;
            SymbolIcon icon;
            Snackbar? snackbar;

            switch (type)
            {
                case SnackbarType.Warning:
                    appearance = ControlAppearance.Caution;
                    icon = new SymbolIcon(SymbolRegular.Alert24);
                    break;
                case SnackbarType.Error:
                    appearance = ControlAppearance.Danger;
                    icon = new SymbolIcon(SymbolRegular.DismissCircle24);
                    break;
                case SnackbarType.Success:
                    appearance = ControlAppearance.Success;
                    icon = new SymbolIcon(SymbolRegular.CheckmarkCircle24);
                    break;
                default:
                    appearance = ControlAppearance.Secondary;
                    icon = new SymbolIcon(SymbolRegular.Info24);
                    break;
            }

            mainSnackbar ??= new Snackbar(mainWindow?.snackbarHost);
            snackbar = mainSnackbar;

            snackbar.SetCurrentValue(Snackbar.TitleProperty, title);
            snackbar.SetCurrentValue(System.Windows.Controls.ContentControl.ContentProperty, message);
            snackbar.SetCurrentValue(Snackbar.AppearanceProperty, appearance);
            snackbar.SetCurrentValue(Snackbar.IconProperty, icon);
            snackbar.SetCurrentValue(Snackbar.TimeoutProperty, TimeSpan.FromSeconds(timeout));

            snackbar.MinWidth = width;
            snackbar.IsCloseButtonEnabled = closeButton;

            snackbar.Show(true);
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
