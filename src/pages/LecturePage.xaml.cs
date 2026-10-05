using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class LecturePage : Page
    {
        public const int MIN_HEIGHT = 520;
        private const string AUTO_VOICE = "自动（按目标语言选择）";

        private record Provider(string Name, string ApiUrl, string[] Models);

        // Index matches the items of `ProviderBox`. The last one is "any OpenAI-compatible API".
        private static readonly Provider[] PROVIDERS =
        {
            new("DeepSeek", "https://api.deepseek.com/chat/completions",
                new[] { "deepseek-chat" }),
            new("Qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
                new[] { "qwen-plus", "qwen-turbo", "qwen-max" }),
            new("Custom", string.Empty, Array.Empty<string>()),
        };

        private bool initializing = true;

        private static OpenAIConfig? ApiConfig => Translator.Setting[Summarizer.SUMMARY_API] as OpenAIConfig;

        public LecturePage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();
            DataContext = Translator.Setting.Lecture;
            ApiCard.DataContext = ApiConfig;

            Loaded += (s, e) =>
            {
                (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(minHeight: MIN_HEIGHT, maxHeight: MIN_HEIGHT);
                Summarizer.CurrentPageChanged += OnCurrentPageChanged;
                LoadApiSetting();
                LoadVoices();
                ShowSlidesInfo();
                initializing = false;
            };
            Unloaded += (s, e) =>
            {
                Summarizer.CurrentPageChanged -= OnCurrentPageChanged;
                initializing = true;
            };
        }

        private void LoadApiSetting()
        {
            var config = ApiConfig;
            if (config == null)
                return;

            int index = PROVIDERS.Length - 1;
            for (int i = 0; i < PROVIDERS.Length - 1; i++)
            {
                if (string.IsNullOrEmpty(config.ApiUrl) ||
                    config.ApiUrl.Contains(new Uri(PROVIDERS[i].ApiUrl).Host, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            ProviderBox.SelectedIndex = index;
            ModelBox.ItemsSource = PROVIDERS[index].Models;
            // Fill in DeepSeek defaults on first use.
            if (string.IsNullOrEmpty(config.ApiUrl))
                ApplyProvider(PROVIDERS[index], config);

            ApiKeyBox.Password = config.ApiKey;
            UseForTranslation.IsChecked = Translator.Setting.ApiName == Summarizer.SUMMARY_API;
        }

        private static void ApplyProvider(Provider provider, OpenAIConfig config)
        {
            if (!string.IsNullOrEmpty(provider.ApiUrl))
                config.ApiUrl = provider.ApiUrl;
            if (provider.Models.Length > 0 && !provider.Models.Contains(config.ModelName))
                config.ModelName = provider.Models[0];
        }

        private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initializing || ProviderBox.SelectedIndex < 0 || ApiConfig == null)
                return;
            var provider = PROVIDERS[ProviderBox.SelectedIndex];
            ModelBox.ItemsSource = provider.Models;
            ApplyProvider(provider, ApiConfig);
            TestApiResult.Text = string.Empty;
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (initializing || ApiConfig == null)
                return;
            ApiConfig.ApiKey = ApiKeyBox.Password.Trim();
        }

        private void UseForTranslation_Changed(object sender, RoutedEventArgs e)
        {
            if (initializing)
                return;
            Translator.Setting.ApiName = UseForTranslation.IsChecked == true ? Summarizer.SUMMARY_API : "Google";
        }

        private async void TestApi_click(object sender, RoutedEventArgs e)
        {
            // Commit fields that only update on lost focus.
            ApiUrlBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            ModelBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();

            TestApiButton.IsEnabled = false;
            TestApiResult.Text = "测试中……";
            try
            {
                await Summarizer.TestConnection();
                TestApiResult.Text = "✓ 连接成功";
            }
            catch (Exception ex)
            {
                TestApiResult.Text = $"✗ {ex.Message}";
            }
            finally
            {
                TestApiButton.IsEnabled = true;
            }
        }

        private void OnCurrentPageChanged(int? page)
        {
            Dispatcher.InvokeAsync(ShowSlidesInfo);
        }

        private void ShowSlidesInfo()
        {
            if (!SlideDeck.IsLoaded)
            {
                SlidesInfo.Text = "未上传课件：按话题变化分节。";
                ClearSlidesButton.Visibility = Visibility.Collapsed;
                return;
            }
            string page = Summarizer.CurrentPage == null ? "识别中" : $"第 {Summarizer.CurrentPage} 页";
            SlidesInfo.Text = $"{SlideDeck.FileName}\n共 {SlideDeck.Pages.Count} 页，当前：{page}";
            ClearSlidesButton.Visibility = Visibility.Visible;
        }

        private async void LoadSlides_click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = SlideDeck.FileFilter };
            if (dialog.ShowDialog() != true)
                return;

            SlidesInfo.Text = "读取中……";
            try
            {
                await SlideDeck.Load(dialog.FileName);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 课件读取失败。", ex.Message, SnackbarType.Error,
                    timeout: 3, closeButton: true);
            }
            ShowSlidesInfo();
        }

        private void ClearSlides_click(object sender, RoutedEventArgs e)
        {
            SlideDeck.Clear();
            ShowSlidesInfo();
        }

        private void LoadVoices()
        {
            var voices = new List<string> { AUTO_VOICE };
            voices.AddRange(Speaker.GetVoices());
            VoiceBox.ItemsSource = voices;

            string current = Translator.Setting.Lecture.VoiceName;
            VoiceBox.SelectedItem = voices.Contains(current) ? current : AUTO_VOICE;
        }

        private void VoiceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initializing || VoiceBox.SelectedItem is not string voice)
                return;
            Translator.Setting.Lecture.VoiceName = voice == AUTO_VOICE ? string.Empty : voice;
        }

        private void TestSpeech_click(object sender, RoutedEventArgs e)
        {
            Speaker.Speak("这是一段试听。老师每讲完一节，你会在耳机里听到这一节的小结。");
        }

        private void SoundSettings_click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "ms-settings:sound", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 无法打开声音设置。", ex.Message, SnackbarType.Error, timeout: 2);
            }
        }

        private void ResetPrompt_click(object sender, RoutedEventArgs e)
        {
            Translator.Setting.Lecture.SummaryPrompt = LectureState.DEFAULT_SUMMARY_PROMPT;
        }
    }
}
