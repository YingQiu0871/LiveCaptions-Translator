using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // Splits the logged captions into sections and asks the LLM configured under "OpenAI" (e.g. DeepSeek)
    // for a short summary of each section.
    //
    // How sections are cut, in order of preference:
    //   1. Slides loaded: the LLM tracks which PDF page the lecturer is on, a new page starts a new section.
    //   2. No slides: the LLM detects a clear change of topic in the transcript.
    //   3. Fallback: by time, when boundary detection keeps failing, or a section grows too long.
    // The user can also end a section at any time (button or Ctrl+Alt+S).
    public static partial class Summarizer
    {
        public const string SUMMARY_API = "OpenAI";

        // A section shorter than this is merged into the next one instead of being summarized on its own.
        private const int MIN_SECTION_CHARS = 80;
        private const int LOOP_INTERVAL_MS = 2000;
        // How often the open section is checked for a boundary, and how much new speech is needed for a check.
        private static readonly TimeSpan BOUNDARY_CHECK_INTERVAL = TimeSpan.FromSeconds(40);
        private const int MIN_NEW_LINES_FOR_CHECK = 2;
        // After this many failed boundary checks in a row, sections fall back to fixed time.
        private const int MAX_DETECTION_FAILURES = 3;

        private static readonly HttpClient client = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(40)
        };

        private static long lastHistoryId = -1;
        private static volatile bool endSectionRequested = false;

        private static DateTime lastBoundaryCheck = DateTime.MinValue;
        private static long lastCheckedHistoryId = 0;
        private static int detectionFailures = 0;

        public static event Action<SectionEntry>? SectionSummarized;
        public static event Action<int?>? CurrentPageChanged;

        // The slide page the lecturer is currently believed to be on (null if unknown or no slides).
        public static int? CurrentPage { get; private set; }

        [GeneratedRegex(@"\{[\s\S]*\}")]
        private static partial Regex JsonObject();

        static Summarizer()
        {
            SlideDeck.Changed += () =>
            {
                SetCurrentPage(null);
                detectionFailures = 0;
            };
        }

        public static void RequestEndSection()
        {
            endSectionRequested = true;
        }

        // Called after the translation history is cleared, as history ids restart from 1.
        public static void ResetCursor()
        {
            lastHistoryId = 0;
            lastCheckedHistoryId = 0;
        }

        public static async Task SummaryLoop()
        {
            // Do not summarize what was logged before this run.
            while (lastHistoryId < 0)
            {
                try
                {
                    lastHistoryId = await SectionLogger.GetMaxHistoryId();
                }
                catch (Exception)
                {
                    await Task.Delay(LOOP_INTERVAL_MS);
                }
            }

            while (true)
            {
                try
                {
                    await Tick();
                }
                catch (Exception ex)
                {
                    SnackbarHost.Show("[ERROR] 小节总结失败。", ex.Message, SnackbarType.Error,
                        timeout: 3, closeButton: true);
                }
                await Task.Delay(LOOP_INTERVAL_MS);
            }
        }

        private static async Task Tick()
        {
            var lecture = Translator.Setting?.Lecture;
            if (lecture == null)
                return;

            bool manual = endSectionRequested;
            if (!manual && !lecture.SummaryEnabled)
                return;

            var lines = (await SectionLogger.LoadHistoryRange(lastHistoryId))
                .Where(line => !string.IsNullOrWhiteSpace(line.SourceText))
                .ToList();

            if (manual)
            {
                endSectionRequested = false;
                if (lines.Count == 0)
                    SnackbarHost.Show("本节还没有新的字幕。", "", SnackbarType.Warning, timeout: 2, closeButton: true);
                else
                    await CloseSection(lines, CurrentPage, lecture);
                return;
            }

            if (lines.Count == 0 || lines.Sum(line => line.SourceText.Length) < MIN_SECTION_CHARS)
                return;

            var elapsed = DateTime.Now - lines[0].Time;
            bool fixedTime = lecture.SegmentMode == SegmentMode.FixedTime ||
                             detectionFailures >= MAX_DETECTION_FAILURES;

            if (fixedTime)
            {
                if (elapsed >= TimeSpan.FromMinutes(lecture.SummaryIntervalMinutes))
                    await CloseSection(lines, CurrentPage, lecture);
                // Retry the detection from time to time.
                if (detectionFailures >= MAX_DETECTION_FAILURES && DateTime.Now - lastBoundaryCheck > TimeSpan.FromMinutes(5))
                    detectionFailures = MAX_DETECTION_FAILURES - 1;
                return;
            }

            // Too long without a detected boundary: close it anyway.
            if (elapsed >= TimeSpan.FromMinutes(lecture.MaxSectionMinutes))
            {
                await CloseSection(lines, CurrentPage, lecture);
                return;
            }

            int newLines = lines.Count(line => line.Id > lastCheckedHistoryId);
            if (DateTime.Now - lastBoundaryCheck < BOUNDARY_CHECK_INTERVAL || newLines < MIN_NEW_LINES_FOR_CHECK)
                return;

            lastBoundaryCheck = DateTime.Now;
            lastCheckedHistoryId = lines[^1].Id;

            Boundary boundary;
            try
            {
                boundary = await DetectBoundary(lines);
                detectionFailures = 0;
            }
            catch (Exception ex)
            {
                detectionFailures++;
                if (detectionFailures >= MAX_DETECTION_FAILURES)
                    SnackbarHost.Show("[WARNING] 无法自动识别分段，暂时改为按时间分节。", ex.Message,
                        SnackbarType.Warning, timeout: 3, closeButton: true);
                return;
            }

            if (boundary.LastLineOfFinishedSection is int last && last >= 1)
            {
                last = Math.Min(last, lines.Count);
                var finished = lines.Take(last).ToList();
                // Too little to stand on its own: keep it with the next section.
                if (finished.Sum(line => line.SourceText.Length) >= MIN_SECTION_CHARS)
                    await CloseSection(finished, boundary.FinishedPage ?? CurrentPage, lecture);
            }
            if (boundary.CurrentPage != null && SlideDeck.GetPage(boundary.CurrentPage) != null)
                SetCurrentPage(boundary.CurrentPage);
        }

        private static void SetCurrentPage(int? page)
        {
            if (CurrentPage == page)
                return;
            CurrentPage = page;
            CurrentPageChanged?.Invoke(page);
        }

        private record Boundary(int? LastLineOfFinishedSection, int? FinishedPage, int? CurrentPage);

        private static async Task<Boundary> DetectBoundary(List<HistoryLine> lines)
        {
            var system = new StringBuilder();
            system.AppendLine(
                "You segment the live transcript of a lecture into sections. " +
                "The transcript comes from speech recognition and may contain errors. " +
                "The lines below are the current open section, numbered from 1.");
            if (SlideDeck.IsLoaded)
            {
                system.AppendLine(
                    "The lecturer is presenting the slides listed below. A section corresponds to one slide page " +
                    "(or one bullet point when a page is discussed for a long time). " +
                    "Decide which page the lecturer is on, and whether they moved to another page within these lines.");
                system.AppendLine(CurrentPage == null
                    ? "The current page is unknown."
                    : $"Before these lines, the lecturer was believed to be on page {CurrentPage}.");
                system.AppendLine("Slides:");
                system.AppendLine(SlideDeck.Outline(CurrentPage));
            }
            else
            {
                system.AppendLine(
                    "Decide whether the lecturer clearly moved on to a new topic or point within these lines. " +
                    "Do not split on small digressions, examples or questions about the same topic.");
            }
            system.AppendLine(
                "Reply with a json object only: " +
                "{\"boundary\": <number of the LAST line of the finished section, or null if all lines are still the same section>, " +
                "\"finished_page\": <page number of the finished section, or null>, " +
                "\"current_page\": <page number the lecturer is on at the last line, or null>}. " +
                "Page numbers must be null when there are no slides.");

            var transcript = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
                transcript.AppendLine($"{i + 1}. {lines[i].SourceText}");

            string output = await Chat(system.ToString(), transcript.ToString(), maxTokens: 80, temperature: 0, json: true);
            var match = JsonObject().Match(output);
            if (!match.Success)
                throw new FormatException($"Unexpected reply: {output}");

            using var doc = JsonDocument.Parse(match.Value);
            int? Read(string name) =>
                doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                    ? value.GetInt32() : null;
            return new Boundary(Read("boundary"), Read("finished_page"), Read("current_page"));
        }

        private static async Task CloseSection(List<HistoryLine> lines, int? page, LectureState lecture)
        {
            string summary;
            try
            {
                summary = await Summarize(lines, page, lecture);
            }
            catch (Exception ex)
            {
                summary = $"[Summary failed] {ex.Message}";
            }

            var section = await SectionLogger.AddSection(
                lines[0].Time, lines[^1].Time, lines[0].Id, lines[^1].Id, summary, page);
            lastHistoryId = lines[^1].Id;
            lastBoundaryCheck = DateTime.Now;

            SectionSummarized?.Invoke(section);
            if (!summary.StartsWith("[Summary failed]"))
                Speaker.EnqueueSummary(summary);
            else
                SnackbarHost.Show("[ERROR] 小节总结失败。", summary, SnackbarType.Error,
                    timeout: 3, closeButton: true);
        }

        private static async Task<string> Summarize(List<HistoryLine> lines, int? page, LectureState lecture)
        {
            string targetLanguage = Translator.Setting.TargetLanguage;
            string language = OpenAIConfig.SupportedLanguages.TryGetValue(targetLanguage, out var name)
                ? name : targetLanguage;

            string system = string.Format(lecture.SummaryPrompt, language);
            var slide = SlideDeck.GetPage(page);
            if (slide != null)
                system += $"\n\nThis section corresponds to slide page {slide.Number}: {slide.Text}";

            var transcript = new StringBuilder();
            foreach (var line in lines)
                transcript.AppendLine($"[{line.Time:HH:mm:ss}] {line.SourceText}");

            return await Chat(system, transcript.ToString(), maxTokens: 600, temperature: 0.3);
        }

        private static async Task<string> Chat(string system, string user, int maxTokens, double temperature,
            bool json = false)
        {
            var config = Translator.Setting[SUMMARY_API] as OpenAIConfig;
            if (config == null || string.IsNullOrWhiteSpace(config.ApiUrl))
                throw new InvalidOperationException(
                    "请先在 API 设置的 \"OpenAI\" 一项里填好接口地址、密钥和模型（例如 DeepSeek）。");

            var requestData = new Dictionary<string, object>
            {
                ["model"] = config.ModelName,
                ["messages"] = new List<BaseLLMConfig.Message>
                {
                    new BaseLLMConfig.Message { role = "system", content = system },
                    new BaseLLMConfig.Message { role = "user", content = user }
                },
                ["temperature"] = temperature,
                ["max_tokens"] = maxTokens,
                ["stream"] = false,
            };
            if (json)
                requestData["response_format"] = new { type = "json_object" };

            var response = await Post(config, requestData);
            // Some OpenAI-compatible servers do not support `response_format`.
            if (json && (int)response.StatusCode is 400 or 422)
            {
                response.Dispose();
                requestData.Remove("response_format");
                response = await Post(config, requestData);
            }

            using (response)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP Error - {(int)response.StatusCode} {response.StatusCode}");

                var responseObj = JsonSerializer.Deserialize<OpenAIConfig.Response>(responseString);
                string output = responseObj?.choices?.FirstOrDefault()?.message?.content ?? string.Empty;
                output = RegexPatterns.ModelThinking().Replace(output, string.Empty).Trim();
                if (string.IsNullOrEmpty(output))
                    throw new InvalidOperationException("The model returned an empty reply.");
                return output;
            }
        }

        private static async Task<HttpResponseMessage> Post(OpenAIConfig config, Dictionary<string, object> requestData)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TextUtil.NormalizeUrl(config.ApiUrl));
            request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            request.Content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }
    }
}
