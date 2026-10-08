using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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

        public record ApiChoice(string Key, string Name);

        // Display names for the translation APIs. DeepSeek / Qwen go through the OpenAI-compatible config of card ①.
        private static readonly Dictionary<string, string> API_NAMES = new()
        {
            [Summarizer.SUMMARY_API] = "DeepSeek / 通义千问（用 ① 的设置）",
            ["Google"] = "Google 翻译（免费）",
            ["Google2"] = "Google 翻译 2（免费）",
        };

        private static SettingWindow? settingWindow;

        private bool initializing = true;
        private bool fillingKeyBox = false;

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
                if (ApiConfig != null)
                    ApiConfig.PropertyChanged += OnApiConfigChanged;
                initializing = true;
                // One failing loader (e.g. an audio device that can't be read) must not leave the page read-only.
                foreach (Action load in new Action[]
                         {
                             LoadEngine, LoadApiSetting, LoadTranslateSetting, LoadDevices, LoadOcrLanguages, LoadVoices,
                             ShowSlidesInfo, ShowLiveCaptionsState, ShowSaveFolder
                         })
                {
                    try
                    {
                        load();
                    }
                    catch (Exception)
                    {
                    }
                }
                initializing = false;
            };
            Unloaded += (s, e) =>
            {
                Summarizer.CurrentPageChanged -= OnCurrentPageChanged;
                if (ApiConfig != null)
                    ApiConfig.PropertyChanged -= OnApiConfigChanged;
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

            FillKeyBox();
        }

        private void FillKeyBox()
        {
            fillingKeyBox = true;
            ApiKeyBox.Password = ApiConfig?.ApiKey ?? string.Empty;
            fillingKeyBox = false;
            ShowApiKeyStatus();
        }

        private void OnApiConfigChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(OpenAIConfig.ApiKey))
                return;
            Dispatcher.InvokeAsync(() =>
            {
                // Changed elsewhere (e.g. the API settings window): show it here too.
                string key = ApiConfig?.ApiKey ?? string.Empty;
                if (ApiKeyBox.Password != key && !ApiKeyBox.IsKeyboardFocusWithin)
                    FillKeyBox();
                else
                    ShowApiKeyStatus();
            });
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

        // Saved on every keystroke / paste, independent of the page state, so the key can't be lost
        // by switching pages.
        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (fillingKeyBox || ApiConfig == null)
                return;
            // Pasted keys sometimes carry spaces, line breaks or invisible characters.
            string key = new string(ApiKeyBox.Password.Where(c => c > ' ' && c < 0x7F).ToArray());
            // Only the user clearing the box may erase a saved key, never a control reloading itself.
            if (key.Length == 0 && ApiConfig.ApiKey.Length > 0 && !ApiKeyBox.IsKeyboardFocusWithin)
            {
                FillKeyBox();
                return;
            }
            if (key != ApiConfig.ApiKey)
            {
                ApiConfig.ApiKey = key;
                Translator.Setting?.Save();
            }
            ShowApiKeyStatus();
        }

        // Shows that a key is stored without revealing it.
        private void ShowApiKeyStatus()
        {
            string key = ApiConfig?.ApiKey ?? string.Empty;
            ApiKeyStatus.Text = key.Length == 0
                ? "还没有保存 API Key。"
                : $"✓ 已保存：{key[..Math.Min(3, key.Length)]}…{key[Math.Max(0, key.Length - 4)..]}（{key.Length} 位），" +
                  $"保存在 {AppPaths.SettingFile}";
        }

        private bool fillingAsrKeyBox = false;

        private void ShowSaveFolder()
        {
            SaveFolderBox.Text = LectureDocument.SaveFolder;
        }

        private void ChooseSaveFolder_click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择转录文件的保存位置",
                InitialDirectory = Directory.Exists(LectureDocument.SaveFolder) ? LectureDocument.SaveFolder : string.Empty,
            };
            if (dialog.ShowDialog() != true)
                return;
            Translator.Setting.Lecture.SaveFolder = dialog.FolderName;
            ShowSaveFolder();
        }

        private void OpenSaveFolder_click(object sender, RoutedEventArgs e)
        {
            LectureDialogs.ShowInFolder(LectureDocument.SaveFolder);
        }

        private void ResetSaveFolder_click(object sender, RoutedEventArgs e)
        {
            Translator.Setting.Lecture.SaveFolder = string.Empty;
            ShowSaveFolder();
        }

        private void LoadEngine()
        {
            var lecture = Translator.Setting.Lecture;
            EngineBox.SelectedIndex = (int)lecture.Engine;
            AliyunPanel.Visibility = lecture.Engine == RecognitionEngine.Aliyun ? Visibility.Visible : Visibility.Collapsed;
            fillingAsrKeyBox = true;
            AsrKeyBox.Password = lecture.AsrApiKey;
            fillingAsrKeyBox = false;
            ShowAsrKeyStatus();
        }

        private void EngineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initializing || EngineBox.SelectedIndex < 0)
                return;
            var engine = (RecognitionEngine)EngineBox.SelectedIndex;
            Translator.Setting.Lecture.Engine = engine;
            AliyunPanel.Visibility = engine == RecognitionEngine.Aliyun ? Visibility.Visible : Visibility.Collapsed;
            // LiveCaptions is only needed by the system engine; with the cloud engine it is closed to save power
            // (it is started again when a class with the system engine starts).
            if (engine == RecognitionEngine.Aliyun && !ClassSession.IsRunning)
            {
                Translator.CloseLiveCaptions();
                ShowLiveCaptionsState();
            }
            else if (Translator.Window != null && LiveCaptionsHandler.IsHidden)
            {
                try
                {
                    LiveCaptionsHandler.HideLiveCaptions(Translator.Window);
                }
                catch (Exception)
                {
                }
            }
            if (ClassSession.IsRunning)
                SnackbarHost.Show("识别方式已更改。", "点“停止”再点“开始”后生效。", SnackbarType.Info, timeout: 3);
        }

        private void AsrKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (fillingAsrKeyBox)
                return;
            var lecture = Translator.Setting.Lecture;
            string key = new string(AsrKeyBox.Password.Where(c => c > ' ' && c < 0x7F).ToArray());
            if (key.Length == 0 && lecture.AsrApiKey.Length > 0 && !AsrKeyBox.IsKeyboardFocusWithin)
            {
                LoadEngine();
                return;
            }
            if (key != lecture.AsrApiKey)
                lecture.AsrApiKey = key;
            ShowAsrKeyStatus();
        }

        private void ShowAsrKeyStatus()
        {
            string key = Translator.Setting.Lecture.AsrApiKey;
            AsrKeyStatus.Text = key.Length == 0
                ? "还没有保存阿里云 API Key。"
                : $"✓ 已保存：{key[..Math.Min(3, key.Length)]}…{key[Math.Max(0, key.Length - 4)..]}（{key.Length} 位）";
        }

        private async void TestAsr_click(object sender, RoutedEventArgs e)
        {
            TestAsrButton.IsEnabled = false;
            TestAsrResult.Text = "测试中……";
            try
            {
                await CloudAsr.Test();
                TestAsrResult.Text = "✓ 连接成功";
            }
            catch (Exception ex)
            {
                TestAsrResult.Text = $"✗ {ex.Message}";
            }
            finally
            {
                TestAsrButton.IsEnabled = true;
            }
        }

        private void LoadTranslateSetting()
        {
            var keys = Translator.Setting.Configs.Keys.ToList();
            var choices = keys
                .OrderBy(key => key == Summarizer.SUMMARY_API ? 0 : API_NAMES.ContainsKey(key) ? 1 : 2)
                .Select(key => new ApiChoice(key, API_NAMES.TryGetValue(key, out var name) ? name : key))
                .ToList();
            TranslateApiBox.ItemsSource = choices;
            TranslateApiBox.SelectedItem = choices.FirstOrDefault(c => c.Key == Translator.Setting.ApiName);
            LoadTargetLanguages();
        }

        private void TranslateApiBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (initializing || TranslateApiBox.SelectedItem is not ApiChoice choice)
                return;
            Translator.Setting.ApiName = choice.Key;
            initializing = true;
            try
            {
                LoadTargetLanguages();
            }
            finally
            {
                initializing = false;
            }
        }

        private void LoadTargetLanguages()
        {
            var configType = Translator.Setting[Translator.Setting.ApiName].GetType();
            PropertyInfo? languagesProp = null;
            // Traverse base classes to find `SupportedLanguages`
            for (var type = configType; type != null && languagesProp == null; type = type.BaseType)
                languagesProp = type.GetProperty("SupportedLanguages", BindingFlags.Public | BindingFlags.Static);
            languagesProp ??= typeof(TranslateAPIConfig).GetProperty(
                "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);

            var supportedLanguages = (Dictionary<string, string>)languagesProp!.GetValue(null)!;
            string targetLang = Translator.Setting.TargetLanguage;
            if (!supportedLanguages.ContainsKey(targetLang))
                supportedLanguages[targetLang] = targetLang;    // add custom language to supported languages
            TargetLangBox.ItemsSource = supportedLanguages.Keys.ToList();
            TargetLangBox.SelectedItem = targetLang;
        }

        private void TargetLangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!initializing && TargetLangBox.SelectedItem != null)
                Translator.Setting.TargetLanguage = TargetLangBox.SelectedItem.ToString();
        }

        private void TargetLangBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!initializing && !string.IsNullOrWhiteSpace(TargetLangBox.Text))
                Translator.Setting.TargetLanguage = TargetLangBox.Text.Trim();
        }

        private void APISettingButton_click(object sender, RoutedEventArgs e)
        {
            if (settingWindow != null && settingWindow.IsLoaded)
                settingWindow.Activate();
            else
            {
                settingWindow = new SettingWindow();
                settingWindow.Closed += (sender, args) => settingWindow = null;
                settingWindow.Show();
            }
        }

        private void ShowLiveCaptionsState()
        {
            LiveCaptionsButton.Content = LiveCaptionsHandler.IsHidden || Translator.Window == null
                ? "显示系统实时辅助字幕"
                : "隐藏系统实时辅助字幕";
        }

        private async void LiveCaptionsButton_click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = Translator.Window ?? await Task.Run(Translator.EnsureLiveCaptions);
                if (LiveCaptionsHandler.IsHidden)
                    LiveCaptionsHandler.RestoreLiveCaptions(window);
                else
                    LiveCaptionsHandler.HideLiveCaptions(window);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 操作失败。", ex.Message, SnackbarType.Error, timeout: 2);
            }
            ShowLiveCaptionsState();
        }

        private async void ExtractGlossary_click(object sender, RoutedEventArgs e)
        {
            GlossaryBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            ExtractGlossaryButton.IsEnabled = false;
            GlossaryResult.Text = "正在从课件中提取术语……";
            try
            {
                var (text, added) = await Glossary.ExtractFromSlides();
                Translator.Setting.Lecture.Glossary = text;
                GlossaryBox.Text = text;
                GlossaryResult.Text = added > 0 ? $"✓ 新增 {added} 个术语，请检查一下译法" : "没有发现新的术语";
            }
            catch (Exception ex)
            {
                GlossaryResult.Text = $"✗ {ex.Message}";
            }
            finally
            {
                ExtractGlossaryButton.IsEnabled = true;
            }
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
                SourceStatus.Text = Translator.Setting.Lecture.Engine == RecognitionEngine.Aliyun
                    ? "✓ 已保存。阿里云识别会直接使用这里选的声音来源。"
                    : "✓ 已保存，点“开始”时会自动设置好系统实时辅助字幕。";
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
            var dialog = new OpenFileDialog { Filter = SlideDeck.FileFilter, RestoreDirectory = true };
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
