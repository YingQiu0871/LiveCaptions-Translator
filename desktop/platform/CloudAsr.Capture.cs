using System.Diagnostics;
using System.Text;

namespace LiveCaptionsTranslator.utils
{
    // Audio capture of CloudAsr on Linux and macOS. Both start a helper process that writes raw 16 kHz mono
    // signed 16-bit little-endian PCM to its standard output:
    //   Linux: `parec` (PulseAudio, also served by PipeWire through pipewire-pulse); the computer's output is the
    //          monitor of the default sink.
    //   macOS: `lct-audio` (desktop/macos/lct-audio.swift) next to the program: ScreenCaptureKit for the computer's
    //          output, AVAudioEngine for the microphone.
    public static partial class CloudAsr
    {
        private const int PCM_RATE = 16000;

        // Linux only: the PulseAudio source used for the microphone instead of the default one (see AudioDevices).
        public static string? MicrophoneSource { get; set; }

        private static readonly object captureLock = new();
        private static Process? helper;
        private static CancellationTokenSource? captureCts;

        private static void BeginCapture(bool useMicrophone)
        {
            lock (captureLock)
            {
                EndCapture();
                var info = OperatingSystem.IsMacOS() ? MacHelper(useMicrophone) : LinuxHelper(useMicrophone);
                if (info == null)
                    return;
                try
                {
                    var process = Process.Start(info);
                    if (process == null)
                    {
                        Report("无法启动音频采集程序。");
                        return;
                    }
                    helper = process;
                    var errors = new StringBuilder();
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            lock (errors)
                                errors.AppendLine(e.Data);
                    };
                    process.BeginErrorReadLine();
                    _ = Task.Run(() => ReadLoop(process, errors));
                }
                catch (Exception ex)
                {
                    Report($"无法启动音频采集：{ex.Message}");
                }
            }
        }

        private static void EndCapture()
        {
            lock (captureLock)
            {
                captureCts?.Cancel();
                captureCts = null;
                Exec.Kill(helper);
                helper = null;
            }
        }

        private static ProcessStartInfo? LinuxHelper(bool useMicrophone)
        {
            if (!Exec.Exists("parec"))
            {
                Report("请先安装 pulseaudio-utils（parec），Linux 上靠它采集声音。");
                return null;
            }
            string source = useMicrophone ? MicrophoneSource ?? "@DEFAULT_SOURCE@" : "@DEFAULT_MONITOR@";
            return Exec.Create("parec", new[]
            {
                "--raw", "--format=s16le", $"--rate={PCM_RATE}", "--channels=1", "--latency-msec=100", "-d", source,
            });
        }

        private static ProcessStartInfo? MacHelper(bool useMicrophone)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "lct-audio");
            if (!File.Exists(path))
            {
                Report("找不到音频采集组件 lct-audio，请重新安装本程序。");
                return null;
            }
            return Exec.Create(path, new[] { useMicrophone ? "--mic" : "--system" });
        }

        // Reads the PCM until the helper ends. If that happens while capturing, tells why.
        private static void ReadLoop(Process process, StringBuilder errors)
        {
            var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[PCM_RATE * 2 / 10];
            int carry = 0;
            try
            {
                while (true)
                {
                    int read = stream.Read(buffer, carry, buffer.Length - carry);
                    if (read <= 0)
                        break;
                    int total = carry + read;
                    int usable = total - (total % 2);
                    if (usable > 0)
                        OnAudio(buffer, usable, PCM_RATE, 1, 16, false);
                    // A read may end in the middle of a sample.
                    carry = total - usable;
                    if (carry > 0)
                        buffer[0] = buffer[usable];
                }
            }
            catch (Exception)
            {
            }

            lock (captureLock)
            {
                if (!ReferenceEquals(helper, process))
                    return;    // Stopped on purpose.
            }
            string message;
            lock (errors)
                message = errors.ToString().Trim();
            if (message.Length == 0)
                message = "音频采集意外结束。";
            Report(message);
        }

        // Shows the problem above the current sentence. It is repeated for a while, because the recognizer's own
        // connection messages would otherwise replace it at once.
        private static void Report(string message)
        {
            StatusChanged?.Invoke(message);
            SnackbarHost.Show("[ERROR] 无法采集声音。", message, SnackbarType.Error, timeout: 8, closeButton: true);
            var cts = new CancellationTokenSource();
            lock (captureLock)
            {
                captureCts?.Cancel();
                captureCts = cts;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 15; i++)
                    {
                        await Task.Delay(2000, cts.Token);
                        StatusChanged?.Invoke(message);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }
    }
}
