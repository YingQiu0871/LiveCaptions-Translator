using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LiveCaptionsTranslator
{
    public partial class WelcomeWindow : Window
    {
        public WelcomeWindow()
        {
            InitializeComponent();
            string permission = OperatingSystem.IsMacOS()
                ? "第一次听电脑声音或麦克风时，macOS 会请求“屏幕录制”或“麦克风”权限，请允许。"
                : "Linux 上靠 PulseAudio / PipeWire 采集声音，需要安装 pulseaudio-utils（parec）。";
            Steps.Text = "本工具用阿里云实时语音识别听课，再用大模型翻译和总结。" +
                         "第一次使用前，请按下面的步骤完成设置：\n" +
                         "\n1. 在本工具“设置”页“〇 输入源”里填入阿里云百炼的 API Key，点“测试连接”。" +
                         "\n2. 在“① 模型与 API”里选 DeepSeek 或通义千问并填入 API Key（小节总结和翻译用）。" +
                         "\n3. 在“〇 输入源”里选“听哪里的声音”：网课选电脑播放的声音，线下课选麦克风。" +
                         $"\n{permission}\n" +
                         "\n程序已经自动跳到设置页。设置完成后，切回“字幕”页点右上角的“开始”就能看到实时翻译了。" +
                         "所有设置都在“设置”页。";
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
