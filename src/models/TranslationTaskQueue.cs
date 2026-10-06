using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.models
{
    public class TranslationTaskQueue
    {
        private readonly object _lock = new object();
        private readonly List<TranslationTask> tasks;
        // Results are logged strictly in the order the sentences were spoken.
        private Task logChain = Task.CompletedTask;
        private long displayedSeq = -1;
        private long nextSeq = 0;

        private (string translatedText, bool isChoke) output;
        public (string translatedText, bool isChoke) Output => output;

        public TranslationTaskQueue()
        {
            tasks = new List<TranslationTask>();
            output = (string.Empty, false);
        }

        public void Enqueue(Func<CancellationToken, Task<(string, bool)>> worker, string originalText)
        {
            lock (_lock)
            {
                var newTranslationTask = new TranslationTask(worker, originalText, new CancellationTokenSource(), nextSeq++);
                tasks.Add(newTranslationTask);
                logChain = logChain.ContinueWith(_ => LogInOrder(newTranslationTask)).Unwrap();
                // Run `OnTaskCompleted` in a new thread.
                newTranslationTask.Task.ContinueWith(
                    task => OnTaskCompleted(newTranslationTask),
                    TaskContinuationOptions.OnlyOnRanToCompletion
                );
            }
        }

        private void OnTaskCompleted(TranslationTask translationTask)
        {
            lock (_lock)
            {
                // Previews of an unfinished sentence are outdated once a later one is ready; finished sentences
                // are never dropped, so every sentence ends up in the transcript.
                var index = tasks.IndexOf(translationTask);
                for (int i = index - 1; i >= 0; i--)
                {
                    if (!tasks[i].IsComplete)
                    {
                        tasks[i].CTS.Cancel();
                        tasks.RemoveAt(i);
                    }
                }
                tasks.Remove(translationTask);

                if (translationTask.Seq > displayedSeq)
                {
                    displayedSeq = translationTask.Seq;
                    output = translationTask.Task.Result;
                }
            }
        }

        private async Task LogInOrder(TranslationTask translationTask)
        {
            (string translatedText, bool isChoke) result;
            try
            {
                result = await translationTask.Task;
            }
            catch (Exception)
            {
                // Cancelled preview, or a failed request: nothing to log.
                return;
            }

            try
            {
                bool isOverwrite = await Translator.IsOverwrite(translationTask.OriginalText);
                if (!isOverwrite)
                    await Translator.AddContexts();
                await Translator.Log(translationTask.OriginalText, result.translatedText, isOverwrite);

                // Read complete sentences aloud (only when the speak mode includes translations).
                if (result.isChoke)
                    Speaker.EnqueueTranslation(translationTask.OriginalText, result.translatedText);
            }
            catch (Exception)
            {
            }
        }
    }

    public class TranslationTask
    {
        public Task<(string, bool)> Task { get; }
        public string OriginalText { get; }
        public CancellationTokenSource CTS { get; }
        public long Seq { get; }
        public bool IsComplete => OriginalText.Length > 0 &&
                                  Array.IndexOf(TextUtil.PUNC_EOS, OriginalText[^1]) != -1;

        public TranslationTask(Func<CancellationToken, Task<(string, bool)>> worker,
            string originalText, CancellationTokenSource cts, long seq)
        {
            Task = worker(cts.Token);
            OriginalText = originalText;
            CTS = cts;
            Seq = seq;
        }
    }
}