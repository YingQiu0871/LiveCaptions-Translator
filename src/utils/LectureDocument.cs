using System.IO;
using System.Text;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // The full record of one lecture: section summaries, each followed by the original text and its translation.
    // Shown in the history and saved as a Markdown file in the folder chosen in the settings.
    public static class LectureDocument
    {
        public static string DefaultSaveFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "课堂同传助手");

        public static string SaveFolder
        {
            get
            {
                string folder = Translator.Setting?.Lecture.SaveFolder ?? string.Empty;
                return string.IsNullOrWhiteSpace(folder) ? DefaultSaveFolder : folder;
            }
        }

        public static async Task<List<LectureSectionView>> Build(LectureRecord lecture)
        {
            long last = await LectureStore.EffectiveLastHistoryId(lecture);
            var lines = (await SectionLogger.LoadHistoryRange(lecture.FirstHistoryId, last))
                .Where(line => !string.IsNullOrWhiteSpace(line.SourceText))
                .ToList();
            var paragraphs = (await SectionLogger.LoadParagraphs(lecture.FirstHistoryId))
                .Where(paragraph => paragraph.FirstHistoryId <= last)
                .ToList();
            var sections = await SectionLogger.LoadSectionsInRange(lecture.FirstHistoryId, last);
            lines = MergeParagraphs(lines, paragraphs, fadeUnrefined: false);

            var views = new List<LectureSectionView>();
            int index = 0;
            for (int i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                // Sentences said before the first summarized section (rare) go into it as well.
                var sectionLines = new List<HistoryLine>();
                while (index < lines.Count && lines[index].Id <= section.LastHistoryId)
                    sectionLines.Add(lines[index++]);
                views.Add(new LectureSectionView
                {
                    Header = $"{i + 1}. {section.TimeRange}",
                    Title = section.Title,
                    Summary = section.Body,
                    Lines = sectionLines,
                });
            }
            if (index < lines.Count)
            {
                var rest = lines.Skip(index).ToList();
                views.Add(new LectureSectionView
                {
                    Header = sections.Count == 0
                        ? $"{rest[0].Time:HH:mm}–{rest[^1].Time:HH:mm}"
                        : $"{sections.Count + 1}. {rest[0].Time:HH:mm}–{rest[^1].Time:HH:mm}",
                    Title = sections.Count == 0 ? "全文（没有小节总结）" : "（还没有总结）",
                    Lines = rest,
                });
            }
            return views;
        }

        // Refined paragraphs replace the sentences they cover; the sentences after them are kept as they came in
        // (drawn fainter on the caption page while refinement is on, since they will be replaced shortly).
        public static List<HistoryLine> MergeParagraphs(List<HistoryLine> lines, List<ParagraphEntry> paragraphs,
            bool fadeUnrefined)
        {
            if (paragraphs.Count == 0 && !fadeUnrefined)
                return lines;

            var merged = new List<HistoryLine>();
            int p = 0;
            foreach (var line in lines)
            {
                while (p < paragraphs.Count && paragraphs[p].LastHistoryId < line.Id)
                    p++;
                if (p < paragraphs.Count && line.Id >= paragraphs[p].FirstHistoryId)
                {
                    // The first sentence of a paragraph stands for the whole paragraph.
                    if (line.Id == paragraphs[p].FirstHistoryId || merged.Count == 0 ||
                        merged[^1].Id < paragraphs[p].FirstHistoryId)
                    {
                        merged.Add(new HistoryLine
                        {
                            Id = line.Id,
                            Time = paragraphs[p].Time,
                            SourceText = paragraphs[p].Source,
                            TranslatedText = paragraphs[p].Translation,
                        });
                    }
                    continue;
                }
                merged.Add(new HistoryLine
                {
                    Id = line.Id,
                    Time = line.Time,
                    SourceText = line.SourceText,
                    TranslatedText = line.TranslatedText,
                    Opacity = fadeUnrefined ? 0.6 : 1.0,
                });
            }
            return merged;
        }

        public static string CleanTranslation(string text)
        {
            text = RegexPatterns.NoticePrefix().Replace(text ?? string.Empty, string.Empty).Trim();
            return text == "N/A" || text.StartsWith("[ERROR]") || text.StartsWith("[WARNING]") ? string.Empty : text;
        }

        public static async Task<string> ToMarkdown(LectureRecord lecture)
        {
            var sections = await Build(lecture);
            var sb = new StringBuilder();
            sb.AppendLine($"# {lecture.Name}");
            sb.AppendLine();
            string end = lecture.EndTime is DateTime endTime ? $"–{endTime:HH:mm}" : string.Empty;
            sb.AppendLine($"课程：{lecture.CourseName}　时间：{lecture.StartTime:yyyy-MM-dd HH:mm}{end}");
            sb.AppendLine();

            if (sections.Any(section => !string.IsNullOrWhiteSpace(section.Summary)))
            {
                sb.AppendLine("## 小节总结");
                sb.AppendLine();
                foreach (var section in sections)
                {
                    sb.AppendLine($"### {section.Header} {section.Title}");
                    sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(section.Summary))
                    {
                        sb.AppendLine(section.Summary);
                        sb.AppendLine();
                    }
                }
            }

            sb.AppendLine("## 原文与译文");
            sb.AppendLine();
            foreach (var section in sections)
            {
                sb.AppendLine($"### {section.Header} {section.Title}");
                sb.AppendLine();
                foreach (var line in section.Lines)
                {
                    sb.AppendLine($"`{line.Time:HH:mm:ss}` {line.SourceText}");
                    sb.AppendLine();
                    string translated = CleanTranslation(line.TranslatedText);
                    if (!string.IsNullOrEmpty(translated))
                    {
                        sb.AppendLine($"> {translated}");
                        sb.AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (cleaned.Length > 100)
                cleaned = cleaned[..100];
            return cleaned.Length == 0 ? "未命名" : cleaned;
        }

        // Writes <save folder>\<course>\<lecture name>.md (replacing this lecture's earlier file) and returns its path.
        public static async Task<string> Save(LectureRecord lecture)
        {
            string folder = Path.Combine(SaveFolder, SafeFileName(lecture.CourseName));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SafeFileName(lecture.Name) + ".md");
            for (int n = 2; File.Exists(path) &&
                            !string.Equals(path, lecture.FilePath, StringComparison.OrdinalIgnoreCase); n++)
                path = Path.Combine(folder, $"{SafeFileName(lecture.Name)} ({n}).md");

            string markdown = await ToMarkdown(lecture);
            await File.WriteAllTextAsync(path, markdown, new UTF8Encoding(false));

            // Renamed or moved to another course: remove the file under the old name.
            if (!string.IsNullOrEmpty(lecture.FilePath) &&
                !string.Equals(path, lecture.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(lecture.FilePath);
                }
                catch (Exception)
                {
                }
            }
            await LectureStore.SetFilePath(lecture.Id, path);
            lecture.FilePath = path;
            return path;
        }
    }
}
