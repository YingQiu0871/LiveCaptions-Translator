using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // Real-time speech recognition with Alibaba Cloud Model Studio (DashScope, e.g. paraformer-realtime-v2),
    // used instead of Windows Live Captions when the user picks it: much better with technical terms.
    //
    // Audio comes from the computer's output (loopback) or the default microphone, is turned into 16 kHz mono
    // 16-bit PCM and streamed over the DashScope WebSocket API:
    //   run-task -> task-started -> binary audio ... -> result-generated (partial / sentence_end) -> finish-task.
    //
    // This file is the protocol and the signal processing, shared by every platform. Capturing the audio is the
    // platform part (CloudAsr.Capture.Windows.cs with WASAPI; desktop/platform/CloudAsr.Capture.cs elsewhere):
    // it implements `BeginCapture` / `EndCapture` and feeds `OnAudio`.
    public static partial class CloudAsr
    {
        private const int SAMPLE_RATE = 16000;
        private const int CHUNK_MS = 100;

        private static readonly object audioLock = new();
        private static readonly List<byte> pending = new();
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

        // Realtime models with the same run-task protocol, in the order they are tried when one cannot be used.
        // The snapshot is only offered for the three languages it supports.
        private const string FUN_ASR_ALIAS = "fun-asr-realtime";
        private static readonly string[] FALLBACK_MODELS =
            { "paraformer-realtime-v2", FUN_ASR_ALIAS, "fun-asr-realtime-2026-02-28" };
        // Models that cannot be used (free quota gone, or refused), remembered until the app is closed.
        // Guarded by `skipLock`: a cancelled old class and the new one may touch it at the same time.
        private static readonly object skipLock = new();
        private static readonly HashSet<string> skippedModels = new(StringComparer.OrdinalIgnoreCase);
        // The subset whose free quota is gone, for the message.
        private static readonly List<string> quotaModels = new();
        private static string lastQuotaCode = string.Empty;

        // What one class (one SessionLoop) knows; passed around instead of statics, so that a cancelled old loop
        // cannot apply the state of the new one.
        private sealed class SessionState
        {
            // The model in the settings when the class started.
            public string Configured = string.Empty;
            public string Model = string.Empty;
            // The hot word list made for `Model`, if any.
            public string? VocabularyId;
            // A fallback happened in this class and has not been announced yet.
            public bool FellBack;
            // The current task has been started by the server.
            public volatile bool Started;
        }

        // The task failed with an error code of the service (as opposed to a network or protocol problem).
        private sealed class AsrTaskException : InvalidOperationException
        {
            public string Code { get; }
            public AsrTaskException(string code, string message) : base(message) => Code = code;
        }

        // The model's free quota is gone, or the account has no balance ("use free tier only" in the console).
        internal static bool IsQuotaError(string? code) =>
            !string.IsNullOrEmpty(code) &&
            (code.StartsWith("AllocationQuota", StringComparison.OrdinalIgnoreCase) || IsArrears(code));

        // Another model would not help: the whole account is out of balance.
        internal static bool IsArrears(string? code) =>
            !string.IsNullOrEmpty(code) && code.StartsWith("Arrearage", StringComparison.OrdinalIgnoreCase);

        internal static string QuotaMessage(string? code, IEnumerable<string>? models = null)
        {
            if (IsArrears(code))
                return "阿里云账号欠费或没有可用余额，请到百炼控制台充值后再试。";
            var names = models?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            if (names.Count == 0)
                names.Add("paraformer-realtime-v2");
            return "阿里云百炼的实时语音识别免费额度已用完。请在百炼控制台「费用用量 → 免费额度」里，点「额度用尽或过期模型」，" +
                   $"找到 {string.Join("、", names)}，关掉「用完即停」；或给账号充值后改为按量付费。";
        }

        internal static bool IsFunAsr(string model) =>
            model.StartsWith("fun-asr", StringComparison.OrdinalIgnoreCase);

        // A dated Fun-ASR version rather than the alias.
        private static bool IsFunAsrSnapshot(string model) =>
            IsFunAsr(model) && !string.Equals(model, FUN_ASR_ALIAS, StringComparison.OrdinalIgnoreCase);

        // The configured model first, then the fallback list.
        internal static List<string> CandidateModels(string configured, string? language)
        {
            string lang = (language ?? string.Empty).Split('-')[0].ToLowerInvariant();
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured))
                result.Add(configured.Trim());
            foreach (string model in FALLBACK_MODELS)
            {
                if (IsFunAsrSnapshot(model) && lang != "zh" && lang != "en" && lang != "ja")
                    continue;
                if (!result.Contains(model, StringComparer.OrdinalIgnoreCase))
                    result.Add(model);
            }
            return result;
        }

        internal static string? NextModel(string configured, string? language, ISet<string> skipped) =>
            CandidateModels(configured, language).FirstOrDefault(model => !skipped.Contains(model));

        private static HashSet<string> SkippedSnapshot()
        {
            lock (skipLock)
            {
                return new HashSet<string>(skippedModels, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void Skip(string model, string? quotaCode)
        {
            lock (skipLock)
            {
                skippedModels.Add(model);
                if (quotaCode != null)
                {
                    lastQuotaCode = quotaCode;
                    if (!quotaModels.Contains(model, StringComparer.OrdinalIgnoreCase))
                        quotaModels.Add(model);
                }
            }
        }

        private static void Unskip(string model)
        {
            lock (skipLock)
            {
                skippedModels.Remove(model);
                quotaModels.RemoveAll(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));
            }
        }

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
            EndCapture();
            lock (audioLock)
            {
                pending.Clear();
            }
        }

        // Connects, starts a task and closes it again: checks the key, the model and the network.
        // When the model's quota is gone, the other models are tried and the working one is saved; the returned
        // text then says so (null when the configured model works).
        public static async Task<string?> Test()
        {
            var lecture = Translator.Setting.Lecture;
            string configured = lecture.AsrModel;
            string code = string.Empty;
            var exhausted = new List<string>();
            foreach (string model in CandidateModels(configured, lecture.AsrLanguage))
            {
                bool isConfigured = string.Equals(model, configured, StringComparison.OrdinalIgnoreCase);
                try
                {
                    await TestOnce(model);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("连接超时，请检查网络和接口地址。");
                }
                catch (AsrTaskException ex) when (IsArrears(ex.Code))
                {
                    // The whole account: no model would work.
                    throw;
                }
                catch (AsrTaskException ex) when (IsQuotaError(ex.Code))
                {
                    Skip(model, ex.Code);
                    code = ex.Code;
                    exhausted.Add(model);
                    continue;
                }
                catch (AsrTaskException) when (!isConfigured)
                {
                    // Not available to this account (or not for this language): try the next one.
                    Skip(model, null);
                    continue;
                }
                Unskip(model);
                if (isConfigured)
                    return null;
                lecture.AsrModel = model;
                return $"{configured} 额度已用完，已改用 {model}";
            }
            throw new InvalidOperationException(QuotaMessage(code, exhausted));
        }

        private static async Task TestOnce(string model)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var ws = await Connect(timeout.Token);
            string taskId = Guid.NewGuid().ToString("N");
            await SendText(ws, RunTask(taskId, model, null), timeout.Token);
            var buffer = new byte[8192];
            while (true)
            {
                var (evt, root) = await ReceiveEvent(ws, buffer, timeout.Token);
                if (evt == "task-started")
                    break;
                if (evt == "task-failed")
                    throw TaskFailed(root);
            }
            await SendText(ws, FinishTask(taskId), timeout.Token);
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeout.Token);
            }
            catch (Exception)
            {
                // Best effort: the server may already have closed the connection.
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
            BeginCapture(useMicrophone);
        }

        // Mixes to mono, resamples to 16 kHz and stores 16-bit samples for the sender.
        // `buffer` holds interleaved samples of the given format, `count` bytes of it.
        private static void OnAudio(byte[] buffer, int count, int sampleRate, int channels, int bits, bool isFloat)
        {
            channels = Math.Max(channels, 1);
            int bytesPerSample = bits / 8;
            int blockAlign = bytesPerSample * channels;
            if (bytesPerSample == 0 || blockAlign == 0 || sampleRate <= 0)
                return;
            double step = (double)sampleRate / SAMPLE_RATE;

            // Our own speech on the computer's output would be recognized again as the lecture: send silence then.
            bool mute = !isMicrophone && Speaker.SuppressCaptions;
            var output = new List<byte>(count / blockAlign / Math.Max((int)step, 1) * 2 + 4);
            for (int offset = 0; offset + blockAlign <= count; offset += blockAlign)
            {
                double sum = 0;
                for (int ch = 0; ch < channels; ch++)
                    sum += ReadSample(buffer, offset + ch * bytesPerSample, bits, isFloat);
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
            var lecture = Translator.Setting.Lecture;
            var state = new SessionState { Configured = lecture.AsrModel };
            string? language = lecture.AsrLanguage;
            lock (skipLock)
            {
                // Everything was used up in an earlier class: the user may have fixed it in the console since, so
                // try again instead of refusing for ever.
                if (CandidateModels(state.Configured, language).All(skippedModels.Contains))
                {
                    skippedModels.Clear();
                    quotaModels.Clear();
                    lastQuotaCode = string.Empty;
                }
            }
            // A model found unusable in an earlier class is skipped at once.
            state.Model = NextModel(state.Configured, language, SkippedSnapshot()) ?? state.Configured;
            state.FellBack = !string.Equals(state.Model, state.Configured, StringComparison.OrdinalIgnoreCase);
            await SyncVocabulary(state, token);
            int failures = 0;
            bool firstSession = true;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    StatusChanged?.Invoke("正在连接阿里云语音识别……");
                    bool keepBuffered = firstSession;
                    firstSession = false;
                    state.Started = false;
                    await RunSession(state, token, keepBuffered);
                    failures = 0;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                        return;
                    if (ex is AsrTaskException { } failed)
                    {
                        if (IsArrears(failed.Code))
                        {
                            // The whole account: retrying or another model cannot help.
                            lock (skipLock)
                            {
                                lastQuotaCode = failed.Code;
                            }
                            ReportQuotaGone(failed.Code);
                            return;
                        }
                        bool quota = IsQuotaError(failed.Code);
                        bool unusable = !quota && !state.Started &&
                                        !string.Equals(state.Model, state.Configured, StringComparison.OrdinalIgnoreCase);
                        if (quota || unusable)
                        {
                            string failedModel = state.Model;
                            Skip(failedModel, quota ? failed.Code : null);
                            string? next = NextModel(state.Configured, language, SkippedSnapshot());
                            if (next == null)
                            {
                                // Retrying cannot help: stop here instead of reconnecting forever.
                                bool anyQuota;
                                lock (skipLock)
                                {
                                    anyQuota = quotaModels.Count > 0;
                                }
                                if (anyQuota)
                                    ReportQuotaGone(failed.Code);
                                else
                                    StatusChanged?.Invoke($"阿里云语音识别出错：{failed.Message}");
                                return;
                            }
                            state.Model = next;
                            state.FellBack = true;
                            firstSession = true;
                            failures = 0;
                            StatusChanged?.Invoke(quota
                                ? $"{failedModel} 免费额度已用完，正在改用 {next}……"
                                : $"{failedModel} 无法使用，正在改用 {next}……");
                            await SyncVocabulary(state, token);
                            continue;
                        }
                    }
                    failures++;
                    // A hot word list that was deleted or made for another model makes the task fail.
                    if (state.VocabularyId != null && ex.Message.Contains("vocabulary", StringComparison.OrdinalIgnoreCase))
                    {
                        state.VocabularyId = null;
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

        private static void ReportQuotaGone(string code)
        {
            string[] models;
            lock (skipLock)
            {
                models = quotaModels.ToArray();
            }
            string message = QuotaMessage(code, models);
            StatusChanged?.Invoke(message);
            SnackbarHost.Show("[ERROR] 语音识别无法继续。", message, SnackbarType.Error, timeout: 15, closeButton: true);
        }

        // The model works after a fallback: keep it for the next class and say so once.
        private static void KeepSessionModel(SessionState state, CancellationToken token)
        {
            if (!state.FellBack)
                return;
            state.FellBack = false;
            var lecture = Translator.Setting.Lecture;
            // A cancelled class, or a model the user has changed in the meantime, is left alone.
            if (token.IsCancellationRequested ||
                !string.Equals(lecture.AsrModel, state.Configured, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state.Model, state.Configured, StringComparison.OrdinalIgnoreCase))
                return;
            lecture.AsrModel = state.Model;
            SnackbarHost.Show($"{state.Configured} 的免费额度已用完，已自动改用 {state.Model}。",
                "下节课会直接使用它。", SnackbarType.Warning, timeout: 8, closeButton: true);
        }

        private static async Task SyncVocabulary(SessionState state, CancellationToken token)
        {
            state.VocabularyId = null;
            try
            {
                if (Glossary.Current.Count == 0)
                    return;
                StatusChanged?.Invoke("正在上传术语表热词……");
                state.VocabularyId = await Glossary.SyncAsrVocabulary(state.Model, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Cancelled: the session was stopped, so there is nothing to report.
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[WARNING] 术语表热词上传失败，这节课先不用热词。", ex.Message,
                    SnackbarType.Warning, timeout: 5, closeButton: true);
            }
        }

        // keepBuffered: send what was said while the hot words were uploaded and the connection was made (first
        // connection of a class); after a reconnect the backlog is dropped instead.
        private static async Task RunSession(SessionState state, CancellationToken token, bool keepBuffered)
        {
            using var ws = await Connect(token);
            string taskId = Guid.NewGuid().ToString("N");
            await SendText(ws, RunTask(taskId, state.Model, state.VocabularyId), token);

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
                            state.Started = true;
                            started.TrySetResult();
                            Unskip(state.Model);
                            KeepSessionModel(state, token);
                            StatusChanged?.Invoke(string.Empty);
                            break;
                        case "result-generated":
                            OnResult(root);
                            break;
                        case "task-failed":
                            throw TaskFailed(root);
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

        private static string RunTask(string taskId, string model, string? vocabulary)
        {
            var lecture = Translator.Setting.Lecture;
            var parameters = new Dictionary<string, object>
            {
                ["format"] = "pcm",
                ["sample_rate"] = SAMPLE_RATE,
                ["heartbeat"] = true,
                // Used when the model splits by pauses: wait longer before ending a sentence (default 800 ms).
                ["max_sentence_silence"] = 1300,
                ["punctuation_prediction_enabled"] = true,
            };
            bool funAsr = IsFunAsr(model);
            if (!funAsr)
            {
                // Keep every word: disfluency removal also drops real words now and then.
                parameters["disfluency_removal_enabled"] = false;
                // Split sentences by meaning rather than by short pauses: a lecturer pauses mid-sentence a lot,
                // and pause-based splitting turns one sentence into fragments like "I." and "And you, I'm.".
                // Fun-ASR does not document either parameter.
                parameters["semantic_punctuation_enabled"] = true;
            }
            string language = (lecture.AsrLanguage ?? string.Empty).Trim();
            string baseLanguage = language.Split('-')[0].ToLowerInvariant();
            bool detect = language.Length == 0 || baseLanguage == "auto";
            // The Fun-ASR alias takes about thirty languages (not Cantonese), its dated snapshot only three.
            bool hint = !detect && (!funAsr || (IsFunAsrSnapshot(model)
                ? baseLanguage is "zh" or "en" or "ja"
                : baseLanguage != "yue"));
            if (hint)
                parameters["language_hints"] = new[] { language };
            if (!string.IsNullOrEmpty(vocabulary))
                parameters["vocabulary_id"] = vocabulary;

            return JsonSerializer.Serialize(new
            {
                header = new { action = "run-task", task_id = taskId, streaming = "duplex" },
                payload = new
                {
                    task_group = "audio",
                    task = "asr",
                    function = "recognition",
                    model,
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

        private static InvalidOperationException TaskFailed(JsonElement root)
        {
            if (!root.TryGetProperty("header", out var header))
                return new InvalidOperationException("识别任务失败。");
            string code = header.TryGetProperty("error_code", out var c) ? c.GetString() ?? "" : "";
            string message = header.TryGetProperty("error_message", out var m) ? m.GetString() ?? "" : "";
            // The service explains a used-up quota in English only.
            if (IsQuotaError(code))
                return new AsrTaskException(code, QuotaMessage(code));
            return new AsrTaskException(code, $"识别任务失败：{code} {message}".Trim());
        }
    }
}
