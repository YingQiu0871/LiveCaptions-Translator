using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // Reads summaries (and optionally translations) aloud. macOS: the `say` command, optionally to a chosen output
    // device. Linux: speech-dispatcher (`spd-say`), or `espeak-ng` if that is missing; they play on the default output.
    // Same public API and queue rules as the Windows version in src/utils/Speaker.cs.
    public static class Speaker
    {
        // Translations older than this backlog are dropped, so that speech never lags far behind the lecture.
        private const int MAX_TRANSLATION_BACKLOG = 2;

        private static readonly object queueLock = new();
        private static readonly LinkedList<(string Text, bool IsSummary)> queue = new();
        private static readonly AutoResetEvent signal = new(false);

        // Keep ignoring captions a little after speech ends: the recognizer lags behind the audio.
        private static readonly TimeSpan SUPPRESS_TAIL = TimeSpan.FromMilliseconds(1500);

        private static readonly object processLock = new();
        private static Process? current;
        private static volatile bool stopRequested = false;
        // The `spd-say -C` started by the last stop: the worker waits for it, or it would cancel the next speech.
        private static volatile Task? pendingCancel;
        private static volatile bool speakingOnDefaultOutput = false;
        private static DateTime suppressUntil = DateTime.MinValue;
        private static string lastSpokenSource = string.Empty;
        private static string? missingToolReported;
        private static string? failureReported;
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

        // True while our speech plays on the default output device, which the system audio capture listens to.
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

            // The recognizer often revises a finished sentence; do not read the same sentence twice.
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
            bool wasSpeaking = speakingOnDefaultOutput;
            bool dispatcher = false;
            lock (processLock)
            {
                dispatcher = current != null && Path.GetFileName(current.StartInfo.FileName) == "spd-say";
                Exec.Kill(current);
            }
            if (dispatcher)
            {
                // Killing the client does not stop speech-dispatcher: it keeps talking until told to cancel.
                pendingCancel = Exec.Run("spd-say", new[] { "-C" }, 2000);
            }
            // The audio may still be draining after the cancel: keep ignoring captions for a while longer.
            if (wasSpeaking)
                suppressUntil = DateTime.Now + SUPPRESS_TAIL;
        }

        // The voices to choose from. macOS: `say -v '?'`; Linux: the voices of speech-dispatcher.
        public static List<string> GetVoices()
        {
            try
            {
                return OperatingSystem.IsMacOS()
                    ? MacVoices().Select(voice => voice.Name).ToList()
                    : LinuxVoices();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        // `Alex                en_US    # Most people recognize me by my voice.`
        private static List<(string Name, string Locale)> macVoices = new();
        private static List<(string Name, string Locale)> MacVoices()
        {
            if (macVoices.Count > 0)
                return macVoices;
            string? text = Exec.Run("say", new[] { "-v", "?" }).GetAwaiter().GetResult();
            var voices = new List<(string, string)>();
            foreach (string line in (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(line, @"^(.+?)\s+([A-Za-z]{2,3}[_-][A-Za-z0-9]+)\s+#");
                if (match.Success)
                    voices.Add((match.Groups[1].Value.Trim(), match.Groups[2].Value));
            }
            return macVoices = voices;
        }

        // `spd-say -L`: a header line, then "NAME  LANGUAGE  VARIANT" per voice.
        private static List<string> LinuxVoices()
        {
            string? text = Exec.Run("spd-say", new[] { "-L" }).GetAwaiter().GetResult();
            return (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty)
                .Where(name => name.Length > 0)
                .Distinct()
                .ToList();
        }

        private static void Worker()
        {
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
                        pendingCancel?.Wait(2000);
                    }
                    catch (Exception)
                    {
                    }
                    stopRequested = false;
                    try
                    {
                        string deviceId = Lecture?.SpeechDeviceId ?? string.Empty;
                        // macOS can send speech to another output device; the Linux tools always use the default.
                        // The default device of macOS is not known, so a chosen device counts as a different one.
                        bool chosen = OperatingSystem.IsMacOS() && !string.IsNullOrEmpty(deviceId);
                        speakingOnDefaultOutput = !chosen;
                        SpeakOnce(text, chosen ? deviceId : string.Empty);
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

        private static void SpeakOnce(string text, string deviceId)
        {
            var lecture = Lecture;
            int rate = lecture?.SpeechRate ?? 0;
            int volume = lecture?.SpeechVolume ?? 100;
            string voice = lecture?.VoiceName ?? string.Empty;
            string language = Translator.Setting?.Lecture.EnglishOnly == true
                ? "en-US"
                : Translator.Setting?.TargetLanguage ?? "zh-CN";

            ProcessStartInfo? info;
            if (OperatingSystem.IsMacOS())
                info = MacCommand(text, deviceId, voice, language, rate, volume);
            else
                info = LinuxCommand(text, voice, language, rate, volume);
            if (info == null)
                return;

            Process? process;
            lock (processLock)
            {
                if (stopRequested)
                    return;
                process = current = Process.Start(info);
            }
            if (process == null)
                return;
            using (process)
            {
                // Nobody reads the output; drain it so the tool never blocks.
                process.OutputDataReceived += (s, e) => { };
                process.ErrorDataReceived += (s, e) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                if (process.ExitCode != 0 && !stopRequested)
                    ReportFailed(process.StartInfo.FileName, process.ExitCode);
            }
            lock (processLock)
                current = null;
        }

        private static ProcessStartInfo? MacCommand(string text, string deviceId, string voice, string language,
                                                    int rate, int volume)
        {
            var arguments = new List<string>();
            string chosenVoice = voice;
            if (string.IsNullOrEmpty(chosenVoice))
            {
                // No voice chosen: pick one that speaks the target language.
                string wanted = language.Replace('-', '_');
                chosenVoice = MacVoices().FirstOrDefault(v => v.Locale.Equals(wanted, StringComparison.OrdinalIgnoreCase)).Name
                              ?? MacVoices().FirstOrDefault(v => v.Locale.StartsWith(
                                  wanted.Split('_')[0] + "_", StringComparison.OrdinalIgnoreCase)).Name
                              ?? string.Empty;
            }
            if (!string.IsNullOrEmpty(chosenVoice))
                arguments.AddRange(new[] { "-v", chosenVoice });
            // The Windows rate is -10..10 around normal speech; `say` takes words per minute (default about 175).
            arguments.AddRange(new[] { "-r", Math.Clamp(175 + rate * 15, 80, 400).ToString(CultureInfo.InvariantCulture) });
            if (!string.IsNullOrEmpty(deviceId))
                arguments.AddRange(new[] { "-a", deviceId });
            // `say` has no volume option, but embedded commands do.
            string volm = (Math.Clamp(volume, 0, 100) / 100.0).ToString("0.##", CultureInfo.InvariantCulture);
            arguments.Add("--");
            arguments.Add($"[[volm {volm}]] {text}");

            if (!Exec.Exists("say"))
                return ReportMissing("say");
            return Exec.Create("say", arguments);
        }

        private static ProcessStartInfo? LinuxCommand(string text, string voice, string language, int rate, int volume)
        {
            string shortLanguage = language.Split('-', '_')[0].ToLowerInvariant();
            if (Exec.Exists("spd-say"))
            {
                // -w waits until the speech ends; rate and volume are -100..100.
                var arguments = new List<string> { "-w", "-l", shortLanguage };
                if (!string.IsNullOrEmpty(voice))
                    arguments.AddRange(new[] { "-y", voice });
                arguments.AddRange(new[] { "-r", Math.Clamp(rate * 10, -100, 100).ToString(CultureInfo.InvariantCulture) });
                arguments.AddRange(new[] { "-i", Math.Clamp(volume * 2 - 100, -100, 100).ToString(CultureInfo.InvariantCulture) });
                arguments.Add("--");
                arguments.Add(text);
                return Exec.Create("spd-say", arguments);
            }
            string? espeak = new[] { "espeak-ng", "espeak" }.FirstOrDefault(Exec.Exists);
            if (espeak == null)
                return ReportMissing("spd-say");
            var espeakArguments = new List<string>
            {
                "-v", string.IsNullOrEmpty(voice) ? shortLanguage : voice,
                // Words per minute (default 175); amplitude 0..200.
                "-s", Math.Clamp(175 + rate * 15, 80, 450).ToString(CultureInfo.InvariantCulture),
                "-a", Math.Clamp(volume * 2, 0, 200).ToString(CultureInfo.InvariantCulture),
                "--", text,
            };
            return Exec.Create(espeak, espeakArguments);
        }

        // Once per session and tool: a speech tool that ran but failed (e.g. `say -a` with an unknown device).
        private static void ReportFailed(string fileName, int exitCode)
        {
            string tool = Path.GetFileName(fileName);
            if (failureReported == tool)
                return;
            failureReported = tool;
            SnackbarHost.Show("[ERROR] 语音播报失败。", $"{tool} 异常退出（代码 {exitCode}）。请检查所选的语音和输出设备。",
                SnackbarType.Error, timeout: 5, closeButton: true);
        }

        private static ProcessStartInfo? ReportMissing(string tool)
        {
            if (missingToolReported != tool)
            {
                missingToolReported = tool;
                string hint = tool == "say" ? "找不到 macOS 的 say 命令。"
                    : "请先安装 speech-dispatcher（spd-say）或 espeak-ng。";
                SnackbarHost.Show("[ERROR] 语音播报不可用。", hint, SnackbarType.Error, timeout: 5, closeButton: true);
            }
            return null;
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
