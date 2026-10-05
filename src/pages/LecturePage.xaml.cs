using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class LecturePage : Page
    {
        public const int MIN_HEIGHT = 360;
        private const string AUTO_VOICE = "自动（按目标语言选择）";

        public LecturePage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();
            DataContext = Translator.Setting.Lecture;

            Loaded += (s, e) =>
            {
                (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(minHeight: MIN_HEIGHT, maxHeight: MIN_HEIGHT);
                LoadVoices();
            };
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
            if (VoiceBox.SelectedItem is not string voice)
                return;
            Translator.Setting.Lecture.VoiceName = voice == AUTO_VOICE ? string.Empty : voice;
        }

        private void TestSpeech_click(object sender, RoutedEventArgs e)
        {
            Speaker.Speak("这是一段试听。老师每讲完一节，你会在耳机里听到这一节的小结。");
        }

        private void ResetPrompt_click(object sender, RoutedEventArgs e)
        {
            Translator.Setting.Lecture.SummaryPrompt = LectureState.DEFAULT_SUMMARY_PROMPT;
        }
    }
}
