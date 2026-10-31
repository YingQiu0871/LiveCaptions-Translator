using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LiveCaptionsTranslator.utils
{
    // Reads summaries (and optionally translations) aloud. macOS: the `say` command, optionally to a chosen output
    // device. Linux: speech-dispatcher (`spd-say`), or `espeak-ng` if that is missing; they play on the default output.
    // The queue and the rules for what is read are shared with Windows in src/utils/Speaker.Queue.cs.
    public static partial class Speaker
    {
        private const string SPEECH_FAILED = "[ERROR] 语音播报失败。";

        private static readonly object processLock = new();
        private static Process? current;
        // The `spd-say -C` started by the last stop: the worker waits for it, or it would cancel the next speech.
        private static volatile Task? pendingCancel;
        private static string? missingToolReported;
        private static string? failureReported;
        private static List<(string Name, string Locale)> macVoices = new();

        [GeneratedRegex(@"^(.+?)\s+([A-Za-z]{2,3}[_-][A-Za-z0-9]+)\s+#")]
        private static partial Regex MacVoiceLine();

        public static void StopAll()
        {
            ClearQueue();
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

        // `say -v '?'` prints one voice per line: name, locale, then "# sample sentence".
        private static List<(string Name, string Locale)> MacVoices()
        {
            if (macVoices.Count > 0)
                return macVoices;
            string? text = Exec.Run("say", new[] { "-v", "?" }).GetAwaiter().GetResult();
            var voices = new List<(string, string)>();
            foreach (string line in (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = MacVoiceLine().Match(line);
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
                while (TryDequeue(out string text))
                {
                    try
                    {
                        pendingCancel?.Wait(2000);
                    }
                    catch (Exception)
                    {
                        // The cancel only needs to finish before the next speech starts; its failure changes nothing.
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
                        SnackbarHost.Show(SPEECH_FAILED, ex.Message, SnackbarType.Error,
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
            string language = SpeechLanguage();

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

        // The rate setting is -10..10 around normal speech; `say` and espeak-ng take words per minute (normal: 175).
        private static int WordsPerMinute(int rate, int max) => Math.Clamp(175 + rate * 15, 80, max);

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
            arguments.AddRange(new[] { "-r", WordsPerMinute(rate, 400).ToString(CultureInfo.InvariantCulture) });
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
                // Words per minute; amplitude 0..200.
                "-s", WordsPerMinute(rate, 450).ToString(CultureInfo.InvariantCulture),
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
            SnackbarHost.Show(SPEECH_FAILED, $"{tool} 异常退出（代码 {exitCode}）。请检查所选的语音和输出设备。",
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
    }
}
