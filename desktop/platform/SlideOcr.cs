using UglyToad.PdfPig;

namespace LiveCaptionsTranslator.utils
{
    // Reads scanned (image-only) PDF pages with Tesseract: `pdftoppm` (poppler) renders a page, `tesseract` reads it.
    // Without these two tools there is no OCR: no languages are offered and recognizing fails with a hint.
    public static class SlideOcr
    {
        // Pages are rendered at this resolution before recognition.
        private const int RENDER_DPI = 200;

        public record OcrLanguage(string Tag, string Name)
        {
            public override string ToString() => Name;
        }

        // (Tesseract language code, language tag as in the Windows OCR list, name)
        private static readonly (string Code, string Tag, string Name)[] Known =
        {
            ("eng", "en-US", "English"),
            ("chi_sim", "zh-Hans-CN", "中文（简体）"),
            ("chi_tra", "zh-Hant-TW", "中文（繁體）"),
            ("jpn", "ja", "日本語"),
            ("kor", "ko", "한국어"),
            ("deu", "de-DE", "Deutsch"),
            ("fra", "fr-FR", "Français"),
            ("spa", "es-ES", "Español"),
            ("ita", "it-IT", "Italiano"),
            ("por", "pt-PT", "Português"),
            ("rus", "ru-RU", "Русский"),
        };

        public static bool IsAvailable => Exec.Exists("pdftoppm") && Exec.Exists("tesseract");

        public static List<OcrLanguage> AvailableLanguages()
        {
            if (!IsAvailable)
                return new List<OcrLanguage>();
            string? text = Exec.Run("tesseract", new[] { "--list-langs" }).GetAwaiter().GetResult();
            var installed = (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .ToHashSet();
            return Known.Where(language => installed.Contains(language.Code))
                .Select(language => new OcrLanguage(language.Tag, language.Name))
                .ToList();
        }

        public static Task<int> PageCount(string path)
        {
            using var document = PdfDocument.Open(Path.GetFullPath(path));
            return Task.FromResult(document.NumberOfPages);
        }

        // Returns the recognized lines of each requested page (1-based page numbers).
        public static async Task<Dictionary<int, List<string>>> RecognizePdf(string path, IList<int> pageNumbers,
            string languageTag, IProgress<string>? progress = null)
        {
            if (!IsAvailable)
                throw new InvalidOperationException(
                    "没有找到 OCR 工具。请安装 poppler（pdftoppm）和 tesseract（含课程语言的语言包）后重试。");
            string code = CodeFor(languageTag);
            string full = Path.GetFullPath(path);
            string directory = Path.Combine(Path.GetTempPath(), "lct-ocr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var result = new Dictionary<int, List<string>>();
                for (int i = 0; i < pageNumbers.Count; i++)
                {
                    int number = pageNumbers[i];
                    progress?.Report($"OCR 识别中 {i + 1}/{pageNumbers.Count} 页……");

                    string prefix = Path.Combine(directory, "page");
                    string png = prefix + ".png";
                    File.Delete(png);
                    await Exec.Run("pdftoppm", new[]
                    {
                        "-r", RENDER_DPI.ToString(), "-f", number.ToString(), "-l", number.ToString(),
                        "-png", "-singlefile", full, prefix,
                    }, 60000);
                    if (!File.Exists(png))
                        continue;
                    string? text = await Exec.Run("tesseract", new[] { png, "stdout", "-l", code }, 120000);
                    if (text == null)
                        continue;
                    result[number] = text.Split('\n')
                        .Select(line => line.Trim())
                        .Where(line => line.Length > 0)
                        .ToList();
                }
                return result;
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (Exception)
                {
                }
            }
        }

        // The slides of a foreign-language class are most likely not in Chinese: English when nothing is chosen.
        private static string CodeFor(string languageTag)
        {
            if (!string.IsNullOrEmpty(languageTag))
            {
                var exact = Known.FirstOrDefault(l => l.Tag.Equals(languageTag, StringComparison.OrdinalIgnoreCase));
                if (exact.Code != null)
                    return exact.Code;
                string lower = languageTag.ToLowerInvariant();
                if (lower.StartsWith("zh-hant") || lower is "zh-tw" or "zh-hk")
                    return "chi_tra";
                if (lower.StartsWith("zh"))
                    return "chi_sim";
                string prefix = lower.Split('-')[0];
                var match = Known.FirstOrDefault(l => l.Tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (match.Code != null)
                    return match.Code;
            }
            return "eng";
        }
    }
}
