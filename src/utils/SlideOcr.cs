using System.IO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LiveCaptionsTranslator.utils
{
    // Reads scanned (image-only) PDF pages with the OCR engine built into Windows.
    // The recognizable languages are those with an OCR language pack installed in Windows.
    public static class SlideOcr
    {
        // Pages are rendered about this wide before recognition.
        private const double RENDER_WIDTH = 2000;

        public record OcrLanguage(string Tag, string Name)
        {
            public override string ToString() => Name;
        }

        public static List<OcrLanguage> AvailableLanguages()
        {
            try
            {
                return OcrEngine.AvailableRecognizerLanguages
                    .Select(language => new OcrLanguage(language.LanguageTag, language.NativeName))
                    .ToList();
            }
            catch (Exception)
            {
                return new List<OcrLanguage>();
            }
        }

        private static OcrEngine CreateEngine(string languageTag)
        {
            OcrEngine? engine = null;
            if (!string.IsNullOrEmpty(languageTag))
            {
                var language = new Windows.Globalization.Language(languageTag);
                if (OcrEngine.IsLanguageSupported(language))
                    engine = OcrEngine.TryCreateFromLanguage(language);
            }
            if (engine == null)
            {
                // The slides of a foreign-language class are most likely not in Chinese.
                var languages = OcrEngine.AvailableRecognizerLanguages;
                var language = languages.FirstOrDefault(l => !l.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                               ?? languages.FirstOrDefault();
                engine = language != null
                    ? OcrEngine.TryCreateFromLanguage(language)
                    : OcrEngine.TryCreateFromUserProfileLanguages();
            }
            return engine ?? throw new InvalidOperationException(
                "Windows 里没有可用的 OCR 语言。请在 设置 → 时间和语言 → 语言和区域 中添加课程语言（含\"光学字符识别\"）。");
        }

        public static async Task<int> PageCount(string path)
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            var document = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            return (int)document.PageCount;
        }

        // Returns the recognized lines of each requested page (1-based page numbers).
        public static async Task<Dictionary<int, List<string>>> RecognizePdf(string path, IList<int> pageNumbers,
            string languageTag, IProgress<string>? progress = null)
        {
            var engine = CreateEngine(languageTag);
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            var document = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

            var result = new Dictionary<int, List<string>>();
            for (int i = 0; i < pageNumbers.Count; i++)
            {
                int number = pageNumbers[i];
                if (number < 1 || number > document.PageCount)
                    continue;
                progress?.Report($"OCR 识别中 {i + 1}/{pageNumbers.Count} 页……");

                using var page = document.GetPage((uint)(number - 1));
                double scale = RENDER_WIDTH / Math.Max(page.Size.Width, 1);
                uint width = (uint)Math.Clamp(page.Size.Width * scale, 1, OcrEngine.MaxImageDimension);
                uint height = (uint)Math.Clamp(page.Size.Height * scale, 1, OcrEngine.MaxImageDimension);

                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions
                {
                    DestinationWidth = width,
                    DestinationHeight = height,
                });
                var decoder = await BitmapDecoder.CreateAsync(stream);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                var ocrResult = await engine.RecognizeAsync(bitmap);
                result[number] = ocrResult.Lines.Select(line => line.Text).ToList();
            }
            return result;
        }
    }
}
