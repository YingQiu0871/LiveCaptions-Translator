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
        public string Text { get; set; } = string.Empty;
    }

    // The lecture slides (PDF) loaded for the current class, used to align sections with pages.
    public static partial class SlideDeck
    {
        private const int TEXT_MAX_CHARS = 500;

        private static List<SlidePage> pages = new();

        public static IReadOnlyList<SlidePage> Pages => pages;
        public static string FileName { get; private set; } = string.Empty;
        public static bool IsLoaded => pages.Count > 0;
        // How many pages of the loaded PDF were read with OCR.
        public static int OcrPageCount { get; private set; } = 0;
        private static int lastOcrPageCount = 0;

        public static event Action? Changed;

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();

        public static string FileFilter => "课件 PDF (*.pdf)|*.pdf|PowerPoint (*.pptx)|*.pptx|全部课件 (*.pdf;*.pptx)|*.pdf;*.pptx";

        // A page with less text than this is treated as scanned and read with OCR.
        private const int MIN_TEXT_CHARS = 30;

        public static async Task Load(string path, IProgress<string>? progress = null)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            lastOcrPageCount = 0;
            var loaded = extension switch
            {
                ".pdf" => await ReadPdfWithOcr(path, progress),
                ".pptx" => await Task.Run(() => ReadPptx(path)),
                ".ppt" => throw new NotSupportedException("不支持旧版 .ppt，请在 PowerPoint 里另存为 .pdf 或 .pptx。"),
                _ => throw new NotSupportedException("只支持 PDF 和 PPTX 课件。"),
            };

            if (loaded.Count == 0 || loaded.All(page => string.IsNullOrEmpty(page.Text)))
                throw new InvalidDataException("课件里没有读到文字。");

            pages = loaded;
            FileName = Path.GetFileName(path);
            OcrPageCount = lastOcrPageCount;
            Changed?.Invoke();
        }

        // Text layer first; pages without one (scans, slides exported as pictures) go through OCR.
        private static async Task<List<SlidePage>> ReadPdfWithOcr(string path, IProgress<string>? progress)
        {
            var lecture = Translator.Setting?.Lecture;
            bool forceOcr = lecture?.ForceOcr ?? false;

            progress?.Report("读取课件文字……");
            List<SlidePage> result;
            try
            {
                result = await Task.Run(() => ReadPdf(path));
            }
            catch (Exception)
            {
                // Unreadable text layer: OCR every page.
                int count = await SlideOcr.PageCount(path);
                result = Enumerable.Range(1, count).Select(n => MakePage(n, new List<string>())).ToList();
                forceOcr = true;
            }

            var scanned = result
                .Where(page => forceOcr || page.Text.Length < MIN_TEXT_CHARS)
                .Select(page => page.Number)
                .ToList();
            if (scanned.Count == 0)
                return result;

            Dictionary<int, List<string>> recognized;
            try
            {
                recognized = await SlideOcr.RecognizePdf(path, scanned, lecture?.OcrLanguage ?? string.Empty, progress);
            }
            catch (Exception ex)
            {
                if (result.All(page => string.IsNullOrEmpty(page.Text)))
                    throw new InvalidDataException($"这是扫描版课件，OCR 失败：{ex.Message}");
                SnackbarHost.Show("[WARNING] 部分页面 OCR 失败。", ex.Message, SnackbarType.Warning,
                    timeout: 3, closeButton: true);
                return result;
            }

            for (int i = 0; i < result.Count; i++)
            {
                if (recognized.TryGetValue(result[i].Number, out var lines) && lines.Count > 0)
                    result[i] = MakePage(result[i].Number, lines);
            }
            lastOcrPageCount = recognized.Count(pair => pair.Value.Count > 0);
            return result;
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
                result.Add(MakePage(page.Number, lines));
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

                var lines = new List<string>();
                foreach (var shape in slide.Descendants(NS_P + "sp"))
                {
                    foreach (var paragraph in shape.Descendants(NS_A + "p"))
                    {
                        string line = string.Concat(paragraph.Descendants(NS_A + "t").Select(t => t.Value)).Trim();
                        if (line.Length == 0)
                            continue;
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
                result.Add(MakePage(number, lines));
            }
            return result;
        }

        private static SlidePage MakePage(int number, List<string> lines) => new()
        {
            Number = number,
            Text = Truncate(Whitespace().Replace(string.Join(" / ", lines), " ").Trim(), TEXT_MAX_CHARS),
        };

        public static void Clear()
        {
            pages = new List<SlidePage>();
            FileName = string.Empty;
            OcrPageCount = 0;
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
