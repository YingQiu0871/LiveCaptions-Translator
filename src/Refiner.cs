using System.Text;
using System.Text.Json;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // Paragraph refinement: every few sentences, the raw speech-recognition sentences are sent again to the
    // LLM together with the previous paragraph, the subject and the current slide. It fixes recognition errors
    // and translates them as one paragraph, which is far more accurate than translating each sentence alone.
    // The live per-sentence translation stays for speed; the caption page replaces it with the paragraph.
    public static class Refiner
    {
        private const int LOOP_INTERVAL_MS = 1000;
        private const int MIN_LINES = 3;
        private const int MAX_LINES = 6;
        private const int MAX_CHARS = 700;
        private static readonly TimeSpan PAUSE = TimeSpan.FromSeconds(4);
        private const int MAX_FAILURES = 2;

        // Last sentence already covered by a paragraph.
        private static long lastRefinedId = -1;
        private static string previousParagraph = string.Empty;
        private static int failures = 0;

        public static event Action? ParagraphRefined;

        public static bool Enabled
        {
            get
            {
                var lecture = Translator.Setting?.Lecture;
                // 纯英文模式 only cleans up the recognized text, so it does not depend on the translation API.
                return lecture != null && lecture.RefineParagraphs &&
                       (lecture.EnglishOnly ||
                        (!lecture.CaptionsOnly && Translator.Setting?.ApiName == Summarizer.SUMMARY_API));
            }
        }

        // Called when a class starts.
        public static void Reset(long firstHistoryId)
        {
            lastRefinedId = firstHistoryId;
            previousParagraph = string.Empty;
            failures = 0;
        }

        public static async Task RefineLoop()
        {
            while (true)
            {
                try
                {
                    await Tick();
                }
                catch (Exception)
                {
                    // A failed round is retried on the next tick; after that the sentences stay as they are.
                }
                // Between classes there is little to do: check less often.
                await Task.Delay(ClassSession.IsRunning ? LOOP_INTERVAL_MS : 5 * LOOP_INTERVAL_MS);
            }
        }

        private static async Task Tick()
        {
            if (!Enabled || lastRefinedId < 0 || ClassSession.FirstHistoryId < 0)
                return;

            var lines = (await SectionLogger.LoadHistoryRange(lastRefinedId))
                .Where(line => !string.IsNullOrWhiteSpace(line.SourceText))
                .ToList();
            if (lines.Count == 0)
                return;

            // Wait for a natural break, or for enough text, but don't hold sentences back for long.
            int chars = lines.Sum(line => line.SourceText.Length);
            bool paused = DateTime.Now - lines[^1].Time >= PAUSE || !ClassSession.IsRunning;
            bool ready = lines.Count >= MAX_LINES || chars >= MAX_CHARS ||
                         (paused && (lines.Count >= MIN_LINES || chars >= 120 || DateTime.Now - lines[0].Time >= PAUSE * 3));
            if (!ready)
                return;

            var batch = lines.Take(MAX_LINES).ToList();
            string source, translation;
            try
            {
                (source, translation) = await Refine(batch);
                failures = 0;
            }
            catch (Exception)
            {
                if (++failures <= MAX_FAILURES)
                    return;
                // Keep going instead of retrying the same sentences forever.
                failures = 0;
                source = string.Join(" ", batch.Select(line => line.SourceText));
                translation = string.Join(" ", batch.Select(line => StripLatency(line.TranslatedText)));
            }

            await SectionLogger.AddParagraph(batch[0].Time, batch[0].Id, batch[^1].Id, source, translation);
            lastRefinedId = batch[^1].Id;
            previousParagraph = source;
            Speaker.EnqueueParagraph(translation);
            ParagraphRefined?.Invoke();
        }

        private static async Task<(string Source, string Translation)> Refine(List<HistoryLine> batch)
        {
            bool englishOnly = Translator.Setting.Lecture.EnglishOnly;
            string language = LectureState.OutputLanguage();

            var system = new StringBuilder();
            system.Append(
                "You are a professional interpreter for a university lecture. " +
                "The user message holds consecutive sentences produced by automatic speech recognition. " +
                "Recognition makes mistakes: wrong words that sound alike, split or merged sentences, " +
                "letters spelled out one by one (e.g. \"D N A\" means DNA). " +
                "1) Rewrite them as one clean paragraph in the original language. Fix a word only when the context, " +
                "the subject or the slide makes the intended word clear; never add content that was not said. ");
            if (englishOnly)
                system.Append("Answer only with JSON: {\"source\": \"...\"}.");
            else
                system.Append(
                    $"2) Translate that paragraph into {language}, faithfully and fluently, using the standard " +
                    "terminology of the field. For important technical terms, keep the original term in parentheses " +
                    "the first time it appears. " +
                    "Answer only with JSON: {\"source\": \"...\", \"translation\": \"...\"}.");
            system.Append(LectureState.SubjectHint());
            var slide = SlideDeck.GetPage(Summarizer.CurrentPage);
            if (slide != null && !string.IsNullOrWhiteSpace(slide.Text))
            {
                string text = slide.Text.Length > 1500 ? slide.Text[..1500] : slide.Text;
                system.Append($"\n\nThe lecturer is probably on this slide (page {slide.Number}):\n{text}");
            }
            if (!string.IsNullOrEmpty(previousParagraph))
                system.Append($"\n\nThe previous paragraph, for context only (do not repeat it):\n{previousParagraph}");

            string user = string.Join("\n", batch.Select(line => line.SourceText));
            string output = await Summarizer.Chat(system.ToString(), user, maxTokens: 1500, temperature: 0.2, json: true);

            int start = output.IndexOf('{');
            int end = output.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new FormatException("模型没有按格式回答。");
            using var doc = JsonDocument.Parse(output[start..(end + 1)]);
            string source = doc.RootElement.GetProperty("source").GetString() ?? string.Empty;
            // 纯英文模式 has no translation.
            string translation = englishOnly ? string.Empty
                : doc.RootElement.TryGetProperty("translation", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(source) || (!englishOnly && string.IsNullOrWhiteSpace(translation)))
                throw new FormatException("模型返回了空内容。");
            return (source.Trim(), translation.Trim());
        }

        private static string StripLatency(string text) =>
            RegexPatterns.NoticePrefix().Replace(text, string.Empty).Trim();
    }
}
