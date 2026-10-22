using Avalonia.Controls;
using Avalonia.Interactivity;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class InfoPage : UserControl
    {
        public InfoPage()
        {
            InitializeComponent();
            Version.Content = UpdateChecker.CurrentVersionText;
            AutoCheckBox.IsChecked = Translator.Setting?.Lecture.AutoCheckUpdate ?? true;
            UpdateStatus.Text = UpdateChecker.CurrentBuild == null ? "开发版本，不检查更新" : "尚未检查";
            CheckUpdateButton.IsEnabled = UpdateChecker.CurrentBuild != null;
        }

        private async void CheckUpdateButton_Click(object? sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            UpdateLink.IsVisible = false;
            UpdateStatus.Text = "正在检查……";
            var info = await UpdateChecker.CheckAsync();
            CheckUpdateButton.IsEnabled = true;
            if (info == null)
            {
                UpdateStatus.Text = "检查失败，请稍后再试";
                return;
            }
            if (Translator.Setting != null)
                Translator.Setting.Lecture.LastUpdateCheck = DateTime.Now;
            if (!info.IsNewer)
            {
                UpdateStatus.Text = "已是最新版本";
                return;
            }
            UpdateStatus.Text = string.Empty;
            UpdateLink.Content = $"发现新版本 {info.Tag}";
            UpdateLink.NavigateUri = new Uri(info.Url);
            UpdateLink.IsVisible = true;
        }

        private void AutoCheckBox_Click(object? sender, RoutedEventArgs e)
        {
            if (Translator.Setting != null)
                Translator.Setting.Lecture.AutoCheckUpdate = AutoCheckBox.IsChecked == true;
        }
    }
}
