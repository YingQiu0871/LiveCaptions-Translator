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

        public static event Action? StateChanged;

        public static async Task Start()
        {
            if (IsRunning || IsStarting)
                return;
            IsStarting = true;
            StateChanged?.Invoke();
            try
            {
                SetHint("正在启动实时辅助字幕……");
                string? problem = await Task.Run(Prepare);
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
                IsRunning = true;
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

        public static void Stop()
        {
            if (!IsRunning)
                return;
            IsRunning = false;
            generation++;
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

        private static void SetHint(string text)
        {
            if (Translator.Caption != null)
                Translator.Caption.StatusHint = text;
        }
    }
}
