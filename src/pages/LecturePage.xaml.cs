using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Wpf.Ui.Appearance;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class LecturePage : Page
    {
        public const int MIN_HEIGHT = 520;
        private const string AUTO_VOICE = "自动（按目标语言选择）";
        private const string AUTO_OCR = "自动";
        private const string DEFAULT_OUTPUT = "Windows 默认输出设备";

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
            ScrollHelper.UseOwnScrollViewer(this, PageScroll);

            Loaded += (s, e) =>
            {
                (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(minHeight: MIN_HEIGHT, maxHeight: MIN_HEIGHT);
                Summarizer.CurrentPageChanged += OnCurrentPageChanged;
                LoadApiSetting();
                LoadDevices();
                LoadOcrLanguages();
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

        private void LoadDevices()
        {
            bool wasInitializing = initializing;
            initializing = true;

            SourceBox.SelectedIndex = (int)Translator.Setting.Lecture.InputSource;

            var microphones = AudioDevices.List(DataFlow.Capture);
            MicBox.ItemsSource = microphones;
            MicBox.SelectedItem = microphones.FirstOrDefault(device => device.IsDefault);
            MicBox.IsEnabled = Translator.Setting.Lecture.InputSource == InputSource.Microphone;

            var outputs = new List<object> { DEFAULT_OUTPUT };
            var renderDevices = AudioDevices.List(DataFlow.Render);
            outputs.AddRange(renderDevices);
            SpeechDeviceBox.ItemsSource = outputs;
            string speechDeviceId = Translator.Setting.Lecture.SpeechDeviceId;
            SpeechDeviceBox.SelectedItem = renderDevices.FirstOrDefault(device => device.Id == speechDeviceId)
                                           ?? (object)DEFAULT_OUTPUT;
            ShowSpeechDeviceHint();

            initializing = wasInitializing;
        }

        private void RefreshDevices_click(object sender, RoutedEventArgs e)
        {
            LoadDevices();
        }

        private async void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initializing || SourceBox.SelectedIndex < 0)
                return;
            var source = (InputSource)SourceBox.SelectedIndex;
            Translator.Setting.Lecture.InputSource = source;
            MicBox.IsEnabled = source == InputSource.Microphone;
            ShowSpeechDeviceHint();

            var window = Translator.Window;
            if (window == null)
            {
                SourceStatus.Text = "实时字幕还没启动，请稍后再切换一次。";
                return;
            }

            bool useMicrophone = source == InputSource.Microphone;
            SourceStatus.Text = "正在设置实时字幕……";
            SourceBox.IsEnabled = false;
            bool ok;
            try
            {
                ok = await Task.Run(() => LiveCaptionsMicrophone.Set(window, useMicrophone));
            }
            catch (Exception)
            {
                ok = false;
            }
            finally
            {
                SourceBox.IsEnabled = true;
            }

            if (ok)
            {
                SourceStatus.Text = useMicrophone
                    ? "✓ 已打开实时字幕的\"包含麦克风音频\"。"
                    : "✓ 已关闭实时字幕的\"包含麦克风音频\"，只听电脑播放的声音。";
            }
            else
            {
                SourceStatus.Text = useMicrophone
                    ? "没能自动设置。已打开实时字幕的设置菜单，请在\"首选项\"里勾选\"包含麦克风音频\"，然后点\"隐藏\"。"
                    : "没能自动设置。已打开实时字幕的设置菜单，请在\"首选项\"里取消\"包含麦克风音频\"，然后点\"隐藏\"。";
                await Task.Run(() => LiveCaptionsMicrophone.ShowSettings(window));
            }
        }

        private void MicBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initializing || MicBox.SelectedItem is not AudioDevice device || device.IsDefault)
                return;
            try
            {
                AudioDevices.SetDefault(device.Id);
                Translator.RestartLiveCaptions();
                SourceStatus.Text = $"✓ 已改用：{device.Name}";
            }
            catch (Exception ex)
            {
                SourceStatus.Text = $"✗ 切换麦克风失败：{ex.Message}";
            }
            LoadDevices();
        }

        private void SpeechDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initializing)
                return;
            Translator.Setting.Lecture.SpeechDeviceId =
                SpeechDeviceBox.SelectedItem is AudioDevice device ? device.Id : string.Empty;
            ShowSpeechDeviceHint();
        }

        private void ShowSpeechDeviceHint()
        {
            string speechDeviceId = Translator.Setting.Lecture.SpeechDeviceId;
            bool onDefault = string.IsNullOrEmpty(speechDeviceId) ||
                             speechDeviceId == AudioDevices.DefaultId(DataFlow.Render);
            SpeechDeviceHint.Text = onDefault
                ? "播报和电脑声音走同一个设备，实时字幕会听到播报，所以播报时会暂停接收字幕。" +
                  "线下课建议在这里直接选蓝牙耳机，并把 Windows 默认输出设为电脑扬声器。"
                : "播报只从这个设备播出，不会被实时字幕听到。";
        }

        private void LoadOcrLanguages()
        {
            var languages = new List<object> { AUTO_OCR };
            var available = SlideOcr.AvailableLanguages();
            languages.AddRange(available);
            OcrLanguageBox.ItemsSource = languages;
            string current = Translator.Setting.Lecture.OcrLanguage;
            OcrLanguageBox.SelectedItem = available.FirstOrDefault(l => l.Tag == current) ?? (object)AUTO_OCR;
        }

        private void OcrLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initializing)
                return;
            Translator.Setting.Lecture.OcrLanguage =
                OcrLanguageBox.SelectedItem is SlideOcr.OcrLanguage language ? language.Tag : string.Empty;
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
            string ocr = SlideDeck.OcrPageCount > 0 ? $"（其中 {SlideDeck.OcrPageCount} 页用 OCR 识别）" : string.Empty;
            SlidesInfo.Text = $"{SlideDeck.FileName}\n共 {SlideDeck.Pages.Count} 页{ocr}，当前：{page}";
            ClearSlidesButton.Visibility = Visibility.Visible;
        }

        private async void LoadSlides_click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = SlideDeck.FileFilter };
            if (dialog.ShowDialog() != true)
                return;

            SlidesInfo.Text = "读取中……";
            LoadSlidesButton.IsEnabled = false;
            try
            {
                await SlideDeck.Load(dialog.FileName, new Progress<string>(text => SlidesInfo.Text = text));
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 课件读取失败。", ex.Message, SnackbarType.Error,
                    timeout: 3, closeButton: true);
            }
            finally
            {
                LoadSlidesButton.IsEnabled = true;
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
