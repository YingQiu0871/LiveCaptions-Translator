using System.Diagnostics;
using System.Windows.Automation;

using LiveCaptionsTranslator.apis;

namespace LiveCaptionsTranslator.utils
{
    public static class LiveCaptionsHandler
    {
        public static readonly string PROCESS_NAME = "LiveCaptions";

        private static AutomationElement? captionsTextBlock = null;

        public static AutomationElement LaunchLiveCaptions()
        {
            // Init
            captionsTextBlock = null;
            IsHidden = false;
            KillAllProcessesByPName(PROCESS_NAME);
            var process = Process.Start(PROCESS_NAME);

            // Search for window
            AutomationElement? window = null;
            for (int attemptCount = 0;
                 window == null || window.Current.ClassName.CompareTo("LiveCaptionsDesktopWindow") != 0;
                 attemptCount++)
            {
                window = FindWindowByPId(process.Id);
                if (attemptCount > 10000)
                    throw new Exception("Failed to launch LiveCaptions!");
            }

            return window;
        }

        public static void KillLiveCaptions(AutomationElement window)
        {
            // Search for process
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);
            WindowsAPI.GetWindowThreadProcessId(hWnd, out int processId);
            var process = Process.GetProcessById(processId);

            // Kill process
            process.Kill();
            process.WaitForExit();
        }

        public static bool IsHidden { get; private set; } = false;

        private const int HIDDEN_STYLES = WindowsAPI.WS_EX_LAYERED | WindowsAPI.WS_EX_TRANSPARENT |
                                          WindowsAPI.WS_EX_TOOLWINDOW | WindowsAPI.WS_EX_NOACTIVATE;

        // A thin strip right above the taskbar, centered, in physical pixels.
        private static (int Left, int Top, int Width, int Height) DockRect(nint hWnd)
        {
            if (!WindowsAPI.SystemParametersInfo(WindowsAPI.SPI_GETWORKAREA, 0, out RECT area, 0))
                return (800, 600, 600, 200);
            uint dpi = 96;
            try
            {
                dpi = Math.Max(WindowsAPI.GetDpiForWindow(hWnd), 96);
            }
            catch (Exception)
            {
            }
            int areaWidth = area.Right - area.Left;
            int width = Math.Min(areaWidth, Math.Max((int)(areaWidth * 0.6), (int)(700 * dpi / 96.0)));
            int height = (int)(120 * dpi / 96.0);
            int left = area.Left + (areaWidth - width) / 2;
            int top = area.Bottom - height;
            return (left, top, width, height);
        }

        private static void Dock(nint hWnd)
        {
            var (left, top, width, height) = DockRect(hWnd);
            WindowsAPI.MoveWindow(hWnd, left, top, width, height, true);
        }

