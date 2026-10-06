using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using LiveCaptionsTranslator.Utils;

namespace LiveCaptionsTranslator
{
    public partial class CaptionPage : Page
    {
        private static CaptionPage instance;
        public static CaptionPage Instance => instance;

        public CaptionPage()
        {
            InitializeComponent();
            DataContext = Translator.Caption;
            instance = this;

            SectionColumn.Width = new GridLength(Math.Max(Translator.Setting.MainWindow.SectionPanelWidth, 180));
            ScrollHelper.UseOwnScrollViewer(this, TranscriptScroll, SectionScroll);

            Loaded += async (s, e) =>
            {
                AutoHeight();
                Summarizer.SectionSummarized += OnSectionSummarized;
                Translator.TranslationLogged += OnTranslationLogged;
                ClassSession.StateChanged += OnTranslationLogged;
                await LoadTranscript();
                await LoadSections(scrollToEnd: true);
            };
            Unloaded += (s, e) =>
            {
                Summarizer.SectionSummarized -= OnSectionSummarized;
                Translator.TranslationLogged -= OnTranslationLogged;
                ClassSession.StateChanged -= OnTranslationLogged;
            };

            ApplyFontSizes();
        }

        private void OnTranslationLogged()
        {
            Dispatcher.InvokeAsync(async () => await LoadTranscript());
        }

        // Shows every sentence logged since the class was started.
        private async Task LoadTranscript()
        {
            List<HistoryLine> lines = new();
            if (ClassSession.FirstHistoryId >= 0)
            {
                try
                {
                    lines = await SectionLogger.LoadHistoryRange(ClassSession.FirstHistoryId);
                }
                catch (Exception)
                {
                    return;
                }
            }

            // Keep following new sentences unless the user scrolled up to read.
            bool atEnd = TranscriptScroll.VerticalOffset >= TranscriptScroll.ScrollableHeight - 20;
            TranscriptList.ItemsSource = lines;
            TranscriptEmptyHint.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (atEnd)
            {
                TranscriptScroll.UpdateLayout();
                TranscriptScroll.ScrollToEnd();
            }
        }

        private void OnSectionSummarized(SectionEntry section)
        {
            Dispatcher.InvokeAsync(async () => await LoadSections(scrollToEnd: true));
        }

        private async Task LoadSections(bool scrollToEnd = false)
        {
            List<SectionEntry> sections;
            try
            {
                sections = await SectionLogger.LoadSections(DateTime.Today);
            }
            catch (Exception)
            {
                return;
            }
            SectionList.ItemsSource = sections;
            SectionEmptyHint.Visibility = sections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (scrollToEnd)
                SectionScroll.ScrollToEnd();
        }

        private void SectionSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            Translator.Setting.MainWindow.SectionPanelWidth = Math.Round(SectionColumn.ActualWidth);
        }

        private async void TextBlock_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
            {
                // Another program (clipboard manager, remote desktop, IME) may hold the clipboard for a moment.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        Clipboard.SetDataObject(textBlock.Text, true);
                        SnackbarHost.Show("已复制。", textBlock.Text, SnackbarType.Info, 100);
                        break;
                    }
                    catch (Exception) when (attempt < 4)
                    {
                        await Task.Delay(150);
                    }
                    catch (Exception)
                    {
                        SnackbarHost.Show("没能复制：剪贴板被其他程序占用，请再点一次。", string.Empty, SnackbarType.Warning, timeout: 2);
                        break;
                    }
                }
            }
        }

        private void ApplyFontSizes()
        {
            OriginalCaption.FontSize = Translator.Setting.MainWindow.OriginalFontSize;
            TranslatedCaption.FontSize = Translator.Setting.MainWindow.TranslatedFontSize;
        }

        private void OriginalCard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;
            Translator.Setting.MainWindow.OriginalFontSize =
                AdjustFontSize(Translator.Setting.MainWindow.OriginalFontSize, e.Delta);
            ApplyFontSizes();
            e.Handled = true;
        }

        private void TranslatedCard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;
            Translator.Setting.MainWindow.TranslatedFontSize =
                AdjustFontSize(Translator.Setting.MainWindow.TranslatedFontSize, e.Delta);
            ApplyFontSizes();
            e.Handled = true;
        }

        private static int AdjustFontSize(int current, int wheelDelta)
        {
            int next = current + (wheelDelta > 0 ? StyleConsts.DELTA_FONT_SIZE : -StyleConsts.DELTA_FONT_SIZE);
            return Math.Clamp(next, StyleConsts.MIN_FONT_SIZE, StyleConsts.MAX_FONT_SIZE);
        }

        public void AutoHeight()
        {
            (App.Current.MainWindow as MainWindow).AutoHeightAdjust(
                minHeight: (int)App.Current.MainWindow.MinHeight);
        }
    }
}
