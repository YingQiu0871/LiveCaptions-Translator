using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // The user's list of technical terms for the current course, one per line, optionally with the translation to
    // use: "eigenvalue = 特征值". The terms are sent to the cloud recognizer as hot words, and the whole list is added
    // to the translation, refinement, summary and notes prompts so the same term is always translated the same way.
    public static class Glossary
    {
        public record Entry(string Term, string Translation);

        private const int MAX_PROMPT_CHARS = 3000;
        private const int MAX_HOT_WORDS = 500;
        private const string VOCABULARY_PREFIX = "lctgloss";
        private static readonly char[] SEPARATORS = { '=', '＝', '\t' };
        private static readonly string[] HOT_WORD_LANGUAGES = { "zh", "en", "ja", "yue", "ko", "de", "fr", "ru" };

        private static readonly HttpClient client = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static List<Entry> Parse(string? text)
        {
            var entries = new List<Entry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in (text ?? string.Empty).Split('\n'))
            {
                string line = rawLine.Trim().TrimStart('-', '*', '•').Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                string term = line, translation = string.Empty;
                int separator = line.IndexOfAny(SEPARATORS);
                if (separator > 0)
                {
                    term = line[..separator].Trim();
                    translation = line[(separator + 1)..].Trim();
                }
                if (term.Length > 0 && seen.Add(term))
                    entries.Add(new Entry(term, translation));
            }
            return entries;
        }

        public static List<Entry> Current => Parse(Translator.Setting?.Lecture.Glossary);

        public static string Format(IEnumerable<Entry> entries) => string.Join(Environment.NewLine,
            entries.Select(entry => entry.Translation.Length > 0 ? $"{entry.Term} = {entry.Translation}" : entry.Term));

        // Added to the LLM prompts (via LectureState.SubjectHint).
        public static string PromptHint()
        {
            var entries = Current;
            if (entries.Count == 0)
                return string.Empty;
            var sb = new StringBuilder();
            sb.Append(" Glossary of this course (the recognizer may mishear these terms; restore them, and translate " +
                      "them exactly as given after \"=\"): ");
            foreach (var entry in entries)
            {
                string item = entry.Translation.Length > 0 ? $"{entry.Term} = {entry.Translation}; " : $"{entry.Term}; ";
                if (sb.Length + item.Length > MAX_PROMPT_CHARS)
                    break;
                sb.Append(item);
            }
            return sb.ToString().TrimEnd(' ', ';') + ".";
        }

        // Asks the LLM for the technical terms of the loaded slides and returns the glossary with the new terms
        // added after the existing ones (existing lines are kept as they are).
        public static async Task<(string Text, int Added)> ExtractFromSlides()
        {
            if (!SlideDeck.IsLoaded)
                throw new InvalidOperationException("还没有上传课件。");
            var slides = new StringBuilder();
            foreach (var page in SlideDeck.Pages)
            {
                if (string.IsNullOrWhiteSpace(page.Text))
                    continue;
                slides.AppendLine($"[Page {page.Number}] {page.Text.Trim()}");
                if (slides.Length > 16000)
                    break;
            }
            if (slides.Length == 0)
                throw new InvalidOperationException("课件里没有识别到文字。");

            string language = LectureState.OutputLanguage();
            // 纯英文模式: the terms only help the recognizer and keep the spelling consistent.
            string system = Translator.Setting.Lecture.EnglishOnly
                ? "You build a glossary for a student who follows this lecture with live speech recognition. " +
                  "From the slides, pick the technical terms, names, abbreviations and formulas' names that a speech " +
                  "recognizer is likely to get wrong (at most 80, most important first; skip everyday words). " +
                  "Answer with one term per line, no translation, no numbering, no other text." + SubjectOnlyHint()
                : "You build a glossary for a student who follows this lecture with live speech recognition and " +
                  $"translation into {language}. From the slides, pick the technical terms, names, abbreviations and " +
                  "formulas' names that a speech recognizer is likely to get wrong or a translator to translate " +
                  "inconsistently (at most 80, most important first; skip everyday words). Answer with one term per " +
                  $"line in the form \"term = standard {language} translation\" (for names and abbreviations that are " +
                  "not translated, just the term). No numbering, no other text." + SubjectOnlyHint();
            string answer = await Summarizer.Chat(system, slides.ToString(), maxTokens: 2000, temperature: 0,
                longRequest: true);

            var existing = Current;
            var known = new HashSet<string>(existing.Select(entry => entry.Term), StringComparer.OrdinalIgnoreCase);
            var added = Parse(answer).Where(entry => entry.Term.Length <= 60 && known.Add(entry.Term)).ToList();
            string text = (Translator.Setting.Lecture.Glossary ?? string.Empty).TrimEnd();
            if (added.Count > 0)
                text = (text.Length > 0 ? text + Environment.NewLine : string.Empty) + Format(added);
            return (text, added.Count);
        }

        private static string SubjectOnlyHint()
        {
            string subject = Translator.Setting?.Lecture.Subject?.Trim() ?? string.Empty;
            return subject.Length == 0 ? string.Empty : $" The course is about {subject}.";
        }

        // Hot words for the cloud recognizer: only the terms, within DashScope's limits (up to 7 words for Latin
        // text, 15 characters otherwise).
        private static List<string> HotWords() => Current
            .Select(entry => entry.Term)
            .Where(term => term.All(c => c < 0x80)
                ? term.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 7
                : term.Length <= 15)
            .Take(MAX_HOT_WORDS)
            .ToList();

        // Creates or updates the hot word list on Alibaba Cloud when the glossary changed, and returns its id
        // (null when the glossary is empty). One list is reused, since an account may only have ten.
        public static async Task<string?> SyncAsrVocabulary(CancellationToken token)
        {
            var lecture = Translator.Setting.Lecture;
            var words = HotWords();
            if (words.Count == 0)
                return null;

            string language = (lecture.AsrLanguage ?? string.Empty).Split('-')[0].ToLowerInvariant();
            var vocabulary = words.Select(word =>
            {
                var item = new Dictionary<string, object> { ["text"] = word, ["weight"] = 4 };
                if (HOT_WORD_LANGUAGES.Contains(language))
                    item["lang"] = language;
                return item;
            }).ToList();

            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                lecture.AsrModel + "\n" + language + "\n" + string.Join("\n", words))))[..16];
            if (!string.IsNullOrEmpty(lecture.AsrVocabularyId) && lecture.AsrVocabularyHash == hash)
                return lecture.AsrVocabularyId;

            if (!string.IsNullOrEmpty(lecture.AsrVocabularyId))
            {
                try
                {
                    await Call(new { action = "update_vocabulary", vocabulary_id = lecture.AsrVocabularyId, vocabulary },
                        token);
                    lecture.AsrVocabularyHash = hash;
                    return lecture.AsrVocabularyId;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Deleted in the console, or made for another model: start a new list.
                    try
                    {
                        await Call(new { action = "delete_vocabulary", vocabulary_id = lecture.AsrVocabularyId }, token);
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            var output = await Call(new
            {
                action = "create_vocabulary",
                target_model = lecture.AsrModel,
                prefix = VOCABULARY_PREFIX,
                vocabulary,
            }, token);
            string id = output.ValueKind == JsonValueKind.Object && output.TryGetProperty("vocabulary_id", out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
            if (id.Length == 0)
                throw new InvalidOperationException("阿里云没有返回热词表 ID。");
            lecture.AsrVocabularyId = id;
            lecture.AsrVocabularyHash = hash;
            return id;
        }

        // The hot word list did not work: update it next time. The id is kept, so that a list that still exists is
        // reused (or deleted before a new one is made) instead of being left behind; an account may only have ten.
        public static void RecheckAsrVocabulary()
        {
            Translator.Setting.Lecture.AsrVocabularyHash = string.Empty;
        }

        // The customization API lives on the same host as the WebSocket endpoint.
        private static string CustomizationUrl()
        {
            var endpoint = new Uri(Translator.Setting.Lecture.AsrEndpoint);
            return $"https://{endpoint.Authority}/api/v1/services/audio/asr/customization";
        }

        private static async Task<JsonElement> Call(object input, CancellationToken token)
        {
            var lecture = Translator.Setting.Lecture;
            if (string.IsNullOrWhiteSpace(lecture.AsrApiKey))
                throw new InvalidOperationException("还没有填阿里云 API Key。");
            string json = JsonSerializer.Serialize(new { model = "speech-biasing", input });
            var request = new HttpRequestMessage(HttpMethod.Post, CustomizationUrl())
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {lecture.AsrApiKey.Trim()}");

            using var response = await client.SendAsync(request, token);
            string body = await response.Content.ReadAsStringAsync(token);
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(body).RootElement;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"热词接口返回了无法识别的内容（HTTP {(int)response.StatusCode}）。");
            }
            if (!response.IsSuccessStatusCode)
            {
                string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                throw new InvalidOperationException($"热词接口出错（HTTP {(int)response.StatusCode}）：{message}");
            }
            return root.TryGetProperty("output", out var output) ? output.Clone() : default;
        }
    }
}
