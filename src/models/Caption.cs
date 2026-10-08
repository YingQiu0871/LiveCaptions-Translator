using System.ComponentModel;
using System.Runtime.CompilerServices;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.models
{
    public class Caption : INotifyPropertyChanged
    {
        public const int MAX_CONTEXTS = 10;

        private static Caption? instance = null;
        public event PropertyChangedEventHandler? PropertyChanged;

        private string displayOriginalCaption = string.Empty;
        private string displayTranslatedCaption = string.Empty;
        private string overlayOriginalCaption = " ";
        private string overlayCurrentTranslation = " ";
        private string overlayNoticePrefix = " ";
        private string statusHint = ClassSession.IDLE_HINT;

        public string OriginalCaption { get; set; } = string.Empty;
        public string TranslatedCaption { get; set; } = string.Empty;

        public Queue<TranslationHistoryEntry> Contexts { get; } = new(MAX_CONTEXTS);

        public IEnumerable<TranslationHistoryEntry> AwareContexts => GetPreviousContexts(Translator.Setting.NumContexts);
        public string AwareContextsCaption => GetPreviousText(Translator.Setting.NumContexts, TextType.Caption);

        public IEnumerable<TranslationHistoryEntry> DisplayLogCards =>
            GetPreviousContexts(Translator.Setting.DisplaySentences).Reverse();

        public string DisplayOriginalCaption
        {
            get => displayOriginalCaption;
            set
            {
                displayOriginalCaption = value;
                OnPropertyChanged("DisplayOriginalCaption");
            }
        }
        private string sourceWarning = string.Empty;

        // A problem with what LiveCaptions delivers, shown above the current sentence.
        public string SourceWarning
        {
            get => sourceWarning;
            set
            {
                if (sourceWarning == value)
                    return;
                sourceWarning = value;
                OnPropertyChanged("SourceWarning");
                OnPropertyChanged("SourceWarningVisibility");
            }
        }
        public System.Windows.Visibility SourceWarningVisibility =>
            string.IsNullOrEmpty(sourceWarning) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        // Shown on the caption page while there is no caption yet.
        public string StatusHint
        {
            get => statusHint;
            set
            {
                statusHint = value;
                OnPropertyChanged("StatusHint");
            }
        }
        public string DisplayTranslatedCaption
        {
            get => displayTranslatedCaption;
            set
            {
                displayTranslatedCaption = value;
                OnPropertyChanged("DisplayTranslatedCaption");
            }
        }

        public string OverlayOriginalCaption
        {
            get => overlayOriginalCaption;
            set
            {
                overlayOriginalCaption = value;
                OnPropertyChanged("OverlayOriginalCaption");
            }
        }
        public string OverlayNoticePrefix
        {
            get => overlayNoticePrefix;
            set
            {
                overlayNoticePrefix = value;
                OnPropertyChanged("OverlayNoticePrefix");
            }
        }
        public string OverlayCurrentTranslation
        {
            get => overlayCurrentTranslation;
            set
            {
                overlayCurrentTranslation = value;
                OnPropertyChanged("OverlayCurrentTranslation");
            }
        }

        public string OverlayPreviousTranslation =>
            GetPreviousText(Translator.Setting.DisplaySentences, TextType.Translation);

        private Caption()
        {
        }

        public static Caption GetInstance()
        {
            if (instance != null)
                return instance;
            instance = new Caption();
            return instance;
        }

        private static bool IsUsableContext(TranslationHistoryEntry? entry) =>
            entry != null && string.CompareOrdinal(entry.TranslatedText, "N/A") != 0 &&
            !entry.TranslatedText.Contains("[ERROR]") && !entry.TranslatedText.Contains("[WARNING]");

        public string GetPreviousText(int count, TextType textType)
        {
            if (count <= 0 || Contexts.Count == 0)
                return string.Empty;

            var prev = Contexts
                .Reverse().Take(count).Reverse()
                .Select(entry => !IsUsableContext(entry) ?
                    "" : (textType == TextType.Caption ? entry.SourceText : entry.TranslatedText))
                .Aggregate((accu, cur) =>
                {
                    if (!string.IsNullOrEmpty(accu))
                    {
                        if (Array.IndexOf(TextUtil.PUNC_EOS, accu[^1]) == -1)
                            accu += TextUtil.isCJChar(accu[^1]) ? "。" : ". ";
                        else
                            accu += TextUtil.isCJChar(accu[^1]) ? "" : " ";
                    }
                    cur = RegexPatterns.NoticePrefix().Replace(cur, "");
                    return accu + cur;
                });

            if (textType == TextType.Translation)
                prev = RegexPatterns.NoticePrefix().Replace(prev, "");
            if (!string.IsNullOrEmpty(prev) && Array.IndexOf(TextUtil.PUNC_EOS, prev[^1]) == -1)
                prev += TextUtil.isCJChar(prev[^1]) ? "。" : ".";
            if (!string.IsNullOrEmpty(prev) && prev[^1] < 0x80)
                prev += " ";
            return prev;
        }

        public IEnumerable<TranslationHistoryEntry> GetPreviousContexts(int count)
        {
            if (count <= 0 || Contexts.Count == 0)
                return [];

            return Contexts
                .Reverse().Take(count).Reverse()
                .Where(IsUsableContext);
        }

        public System.Windows.Visibility OriginalVisibility =>
            // Without translation the original is all there is.
            Translator.Setting?.Lecture.ShowOriginal == false && Translator.Setting?.Lecture.SkipTranslation == false
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public enum TextType
    {
        Caption,
        Translation
    }
}