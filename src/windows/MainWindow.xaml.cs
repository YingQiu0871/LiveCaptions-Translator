using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using LiveCaptionsTranslator.utils;
using LiveCaptionsTranslator.Utils;
using Button = Wpf.Ui.Controls.Button;

namespace LiveCaptionsTranslator
{
    public partial class MainWindow : FluentWindow
    {
        public OverlayWindow? OverlayWindow { get; set; } = null;
        private const double DEFAULT_WIDTH = 900;
        private const double DEFAULT_HEIGHT = 560;

        public bool IsAutoHeight { get; set; } = true;

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int HOTKEY_END_SECTION = 0x5301;
        private const int HOTKEY_TOGGLE_SPEECH = 0x5302;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainWindow()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();

            SourceInitialized += (s, e) => RegisterHotKeys();
            Closed += (s, e) => UnregisterHotKeys();
            Speaker.MutedChanged += muted => Dispatcher.InvokeAsync(() => ShowSpeakerState(muted));
            ClassSession.StateChanged += () => Dispatcher.InvokeAsync(ShowSessionState);

            Loaded += (s, e) =>
            {
                SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);
                RootNavigation.Navigate(typeof(CaptionPage));
                IsAutoHeight = true;
                CheckForFirstUse();
                // This fork adds lecture features; do not prompt to install upstream releases.
            };

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            var windowState = WindowHandler.LoadState(this, Translator.Setting);
            if (windowState.Left <= 0 || windowState.Left >= screenWidth ||
                windowState.Top <= 0 || windowState.Top >= screenHeight)
            {
                WindowHandler.RestoreState(this, new Rect(
                    (screenWidth - DEFAULT_WIDTH) / 2, (screenHeight - DEFAULT_HEIGHT) / 2, DEFAULT_WIDTH, DEFAULT_HEIGHT));
            }
            else
                WindowHandler.RestoreState(this, windowState);
            // Older versions saved a thin caption strip.
            if (Height < MinHeight)
                Height = DEFAULT_HEIGHT;
            KeepOnScreen();

