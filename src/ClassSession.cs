using System.Windows.Automation;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // Start / stop of a class: captions are only read and translated while a session runs.
    public static class ClassSession
    {
        public const string IDLE_HINT = "点击右上角的“开始”按钮开始上课。";
        private const int NO_CAPTION_WARNING_SECONDS = 15;

        private static int generation = 0;

        public static bool IsRunning { get; private set; } = false;
        public static bool IsStarting { get; private set; } = false;
        public static DateTime LastCaptionTime { get; set; } = DateTime.MinValue;
        // History rows after this id belong to the current class; -1 before the first start.
        public static long FirstHistoryId { get; private set; } = -1;
        // The recording in the history that the running class is saved as; -1 if none.
        public static long CurrentLectureId { get; private set; } = -1;

        public static event Action? StateChanged;

        public static async Task Start()
        {
            if (IsRunning || IsStarting)
                return;
            IsStarting = true;
            StateChanged?.Invoke();
            try
            {
                bool cloud = Translator.Setting.Lecture.Engine == RecognitionEngine.Aliyun;
                if (cloud && string.IsNullOrWhiteSpace(Translator.Setting.Lecture.AsrApiKey))
                {
                    SetHint("识别方式选了阿里云，但还没有填阿里云 API Key：请到“设置”页的“⓪ 输入源”里填写。");
                    return;
                }
                SetHint(cloud ? "正在连接阿里云语音识别……" : "正在启动实时辅助字幕……");
                string? problem = cloud ? null : await Task.Run(Prepare);
                try
                {
                    FirstHistoryId = await SectionLogger.GetMaxHistoryId();
                }
                catch (Exception)
                {
                    FirstHistoryId = 0;
                }

                LastCaptionTime = DateTime.MinValue;
                Translator.ResetSentences();
                Refiner.Reset(FirstHistoryId);
                Summarizer.StartAt(FirstHistoryId);
                await StartLecture();
                IsRunning = true;
                if (cloud)
                    Translator.CloseLiveCaptions();    // Not needed; its own recognition would only cost power.
                if (cloud)
                    CloudAsr.Start(Translator.Setting.Lecture.InputSource == InputSource.Microphone);
                SetHint(problem ?? "已开始，正在听……说话或播放课程声音后，原文会出现在这里。");
                _ = WatchForSilence(++generation, problem);
            }
            catch (Exception ex)
            {
                SetHint($"启动失败：{ex.Message}\n请到“设置”页点“显示系统实时辅助字幕”，确认系统的实时辅助字幕能正常打开。");
            }
            finally
            {
                IsStarting = false;
                StateChanged?.Invoke();
            }
        }

        // Every class gets its own entry in the history, in the course chosen last time.
        private static async Task StartLecture()
        {
            var now = DateTime.Now;
            string course = LectureStore.CleanCourseName(Translator.Setting.Lecture.CurrentCourse);
            try
            {
                CurrentLectureId = await LectureStore.StartLecture(course, $"{now:yyyy-MM-dd HH:mm} 课堂", now, FirstHistoryId);
            }
            catch (Exception)
            {
                CurrentLectureId = -1;
            }
        }

        // After `Stop`: waits for the last sentences to be translated, closes the recording and summarizes its
        // last section. Returns the recording, or null if there is none.
        public static async Task<LectureRecord?> FinishLecture(long lectureId)
        {
            if (lectureId < 0)
                return null;
            // Translations still on their way are logged a moment after stopping.
            long last = await SectionLogger.GetMaxHistoryId();
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(1000);
                long now = await SectionLogger.GetMaxHistoryId();
                if (now == last && i >= 1)
                    break;
                last = now;
            }
            // A new class was started meanwhile: its sentences are not part of this one.
            if (IsRunning && CurrentLectureId != lectureId)
                last = Math.Min(last, FirstHistoryId);
            await LectureStore.FinishLecture(lectureId, DateTime.Now, last);
            try
            {
                await Summarizer.FinishLecture(last);
            }
            catch (Exception)
            {
                // The transcript is saved anyway; only the summary of the last section is missing.
            }
            return await LectureStore.GetLecture(lectureId);
        }

        public static void Stop()
        {
            if (!IsRunning)
                return;
            IsRunning = false;
            generation++;
            CloudAsr.Stop();
            Speaker.StopAll();
            SetHint("已停止。点击右上角的“开始”继续。");
            StateChanged?.Invoke();
        }

        // Makes sure LiveCaptions runs and listens to the chosen input. Returns a warning to show, if any.
        private static string? Prepare()
        {
            var window = Translator.Window;
            if (!IsAlive(window))
            {
                window = LiveCaptionsHandler.LaunchLiveCaptions();
                LiveCaptionsHandler.FixLiveCaptions(window);
                LiveCaptionsHandler.HideLiveCaptions(window);
                Translator.Window = window;
            }

            bool useMicrophone = Translator.Setting.Lecture.InputSource == InputSource.Microphone;
            SetHint(useMicrophone ? "正在打开实时字幕的“包含麦克风音频”……" : "正在关闭实时字幕的“包含麦克风音频”……");
            bool microphoneSet;
            try
            {
                microphoneSet = LiveCaptionsMicrophone.Set(window, useMicrophone);
            }
            catch (Exception)
            {
                microphoneSet = false;
            }

            if (!LiveCaptionsHandler.HasCaptionsTextBlock(window))
            {
                return "找不到实时辅助字幕的文字区域，可能是系统字幕还没完成首次设置。" +
                       "请到“设置”页点“显示系统实时辅助字幕”，按系统提示下载语音识别文件后再点“开始”。";
            }

            if (!microphoneSet && useMicrophone)
            {
                LiveCaptionsMicrophone.ShowSettings(window);
                return "已开始，但没能自动打开麦克风。已弹出实时辅助字幕的设置菜单：" +
                       "请在“首选项”里勾选“包含麦克风音频”，再点本工具“设置”页的“隐藏系统实时辅助字幕”。";
            }
            return null;
        }

        private static async Task WatchForSilence(int session, string? problem)
        {
            await Task.Delay(TimeSpan.FromSeconds(NO_CAPTION_WARNING_SECONDS));
            if (session != generation || !IsRunning || LastCaptionTime != DateTime.MinValue)
                return;

            bool useMicrophone = Translator.Setting.Lecture.InputSource == InputSource.Microphone;
            if (Translator.Setting.Lecture.Engine == RecognitionEngine.Aliyun)
            {
                SetHint($"已经 {NO_CAPTION_WARNING_SECONDS} 秒没有识别到文字。" + (useMicrophone
                    ? "现在听的是 Windows 默认麦克风，请确认它没有静音，并在“设置 ⓪”里选对了麦克风。"
                    : "现在听的是电脑正在播放的声音（Windows 默认输出设备），请确认课程声音正在播放、没有静音。"));
                return;
            }
            string advice = useMicrophone
                ? "现在听的是麦克风。请确认实时辅助字幕“首选项”里已勾选“包含麦克风音频”，" +
                  "并在“设置 ⓪”里选对了麦克风（不要选蓝牙耳机的麦克风）。"
                : "现在只听电脑播放的声音，线下上课请在“设置 ⓪”里改成“麦克风”。";
            SetHint($"已经 {NO_CAPTION_WARNING_SECONDS} 秒没有识别到文字。{advice}" +
                    "\n也可以到“设置”页点“显示系统实时辅助字幕”，看系统的实时辅助字幕窗口里有没有出字。" +
                    (problem == null ? string.Empty : $"\n{problem}"));
        }

        private static bool IsAlive(AutomationElement? window)
        {
            if (window == null)
                return false;
            try
            {
                _ = window.Current.Name;
                return true;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        static ClassSession()
        {
            // Connection problems of the cloud recognizer are shown above the current sentence.
            CloudAsr.StatusChanged += status =>
            {
                if (Translator.Caption == null || !IsRunning)
                    return;
                if (string.IsNullOrEmpty(status))
                {
                    if (Translator.Caption.SourceWarning.StartsWith("阿里云") || Translator.Caption.SourceWarning.StartsWith("正在连接阿里云"))
                        Translator.Caption.SourceWarning = string.Empty;
                }
                else
                    Translator.Caption.SourceWarning = status;
            };
        }

        private static void SetHint(string text)
        {
            if (Translator.Caption != null)
                Translator.Caption.StatusHint = text;
        }
    }
}
