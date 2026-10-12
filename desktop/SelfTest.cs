using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace LiveCaptionsTranslator
{
    // `LiveCaptionsTranslator --selftest`: exercises the core without any window and exits with 0 if it all works.
    // It runs in a temporary data folder, so it never touches the user's settings and history.
    public static class SelfTest
    {
        public static int Run()
        {
            string directory = Path.Combine(Path.GetTempPath(), "lct-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("LCT_DATA_DIR", directory);
            int failures = 0;

            void Check(string name, Func<string> test)
            {
                try
                {
                    Console.WriteLine($"[ OK ] {name}: {test()}");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Console.WriteLine($"Platform: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} " +
                              $"({System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier})");
            Console.WriteLine($"Data folder: {AppPaths.DataDirectory}");

            Check("settings", () =>
            {
                var setting = Translator.Setting ?? throw new InvalidOperationException("no settings");
                setting.Save();
                if (!File.Exists(AppPaths.SettingFile))
                    throw new FileNotFoundException("setting.json was not written");
                var reloaded = Setting.Load();
                if (reloaded.Lecture.Engine != RecognitionEngine.Aliyun)
                    throw new InvalidOperationException("the cloud engine is not forced");
                return $"engine {reloaded.Lecture.Engine}, target {reloaded.TargetLanguage}";
            });

            Check("sqlite (courses and lectures)", () =>
            {
                SectionLogger.EnsureTables();
                long courseId = LectureStore.AddCourse("自测课程").GetAwaiter().GetResult();
                long lectureId = LectureStore.StartLecture("自测课程", "自测", DateTime.Now, 0).GetAwaiter().GetResult();
                LectureStore.FinishLecture(lectureId, DateTime.Now, 0).GetAwaiter().GetResult();
                var lecture = LectureStore.GetLecture(lectureId).GetAwaiter().GetResult()
                              ?? throw new InvalidOperationException("lecture not found");
                var courses = LectureStore.LoadCourses().GetAwaiter().GetResult();
                return $"course {courseId}, lecture {lecture.Id} \"{lecture.Name}\", {courses.Count} course(s)";
            });

            Check("history", () =>
            {
                SQLiteHistoryLogger.LogTranslation("hello", "你好", "zh-CN", "SelfTest").GetAwaiter().GetResult();
                long last = SectionLogger.GetMaxHistoryId().GetAwaiter().GetResult();
                if (last < 1)
                    throw new InvalidOperationException("no history row");
                return $"last history id {last}";
            });

            Check("pdf slides (PdfPig)", () =>
            {
                string path = Path.Combine(directory, "slides.pdf");
                var builder = new PdfDocumentBuilder();
                var font = builder.AddStandard14Font(Standard14Font.Helvetica);
                for (int i = 1; i <= 2; i++)
                {
                    var page = builder.AddPage(595, 842);
                    page.AddText($"Slide {i}: dynamic programming splits a problem into overlapping subproblems " +
                                 "and stores their answers so that each is computed only once.",
                        12, new UglyToad.PdfPig.Core.PdfPoint(40, 780), font);
                }
                File.WriteAllBytes(path, builder.Build());
                SlideDeck.Load(path).GetAwaiter().GetResult();
                if (!SlideDeck.IsLoaded)
                    throw new InvalidOperationException("no page read");
                return $"{SlideDeck.Pages.Count} page(s), first: \"{SlideDeck.Pages[0].Text[..Math.Min(40, SlideDeck.Pages[0].Text.Length)]}\"";
            });

            Check("markdown folder", () => LectureDocument.DefaultSaveFolder);

            foreach (string tool in new[] { "parec", "pactl", "say", "spd-say", "espeak-ng", "pdftoppm", "tesseract" })
                Console.WriteLine($"[INFO] {tool}: {(Exec.Find(tool) ?? "not found")}");
            Console.WriteLine($"[INFO] OCR languages: {string.Join(", ", SlideOcr.AvailableLanguages())}");

            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception)
            {
                // Removing the temporary folder is best effort.
            }
            Console.WriteLine(failures == 0 ? "SELFTEST OK" : $"SELFTEST FAILED ({failures})");
            return failures == 0 ? 0 : 1;
        }
    }
}
