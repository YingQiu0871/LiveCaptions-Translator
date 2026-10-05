namespace LiveCaptionsTranslator.models
{
    public class SectionEntry
    {
        public long Id { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long FirstHistoryId { get; set; }
        public long LastHistoryId { get; set; }
        public string Summary { get; set; } = string.Empty;
        public int? PageNumber { get; set; }

        public string TimeRange => PageNumber == null
            ? $"{StartTime:HH:mm}–{EndTime:HH:mm}"
            : $"{StartTime:HH:mm}–{EndTime:HH:mm} · P{PageNumber}";
        public string Title
        {
            get
            {
                var lines = SummaryLines;
                return lines.Length > 0 ? lines[0].TrimStart('#', ' ') : string.Empty;
            }
        }
        public string Body => string.Join("\n", SummaryLines.Skip(1));

        private string[] SummaryLines => Summary
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public class HistoryLine
    {
        public long Id { get; set; }
        public DateTime Time { get; set; }
        public string SourceText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
    }
}
