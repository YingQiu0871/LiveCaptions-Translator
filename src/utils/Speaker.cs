using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using NAudio.CoreAudioApi;
using NAudio.Wave;

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

        // Keep ignoring captions a little after speech ends: LiveCaptions lags behind the audio.
        private static readonly TimeSpan SUPPRESS_TAIL = TimeSpan.FromMilliseconds(1500);

        private static SpeechSynthesizer? synthesizer;
        private static volatile bool stopRequested = false;
        private static volatile bool speakingOnDefaultOutput = false;
        private static DateTime suppressUntil = DateTime.MinValue;
        private static string lastSpokenSource = string.Empty;
        public static event Action<bool>? MutedChanged;

        public static bool Muted
        {
            get => Lecture?.SpeechMuted ?? false;
            set
            {
                if (Lecture != null)
                    Lecture.SpeechMuted = value;
                if (value)
                    StopAll();
                MutedChanged?.Invoke(value);
            }
        }

        private static LectureState? Lecture => Translator.Setting?.Lecture;

        // True while our speech plays on the default output device, which LiveCaptions listens to.
        public static bool SuppressCaptions => speakingOnDefaultOutput || DateTime.Now < suppressUntil;

        public static void Start()
        {
            var thread = new Thread(Worker) { IsBackground = true, Name = "Speaker" };
            thread.Start();
        }

        public static void EnqueueSummary(string summary)
        {
            if (Muted || Lecture == null || Lecture.SpeakMode is SpeakMode.Off or SpeakMode.Interpretation)
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

        // A refined paragraph, read as one piece.
        public static void EnqueueParagraph(string translatedText)
        {
            if (Muted || Lecture == null || Lecture.SpeakMode != SpeakMode.SummaryAndTranslation)
                return;
            string text = CleanForSpeech(translatedText);
            if (string.IsNullOrWhiteSpace(text))
                return;
            lock (queueLock)
            {
                queue.AddLast((text, false));
            }
            signal.Set();
        }

        public static void EnqueueTranslation(string sourceText, string translatedText)
        {
            if (Muted || Lecture == null)
                return;
            // Interpretation reads every sentence at once. Otherwise, with paragraph refinement on, the refined
            // paragraph is read instead of each raw sentence (a few seconds later, but more accurate).
            bool read = Lecture.SpeakMode == SpeakMode.Interpretation ||
                        (Lecture.SpeakMode == SpeakMode.SummaryAndTranslation && !Refiner.Enabled);
            if (!read)
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
            stopRequested = true;
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

                    stopRequested = false;
                    try
                    {
                        ApplySettings(synthesizer);
                        string deviceId = Lecture?.SpeechDeviceId ?? string.Empty;
                        using var device = string.IsNullOrEmpty(deviceId) ? null : AudioDevices.Get(deviceId);

                        // Re-checked every time: the default device changes when earphones connect or disconnect.
                        speakingOnDefaultOutput = device == null ||
                                                  device.ID == AudioDevices.DefaultId(DataFlow.Render);
                        if (device == null)
                            SpeakToDefaultDevice(synthesizer, text);
                        else
                            SpeakToDevice(synthesizer, text, device);
                    }
                    catch (Exception ex)
                    {
                        SnackbarHost.Show("[ERROR] 语音播报失败。", ex.Message, SnackbarType.Error,
                            timeout: 3, closeButton: true);
                    }
                    finally
                    {
                        if (speakingOnDefaultOutput)
                            suppressUntil = DateTime.Now + SUPPRESS_TAIL;
                        speakingOnDefaultOutput = false;
                    }
                }
            }
        }

        private static void SpeakToDefaultDevice(SpeechSynthesizer synth, string text)
        {
            synth.SetOutputToDefaultAudioDevice();
            var prompt = synth.SpeakAsync(text);
            while (!prompt.IsCompleted)
                Thread.Sleep(50);
        }

        // Plays on a chosen device (e.g. the earphones) while the Windows default output stays elsewhere.
        private static void SpeakToDevice(SpeechSynthesizer synth, string text, MMDevice device)
        {
            // 48 kHz stereo matches the shared-mode mix format of most devices.
            using var stream = new MemoryStream();
            synth.SetOutputToAudioStream(stream,
                new System.Speech.AudioFormat.SpeechAudioFormatInfo(48000,
                    System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Stereo));
            try
            {
                synth.Speak(text);
            }
            finally
            {
                synth.SetOutputToNull();
            }
            if (stopRequested)
                return;

            stream.Position = 0;
            using var reader = new RawSourceWaveStream(stream, new WaveFormat(48000, 16, 2));
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            output.Init(reader);
            output.Play();
            while (output.PlaybackState == PlaybackState.Playing && !stopRequested)
                Thread.Sleep(50);
            output.Stop();
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
