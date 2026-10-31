namespace LiveCaptionsTranslator.utils
{
    // The languages a lecture can be given in, and what each part of the app needs to know about them.
    // Code: the short code kept in Lecture.AsrLanguage ("de"); Culture: the BCP-47 tag used for TTS voices and the
    // Windows OCR engine; Tesseract: the traineddata name on Linux / macOS.
    public record LectureLanguage(string Code, string Name, string EnglishName, string Culture, string Tesseract)
    {
        public override string ToString() => Name;
    }

    public static class LectureLanguages
    {
        public const string AUTO = "";

        public static readonly LectureLanguage[] All =
        {
            new("en", "英语", "English", "en-US", "eng"),
            new("zh", "中文", "Chinese", "zh-CN", "chi_sim"),
            new("yue", "粤语", "Cantonese", "zh-HK", "chi_tra"),
            new("ja", "日语", "Japanese", "ja-JP", "jpn"),
            new("ko", "韩语", "Korean", "ko-KR", "kor"),
            new("de", "德语", "German", "de-DE", "deu"),
            new("fr", "法语", "French", "fr-FR", "fra"),
            new("es", "西班牙语", "Spanish", "es-ES", "spa"),
            new("it", "意大利语", "Italian", "it-IT", "ita"),
            new("pt", "葡萄牙语", "Portuguese", "pt-PT", "por"),
            new("ru", "俄语", "Russian", "ru-RU", "rus"),
            new("nl", "荷兰语", "Dutch", "nl-NL", "nld"),
            new("pl", "波兰语", "Polish", "pl-PL", "pol"),
            new("sv", "瑞典语", "Swedish", "sv-SE", "swe"),
            new("cs", "捷克语", "Czech", "cs-CZ", "ces"),
            new("ar", "阿拉伯语", "Arabic", "ar-SA", "ara"),
            new("vi", "越南语", "Vietnamese", "vi-VN", "vie"),
            new("th", "泰语", "Thai", "th-TH", "tha"),
        };

        // The short code of a setting value ("de-DE" -> "de"); empty for auto / unknown.
        public static string Code(string? language)
        {
            string code = (language ?? string.Empty).Trim().Split('-', '_')[0].ToLowerInvariant();
            return code == "auto" ? AUTO : code;
        }

        // The entry for a code or culture tag ("de", "de-DE", "zh-Hant-TW" -> Chinese; Cantonese only for "yue"); null for auto or an unknown language.
        public static LectureLanguage? Find(string? language)
        {
            string code = Code(language);
            if (code.Length == 0)
                return null;
            return All.FirstOrDefault(l => l.Code == code);
        }

        // The language of the current lecture, as set by the user; null when it is to be detected.
        public static LectureLanguage? Current => Find(Translator.Setting?.Lecture.AsrLanguage);

        // "German", or a description for the prompts when the language is not known.
        public static string EnglishName(string? language) =>
            Find(language)?.EnglishName ?? "the original language of the lecture";

        public static string Name(string? language) => Find(language)?.Name ?? "课程原语言";
    }
}