            ToggleTopmost(Translator.Setting.MainWindow.Topmost);
            ShowSpeakerState(Speaker.Muted);
            ShowLogCard(Translator.Setting.MainWindow.CaptionLogEnabled);
        }

        private void TopmostButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTopmost(!this.Topmost);
        }

        private void OverlayModeButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var symbolIcon = button?.Icon as SymbolIcon;

            if (OverlayWindow == null)
            {
                symbolIcon.Symbol = SymbolRegular.ClosedCaption24;
                symbolIcon.Filled = true;

                OverlayWindow = new OverlayWindow();
                OverlayWindow.SizeChanged +=
                    (s, e) => WindowHandler.SaveState(OverlayWindow, Translator.Setting);
                OverlayWindow.LocationChanged +=
                    (s, e) => WindowHandler.SaveState(OverlayWindow, Translator.Setting);

                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                var windowState = WindowHandler.LoadState(OverlayWindow, Translator.Setting);
                if (windowState.Left <= 0 || windowState.Left >= screenWidth ||
                    windowState.Top <= 0 || windowState.Top >= screenHeight)
                {
                    WindowHandler.RestoreState(OverlayWindow, new Rect(
                        (screenWidth - 650) / 2, screenHeight * 5 / 6 - 135, 650, 135));
                }
                else
                    WindowHandler.RestoreState(OverlayWindow, windowState);

                OverlayWindow.Show();
            }
            else
            {
                symbolIcon.Symbol = SymbolRegular.ClosedCaptionOff24;
                symbolIcon.Filled = false;

                switch (OverlayWindow.OnlyMode)
                {
                    case CaptionVisible.TranslationOnly:
                        OverlayWindow.OnlyMode = CaptionVisible.SubtitleOnly;
                        OverlayWindow.OnlyMode = CaptionVisible.Both;
                        break;
                    case CaptionVisible.SubtitleOnly:
                        OverlayWindow.OnlyMode = CaptionVisible.Both;
                        break;
                }

                OverlayWindow.Close();
                OverlayWindow = null;
            }
        }

        private void LogOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var symbolIcon = button?.Icon as SymbolIcon;

            if (Translator.LogOnlyFlag)
            {
                Translator.LogOnlyFlag = false;
                symbolIcon.Filled = false;
            }
            else
            {
                Translator.LogOnlyFlag = true;
                symbolIcon.Filled = true;
            }

            Translator.ClearContexts();
        }

        private void EndSectionButton_Click(object sender, RoutedEventArgs e)
        {
            EndSection();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ClassSession.IsRunning)
                ClassSession.Stop();
            else
            {
                RootNavigation.Navigate(typeof(CaptionPage));
                await ClassSession.Start();
            }
        }

        private void ShowSessionState()
        {
            StartButton.IsEnabled = !ClassSession.IsStarting;
            StartButton.Content = ClassSession.IsStarting ? "启动中…" : ClassSession.IsRunning ? "停止" : "开始";
            StartButton.Appearance = ClassSession.IsRunning ? ControlAppearance.Secondary : ControlAppearance.Primary;
            if (StartButton.Icon is SymbolIcon icon)
                icon.Symbol = ClassSession.IsRunning ? SymbolRegular.Stop16 : SymbolRegular.Play16;
        }

        private void SpeechSwitch_Click(object sender, RoutedEventArgs e)
        {
            Speaker.Muted = SpeechSwitch.IsChecked != true;
        }

        private static void EndSection()
        {
            Summarizer.RequestEndSection();
            SnackbarHost.Show("正在总结本节……", "", SnackbarType.Info, timeout: 1);
        }

        private void ShowSpeakerState(bool muted)
        {
            SpeechSwitch.IsChecked = !muted;
        }

        private void RegisterHotKeys()
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(HotKeyHook);
            // Ctrl+Alt+S: end the current section; Ctrl+Alt+M: mute / unmute speech.
            bool ok = RegisterHotKey(handle, HOTKEY_END_SECTION, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x53);
            ok &= RegisterHotKey(handle, HOTKEY_TOGGLE_SPEECH, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x4D);
            if (!ok)
                SnackbarHost.Show("[WARNING] 快捷键注册失败。",
                    "Ctrl+Alt+S / Ctrl+Alt+M 可能被其他程序占用。", SnackbarType.Warning,
                    timeout: 3, closeButton: true);
        }

        private void UnregisterHotKeys()
        {
            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_END_SECTION);
            UnregisterHotKey(handle, HOTKEY_TOGGLE_SPEECH);
        }

        private IntPtr HotKeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY)
                return IntPtr.Zero;

            switch (wParam.ToInt32())
            {
                case HOTKEY_END_SECTION:
                    EndSection();
                    handled = true;
                    break;
                case HOTKEY_TOGGLE_SPEECH:
                    Speaker.Muted = !Speaker.Muted;
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        private void CaptionLogButton_Click(object sender, RoutedEventArgs e)
        {
            Translator.Setting.MainWindow.CaptionLogEnabled = !Translator.Setting.MainWindow.CaptionLogEnabled;
            ShowLogCard(Translator.Setting.MainWindow.CaptionLogEnabled);
            CaptionPage.Instance?.AutoHeight();
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            var window = sender as Window;
            WindowHandler.SaveState(window, Translator.Setting);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            MainWindow_LocationChanged(sender, e);
            IsAutoHeight = false;
        }

        public void ToggleTopmost(bool enabled)
        {
            var button = TopmostButton as Button;
            var symbolIcon = button?.Icon as SymbolIcon;
            symbolIcon.Filled = enabled;
            this.Topmost = enabled;
            Translator.Setting.MainWindow.Topmost = enabled;
        }

        private void CheckForFirstUse()
        {
            if (!Translator.FirstUseFlag)
                return;

            RootNavigation.Navigate(typeof(SettingPage));
            LiveCaptionsHandler.RestoreLiveCaptions(Translator.Window);

            Dispatcher.InvokeAsync(() =>
            {
                var welcomeWindow = new WelcomeWindow
                {
                    Owner = this
                };
                welcomeWindow.Show();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private async Task CheckForUpdates()
        {
            if (Translator.FirstUseFlag)
                return;

            string latestVersion = string.Empty;
            try
            {
                latestVersion = await UpdateUtil.GetLatestVersion();
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 检查更新失败。", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);

                return;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            var ignoredVersion = Translator.Setting.IgnoredUpdateVersion;
            if (!string.IsNullOrEmpty(ignoredVersion) && ignoredVersion == latestVersion)
                return;
            if (!string.IsNullOrEmpty(latestVersion) && latestVersion != currentVersion)
            {
                var dialog = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "发现新版本",
                    Content = $"检测到新版本：{latestVersion}\n" +
                              $"当前版本：{currentVersion}\n" +
                              $"请到 GitHub 下载最新版本。",
                    PrimaryButtonText = "去更新",
                    CloseButtonText = "忽略此版本"
                };
                var result = await dialog.ShowDialogAsync();

                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    var url = UpdateUtil.GitHubReleasesUrl;
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        SnackbarHost.Show("[ERROR] 打开浏览器失败。", ex.Message, SnackbarType.Error,
                            timeout: 2, closeButton: true);
                    }
                }
                else
                    Translator.Setting.IgnoredUpdateVersion = latestVersion;
            }
        }

        public void ShowLogCard(bool enabled)
        {
            if (CaptionLogButton.Icon is SymbolIcon icon)
            {
                if (enabled)
                    icon.Symbol = SymbolRegular.History24;
                else
                    icon.Symbol = SymbolRegular.HistoryDismiss24;
                CaptionPage.Instance?.CollapseTranslatedCaption(enabled);
            }
        }

        // Only grows the window when a page needs more room. Shrinking it to a thin caption strip
        // (upstream behaviour) hides most of the navigation; the overlay window serves that purpose.
        public void AutoHeightAdjust(int minHeight = -1, int maxHeight = -1)
        {
            if (minHeight > 0 && Height < minHeight)
            {
                Height = minHeight;
                IsAutoHeight = true;
                KeepOnScreen();
            }
        }

        private void KeepOnScreen()
        {
            var workArea = SystemParameters.WorkArea;
            double height = Math.Max(Height, MinHeight);
            if (height > workArea.Height)
                Height = height = workArea.Height;
            if (Top + height > workArea.Bottom)
                Top = Math.Max(workArea.Top, workArea.Bottom - height);
        }
    }
}