        // LiveCaptions stops updating its text when its window is minimized or off screen, so captions would
        // only arrive while it is visible. Instead it stays on screen right above the taskbar, on top, but almost
        // fully transparent and click-through: Windows still treats it as visible, so it keeps writing captions.
        public static void HideLiveCaptions(AutomationElement window)
        {
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);
            int exStyle = WindowsAPI.GetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE);

            WindowsAPI.ShowWindow(hWnd, WindowsAPI.SW_RESTORE);
            Dock(hWnd);
            WindowsAPI.SetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE, exStyle | HIDDEN_STYLES);
            WindowsAPI.SetLayeredWindowAttributes(hWnd, 0, 1, WindowsAPI.LWA_ALPHA);
            // On top, so that no other window covers it completely.
            WindowsAPI.SetWindowPos(hWnd, WindowsAPI.HWND_TOPMOST, 0, 0, 0, 0,
                WindowsAPI.SWP_NOMOVE | WindowsAPI.SWP_NOSIZE | WindowsAPI.SWP_NOACTIVATE | WindowsAPI.SWP_FRAMECHANGED);
            IsHidden = true;
        }

        // Shown: the same thin strip above the taskbar, now visible and usable.
        public static void RestoreLiveCaptions(AutomationElement window)
        {
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);
            int exStyle = WindowsAPI.GetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE);

            if ((exStyle & WindowsAPI.WS_EX_LAYERED) != 0)
                WindowsAPI.SetLayeredWindowAttributes(hWnd, 0, 255, WindowsAPI.LWA_ALPHA);
            WindowsAPI.SetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE, exStyle & ~HIDDEN_STYLES);
            WindowsAPI.SetWindowPos(hWnd, WindowsAPI.HWND_NOTOPMOST, 0, 0, 0, 0,
                WindowsAPI.SWP_NOMOVE | WindowsAPI.SWP_NOSIZE | WindowsAPI.SWP_NOACTIVATE | WindowsAPI.SWP_FRAMECHANGED);
            WindowsAPI.ShowWindow(hWnd, WindowsAPI.SW_RESTORE);
            Dock(hWnd);
            WindowsAPI.SetForegroundWindow(hWnd);
            IsHidden = false;
        }

        public static void FixLiveCaptions(AutomationElement window)
        {
            // Placed on purpose while hidden, see `HideLiveCaptions`.
            if (IsHidden)
                return;
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);

            RECT rect;
            if (!WindowsAPI.GetWindowRect(hWnd, out rect))
                throw new Exception("Unable to get the window rectangle of LiveCaptions!");
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int x = rect.Left;
            int y = rect.Top;

            bool isSuccess = true;
            if (x < 0 || y < 0 || width < 100 || height < 100)
                isSuccess = WindowsAPI.MoveWindow(hWnd, 800, 600, 600, 200, true);
            if (!isSuccess)
                throw new Exception("Failed to fix LiveCaptions!");
        }

        public static string GetCaptions(AutomationElement window)
        {
            if (captionsTextBlock == null)
                captionsTextBlock = FindElementByAId(window, "CaptionsTextBlock");
            try
            {
                return captionsTextBlock?.Current.Name ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
                captionsTextBlock = null;
                throw;
            }
        }

        // LiveCaptions only creates its text area once it is set up, so allow it a few seconds.
        public static bool HasCaptionsTextBlock(AutomationElement window)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                captionsTextBlock ??= FindElementByAId(window, "CaptionsTextBlock");
                if (captionsTextBlock != null)
                    return true;
                Thread.Sleep(500);
            }
            return false;
        }

        private static AutomationElement FindWindowByPId(int processId)
        {
            var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
            return AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
        }

        public static AutomationElement? FindElementByAId(
            AutomationElement window, string automationId, CancellationToken token = default)
        {
            try
            {
                PropertyCondition condition = new PropertyCondition(
                    AutomationElement.AutomationIdProperty, automationId);
                return window.FindFirst(TreeScope.Descendants, condition);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        public static void PrintAllElementsAId(AutomationElement window)
        {
            var treeWalker = TreeWalker.RawViewWalker;
            var stack = new Stack<AutomationElement>();
            stack.Push(window);

            while (stack.Count > 0)
            {
                var element = stack.Pop();
                if (!string.IsNullOrEmpty(element.Current.AutomationId))
                    Console.WriteLine(element.Current.AutomationId);

                var child = treeWalker.GetFirstChild(element);
                while (child != null)
                {
                    stack.Push(child);
                    child = treeWalker.GetNextSibling(child);
                }
            }
        }

        public static bool ClickSettingsButton(AutomationElement window)
        {
            var settingsButton = FindElementByAId(window, "SettingsButton");
            if (settingsButton != null)
            {
                var invokePattern = settingsButton.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                if (invokePattern != null)
                {
                    invokePattern.Invoke();
                    return true;
                }
            }
            return false;
        }

        private static void KillAllProcessesByPName(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
                return;
            foreach (Process process in processes)
            {
                process.Kill();
                process.WaitForExit();
            }
        }
    }
}
