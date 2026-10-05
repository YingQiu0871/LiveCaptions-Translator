using System.Globalization;
using System.Speech.Synthesis;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // Reads summaries (and optionally translations) aloud through the default audio output device,
    // e.g. the connected Bluetooth earphones.
    public static class Speaker
    {
        // Translations older than this backlog are dropped, so that speech never lags far behind the lecture.
        private const int MAX_TRANSLATION_BACKLOG = 2;

        private static readonly object queueLock = new();
        private static readonly LinkedList<(string Text, bool IsSummary)> queue = new();
        private static readonly AutoResetEvent signal = new(false);

        private static SpeechSynthesizer? synthesizer;
        private static string lastSpokenSource = string.Empty;
        private static bool muted = false;

        public static event Action<bool>? MutedChanged;

        public static bool Muted
        {
            get => muted;
            set
            {
                muted = value;
                if (muted)
                    StopAll();
                MutedChanged?.Invoke(muted);
            }
        }

        private static LectureState? Lecture => Translator.Setting?.Lecture;

        public static void Start()
        {
            var thread = new Thread(Worker) { IsBackground = true, Name = "Speaker" };
            thread.Start();
        }

        public static void EnqueueSummary(string summary)
        {
            if (Muted || Lecture == null || Lecture.SpeakMode == SpeakMode.Off)
                return;
            string text = CleanForSpeech(summary);
            if (string.IsNullOrWhiteSpace(text))
                return;

            lock (queueLock)
            {
                // Summaries go before any pending translation.
                var node = queue.First;
                while (node != null && node.Value.IsSummary)
                    node = node.Next;
                if (node == null)
                    queue.AddLast((text, true));
                else
                    queue.AddBefore(node, (text, true));
            }
            signal.Set();
        }

        public static void EnqueueTranslation(string sourceText, string translatedText)
        {
            if (Muted || Lecture == null || Lecture.SpeakMode != SpeakMode.SummaryAndTranslation)
                return;
            if (string.IsNullOrWhiteSpace(translatedText) ||
                translatedText.Contains("[ERROR]") || translatedText.Contains("[WARNING]"))
                return;

            // LiveCaptions often revises a finished sentence; do not read the same sentence twice.
            if (lastSpokenSource.Length > 0 &&
                TextUtil.Similarity(sourceText, lastSpokenSource) > TextUtil.SIM_THRESHOLD)
                return;
            lastSpokenSource = sourceText;

            string text = CleanForSpeech(RegexPatterns.NoticePrefix().Replace(translatedText, string.Empty));
            if (string.IsNullOrWhiteSpace(text))
                return;

            lock (queueLock)
            {
                queue.AddLast((text, false));
                int backlog = queue.Count(item => !item.IsSummary);
                var node = queue.First;
                while (backlog > MAX_TRANSLATION_BACKLOG && node != null)
                {
                    var next = node.Next;
                    if (!node.Value.IsSummary)
                    {
                        queue.Remove(node);
                        backlog--;
                    }
                    node = next;
                }
            }
            signal.Set();
        }

        public static void Speak(string text)
        {
            lock (queueLock)
                queue.AddFirst((CleanForSpeech(text), true));
            signal.Set();
        }

        public static void StopAll()
        {
            lock (queueLock)
                queue.Clear();
            try
            {
                synthesizer?.SpeakAsyncCancelAll();
            }
            catch (Exception)
            {
            }
        }

        public static List<string> GetVoices()
        {
            try
            {
                using var synth = new SpeechSynthesizer();
                return synth.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo.Name)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static void Worker()
        {
            try
            {
                synthesizer = new SpeechSynthesizer();
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 语音播报不可用。", ex.Message, SnackbarType.Error,
                    timeout: 5, closeButton: true);
                return;
            }

            while (true)
            {
                signal.WaitOne();
                while (true)
                {
                    string text;
                    lock (queueLock)
                    {
                        if (queue.First == null)
                            break;
                        text = queue.First.Value.Text;
                        queue.RemoveFirst();
                    }

                    try
                    {
                        ApplySettings(synthesizer);
                        // Re-select every time: the default device changes when earphones connect or disconnect.
                        synthesizer.SetOutputToDefaultAudioDevice();
                        var prompt = synthesizer.SpeakAsync(text);
                        while (!prompt.IsCompleted)
                            Thread.Sleep(50);
                    }
                    catch (Exception ex)
                    {
                        SnackbarHost.Show("[ERROR] 语音播报失败。", ex.Message, SnackbarType.Error,
                            timeout: 3, closeButton: true);
                    }
                }
            }
        }

        private static void ApplySettings(SpeechSynthesizer synth)
        {
            var lecture = Lecture;
            if (lecture == null)
                return;

            synth.Rate = lecture.SpeechRate;
            synth.Volume = lecture.SpeechVolume;

            string wanted = lecture.VoiceName;
            if (!string.IsNullOrEmpty(wanted))
            {
                if (synth.Voice.Name != wanted)
                {
                    try
                    {
                        synth.SelectVoice(wanted);
                        return;
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                    return;
            }

            // No voice chosen: pick one that speaks the target language.
            try
            {
                var culture = new CultureInfo(Translator.Setting?.TargetLanguage ?? "zh-CN");
                if (!synth.Voice.Culture.Name.Equals(culture.Name, StringComparison.OrdinalIgnoreCase))
                    synth.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, culture);
            }
            catch (Exception)
            {
            }
        }

        private static string CleanForSpeech(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.TrimStart('-', '*', '•', '#', ' '))
                .Where(line => line.Length > 0);
            return string.Join("。", lines).Replace("。。", "。");
        }
    }
}
