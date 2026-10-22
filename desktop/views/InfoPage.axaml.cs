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
            ShowLatest();
            // The page is kept alive between visits, so show a result found in the meantime (startup check).
            AttachedToVisualTree += (s, e) => ShowLatest();
        }

        private void ShowLatest()
        {
            if (UpdateChecker.Latest != null && UpdateChecker.CurrentBuild != null)
                ShowUpdateResult(UpdateChecker.Latest);
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
                Translator.Setting.Lecture.LastUpdateCheck = DateTime.UtcNow;
            UpdateChecker.MarkNotified(info);
            ShowUpdateResult(info);
        }

        private void ShowUpdateResult(UpdateInfo info)
        {
            if (!info.IsNewer)
            {
                UpdateStatus.Text = "已是最新版本";
                UpdateLink.IsVisible = false;
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
