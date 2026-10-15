using System.ComponentModel;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // What the bilingual floating window shows: the last few finished sentence pairs and the sentence in progress.
    // Both apps draw it; this class only gathers the text. Changed is raised (at most every UPDATE_MS, on a
    // background thread) when something to show may have changed.
    public sealed class BilingualFeed : IDisposable
    {
        public record Pair(string Original, string Translation);

        public const int MIN_SENTENCES = 1;
        public const int MAX_SENTENCES = 5;
        private const int UPDATE_MS = 120;

        private readonly object gate = new();
        private readonly System.Threading.Timer timer;
        private bool scheduled = false;
        private bool disposed = false;
        private int reloadVersion = 0;
        private int sentences;
        private IReadOnlyList<Pair> finished = Array.Empty<Pair>();

        public event Action? Changed;

        public BilingualFeed(int sentences)
        {
            this.sentences = Math.Clamp(sentences, MIN_SENTENCES, MAX_SENTENCES);
            timer = new System.Threading.Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public int Sentences
        {
            get => sentences;
            set
            {
                sentences = Math.Clamp(value, MIN_SENTENCES, MAX_SENTENCES);
                _ = Reload();
            }
        }
        // Finished sentences after the class started; no hint is needed once there is anything to show.
        public bool SessionStarted => ClassSession.FirstHistoryId >= 0;
        public bool OriginalOnly => Translator.Setting.Lecture.SkipTranslation;
        public IReadOnlyList<Pair> Finished => finished;

        // The sentence being spoken now, or null if there is none or it was just logged as the newest finished one.
        public Pair? Current
        {
            get
            {
                string original = (Translator.Caption.OverlayOriginalCaption ?? string.Empty).Trim();
                if (Translator.Setting.Lecture.Engine != RecognitionEngine.Aliyun)
                {
                    // The system engine's caption holds several sentences; only the last one is in progress.
                    int end = original.Length > 1 ? original[..^1].LastIndexOfAny(TextUtil.PUNC_EOS) : -1;
                    original = original.Substring(end + 1).Trim();
                }
                if (original.Length == 0)
                    return null;
                var last = finished.Count > 0 ? finished[^1] : null;
                if (last != null && last.Original.Trim() == original)
                    return null;
                // The shown translation may still belong to the previous sentence.
                string source = Normalize(Translator.Caption.OverlayTranslationSource);
                string translation = source.Length > 0 && Normalize(original).StartsWith(source, StringComparison.Ordinal)
                    ? LectureDocument.CleanTranslation(Translator.Caption.OverlayCurrentTranslation)
                    : string.Empty;
                return new Pair(original, translation);
            }
        }

        private static string Normalize(string text)
        {
            return string.Concat((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
        }

        public void Start()
        {
            Translator.Caption.PropertyChanged += OnCaptionChanged;
            Translator.TranslationLogged += OnLogged;
            ClassSession.StateChanged += OnLogged;
            _ = Reload();
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                timer.Dispose();
            }
            Translator.Caption.PropertyChanged -= OnCaptionChanged;
            Translator.TranslationLogged -= OnLogged;
            ClassSession.StateChanged -= OnLogged;
        }

        private void OnCaptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "OverlayOriginalCaption" or "OverlayCurrentTranslation")
                Schedule();
        }

        private void OnLogged()
        {
            _ = Reload();
        }

        // Reads the last rows from the database. A newer reload makes an older one that is still running void.
        private async Task Reload()
        {
            int version = Interlocked.Increment(ref reloadVersion);
            var pairs = new List<Pair>();
            if (ClassSession.FirstHistoryId >= 0)
            {
                try
                {
                    var lines = await SectionLogger.LoadLastHistory(ClassSession.FirstHistoryId, sentences);
                    pairs.AddRange(lines.Select(line => new Pair(line.SourceText.Trim(), LectureDocument.CleanTranslation(line.TranslatedText))));
                }
                catch (Exception)
                {
                    return;
                }
            }
            if (version != Volatile.Read(ref reloadVersion))
                return;
            finished = pairs;
            Schedule();
        }

        private void Schedule()
        {
            lock (gate)
            {
                if (disposed || scheduled)
                    return;
                scheduled = true;
                timer.Change(UPDATE_MS, Timeout.Infinite);
            }
        }

        private void Fire()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                scheduled = false;
            }
            Changed?.Invoke();
        }
    }
}
