namespace LiveCaptionsTranslator.models
{
    // A course folder in the history: holds the recordings of one course.
    public class CourseEntry
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int LectureCount { get; set; }
        public DateTime? LastTime { get; set; }

        public string Info => LectureCount == 0
            ? "还没有录音"
            : $"{LectureCount} 节课" + (LastTime is DateTime last ? $" · 最近 {last:yyyy-MM-dd}" : string.Empty);
    }

    // One recording, from "开始" to "停止". Its sentences are the history rows in (FirstHistoryId, LastHistoryId].
    public class LectureRecord
    {
        public long Id { get; set; }
        public long CourseId { get; set; }
        public string CourseName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public long FirstHistoryId { get; set; }
        // Null while recording, or when the program was closed during a recording.
        public long? LastHistoryId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public int SentenceCount { get; set; }

        public string Info
        {
            get
            {
                string time = EndTime is DateTime end
                    ? $"{StartTime:yyyy-MM-dd HH:mm}–{end:HH:mm}"
                    : $"{StartTime:yyyy-MM-dd HH:mm}";
                return $"{time} · {SentenceCount} 句";
            }
        }
    }

    // A section of a lecture in the transcript view: its summary, then what was said.
    public class LectureSectionView
    {
        public string Header { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<HistoryLine> Lines { get; set; } = new();

        public System.Windows.Visibility SummaryVisibility => string.IsNullOrWhiteSpace(Summary)
            ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }
}
