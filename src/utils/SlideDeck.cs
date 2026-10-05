using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace LiveCaptionsTranslator.utils
{
    public class SlidePage
    {
        public int Number { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    // The lecture slides (PDF) loaded for the current class, used to align sections with pages.
    public static partial class SlideDeck
    {
        private const int TITLE_MAX_CHARS = 80;
        private const int TEXT_MAX_CHARS = 500;

        private static List<SlidePage> pages = new();

        public static IReadOnlyList<SlidePage> Pages => pages;
        public static string FileName { get; private set; } = string.Empty;
        public static bool IsLoaded => pages.Count > 0;

        public static event Action? Changed;

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();

        public static async Task Load(string path)
        {
            var loaded = await Task.Run(() =>
            {
                var result = new List<SlidePage>();
                using var document = PdfDocument.Open(path);
                foreach (var page in document.GetPages())
                {
                    var lines = page.GetWords()
                        .GroupBy(word => Math.Round(word.BoundingBox.Bottom))
                        .OrderByDescending(group => group.Key)
                        .Select(group => string.Join(" ", group.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToList();
                    string text = Whitespace().Replace(string.Join(" / ", lines), " ").Trim();
                    result.Add(new SlidePage
                    {
                        Number = page.Number,
                        Title = Truncate(lines.FirstOrDefault() ?? string.Empty, TITLE_MAX_CHARS),
                        Text = Truncate(text, TEXT_MAX_CHARS),
                    });
                }
                return result;
            });

            if (loaded.All(page => string.IsNullOrEmpty(page.Text)))
                throw new InvalidDataException("No text found in this PDF (scanned images are not supported).");

            pages = loaded;
            FileName = Path.GetFileName(path);
            Changed?.Invoke();
        }

        public static void Clear()
        {
            pages = new List<SlidePage>();
            FileName = string.Empty;
            Changed?.Invoke();
        }

        public static SlidePage? GetPage(int? number) =>
            number == null ? null : pages.FirstOrDefault(page => page.Number == number);

        // A compact outline of the pages around `currentPage` for the LLM prompt.
        public static string Outline(int? currentPage, int before = 2, int after = 6)
        {
            var sb = new StringBuilder();
            if (currentPage == null)
            {
                // Position unknown: a shorter excerpt of every page.
                foreach (var page in pages)
                    sb.AppendLine($"[Page {page.Number}] {Truncate(page.Text, 150)}");
                return sb.ToString();
            }
            foreach (var page in pages.Where(p => p.Number >= currentPage - before && p.Number <= currentPage + after))
                sb.AppendLine($"[Page {page.Number}] {page.Text}");
            return sb.ToString();
        }

        private static string Truncate(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
