using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveCaptionsTranslator.models
{
    public enum SpeakMode
    {
        Off = 0,
        SummaryOnly = 1,
        SummaryAndTranslation = 2,
    }

    public enum SegmentMode
    {
        // Slides (if loaded) or topic changes decide the sections; time is only the fallback.
        Auto = 0,
        // A new section every `SummaryIntervalMinutes`.
        FixedTime = 1,
    }

    public enum InputSource
    {
        // Sound played by the computer (online class, video).
        ComputerAudio = 0,
        // Sound in the classroom, through a microphone.
        Microphone = 1,
    }

    public class LectureState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public const string DEFAULT_SUMMARY_PROMPT =
            "You are a teaching assistant helping a student follow a lecture given in a foreign language. " +
            "The user message is the speech-recognition transcript of the latest section of the lecture " +
            "(it may contain recognition errors), one line per sentence with its time. " +
            "Summarize this section in {0} for the student. " +
            "If the slide page of this section is given, use it to correct recognition errors and terminology. " +
            "Line 1: a short title of the section (no more than 15 words). " +
            "Then 2 to 4 lines, each starting with \"- \", covering the key ideas, definitions, formulas or examples. " +
            "When a technical term appears, keep the original term in parentheses after the translation. " +
            "Output plain text only, no Markdown headings, no preamble. " +
            "If the section has no substantive content (small talk, silence, noise), output only the title line.";

        private bool summaryEnabled = true;
        private int summaryIntervalMinutes = 2;
        private SegmentMode segmentMode = SegmentMode.Auto;
        private int maxSectionMinutes = 6;
        private string summaryPrompt = DEFAULT_SUMMARY_PROMPT;
        private SpeakMode speakMode = SpeakMode.SummaryOnly;
        private int speechRate = 2;
        private int speechVolume = 100;
        private string voiceName = string.Empty;
        private string speechDeviceId = string.Empty;
        private InputSource inputSource = InputSource.ComputerAudio;
        private string ocrLanguage = string.Empty;
        private bool forceOcr = false;
        private bool speechMuted = false;
        private bool showOriginal = true;

        public bool SummaryEnabled
        {
            get => summaryEnabled;
            set
            {
                summaryEnabled = value;
                OnPropertyChanged("SummaryEnabled");
            }
        }
        public int SummaryIntervalMinutes
        {
            get => summaryIntervalMinutes;
            set
            {
                summaryIntervalMinutes = Math.Clamp(value, 1, 60);
                OnPropertyChanged("SummaryIntervalMinutes");
            }
        }
        public SegmentMode SegmentMode
        {
            get => segmentMode;
            set
            {
                segmentMode = value;
                OnPropertyChanged("SegmentMode");
                OnPropertyChanged("SegmentModeIndex");
            }
        }
        [System.Text.Json.Serialization.JsonIgnore]
        public int SegmentModeIndex
        {
            get => (int)segmentMode;
            set => SegmentMode = (SegmentMode)Math.Clamp(value, 0, 1);
        }
        // In Auto mode, a section is closed after this long even if the topic has not changed.
        public int MaxSectionMinutes
        {
            get => maxSectionMinutes;
            set
            {
                maxSectionMinutes = Math.Clamp(value, 2, 30);
                OnPropertyChanged("MaxSectionMinutes");
            }
        }
        public string SummaryPrompt
        {
            get => summaryPrompt;
            set
            {
                summaryPrompt = value;
                OnPropertyChanged("SummaryPrompt");
            }
        }
        public SpeakMode SpeakMode
        {
            get => speakMode;
            set
            {
                speakMode = value;
                OnPropertyChanged("SpeakMode");
                OnPropertyChanged("SpeakModeIndex");
            }
        }
        // For binding with ComboBox.SelectedIndex
        [System.Text.Json.Serialization.JsonIgnore]
        public int SpeakModeIndex
        {
            get => (int)speakMode;
            set => SpeakMode = (SpeakMode)Math.Clamp(value, 0, 2);
        }
        public int SpeechRate
        {
            get => speechRate;
            set
            {
                speechRate = Math.Clamp(value, -10, 10);
                OnPropertyChanged("SpeechRate");
            }
        }
        public int SpeechVolume
        {
            get => speechVolume;
            set
            {
                speechVolume = Math.Clamp(value, 0, 100);
                OnPropertyChanged("SpeechVolume");
            }
        }
        public string VoiceName
        {
            get => voiceName;
            set
            {
                voiceName = value ?? string.Empty;
                OnPropertyChanged("VoiceName");
            }
        }

        // Empty: the Windows default output device.
        public string SpeechDeviceId
        {
            get => speechDeviceId;
            set
            {
                speechDeviceId = value ?? string.Empty;
                OnPropertyChanged("SpeechDeviceId");
            }
        }
        public InputSource InputSource
        {
            get => inputSource;
            set
            {
                inputSource = value;
                OnPropertyChanged("InputSource");
            }
        }
        // Language tag of the Windows OCR engine for scanned slides; empty: chosen automatically.
        public string OcrLanguage
        {
            get => ocrLanguage;
            set
            {
                ocrLanguage = value ?? string.Empty;
                OnPropertyChanged("OcrLanguage");
            }
        }
        public bool ForceOcr
        {
            get => forceOcr;
            set
            {
                forceOcr = value;
                OnPropertyChanged("ForceOcr");
            }
        }

        // The earphone speech switch; kept across restarts.
        public bool SpeechMuted
        {
            get => speechMuted;
            set
            {
                speechMuted = value;
                OnPropertyChanged("SpeechMuted");
            }
        }

        // 中外对照: keep the original sentence above its translation on the caption page.
        public bool ShowOriginal
        {
            get => showOriginal;
            set
            {
                showOriginal = value;
                OnPropertyChanged("ShowOriginal");
                Translator.Caption?.OnPropertyChanged("OriginalVisibility");
            }
        }

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            Translator.Setting?.Save();
        }
    }
}
