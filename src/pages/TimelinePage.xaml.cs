using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class TimelinePage : Page
    {
        public const int MIN_HEIGHT = 360;

        private List<SectionEntry> sections = new();

        public TimelinePage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();

            DayPicker.SelectedDate = DateTime.Today;

            Loaded += async (s, e) =>
            {
                (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(minHeight: MIN_HEIGHT, maxHeight: MIN_HEIGHT);
                Summarizer.SectionSummarized += OnSectionSummarized;
                Summarizer.CurrentPageChanged += OnCurrentPageChanged;
                ShowSlidesInfo();
                await LoadSections();
            };
            Unloaded += (s, e) =>
            {
                Summarizer.SectionSummarized -= OnSectionSummarized;
                Summarizer.CurrentPageChanged -= OnCurrentPageChanged;
            };
        }

        private DateTime SelectedDay => DayPicker.SelectedDate ?? DateTime.Today;

        private void OnSectionSummarized(SectionEntry section)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (section.StartTime.Date == SelectedDay.Date)
                    await LoadSections(scrollToEnd: true);
            });
        }

        private void OnCurrentPageChanged(int? page)
        {
            Dispatcher.InvokeAsync(ShowSlidesInfo);
        }

        private void ShowSlidesInfo()
        {
            if (!SlideDeck.IsLoaded)
            {
                SlidesInfo.Text = "未载入课件：按语义分节";
                ClearSlides.Visibility = Visibility.Collapsed;
                return;
            }
            string page = Summarizer.CurrentPage == null ? "识别中" : $"第 {Summarizer.CurrentPage} 页";
            SlidesInfo.Text = $"{SlideDeck.FileName}（共 {SlideDeck.Pages.Count} 页，当前{page}）";
            ClearSlides.Visibility = Visibility.Visible;
        }

        private async void LoadSlides_click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                await SlideDeck.Load(dialog.FileName);
                SnackbarHost.Show("课件已载入。", $"共 {SlideDeck.Pages.Count} 页，之后按页码分节。",
                    SnackbarType.Success, timeout: 2);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 课件载入失败。", ex.Message, SnackbarType.Error,
                    timeout: 3, closeButton: true);
            }
            ShowSlidesInfo();
        }

        private void ClearSlides_click(object sender, RoutedEventArgs e)
        {
            SlideDeck.Clear();
            ShowSlidesInfo();
        }

        private async Task LoadSections(bool scrollToEnd = false)
        {
            try
            {
                sections = await SectionLogger.LoadSections(SelectedDay);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 读取时间线失败。", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
                return;
            }
            SectionList.ItemsSource = sections;
            EmptyHint.Visibility = sections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (scrollToEnd)
                SectionScroll.ScrollToEnd();
        }

        private async void DayPicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                await LoadSections();
        }

        private void EndSection_click(object sender, RoutedEventArgs e)
        {
            Summarizer.RequestEndSection();
            SnackbarHost.Show("正在总结本节……", "", SnackbarType.Info, timeout: 1);
        }

        private async void Refresh_click(object sender, RoutedEventArgs e)
        {
            await LoadSections();
        }

        private async void Export_click(object sender, RoutedEventArgs e)
        {
            if (sections.Count == 0)
            {
                SnackbarHost.Show("这一天没有可导出的小节。", "", SnackbarType.Warning, timeout: 2);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Markdown (*.md)|*.md",
                DefaultExt = ".md",
                FileName = $"课堂笔记_{SelectedDay:yyyy-MM-dd}.md"
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string markdown = await BuildMarkdown(SelectedDay, sections);
                await File.WriteAllTextAsync(dialog.FileName, markdown, new UTF8Encoding(false));
                SnackbarHost.Show("已导出。", dialog.FileName, SnackbarType.Success, timeout: 2);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 导出失败。", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
            }
        }

        public static async Task<string> BuildMarkdown(DateTime day, List<SectionEntry> sections)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# 课堂笔记 {day:yyyy-MM-dd}");
            sb.AppendLine();

            sb.AppendLine("## 时间线");
            sb.AppendLine();
            for (int i = 0; i < sections.Count; i++)
                sb.AppendLine($"{i + 1}. **{sections[i].TimeRange}** {sections[i].Title}");
            sb.AppendLine();

            for (int i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                sb.AppendLine($"## {i + 1}. {section.TimeRange} {section.Title}");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(section.Body))
                {
                    sb.AppendLine(section.Body);
                    sb.AppendLine();
                }

                var lines = await SectionLogger.LoadHistoryRange(section.FirstHistoryId - 1, section.LastHistoryId);
                if (lines.Count == 0)
                    continue;

                sb.AppendLine("<details><summary>原文与译文</summary>");
                sb.AppendLine();
                foreach (var line in lines)
                {
                    string translated = RegexPatterns.NoticePrefix().Replace(line.TranslatedText, string.Empty).Trim();
                    sb.AppendLine($"- `{line.Time:HH:mm:ss}` {line.SourceText}");
                    if (!string.IsNullOrEmpty(translated) && translated != "N/A" && !translated.StartsWith("[ERROR]"))
                        sb.AppendLine($"  - {translated}");
                }
                sb.AppendLine();
                sb.AppendLine("</details>");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
