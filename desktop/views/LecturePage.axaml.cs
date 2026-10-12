using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class LecturePage : UserControl
    {
        public const int MIN_HEIGHT = 520;
        private const string AUTO_VOICE = "自动（按目标语言选择）";
        private const string AUTO_OCR = "自动";
        private const string DEFAULT_OUTPUT = "系统默认输出设备";

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

        public record Choice(string Tag, string Name)
        {
            public override string ToString() => Name;
        }

        public record ApiChoice(string Key, string Name)
        {
            public override string ToString() => Name;
        }

        private static readonly Choice[] ASR_LANGUAGES =
        {
            new("en", "英语"), new("zh", "中文"), new("ja", "日语"), new("ko", "韩语"), new("de", "德语"),
            new("fr", "法语"), new("ru", "俄语"), new("yue", "粤语"), new("", "自动判断"),
        };

        // Display names for the translation APIs. DeepSeek / Qwen go through the OpenAI-compatible config of card ①.
        private static readonly Dictionary<string, string> API_NAMES = new()
        {
            [Summarizer.SUMMARY_API] = "DeepSeek / 通义千问（用 ① 的设置）",
            ["Google"] = "Google 翻译（免费）",
            ["Google2"] = "Google 翻译 2（免费）",
        };

        private bool initializing = true;
        private bool fillingKeyBox = false;
        private bool fillingAsrKeyBox = false;
        private readonly List<Action> pendingCommits = new();

        private static OpenAIConfig? ApiConfig => Translator.Setting[Summarizer.SUMMARY_API] as OpenAIConfig;
        private static LectureState Lecture => Translator.Setting.Lecture;

        public LecturePage()
        {
            InitializeComponent();
            DataContext = Lecture;
            ContextAwareSwitch.DataContext = Translator.Setting;
            ContextAwareSwitch.IsChecked = Translator.Setting.ContextAware;
            ContextAwareSwitch.IsCheckedChanged += (s, e) => Translator.Setting.ContextAware = ContextAwareSwitch.IsChecked == true;
            LatencySwitch.IsChecked = Translator.Setting.MainWindow.LatencyShow;
            LatencySwitch.IsCheckedChanged += (s, e) =>
                Translator.Setting.MainWindow.LatencyShow = LatencySwitch.IsChecked == true;

            AsrKeyBox.TextChanged += AsrKeyBox_TextChanged;
            ApiKeyBox.TextChanged += ApiKeyBox_TextChanged;
            AsrLanguageBox.ItemsSource = ASR_LANGUAGES;
            AsrLanguageBox.SelectionChanged += (s, e) =>
            {
                if (!initializing && AsrLanguageBox.SelectedItem is Choice choice)
                    Lecture.AsrLanguage = choice.Tag;
            };
            AsrModelBox.ItemsSource = new[] { "paraformer-realtime-v2", "fun-asr-realtime" };
            CommitOnLeave(AsrModelBox, () => Lecture.AsrModel, value => Lecture.AsrModel = value);
            CommitOnLeave(AsrEndpointBox, () => Lecture.AsrEndpoint, value => Lecture.AsrEndpoint = value);
            CommitOnLeave(ApiUrlBox, () => ApiConfig?.ApiUrl ?? string.Empty, value =>
            {
                if (ApiConfig != null)
                    ApiConfig.ApiUrl = value;
            });
            CommitOnLeave(ModelBox, () => ApiConfig?.ModelName ?? string.Empty, value =>
            {
                if (ApiConfig != null)
                    ApiConfig.ModelName = value;
            });
            CommitOnLeave(SubjectBox, () => Lecture.Subject, value => Lecture.Subject = value);
            CommitOnLeave(GlossaryBox, () => Lecture.Glossary, value => Lecture.Glossary = value);
            CommitOnLeave(SummaryPromptBox, () => Lecture.SummaryPrompt, value => Lecture.SummaryPrompt = value);
            CommitOnLeave(TargetLangBox, () => Translator.Setting.TargetLanguage, value =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                    Translator.Setting.TargetLanguage = value.Trim();
            });

            SegmentModeBox.SelectionChanged += (s, e) =>
            {
                if (!initializing && SegmentModeBox.SelectedIndex >= 0)
                    Lecture.SegmentModeIndex = SegmentModeBox.SelectedIndex;
            };
            SpeakModeBox.SelectionChanged += (s, e) =>
            {
                if (!initializing && SpeakModeBox.SelectedIndex >= 0)
                    Lecture.SpeakModeIndex = SpeakModeBox.SelectedIndex;
            };
            BindSlider(MaxSectionSlider, () => Lecture.MaxSectionMinutes, v => Lecture.MaxSectionMinutes = v,
                MaxSectionLabel, v => $"每节最长：{v}分钟");
            BindSlider(IntervalSlider, () => Lecture.SummaryIntervalMinutes, v => Lecture.SummaryIntervalMinutes = v,
                IntervalLabel, v => $"按时间分节：每{v}分钟一节");
            BindSlider(RateSlider, () => Lecture.SpeechRate, v => Lecture.SpeechRate = v, RateLabel, v => $"语速：{v}");
            BindSlider(VolumeSlider, () => Lecture.SpeechVolume, v => Lecture.SpeechVolume = v, VolumeLabel,
                v => $"音量：{v}");
            BindSlider(SyncSlider, () => Translator.Setting.MaxSyncInterval, v => Translator.Setting.MaxSyncInterval = v,
                SyncLabel, v => $"翻译频率：字幕每变化{v}次翻译一次");
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Shell.Main?.AutoHeightAdjust(minHeight: MIN_HEIGHT);
            Summarizer.CurrentPageChanged += OnCurrentPageChanged;
            if (ApiConfig != null)
                ApiConfig.PropertyChanged += OnApiConfigChanged;
            Load();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Summarizer.CurrentPageChanged -= OnCurrentPageChanged;
            if (ApiConfig != null)
                ApiConfig.PropertyChanged -= OnApiConfigChanged;
            // Text still being edited is kept.
            foreach (var commit in pendingCommits)
                commit();
            initializing = true;
        }

        private async void Load()
        {
            initializing = true;
            // One failing loader (e.g. an audio device that can't be read) must not leave the page read-only.
            foreach (Action load in new Action[]
                     {
                         LoadEngine, LoadSettings, LoadApiSetting, LoadTranslateSetting, LoadOcrLanguages, LoadVoices,
                         ShowSlidesInfo, ShowSaveFolder
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
            try
            {
                await LoadDevices();
            }
            catch (Exception)
            {
            }
            initializing = false;
        }

        // A text field whose value is written when it loses the focus (and when the page is left), not on every key.
        private void CommitOnLeave(EditableCombo box, Func<string> get, Action<string> set)
        {
            void Commit()
            {
                if (!initializing && box.Text != get())
                    set(box.Text);
            }
            box.LostFocus += (s, e) => Commit();
            box.ItemPicked += item =>
            {
                if (!initializing && item != get())
                    set(item);
            };
            pendingCommits.Add(Commit);
        }

        private void CommitOnLeave(TextBox box, Func<string> get, Action<string> set)
        {
            void Commit()
            {
                string text = box.Text ?? string.Empty;
                if (!initializing && text != get())
                    set(text);
            }
            box.LostFocus += (s, e) => Commit();
            pendingCommits.Add(Commit);
        }

        private void BindSlider(Slider slider, Func<int> get, Action<int> set, TextBlock label, Func<int, string> text)
        {
            slider.PropertyChanged += (s, e) =>
            {
                if (e.Property != RangeBase.ValueProperty || initializing)
                    return;
                int value = (int)Math.Round(slider.Value);
                if (value != get())
                    set(value);
                label.Text = text(get());
            };
            sliderShown.Add(() =>
            {
                slider.Value = get();
                label.Text = text(get());
            });
        }

        private readonly List<Action> sliderShown = new();

        private void LoadSettings()
        {
            SegmentModeBox.SelectedIndex = Lecture.SegmentModeIndex;
            SpeakModeBox.SelectedIndex = Lecture.SpeakModeIndex;
            ContextAwareSwitch.IsChecked = Translator.Setting.ContextAware;
            LatencySwitch.IsChecked = Translator.Setting.MainWindow.LatencyShow;
            foreach (var show in sliderShown)
                show();
            AsrModelBox.Text = Lecture.AsrModel;
            AsrEndpointBox.Text = Lecture.AsrEndpoint;
            SubjectBox.Text = Lecture.Subject;
            GlossaryBox.Text = Lecture.Glossary;
            SummaryPromptBox.Text = Lecture.SummaryPrompt;
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

            ShowApiFields();
            FillKeyBox();
        }

        private void ShowApiFields()
        {
            ApiUrlBox.Text = ApiConfig?.ApiUrl ?? string.Empty;
            ModelBox.Text = ApiConfig?.ModelName ?? string.Empty;
        }

        private void FillKeyBox()
        {
            fillingKeyBox = true;
            ApiKeyBox.Text = ApiConfig?.ApiKey ?? string.Empty;
            fillingKeyBox = false;
            ShowApiKeyStatus();
        }

        private void OnApiConfigChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(OpenAIConfig.ApiKey))
                return;
            Dispatcher.UIThread.Post(() =>
            {
                // Changed elsewhere (e.g. the API settings window): show it here too.
                string key = ApiConfig?.ApiKey ?? string.Empty;
                if (ApiKeyBox.Text != key && !ApiKeyBox.IsKeyboardFocusWithin)
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

        private void ProviderBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (initializing || ProviderBox.SelectedIndex < 0 || ApiConfig == null)
                return;
            var provider = PROVIDERS[ProviderBox.SelectedIndex];
            ModelBox.ItemsSource = provider.Models;
            ApplyProvider(provider, ApiConfig);
            initializing = true;
            ShowApiFields();
            initializing = false;
            TestApiResult.Text = string.Empty;
        }

        // Saved on every keystroke / paste, independent of the page state, so the key can't be lost
        // by switching pages.
        private void ApiKeyBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (fillingKeyBox || ApiConfig == null)
                return;
            string key = CleanKey(ApiKeyBox.Text ?? string.Empty);
            // Only the user clearing the box may erase a saved key, never a control reloading itself.
            if (key.Length == 0 && ApiConfig.ApiKey.Length > 0 && !ApiKeyBox.IsKeyboardFocusWithin)
            {
                FillKeyBox();
                return;
            }
            if (key != ApiConfig.ApiKey)
            {
                bool firstKey = ApiConfig.ApiKey.Length == 0 && key.Length > 0;
                ApiConfig.ApiKey = key;
                if (firstKey)
                    SwitchTranslateApiToSummary();
                Translator.Setting?.Save();
            }
            ShowApiKeyStatus();
        }

        // Paragraph refinement and the glossary in translation only work when the translation API is the
        // one configured in ①. Fresh installs start on Google so translation works without a key, so the
        // first saved key moves a Google default over; a deliberately chosen API is left alone.
        private void SwitchTranslateApiToSummary()
        {
            var setting = Translator.Setting;
            if (setting == null || (setting.ApiName != "Google" && setting.ApiName != "Google2"))
                return;
            setting.ApiName = Summarizer.SUMMARY_API;
            bool wasInitializing = initializing;
            initializing = true;
            try
            {
                if (TranslateApiBox.ItemsSource is IEnumerable<ApiChoice> choices)
                    TranslateApiBox.SelectedItem = choices.FirstOrDefault(c => c.Key == setting.ApiName);
                LoadTargetLanguages();
            }
            finally
            {
                initializing = wasInitializing;
            }
        }

        // Shows that a key is stored without revealing it.
        private void ShowApiKeyStatus()
        {
            string key = ApiConfig?.ApiKey ?? string.Empty;
            ApiKeyStatus.Text = key.Length == 0
                ? "还没有保存 API Key。"
                : $"✓ 已保存：{MaskKey(key)}，" +
                  $"保存在 {AppPaths.SettingFile}";
        }

        // Pasted keys sometimes carry spaces, line breaks or invisible characters.
        private static string CleanKey(string text)
        {
            return new string(text.Where(c => c > ' ' && c < 0x7F).ToArray());
        }

        private static string MaskKey(string key)
        {
            return $"{key[..Math.Min(3, key.Length)]}…{key[Math.Max(0, key.Length - 4)..]}（{key.Length} 位）";
        }

        private void ShowSaveFolder()
        {
            SaveFolderBox.Text = LectureDocument.SaveFolder;
        }

        private async void ChooseSaveFolder_click(object? sender, RoutedEventArgs e)
        {
            string? folder = await Shell.PickFolder("选择转录文件的保存位置", LectureDocument.SaveFolder);
            if (folder == null)
                return;
            Lecture.SaveFolder = folder;
            ShowSaveFolder();
        }

        private void OpenSaveFolder_click(object? sender, RoutedEventArgs e)
        {
            LectureDialogs.ShowInFolder(LectureDocument.SaveFolder);
        }

        private void ResetSaveFolder_click(object? sender, RoutedEventArgs e)
        {
            Lecture.SaveFolder = string.Empty;
            ShowSaveFolder();
        }

        // Live Captions does not exist here, so the cloud recognizer is the only engine.
        private void LoadEngine()
        {
            AsrLanguageBox.SelectedItem = ASR_LANGUAGES.FirstOrDefault(c => c.Tag == Lecture.AsrLanguage);
            fillingAsrKeyBox = true;
            AsrKeyBox.Text = Lecture.AsrApiKey;
            fillingAsrKeyBox = false;
            ShowAsrKeyStatus();
        }

        private void AsrKeyBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (fillingAsrKeyBox)
                return;
            string key = CleanKey(AsrKeyBox.Text ?? string.Empty);
            if (key.Length == 0 && Lecture.AsrApiKey.Length > 0 && !AsrKeyBox.IsKeyboardFocusWithin)
            {
                LoadEngine();
                return;
            }
            if (key != Lecture.AsrApiKey)
                Lecture.AsrApiKey = key;
            ShowAsrKeyStatus();
        }

        private void ShowAsrKeyStatus()
        {
            string key = Lecture.AsrApiKey;
            AsrKeyStatus.Text = key.Length == 0
                ? "还没有保存阿里云 API Key。"
                : $"✓ 已保存：{MaskKey(key)}";
        }

        private async void TestAsr_click(object? sender, RoutedEventArgs e)
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

        private void TranslateApiBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
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
            TargetLangBox.Text = targetLang;
        }

        private void APISettingButton_click(object? sender, RoutedEventArgs e)
        {
            ApiSettingWindow.ShowSingle(Shell.Main);
        }

        private async void ExtractGlossary_click(object? sender, RoutedEventArgs e)
        {
            Lecture.Glossary = GlossaryBox.Text ?? string.Empty;
            ExtractGlossaryButton.IsEnabled = false;
            GlossaryResult.Text = "正在从课件中提取术语……";
            try
            {
                var (text, added) = await Glossary.ExtractFromSlides();
                Lecture.Glossary = text;
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

        private async void TestApi_click(object? sender, RoutedEventArgs e)
        {
            // Commit fields that only update on lost focus.
            foreach (var commit in pendingCommits)
                commit();

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

        private async Task LoadDevices()
        {
            bool wasInitializing = initializing;
            initializing = true;

            SourceBox.SelectedIndex = (int)Lecture.InputSource;
            bool mac = OperatingSystem.IsMacOS();
            // macOS has no list of microphones: the system default one is used.
            MicPanel.IsVisible = !mac;
            MicHint.Text = mac
                ? "用的是系统默认麦克风，可在系统设置的“声音”里更换。第一次使用时 macOS 会请求麦克风权限。"
                : "这里选的麦克风只供本程序录音，不会更改系统的默认录音设备。不要选蓝牙耳机自带的麦克风，否则耳机会切换到通话模式，音质变差。";

            var microphones = await AudioDevices.ListAsync(DataFlow.Capture);
            MicBox.ItemsSource = microphones;
            string chosen = DesktopSettings.MicrophoneId;
            MicBox.SelectedItem = microphones.FirstOrDefault(device => device.Id == chosen && chosen.Length > 0)
                                  ?? microphones.FirstOrDefault(device => device.IsDefault);
            MicBox.IsEnabled = Lecture.InputSource == InputSource.Microphone;

            var outputs = new List<object> { DEFAULT_OUTPUT };
            var renderDevices = await AudioDevices.ListAsync(DataFlow.Render);
            outputs.AddRange(renderDevices);
            SpeechDeviceBox.ItemsSource = outputs;
            string speechDeviceId = Lecture.SpeechDeviceId;
            SpeechDeviceBox.SelectedItem = renderDevices.FirstOrDefault(device => device.Id == speechDeviceId)
                                           ?? (object)DEFAULT_OUTPUT;
            ShowSpeechDeviceHint();

            initializing = wasInitializing;
        }

        private async void RefreshDevices_click(object? sender, RoutedEventArgs e)
        {
            await LoadDevices();
        }

        private void SourceBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (initializing || SourceBox.SelectedIndex < 0)
                return;
            var source = (InputSource)SourceBox.SelectedIndex;
            Lecture.InputSource = source;
            MicBox.IsEnabled = source == InputSource.Microphone;
            ShowSpeechDeviceHint();
            SourceStatus.Text = "✓ 已保存。阿里云识别会直接使用这里选的声音来源。";
        }

        private void MicBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (initializing || MicBox.SelectedItem is not AudioDevice device)
                return;
            // The default device is followed, whichever it is.
            string source = device.IsDefault ? string.Empty : device.Id;
            DesktopSettings.MicrophoneId = source;
            CloudAsr.MicrophoneSource = source.Length == 0 ? null : source;
            SourceStatus.Text = $"✓ 已改用：{device.Name}。点“停止”再点“开始”后生效。";
        }

        private void SpeechDeviceBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (initializing)
                return;
            Lecture.SpeechDeviceId = SpeechDeviceBox.SelectedItem is AudioDevice device ? device.Id : string.Empty;
            ShowSpeechDeviceHint();
        }

        private void ShowSpeechDeviceHint()
        {
            string speechDeviceId = Lecture.SpeechDeviceId;
            bool onDefault = string.IsNullOrEmpty(speechDeviceId) ||
                             speechDeviceId == AudioDevices.DefaultId(DataFlow.Render);
            SpeechDeviceHint.Text = onDefault
                ? "播报和电脑声音走同一个设备，识别会听到播报，所以播报时会暂停接收字幕。" +
                  "线下课建议在这里直接选蓝牙耳机，并把系统默认输出设为电脑扬声器。"
                : "播报只从这个设备播出，不会被识别听到。";
        }

        private void LoadOcrLanguages()
        {
            var languages = new List<object> { AUTO_OCR };
            var available = SlideOcr.AvailableLanguages();
            languages.AddRange(available);
            OcrLanguageBox.ItemsSource = languages;
            string current = Lecture.OcrLanguage;
            OcrLanguageBox.SelectedItem = available.FirstOrDefault(l => l.Tag == current) ?? (object)AUTO_OCR;
            OcrHint.Text = "上传后按老师讲到的页码分节，小结也会参考这一页的文字纠正术语。没有文字的页面（扫描件、图片）会自动用 Tesseract OCR 识别；" +
                           "OCR 语言来自已安装的 Tesseract 语言包。也支持 .pptx。" +
                           (SlideOcr.IsAvailable ? string.Empty : "\n还没有找到 OCR 工具：请安装 poppler-utils（pdftoppm）和 tesseract。");
        }

        private void OcrLanguageBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (initializing)
                return;
            Lecture.OcrLanguage =
                OcrLanguageBox.SelectedItem is SlideOcr.OcrLanguage language ? language.Tag : string.Empty;
        }

        private void OnCurrentPageChanged(int? page)
        {
            Dispatcher.UIThread.Post(ShowSlidesInfo);
        }

        private void ShowSlidesInfo()
        {
            if (!SlideDeck.IsLoaded)
            {
                SlidesInfo.Text = "未上传课件：按话题变化分节。";
                ClearSlidesButton.IsVisible = false;
                return;
            }
            string page = Summarizer.CurrentPage == null ? "识别中" : $"第 {Summarizer.CurrentPage} 页";
            string ocr = SlideDeck.OcrPageCount > 0 ? $"（其中 {SlideDeck.OcrPageCount} 页用 OCR 识别）" : string.Empty;
            SlidesInfo.Text = $"{SlideDeck.FileName}\n共 {SlideDeck.Pages.Count} 页{ocr}，当前：{page}";
            ClearSlidesButton.IsVisible = true;
        }

        private async void LoadSlides_click(object? sender, RoutedEventArgs e)
        {
            string? path = await Shell.PickFile("选择课件", Shell.SlideFiles);
            if (path == null)
                return;

            SlidesInfo.Text = "读取中……";
            LoadSlidesButton.IsEnabled = false;
            try
            {
                await SlideDeck.Load(path, new Progress<string>(text => SlidesInfo.Text = text));
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

        private void ClearSlides_click(object? sender, RoutedEventArgs e)
        {
            SlideDeck.Clear();
            ShowSlidesInfo();
        }

        private void LoadVoices()
        {
            var voices = new List<string> { AUTO_VOICE };
            voices.AddRange(Speaker.GetVoices());
            VoiceBox.ItemsSource = voices;

            string current = Lecture.VoiceName;
            VoiceBox.SelectedItem = voices.Contains(current) ? current : AUTO_VOICE;
        }

        private void VoiceBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (initializing || VoiceBox.SelectedItem is not string voice)
                return;
            Lecture.VoiceName = voice == AUTO_VOICE ? string.Empty : voice;
        }

        private void TestSpeech_click(object? sender, RoutedEventArgs e)
        {
            Speaker.Speak("这是一段试听。老师每讲完一节，你会在耳机里听到这一节的小结。");
        }

        private void SoundSettings_click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!Shell.OpenSoundSettings())
                    SnackbarHost.Show("没有找到系统的声音设置。", "请在系统设置的“声音”里选择输出设备。", SnackbarType.Warning, timeout: 3);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 无法打开声音设置。", ex.Message, SnackbarType.Error, timeout: 2);
            }
        }

        private void ResetPrompt_click(object? sender, RoutedEventArgs e)
        {
            Lecture.SummaryPrompt = LectureState.DEFAULT_SUMMARY_PROMPT;
            SummaryPromptBox.Text = Lecture.SummaryPrompt;
        }
    }
}
