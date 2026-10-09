using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // Real-time speech recognition with Alibaba Cloud Model Studio (DashScope, e.g. paraformer-realtime-v2),
    // used instead of Windows Live Captions when the user picks it: much better with technical terms.
    //
    // Audio comes from the computer's output (loopback) or the default microphone, is turned into 16 kHz mono
    // 16-bit PCM and streamed over the DashScope WebSocket API:
    //   run-task -> task-started -> binary audio ... -> result-generated (partial / sentence_end) -> finish-task.
    public static class CloudAsr
    {
        private const int SAMPLE_RATE = 16000;
        private const int CHUNK_MS = 100;

        private static readonly object audioLock = new();
        private static readonly List<byte> pending = new();
        private static IWaveIn? capture;
        private static bool isMicrophone = false;

        // Box-filter resampler state.
        private static double accumulated = 0;
        private static int accumulatedCount = 0;
        private static double phase = 0;

        // Automatic gain: a quiet lecturer (far from the microphone, low volume) is raised to a steady level,
        // otherwise the recognizer drops words and cuts sentences into fragments.
        private const double TARGET_PEAK = 0.5;
        private const double MAX_GAIN = 30;
        private const double NOISE_FLOOR = 0.0015;
        private static double envelope = 0;
        private static double gain = 1;

        private static CancellationTokenSource? cts;
        // The hot word list made from the glossary, if any.
        private static string? vocabularyId;

        // (text, isSentenceEnd)
        public static event Action<string, bool>? Recognized;
        public static event Action<string>? StatusChanged;

        public static void Start(bool useMicrophone)
        {
            Stop();
            cts = new CancellationTokenSource();
            StartCapture(useMicrophone);
            var token = cts.Token;
            _ = Task.Run(() => SessionLoop(token));
        }

        public static void Stop()
        {
            cts?.Cancel();
            cts = null;
            try
            {
                capture?.StopRecording();
                capture?.Dispose();
            }
            catch (Exception)
            {
            }
            capture = null;
            lock (audioLock)
            {
                pending.Clear();
            }
        }

        // Connects, starts a task and closes it again: checks the key, the model and the network.
        public static async Task Test()
        {
            try
            {
                await TestOnce();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("连接超时，请检查网络和接口地址。");
            }
        }

        private static async Task TestOnce()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var ws = await Connect(timeout.Token);
            string taskId = Guid.NewGuid().ToString("N");
            await SendText(ws, RunTask(taskId), timeout.Token);
            var buffer = new byte[8192];
            while (true)
            {
                var (evt, root) = await ReceiveEvent(ws, buffer, timeout.Token);
                if (evt == "task-started")
                    break;
                if (evt == "task-failed")
                    throw new InvalidOperationException(ErrorMessage(root));
            }
            await SendText(ws, FinishTask(taskId), timeout.Token);
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeout.Token);
            }
            catch (Exception)
            {
            }
        }

        private static void StartCapture(bool useMicrophone)
        {
            accumulated = 0;
            accumulatedCount = 0;
            phase = 0;
            envelope = 0;
            gain = 1;
            isMicrophone = useMicrophone;
            capture = useMicrophone ? new WasapiCapture() : new WasapiLoopbackCapture();
            var format = capture.WaveFormat;
            capture.DataAvailable += (s, e) => OnAudio(e.Buffer, e.BytesRecorded, format);
            capture.StartRecording();
        }

        // Mixes to mono, resamples to 16 kHz and stores 16-bit samples for the sender.
        private static void OnAudio(byte[] buffer, int count, WaveFormat format)
        {
            int channels = Math.Max(format.Channels, 1);
            int bytesPerSample = format.BitsPerSample / 8;
            int blockAlign = bytesPerSample * channels;
            if (bytesPerSample == 0 || blockAlign == 0)
                return;
            bool isFloat = format.BitsPerSample == 32 && format.Encoding != WaveFormatEncoding.Pcm;
            double step = (double)format.SampleRate / SAMPLE_RATE;

            // Our own speech on the computer's output would be recognized again as the lecture: send silence then.
            bool mute = !isMicrophone && Speaker.SuppressCaptions;
            var output = new List<byte>(count / blockAlign / Math.Max((int)step, 1) * 2 + 4);
            for (int offset = 0; offset + blockAlign <= count; offset += blockAlign)
            {
                double sum = 0;
                for (int ch = 0; ch < channels; ch++)
                    sum += ReadSample(buffer, offset + ch * bytesPerSample, format.BitsPerSample, isFloat);
                accumulated += sum / channels;
                accumulatedCount++;
                phase += 1;
                if (phase < step)
                    continue;
                phase -= step;
                double value = mute ? 0 : AutoGain(accumulated / accumulatedCount);
                accumulated = 0;
                accumulatedCount = 0;
                short pcm = (short)Math.Round(value * short.MaxValue);
                output.Add((byte)(pcm & 0xFF));
                output.Add((byte)((pcm >> 8) & 0xFF));
            }
            lock (audioLock)
            {
                pending.AddRange(output);
                // Never keep more than 10 s if the connection is down.
                int max = SAMPLE_RATE * 2 * 10;
                if (pending.Count > max)
                    pending.RemoveRange(0, pending.Count - max);
            }
        }

        private static double AutoGain(double x)
        {
            double level = Math.Abs(x);
            // Peak follower: rises at once, falls over about a second.
            envelope = level > envelope ? level : envelope * 0.99995;
            // Below the noise floor nobody is talking: keep the gain instead of blowing up the background noise.
            if (envelope > NOISE_FLOOR)
            {
                double desired = Math.Min(TARGET_PEAK / envelope, MAX_GAIN);
                // Turn down quickly, turn up slowly.
                gain += (desired - gain) * (desired < gain ? 0.01 : 0.0002);
            }
            double y = x * gain;
            // Soft limiter for the peaks the gain hasn't caught yet.
            double magnitude = Math.Abs(y);
            if (magnitude > 0.8)
                y = Math.Sign(y) * (0.8 + 0.2 * Math.Tanh((magnitude - 0.8) / 0.2));
            return y;
        }

        private static double ReadSample(byte[] buffer, int offset, int bits, bool isFloat)
        {
            switch (bits)
            {
                case 16:
                    return BitConverter.ToInt16(buffer, offset) / 32768.0;
                case 24:
                    int value24 = buffer[offset] | (buffer[offset + 1] << 8) | ((sbyte)buffer[offset + 2] << 16);
                    return value24 / 8388608.0;
                case 32:
                    return isFloat
                        ? BitConverter.ToSingle(buffer, offset)
                        : BitConverter.ToInt32(buffer, offset) / 2147483648.0;
                default:
                    return 0;
            }
        }

        private static byte[] TakeChunk()
        {
            int chunkBytes = SAMPLE_RATE * 2 * CHUNK_MS / 1000;
            lock (audioLock)
            {
                if (pending.Count == 0)
                    // Silence keeps the task alive while nothing plays (loopback delivers no data then).
                    return new byte[chunkBytes];
                // Audio buffered while connecting is sent at up to three times real time, not in one block.
                int count = Math.Min(pending.Count, 3 * chunkBytes);
                count -= count % 2;
                if (count == 0)
                    return new byte[chunkBytes];
                var chunk = pending.GetRange(0, count).ToArray();
                pending.RemoveRange(0, count);
                return chunk;
            }
        }

        // Keeps one recognition task running for as long as the class runs, reconnecting when it drops.
        private static async Task SessionLoop(CancellationToken token)
        {
            await SyncVocabulary(token);
            int failures = 0;
            bool firstSession = true;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    StatusChanged?.Invoke("正在连接阿里云语音识别……");
                    bool keepBuffered = firstSession;
                    firstSession = false;
                    await RunSession(token, keepBuffered);
                    failures = 0;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    failures++;
                    // A hot word list that was deleted or made for another model makes the task fail.
                    if (vocabularyId != null && ex.Message.Contains("vocabulary", StringComparison.OrdinalIgnoreCase))
                    {
                        vocabularyId = null;
                        Glossary.RecheckAsrVocabulary();
                        SnackbarHost.Show("[WARNING] 术语表热词没有生效。", "这节课先不用热词继续识别，下次开始时会重新上传。",
                            SnackbarType.Warning, timeout: 5, closeButton: true);
                    }
                    StatusChanged?.Invoke($"阿里云语音识别出错：{ex.Message}" +
                                          (failures > 1 ? $"（第 {failures} 次重试）" : "，正在重连……"));
                }
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * Math.Max(failures, 1), 20)), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static async Task SyncVocabulary(CancellationToken token)
        {
            vocabularyId = null;
            try
            {
                if (Glossary.Current.Count == 0)
                    return;
                StatusChanged?.Invoke("正在上传术语表热词……");
                vocabularyId = await Glossary.SyncAsrVocabulary(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[WARNING] 术语表热词上传失败，这节课先不用热词。", ex.Message,
                    SnackbarType.Warning, timeout: 5, closeButton: true);
            }
        }

        // keepBuffered: send what was said while the hot words were uploaded and the connection was made (first
        // connection of a class); after a reconnect the backlog is dropped instead.
        private static async Task RunSession(CancellationToken token, bool keepBuffered)
        {
            using var ws = await Connect(token);
            string taskId = Guid.NewGuid().ToString("N");
            await SendText(ws, RunTask(taskId), token);

            var started = new TaskCompletionSource();
            var receiver = Task.Run(async () =>
            {
                var buffer = new byte[8192];
                while (ws.State == WebSocketState.Open)
                {
                    var (evt, root) = await ReceiveEvent(ws, buffer, token);
                    switch (evt)
                    {
                        case "task-started":
                            started.TrySetResult();
                            StatusChanged?.Invoke(string.Empty);
                            break;
                        case "result-generated":
                            OnResult(root);
                            break;
                        case "task-failed":
                            throw new InvalidOperationException(ErrorMessage(root));
                        case "task-finished":
                            return;
                    }
                }
            }, token);

            var first = await Task.WhenAny(started.Task, receiver, Task.Delay(TimeSpan.FromSeconds(10), token));
            if (first == receiver)
                await receiver;    // Surfaces the error.
            if (first != started.Task)
                throw new TimeoutException("连接超时。");

            if (!keepBuffered)
            {
                lock (audioLock)
                {
                    pending.Clear();
                }
            }
            while (!token.IsCancellationRequested && !receiver.IsCompleted)
            {
                await Task.Delay(CHUNK_MS, token);
                var chunk = TakeChunk();
                await ws.SendAsync(new ArraySegment<byte>(chunk), WebSocketMessageType.Binary, true, token);
            }
            if (receiver.IsCompleted)
                await receiver;    // Surfaces the error, or the task ended.
        }

        private static void OnResult(JsonElement root)
        {
            if (!root.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("output", out var output) ||
                !output.TryGetProperty("sentence", out var sentence))
                return;
            if (sentence.TryGetProperty("heartbeat", out var heartbeat) && heartbeat.ValueKind == JsonValueKind.True)
                return;
            string text = sentence.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            bool end = sentence.TryGetProperty("sentence_end", out var e) && e.ValueKind == JsonValueKind.True;
            if (!string.IsNullOrWhiteSpace(text))
                Recognized?.Invoke(text.Trim(), end);
        }

        private static async Task<ClientWebSocket> Connect(CancellationToken token)
        {
            var lecture = Translator.Setting.Lecture;
            if (string.IsNullOrWhiteSpace(lecture.AsrApiKey))
                throw new InvalidOperationException("还没有填阿里云 API Key。");
            var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", $"bearer {lecture.AsrApiKey.Trim()}");
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            try
            {
                await ws.ConnectAsync(new Uri(lecture.AsrEndpoint), token);
            }
            catch (WebSocketException ex)
            {
                ws.Dispose();
                string message = ex.Message;
                if (message.Contains("401") || message.Contains("403"))
                    message = "API Key 无效或没有开通实时语音识别（HTTP 401/403）。";
                throw new InvalidOperationException(message);
            }
            return ws;
        }

        private static string RunTask(string taskId)
        {
            var lecture = Translator.Setting.Lecture;
            var parameters = new Dictionary<string, object>
            {
                ["format"] = "pcm",
                ["sample_rate"] = SAMPLE_RATE,
                ["heartbeat"] = true,
                // Keep every word: removing "um"/"uh" also removed real words now and then.
                ["disfluency_removal_enabled"] = false,
                // Split sentences by meaning rather than by short pauses: a lecturer pauses mid-sentence a lot,
                // which turned one sentence into fragments like "I." and "And you, I'm.".
                ["semantic_punctuation_enabled"] = true,
                // Used when the model splits by pauses: wait longer before ending a sentence (default 800 ms).
                ["max_sentence_silence"] = 1300,
                ["punctuation_prediction_enabled"] = true,
            };
            if (!string.IsNullOrWhiteSpace(lecture.AsrLanguage))
                parameters["language_hints"] = new[] { lecture.AsrLanguage };
            if (!string.IsNullOrEmpty(vocabularyId))
                parameters["vocabulary_id"] = vocabularyId;

            return JsonSerializer.Serialize(new
            {
                header = new { action = "run-task", task_id = taskId, streaming = "duplex" },
                payload = new
                {
                    task_group = "audio",
                    task = "asr",
                    function = "recognition",
                    model = lecture.AsrModel,
                    parameters,
                    input = new { },
                },
            });
        }

        private static string FinishTask(string taskId) => JsonSerializer.Serialize(new
        {
            header = new { action = "finish-task", task_id = taskId, streaming = "duplex" },
            payload = new { input = new { } },
        });

        private static Task SendText(ClientWebSocket ws, string text, CancellationToken token) =>
            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, token);

        private static async Task<(string Event, JsonElement Root)> ReceiveEvent(
            ClientWebSocket ws, byte[] buffer, CancellationToken token)
        {
            while (true)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new InvalidOperationException(
                            $"服务器断开了连接{(string.IsNullOrEmpty(ws.CloseStatusDescription) ? "" : "：" + ws.CloseStatusDescription)}");
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;
                using var doc = JsonDocument.Parse(message.ToArray());
                var root = doc.RootElement.Clone();
                string evt = root.TryGetProperty("header", out var header) &&
                             header.TryGetProperty("event", out var e)
                    ? e.GetString() ?? string.Empty
                    : string.Empty;
                return (evt, root);
            }
        }

        private static string ErrorMessage(JsonElement root)
        {
            if (!root.TryGetProperty("header", out var header))
                return "识别任务失败。";
            string code = header.TryGetProperty("error_code", out var c) ? c.GetString() ?? "" : "";
            string message = header.TryGetProperty("error_message", out var m) ? m.GetString() ?? "" : "";
            return $"识别任务失败：{code} {message}".Trim();
        }
    }
}
