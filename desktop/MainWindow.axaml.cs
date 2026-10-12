using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class MainWindow : Window
    {
        public OverlayWindow? OverlayWindow { get; set; } = null;
        private const double DEFAULT_WIDTH = 900;
        private const double DEFAULT_HEIGHT = 560;

        // The labels of the navigation, in order. The caption and about pages are kept; the others are rebuilt
        // every time (they read their data when shown).
        private static readonly (string Name, Func<Control> Create, bool Cache)[] PAGES =
        {
            ("字幕", () => new CaptionPage(), true),
            ("设置", () => new LecturePage(), false),
            ("时间线", () => new TimelinePage(), false),
            ("历史", () => new HistoryPage(), false),
            ("关于", () => new InfoPage(), true),
        };

        private readonly Dictionary<string, Control> cachedPages = new();
        private readonly DispatcherTimer toastTimer = new();

        public MainWindow()
        {
            InitializeComponent();

            NavList.ItemsSource = PAGES.Select(page => page.Name).ToList();
            toastTimer.Tick += (s, e) => HideToast();

            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Alt),
                Command = new ActionCommand(EndSection),
            });
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.M, KeyModifiers.Control | KeyModifiers.Alt),
                Command = new ActionCommand(() => Speaker.Muted = !Speaker.Muted),
            });

            Speaker.MutedChanged += muted => Dispatcher.UIThread.Post(() => ShowSpeakerState(muted));
            ClassSession.StateChanged += () => Dispatcher.UIThread.Post(ShowSessionState);
            SnackbarHost.Shown += ShowToast;

            Opened += (s, e) =>
            {
                Navigate("字幕");
                CheckForFirstUse();
            };
            Closed += (s, e) =>
            {
                SnackbarHost.Shown -= ShowToast;
                OverlayWindow?.Close();
            };

            WindowBounds.Restore(this, "MainWindow", new Size(DEFAULT_WIDTH, DEFAULT_HEIGHT));
            // A height below the minimum falls back to the default.
            if (Height < MinHeight)
                Height = DEFAULT_HEIGHT;
            WindowBounds.Track(this, "MainWindow");

            ToggleTopmost(Translator.Setting.MainWindow.Topmost);
            ShowSpeakerState(Speaker.Muted);
            ShowSessionState();
            string microphone = DesktopSettings.MicrophoneId;
            CloudAsr.MicrophoneSource = string.IsNullOrEmpty(microphone) ? null : microphone;
        }

        public void Navigate(string name)
        {
            int index = Array.FindIndex(PAGES, page => page.Name == name);
            if (index < 0)
                return;
            if (NavList.SelectedIndex == index)
                ShowPage(index);
            else
                NavList.SelectedIndex = index;
        }

        private void NavList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedIndex >= 0)
                ShowPage(NavList.SelectedIndex);
        }

        private void ShowPage(int index)
        {
            var (name, create, cache) = PAGES[index];
            Control page;
            if (!cache)
                page = create();
            else if (!cachedPages.TryGetValue(name, out page!))
                cachedPages[name] = page = create();
            PageHost.Content = page;
        }

        private void TopmostButton_Click(object? sender, RoutedEventArgs e)
        {
            ToggleTopmost(!Topmost);
        }

        private void OverlayModeButton_Click(object? sender, RoutedEventArgs e)
        {
            if (OverlayWindow == null)
            {
                OverlayWindow = new OverlayWindow();
                OverlayWindow.Closed += (s, args) =>
                {
                    OverlayWindow = null;
                    OverlayModeButton.IsChecked = false;
                };
                OverlayModeButton.IsChecked = true;
                OverlayWindow.Show();
            }
            else
            {
                switch (OverlayWindow.OnlyMode)
                {
                    case utils.CaptionVisible.TranslationOnly:
                        OverlayWindow.OnlyMode = utils.CaptionVisible.SubtitleOnly;
                        OverlayWindow.OnlyMode = utils.CaptionVisible.Both;
                        break;
                    case utils.CaptionVisible.SubtitleOnly:
                        OverlayWindow.OnlyMode = utils.CaptionVisible.Both;
                        break;
                }

                OverlayWindow.Close();
                OverlayWindow = null;
                OverlayModeButton.IsChecked = false;
            }
        }

        private void LogOnlyButton_Click(object? sender, RoutedEventArgs e)
        {
            Translator.LogOnlyFlag = !Translator.LogOnlyFlag;
            LogOnlyButton.IsChecked = Translator.LogOnlyFlag;
            Translator.ClearContexts();
        }

        private void EndSectionButton_Click(object? sender, RoutedEventArgs e)
        {
            EndSection();
        }

        private async void StartButton_Click(object? sender, RoutedEventArgs e)
        {
            if (ClassSession.IsRunning)
            {
                long lectureId = ClassSession.CurrentLectureId;
                ClassSession.Stop();
                await LectureSaving.SaveAfterStop(lectureId);
            }
            else
            {
                Navigate("字幕");
                await ClassSession.Start();
            }
        }

        private void ShowSessionState()
        {
            StartButton.IsEnabled = !ClassSession.IsStarting;
            StartButton.Content = ClassSession.IsStarting ? "启动中…" : ClassSession.IsRunning ? "停止" : "开始";
            StartButton.Classes.Set("accent", !ClassSession.IsRunning);
        }

        private void SpeechSwitch_Click(object? sender, RoutedEventArgs e)
        {
            Speaker.Muted = SpeechSwitch.IsChecked != true;
        }

        public static void EndSection()
        {
            Summarizer.RequestEndSection();
            SnackbarHost.Show("正在总结本节……", "", SnackbarType.Info, timeout: 1);
        }

        private void ShowSpeakerState(bool muted)
        {
            SpeechSwitch.IsChecked = !muted;
        }

        public void ToggleTopmost(bool enabled)
        {
            TopmostButton.IsChecked = enabled;
            Topmost = enabled;
            Translator.Setting.MainWindow.Topmost = enabled;
        }

        private void CheckForFirstUse()
        {
            if (!Translator.FirstUseFlag)
                return;

            Navigate("设置");
            Dispatcher.UIThread.Post(() => new WelcomeWindow().Show(this), DispatcherPriority.Background);
        }

        // Only grows the window when a page needs more room.
        public void AutoHeightAdjust(int minHeight = -1)
        {
            if (minHeight > 0 && Height < minHeight)
                Height = minHeight;
        }

        private void ShowToast(string title, string message, SnackbarType type, int timeout, bool closeButton)
        {
            var (background, border) = type switch
            {
                SnackbarType.Warning => ("#FF5C4700", "#FFFFB900"),
                SnackbarType.Error => ("#FF5A1D1D", "#FFE5484D"),
                SnackbarType.Success => ("#FF1B4D2B", "#FF30A46C"),
                _ => ("#FF2B3A4E", "#FF5B8DEF"),
            };
            Toast.Background = new SolidColorBrush(Avalonia.Media.Color.Parse(background));
            Toast.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.Parse(border));
            ToastTitle.Text = title;
            ToastTitle.IsVisible = !string.IsNullOrEmpty(title);
            ToastMessage.Text = message;
            ToastMessage.IsVisible = !string.IsNullOrEmpty(message);
            ToastClose.IsVisible = closeButton;
            foreach (var block in new[] { ToastTitle, ToastMessage })
                block.Foreground = Brushes.White;
            ToastClose.Foreground = Brushes.White;
            Toast.IsVisible = true;

            toastTimer.Stop();
            toastTimer.Interval = TimeSpan.FromSeconds(Math.Max(timeout, 2));
            toastTimer.Start();
        }

        private void HideToast()
        {
            toastTimer.Stop();
            Toast.IsVisible = false;
        }

        private void ToastClose_Click(object? sender, RoutedEventArgs e)
        {
            HideToast();
        }
    }
}
