using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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

        public static string FileFilter => "课件 (*.pdf;*.pptx)|*.pdf;*.pptx";

        public static async Task Load(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            var loaded = extension switch
            {
                ".pdf" => await Task.Run(() => ReadPdf(path)),
                ".pptx" => await Task.Run(() => ReadPptx(path)),
                ".ppt" => throw new NotSupportedException("不支持旧版 .ppt，请在 PowerPoint 里另存为 .pptx 或 PDF。"),
                _ => throw new NotSupportedException("只支持 PDF 和 PPTX 课件。"),
            };

            if (loaded.Count == 0 || loaded.All(page => string.IsNullOrEmpty(page.Text)))
                throw new InvalidDataException("课件里没有读到文字（扫描图片版的课件无法使用）。");

            pages = loaded;
            FileName = Path.GetFileName(path);
            Changed?.Invoke();
        }

        private static List<SlidePage> ReadPdf(string path)
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
                result.Add(MakePage(page.Number, lines.FirstOrDefault(), lines));
            }
            return result;
        }

        private static readonly XNamespace NS_A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace NS_P = "http://schemas.openxmlformats.org/presentationml/2006/main";
        private static readonly XNamespace NS_R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace NS_REL = "http://schemas.openxmlformats.org/package/2006/relationships";

        private static List<SlidePage> ReadPptx(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            XDocument ReadXml(string entryName)
            {
                var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"PPTX 文件不完整：缺少 {entryName}");
                using var stream = entry.Open();
                return XDocument.Load(stream);
            }

            // Slide order comes from presentation.xml; slide files are resolved through its relationships.
            var relationships = ReadXml("ppt/_rels/presentation.xml.rels").Root!
                .Elements(NS_REL + "Relationship")
                .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!);
            var slideIds = ReadXml("ppt/presentation.xml").Root!
                .Element(NS_P + "sldIdLst")?
                .Elements(NS_P + "sldId")
                .Select(e => (string?)e.Attribute(NS_R + "id"))
                .Where(id => id != null)
                .ToList() ?? new List<string?>();

            var result = new List<SlidePage>();
            int number = 0;
            foreach (var id in slideIds)
            {
                number++;
                if (!relationships.TryGetValue(id!, out var target))
                    continue;
                string entryName = target.StartsWith("/") ? target.TrimStart('/') : "ppt/" + target;
                var slide = ReadXml(entryName);

                string? title = null;
                var lines = new List<string>();
                foreach (var shape in slide.Descendants(NS_P + "sp"))
                {
                    string? placeholder = (string?)shape.Descendants(NS_P + "ph").FirstOrDefault()?.Attribute("type");
                    foreach (var paragraph in shape.Descendants(NS_A + "p"))
                    {
                        string line = string.Concat(paragraph.Descendants(NS_A + "t").Select(t => t.Value)).Trim();
                        if (line.Length == 0)
                            continue;
                        if (title == null && placeholder is ("title" or "ctrTitle"))
                            title = line;
                        lines.Add(line);
                    }
                }
                // Text in tables.
                foreach (var cell in slide.Descendants(NS_A + "tc"))
                {
                    string line = string.Concat(cell.Descendants(NS_A + "t").Select(t => t.Value)).Trim();
                    if (line.Length > 0 && !lines.Contains(line))
                        lines.Add(line);
                }
                result.Add(MakePage(number, title ?? lines.FirstOrDefault(), lines));
            }
            return result;
        }

        private static SlidePage MakePage(int number, string? title, List<string> lines) => new()
        {
            Number = number,
            Title = Truncate(title ?? string.Empty, TITLE_MAX_CHARS),
            Text = Truncate(Whitespace().Replace(string.Join(" / ", lines), " ").Trim(), TEXT_MAX_CHARS),
        };

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
