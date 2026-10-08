using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Windows.Automation;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public static class Translator
    {
        private static AutomationElement? window = null;
        private static Caption? caption = null;
        private static Setting? setting = null;

        // `Final`: a finished sentence that goes into the transcript; otherwise a preview of the sentence being spoken.
        private static readonly ConcurrentQueue<(string Text, bool Final)> pendingTextQueue = new();
        private static readonly TranslationTaskQueue translationTaskQueue = new();

        public static AutomationElement? Window
        {
            get => window;
            set => window = value;
        }
        public static Caption? Caption => caption;
        public static Setting? Setting => setting;

        public static bool LogOnlyFlag { get; set; } = false;
        public static bool FirstUseFlag { get; set; } = false;

        public static event Action? TranslationLogged;

        // Complete sentences already sent for translation, newest last. Used so that every sentence
        // is translated exactly once, even when LiveCaptions finishes several sentences between two reads.
        private static readonly LinkedList<string> committedSentences = new();
        private const int MAX_COMMITTED = 40;
        private const int MAX_NEW_PER_READ = 8;
        private static bool sentencesSeeded = false;

        // Called when a class starts: whatever LiveCaptions already shows belongs to before the class.
        public static void ResetSentences()
        {
            lock (committedSentences)
            {
                committedSentences.Clear();
                sentencesSeeded = false;
                lock (cloudLock)
                {
                    heldFragment = string.Empty;
                }
                recentChinese.Clear();
                if (Caption != null)
                    Caption.SourceWarning = string.Empty;
            }
        }

        private static bool IsCommitted(string sentence)
        {
            foreach (var committed in committedSentences)
            {
                if (committed.EndsWith(sentence, StringComparison.Ordinal))
                    return true;
            }
            // LiveCaptions keeps revising recent sentences; a revised one is not a new sentence. The text may
            // also start in the middle of a sentence that scrolled away, so compare with the same-length ending too.
            int checkedCount = 0;
            for (var node = committedSentences.Last; node != null && checkedCount < 10; node = node.Previous, checkedCount++)
            {
                string committed = node.Value;
                if (TextUtil.Similarity(committed, sentence) > 0.75)
                    return true;
                if (sentence.Length >= 12 && committed.Length > sentence.Length &&
                    TextUtil.Similarity(committed[^sentence.Length..], sentence) > 0.75)
                    return true;
            }
            return false;
        }

        public const string ALREADY_CHINESE_WARNING =
            "系统字幕送过来的已经是中文，多半是打开了系统实时辅助字幕自带的“翻译”。它会漏句、延迟，" +
            "中外对照也看不到外语原文。请到“设置”页点“显示系统实时辅助字幕”，在它的 ⚙️ 菜单里关掉“翻译”" +
            "（或把翻译语言设为“无”），再点“隐藏”。如果老师本来就讲中文，可以不管这条提示。";
        private static readonly Queue<bool> recentChinese = new();

        // LiveCaptions can translate by itself (Windows 11 24H2+). Then it hands over Chinese text, which hides the
        // original and loses sentences, so tell the user when the target is Chinese and so is everything we read.
        private static void CheckAlreadyTranslated(string sentence)
        {
            if (Setting?.Lecture.EnglishOnly == true ||
                !(Setting?.TargetLanguage ?? string.Empty).StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                recentChinese.Clear();
                Caption.SourceWarning = string.Empty;
                return;
            }
            int letters = sentence.Count(char.IsLetter);
            int han = sentence.Count(ch => ch >= '\u4E00' && ch <= '\u9FFF');
            recentChinese.Enqueue(letters > 0 && han * 2 > letters);
            while (recentChinese.Count > 6)
                recentChinese.Dequeue();
            int chinese = recentChinese.Count(isChinese => isChinese);
            if (recentChinese.Count >= 4 && chinese >= recentChinese.Count - 1)
                Caption.SourceWarning = ALREADY_CHINESE_WARNING;
            else if (chinese <= 1)
                Caption.SourceWarning = string.Empty;
        }

        private static void Commit(string sentence)
        {
            committedSentences.AddLast(sentence);
            while (committedSentences.Count > MAX_COMMITTED)
                committedSentences.RemoveFirst();
        }

        // The complete sentences (ending with EOS punctuation) in the text, in order.
        private static List<string> CompleteSentences(string text)
        {
            var sentences = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (Array.IndexOf(TextUtil.PUNC_EOS, text[i]) == -1)
                    continue;
                // Keep runs like "?!" or "..." together.
                while (i + 1 < text.Length && Array.IndexOf(TextUtil.PUNC_EOS, text[i + 1]) != -1)
                    i++;
                // "3.5" or "e.g." is not the end of a sentence: Western punctuation must be followed by a space.
                if (text[i] < 0x80 && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]))
                    continue;
                string sentence = text[start..(i + 1)].Trim();
                if (sentence.Length > 1)
                    sentences.Add(sentence);
                start = i + 1;
            }
            return sentences;
        }

        // Queues every sentence LiveCaptions finished since the last read, oldest first.
        private static bool QueueNewSentences(string fullText)
        {
            var sentences = CompleteSentences(fullText);
            bool queued = false;
            lock (committedSentences)
            {
                if (!sentencesSeeded)
                {
                    foreach (var sentence in sentences)
                        Commit(sentence);
                    sentencesSeeded = true;
                    return false;
                }
                // Even a first sentence cut off at the top of LiveCaptions' text is kept, unless it is
                // the end of a sentence we already have.
                int first = Math.Max(0, sentences.Count - MAX_NEW_PER_READ);
                for (int i = first; i < sentences.Count; i++)
                {
                    if (IsCommitted(sentences[i]))
                        continue;
                    Commit(sentences[i]);
                    CheckAlreadyTranslated(sentences[i]);
                    pendingTextQueue.Enqueue((sentences[i], true));
                    queued = true;
                }
            }
            return queued;
        }

        private static int lastPreviewLength = 0;

        // A very short "sentence" from the cloud recognizer ("I.", "So."), usually cut off by a pause: it is held
        // and put in front of the next sentence instead of becoming a line of its own.
        private static readonly object cloudLock = new();
        private static string heldFragment = string.Empty;
        private static readonly System.Threading.Timer fragmentTimer = new(_ => FlushFragment());
        private static readonly TimeSpan FRAGMENT_HOLD = TimeSpan.FromSeconds(4);

        private static bool HasCjk(string text) => text.Any(c => c >= 0x3000);

        private static bool IsFragment(string text) => HasCjk(text)
            ? text.Length < 6
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4;

        private static string JoinFragment(string text)
        {
            if (string.IsNullOrEmpty(heldFragment))
                return text;
            return heldFragment + (HasCjk(text) ? string.Empty : " ") + text;
        }

        // Text from the cloud recognizer: partial results while a sentence is spoken, then the final sentence.
        private static void OnCloudRecognized(string text, bool sentenceEnd)
        {
            if (!ClassSession.IsRunning || Caption == null)
                return;
            ClassSession.LastCaptionTime = DateTime.Now;
            lock (cloudLock)
            {
                text = JoinFragment(text);
                Caption.DisplayOriginalCaption = TextUtil.ShortenDisplaySentence(text, TextUtil.VERYLONG_THRESHOLD);
                Caption.OverlayOriginalCaption = text;

                if (sentenceEnd)
                {
                    lastPreviewLength = 0;
                    if (IsFragment(text))
                    {
                        heldFragment = text;
                        fragmentTimer.Change(FRAGMENT_HOLD, Timeout.InfiniteTimeSpan);
                        return;
                    }
                    heldFragment = string.Empty;
                    fragmentTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    CommitCloudSentence(text);
                }
                else if (text.Length - lastPreviewLength >= 40)
                {
                    // Translate a preview now and then, so a long sentence doesn't leave the translation empty.
                    lastPreviewLength = text.Length;
                    Caption.OriginalCaption = text;
                    pendingTextQueue.Enqueue((text, false));
                }
            }
        }

        // Nothing followed the short fragment: keep it after all.
        private static void FlushFragment()
        {
            lock (cloudLock)
            {
                if (string.IsNullOrEmpty(heldFragment) || !ClassSession.IsRunning || Caption == null)
                    return;
                string text = heldFragment;
                heldFragment = string.Empty;
                CommitCloudSentence(text);
            }
        }

        private static void CommitCloudSentence(string text)
        {
            Caption.OriginalCaption = text;
            lock (committedSentences)
            {
                Commit(text);
                CheckAlreadyTranslated(text);
            }
            pendingTextQueue.Enqueue((text, true));
        }

        static Translator()
        {
            if (!File.Exists(AppPaths.SettingFile))
                FirstUseFlag = true;

            caption = Caption.GetInstance();
            setting = Setting.Load();
            CloudAsr.Recognized += OnCloudRecognized;

            // LiveCaptions runs its own speech recognition all the time, so it is not started at all when the
            // cloud engine is used.
            if (setting.Lecture.Engine == RecognitionEngine.LiveCaptions)
            {
                window = LiveCaptionsHandler.LaunchLiveCaptions();
                LiveCaptionsHandler.FixLiveCaptions(Window);
                LiveCaptionsHandler.HideLiveCaptions(Window);
            }
        }

        // Reading LiveCaptions over UI Automation costs CPU on both sides, so it is read often only while the text
        // is changing. `idleCount` counts in steps of 25 ms whatever the actual interval.
        private const int ACTIVE_POLL_MS = 50;
        private const int IDLE_POLL_MS = 150;
        private const int FAST_POLL_TICKS = 16;

        public static void SyncLoop()
        {
            int idleCount = 0;
            int syncCount = 0;
            bool pauseHandled = false;
            string lastRawText = string.Empty;

            // The sentence still being spoken: translate a preview now and then, so the user is not waiting
            // for the full stop; when the speaker pauses without LiveCaptions adding a full stop, the sentence is over.
            void HandlePause()
            {
                if (Caption.OriginalCaption.Length == 0)
                    return;
                bool unfinished = Array.IndexOf(TextUtil.PUNC_EOS, Caption.OriginalCaption[^1]) == -1;
                if (unfinished && idleCount >= Setting.MaxIdleInterval && !pauseHandled)
                {
                    pauseHandled = true;
                    syncCount = 0;
                    string sentence = Caption.OriginalCaption.Trim();
                    lock (committedSentences)
                    {
                        if (Encoding.UTF8.GetByteCount(sentence) >= TextUtil.SHORT_THRESHOLD && !IsCommitted(sentence))
                        {
                            Commit(sentence);
                            CheckAlreadyTranslated(sentence);
                            pendingTextQueue.Enqueue((sentence, true));
                        }
                    }
                }
                else if (unfinished && syncCount > Setting.MaxSyncInterval)
                {
                    syncCount = 0;
                    pendingTextQueue.Enqueue((Caption.OriginalCaption, false));
                }
            }

            while (true)
            {
                if (Window == null)
                {
                    Thread.Sleep(2000);
                    continue;
                }
                if (!ClassSession.IsRunning || Setting.Lecture.Engine != RecognitionEngine.LiveCaptions)
                {
                    Thread.Sleep(500);
                    continue;
                }

                string fullText = string.Empty;
                try
                {
                    // Check LiveCaptions.exe still alive
                    _ = Window.Current.Name;
                    // Get the text recognized by LiveCaptions (10-20ms)
                    fullText = LiveCaptionsHandler.GetCaptions(Window);
                }
                catch (ElementNotAvailableException)
                {
                    Window = null;
                    continue;
                }
                if (string.IsNullOrEmpty(fullText))
                {
                    Thread.Sleep(IDLE_POLL_MS);
                    continue;
                }
                ClassSession.LastCaptionTime = DateTime.Now;

                int pollMs = idleCount < FAST_POLL_TICKS ? ACTIVE_POLL_MS : IDLE_POLL_MS;
                // Nothing changed (a pause, or silence): skip the parsing and only count the idle time.
                if (string.CompareOrdinal(fullText, lastRawText) == 0)
                {
                    idleCount += pollMs / 25;
                    HandlePause();
                    Thread.Sleep(pollMs);
                    continue;
                }
                lastRawText = fullText;

                // Preprocess
                fullText = RegexPatterns.Acronym().Replace(fullText, "$1$2");
                fullText = RegexPatterns.AcronymWithWords().Replace(fullText, "$1 $2");
                fullText = RegexPatterns.PunctuationSpace().Replace(fullText, "$1 ");
                fullText = RegexPatterns.CJPunctuationSpace().Replace(fullText, "$1");
                // Note: For certain languages (such as Japanese), LiveCaptions excessively uses `\n`.
                // Replace redundant `\n` within sentences with comma or period.
                fullText = TextUtil.ReplaceNewlines(fullText, TextUtil.MEDIUM_THRESHOLD);

                // Prevent adding the last sentence from previous running to log cards
                // before the first sentence is completed.
                if (fullText.IndexOfAny(TextUtil.PUNC_EOS) == -1 && Caption.Contexts.Count > 0)
                    ClearContexts();

                // Get the last sentence.
                int lastEOSIndex;
                if (Array.IndexOf(TextUtil.PUNC_EOS, fullText[^1]) != -1)
                    lastEOSIndex = fullText[0..^1].LastIndexOfAny(TextUtil.PUNC_EOS);
                else
                    lastEOSIndex = fullText.LastIndexOfAny(TextUtil.PUNC_EOS);
                string latestCaption = fullText.Substring(lastEOSIndex + 1);

                // If the last sentence is too short, extend it by adding the previous sentence.
                // Note: LiveCaptions may generate multiple characters including EOS at once.
                if (lastEOSIndex > 0 && Encoding.UTF8.GetByteCount(latestCaption) < TextUtil.SHORT_THRESHOLD)
                {
                    lastEOSIndex = fullText[0..lastEOSIndex].LastIndexOfAny(TextUtil.PUNC_EOS);
                    latestCaption = fullText.Substring(lastEOSIndex + 1);
                }

                // `OverlayOriginalCaption`: The sentence to be displayed on Overlay Window.
                Caption.OverlayOriginalCaption = latestCaption;
                for (int historyCount = Math.Min(Setting.DisplaySentences, Caption.Contexts.Count);
                     historyCount > 0 && lastEOSIndex > 0;
                     historyCount--)
                {
                    lastEOSIndex = fullText[0..lastEOSIndex].LastIndexOfAny(TextUtil.PUNC_EOS);
                    Caption.OverlayOriginalCaption = fullText.Substring(lastEOSIndex + 1);
                }

                // `DisplayOriginalCaption`: The sentence to be displayed on Main Window.
                if (string.CompareOrdinal(Caption.DisplayOriginalCaption, latestCaption) != 0)
                {
                    Caption.DisplayOriginalCaption = latestCaption;
                    // If the last sentence is too long, truncate it when displayed.
                    Caption.DisplayOriginalCaption =
                        TextUtil.ShortenDisplaySentence(Caption.DisplayOriginalCaption, TextUtil.VERYLONG_THRESHOLD);
                }

                // Prepare for `OriginalCaption`. If Expanded, only retain the complete sentence.
                int lastEOS = latestCaption.LastIndexOfAny(TextUtil.PUNC_EOS);
                if (lastEOS != -1)
                    latestCaption = latestCaption.Substring(0, lastEOS + 1);
                // `OriginalCaption`: The sentence to be really translated.
                if (string.CompareOrdinal(Caption.OriginalCaption, latestCaption) != 0)
                {
                    Caption.OriginalCaption = latestCaption;

                    idleCount = 0;
                    pauseHandled = false;
                    if (Array.IndexOf(TextUtil.PUNC_EOS, Caption.OriginalCaption[^1]) == -1 &&
                        Encoding.UTF8.GetByteCount(Caption.OriginalCaption) >= TextUtil.SHORT_THRESHOLD)
                        syncCount++;
                }
                else
                    idleCount += pollMs / 25;

                // Every finished sentence is translated once, including ones that scrolled by between reads.
                if (QueueNewSentences(fullText))
                    syncCount = 0;

                HandlePause();

                Thread.Sleep(pollMs);
            }
        }

        public static async Task TranslateLoop()
        {
            while (true)
            {
                // Check LiveCaptions.exe still alive. It is only needed (and only kept running, since its own speech
                // recognition costs power) for a class that uses the system engine.
                if (Window == null && ClassSession.IsRunning &&
                    Setting.Lecture.Engine == RecognitionEngine.LiveCaptions)
                {
                    Caption.DisplayTranslatedCaption = "[WARNING] 实时辅助字幕意外关闭，正在重启…";
                    Window = LiveCaptionsHandler.LaunchLiveCaptions();
                    LiveCaptionsHandler.FixLiveCaptions(Window);
                    LiveCaptionsHandler.HideLiveCaptions(Window);
                    Caption.DisplayTranslatedCaption = "";
                }

                // Translate
                if (pendingTextQueue.TryDequeue(out var pending))
                {
                    var (originalSnapshot, isFinal) = pending;

                    // LiveCaptions also hears our own speech when it plays on the default output device.
                    if (Speaker.SuppressCaptions)
                        continue;

                    if (LogOnlyFlag)
                    {
                        if (isFinal)
                            await LogOnly(originalSnapshot);
                    }
                    else
                    {
                        if (Setting.Lecture.SkipTranslation)
                        {
                            Caption.TranslatedCaption = string.Empty;
                            Caption.DisplayTranslatedCaption = string.Empty;
                            if (isFinal)
                                await Log(originalSnapshot, string.Empty);
                        }
                        else
                            translationTaskQueue.Enqueue(token => Task.Run(
                                () => Translate(originalSnapshot, token), token), originalSnapshot, isFinal);
                    }
                }

                await Task.Delay(ClassSession.IsRunning || !pendingTextQueue.IsEmpty ? 40 : 300);
            }
        }

        public static async Task DisplayLoop()
        {
            while (true)
            {
                var (translatedText, isChoke) = translationTaskQueue.Output;

                if (Setting.Lecture.SkipTranslation)
                {
                    Caption.TranslatedCaption = string.Empty;
                    Caption.DisplayTranslatedCaption = string.Empty;
                    Caption.OverlayNoticePrefix = string.Empty;
                    Caption.OverlayCurrentTranslation = string.Empty;
                }
                else if (LogOnlyFlag)
                {
                    Caption.TranslatedCaption = string.Empty;
                    Caption.DisplayTranslatedCaption = "[已暂停]";
                    Caption.OverlayNoticePrefix = "[已暂停]";
                    Caption.OverlayCurrentTranslation = string.Empty;
                }
                else if (!string.IsNullOrEmpty(RegexPatterns.NoticePrefix().Replace(
                             translatedText, string.Empty).Trim()) &&
                         string.CompareOrdinal(Caption.TranslatedCaption, translatedText) != 0)
                {
                    // Main page
                    Caption.TranslatedCaption = translatedText;
                    Caption.DisplayTranslatedCaption =
                        TextUtil.ShortenDisplaySentence(Caption.TranslatedCaption, TextUtil.VERYLONG_THRESHOLD);

                    // Overlay window
                    if (Caption.TranslatedCaption.Contains("[ERROR]") || Caption.TranslatedCaption.Contains("[WARNING]"))
                        Caption.OverlayCurrentTranslation = Caption.TranslatedCaption;
                    else
                    {
                        var match = RegexPatterns.NoticePrefixAndTranslation().Match(Caption.TranslatedCaption);
                        Caption.OverlayNoticePrefix = match.Groups[1].Value.Trim();
                        Caption.OverlayCurrentTranslation = match.Groups[2].Value.Trim();
                    }
                }

                // If the original sentence is a complete sentence, choke for better visual experience.
                if (isChoke)
                    await Task.Delay(720);
                // Translations still arrive for a moment after stopping; after that nothing changes here.
                await Task.Delay(ClassSession.IsRunning || translationTaskQueue.IsBusy ? 40 : 300);
            }
        }

        public static async Task<(string, bool)> Translate(string text, CancellationToken token = default)
        {
            string translatedText;
            bool isChoke = Array.IndexOf(TextUtil.PUNC_EOS, text[^1]) != -1;

            try
            {
                var sw = Setting.MainWindow.LatencyShow ? Stopwatch.StartNew() : null;

                if (Setting.ContextAware && !TranslateAPI.IsLLMBased)
                {
                    translatedText = await TranslateAPI.TranslateFunction($"{Caption.AwareContextsCaption} 🔤 {text} 🔤", token);
                    translatedText = RegexPatterns.TargetSentence().Match(translatedText).Groups[1].Value;
                }
                else
                {
                    translatedText = await TranslateAPI.TranslateFunction(text, token);
                    translatedText = translatedText.Replace("🔤", "");
                }

                if (sw != null)
                {
                    sw.Stop();
                    translatedText = $"[{sw.ElapsedMilliseconds,4} ms] " + translatedText;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ($"[ERROR] 翻译失败：{ex.Message}", isChoke);
            }

            return (translatedText, isChoke);
        }

        public static async Task Log(string originalText, string translatedText)
        {
            string targetLanguage, apiName;
            if (Setting != null)
            {
                targetLanguage = Setting.TargetLanguage;
                apiName = Setting.ApiName;
            }
            else
            {
                targetLanguage = "N/A";
                apiName = "N/A";
            }

            try
            {
                await SQLiteHistoryLogger.LogTranslation(originalText, translatedText, targetLanguage, apiName);
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 写入历史记录失败。", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
            }
        }

        public static async Task LogOnly(string originalText)
        {
            try
            {
                await SQLiteHistoryLogger.LogTranslation(originalText, "N/A", "N/A", "LogOnly");
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 写入历史记录失败。", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
            }
        }

        public static async Task AddContexts(CancellationToken token = default)
        {
            var lastLog = await SQLiteHistoryLogger.LoadLastTranslation(token);
            if (lastLog == null)
                return;

            if (Caption?.Contexts.Count >= Caption.MAX_CONTEXTS)
                Caption.Contexts.Dequeue();
            Caption?.Contexts.Enqueue(lastLog);

            Caption?.OnPropertyChanged("DisplayLogCards");
            Caption?.OnPropertyChanged("OverlayPreviousTranslation");
        }

        // Closes LiveCaptions when it is not needed (cloud engine), to save the power its recognition uses.
        public static void CloseLiveCaptions()
        {
            var current = Window;
            if (current == null)
                return;
            Window = null;
            try
            {
                LiveCaptionsHandler.KillLiveCaptions(current);
            }
            catch (Exception)
            {
            }
        }

        // Starts LiveCaptions if it is not running (e.g. to show it for its first-time setup).
        public static AutomationElement EnsureLiveCaptions()
        {
            var current = Window;
            if (current != null)
            {
                try
                {
                    _ = current.Current.Name;
                    return current;
                }
                catch (ElementNotAvailableException)
                {
                }
            }
            current = LiveCaptionsHandler.LaunchLiveCaptions();
            LiveCaptionsHandler.FixLiveCaptions(current);
            LiveCaptionsHandler.HideLiveCaptions(current);
            Window = current;
            return current;
        }

        // Restarts LiveCaptions, e.g. after the default microphone changed. `TranslateLoop` relaunches it.
        public static void RestartLiveCaptions()
        {
            var window = Window;
            if (window == null)
                return;
            try
            {
                LiveCaptionsHandler.KillLiveCaptions(window);
            }
            catch (Exception)
            {
            }
        }

        public static void ClearContexts()
        {
            Caption?.Contexts.Clear();

            Caption?.OnPropertyChanged("DisplayLogCards");
            Caption?.OnPropertyChanged("OverlayPreviousTranslation");
        }


    }
}
