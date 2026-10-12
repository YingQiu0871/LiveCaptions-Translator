using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using LiveCaptionsTranslator.Utils;

namespace LiveCaptionsTranslator
{
    public partial class CaptionPage : UserControl
    {
        private bool showOriginal = true;

        public CaptionPage()
        {
            InitializeComponent();
            Root.ColumnDefinitions[2].Width = new GridLength(Math.Max(Translator.Setting.MainWindow.SectionPanelWidth, 180));
            ApplyFontSizes();

            Splitter.DragCompleted += (s, e) =>
                Translator.Setting.MainWindow.SectionPanelWidth = Math.Round(Root.ColumnDefinitions[2].ActualWidth);

            // The copy handlers sit on the lists, because their items are made from templates.
            TranscriptList.AddHandler(PointerPressedEvent, TextBlock_Pressed, RoutingStrategies.Bubble);
            OriginalCaption.PointerPressed += TextBlock_Pressed;
            TranslatedCaption.PointerPressed += TextBlock_Pressed;
            OriginalCaption.PointerWheelChanged += (s, e) =>
            {
                var window = Translator.Setting.MainWindow;
                AdjustFontOnWheel(e, () => window.OriginalFontSize, size => window.OriginalFontSize = size);
            };
            TranslatedCaption.PointerWheelChanged += (s, e) =>
            {
                var window = Translator.Setting.MainWindow;
                AdjustFontOnWheel(e, () => window.TranslatedFontSize, size => window.TranslatedFontSize = size);
            };
        }

        protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            (Shell.Main)?.AutoHeightAdjust(minHeight: (int)Shell.Main.MinHeight);
            Summarizer.SectionSummarized += OnSectionSummarized;
            Translator.TranslationLogged += OnTranslationLogged;
            Refiner.ParagraphRefined += OnTranslationLogged;
            ClassSession.StateChanged += OnSessionChanged;
            Summarizer.StatusChanged += OnSummaryStatusChanged;
            Translator.Caption.PropertyChanged += OnCaptionChanged;
            ShowCaption();
            OnSummaryStatusChanged();
            await LoadTranscript();
            await LoadSections(scrollToEnd: true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Summarizer.SectionSummarized -= OnSectionSummarized;
            Translator.TranslationLogged -= OnTranslationLogged;
            Refiner.ParagraphRefined -= OnTranslationLogged;
            ClassSession.StateChanged -= OnSessionChanged;
            Summarizer.StatusChanged -= OnSummaryStatusChanged;
            Translator.Caption.PropertyChanged -= OnCaptionChanged;
        }

        // The caption changes on the translation threads: update the texts on the UI thread.
        private void OnCaptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(ShowCaption);
        }

        private void ShowCaption()
        {
            var caption = Translator.Caption;
            OriginalCaption.Text = caption.DisplayOriginalCaption;
            TranslatedCaption.Text = caption.DisplayTranslatedCaption;
            StatusHint.Text = caption.StatusHint;
            StatusHint.IsVisible = string.IsNullOrEmpty(caption.DisplayOriginalCaption);
            WarningText.Text = caption.SourceWarning;
            WarningBorder.IsVisible = caption.IsSourceWarningVisible;

            // Without translation the original is all there is.
            OriginalCaption.IsVisible = caption.IsOriginalVisible;
            if (showOriginal != caption.IsOriginalVisible)
            {
                showOriginal = caption.IsOriginalVisible;
                ShowTranscriptOriginals();
            }
        }

        private void ShowTranscriptOriginals()
        {
            foreach (var block in TranscriptList.GetVisualDescendants().OfType<TextBlock>()
                         .Where(b => b.Classes.Contains("original")))
                block.IsVisible = showOriginal;
        }

        private void OnTranslationLogged()
        {
            Dispatcher.UIThread.Post(async () => await LoadTranscript());
        }

        private void OnSummaryStatusChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                SummaryStatus.Text = Summarizer.Status;
                SummaryStatus.IsVisible = !string.IsNullOrEmpty(Summarizer.Status);
            });
        }

        private void OnSessionChanged()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await LoadTranscript();
                await LoadSections(scrollToEnd: true);
            });
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
                    var paragraphs = await SectionLogger.LoadParagraphs(ClassSession.FirstHistoryId);
                    lines = LectureDocument.MergeParagraphs(lines, paragraphs, fadeUnrefined: Refiner.Enabled);
                }
                catch (Exception)
                {
                    return;
                }
            }

            // Keep following new sentences unless the user scrolled up to read.
            bool atEnd = TranscriptScroll.Offset.Y >= TranscriptScroll.Extent.Height - TranscriptScroll.Viewport.Height - 20;
            TranscriptList.ItemsSource = lines;
            TranscriptEmptyHint.IsVisible = lines.Count == 0;
            Dispatcher.UIThread.Post(() =>
            {
                ShowTranscriptOriginals();
                if (atEnd)
                    TranscriptScroll.ScrollToEnd();
            }, DispatcherPriority.Background);
        }

        private void OnSectionSummarized(SectionEntry section)
        {
            Dispatcher.UIThread.Post(async () => await LoadSections(scrollToEnd: true));
        }

        private async Task LoadSections(bool scrollToEnd = false)
        {
            List<SectionEntry> sections;
            try
            {
                // Only this class's sections: the previous class is in the history.
                sections = ClassSession.FirstHistoryId >= 0
                    ? await SectionLogger.LoadSectionsInRange(ClassSession.FirstHistoryId, long.MaxValue)
                    : await SectionLogger.LoadSections(DateTime.Today);
            }
            catch (Exception)
            {
                return;
            }
            SectionList.ItemsSource = sections;
            SectionEmptyHint.IsVisible = sections.Count == 0;
            if (scrollToEnd)
                Dispatcher.UIThread.Post(() => SectionScroll.ScrollToEnd(), DispatcherPriority.Background);
        }

        private async void TextBlock_Pressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            if (e.Source is not TextBlock textBlock ||
                !(textBlock.Classes.Contains("copy") || textBlock == OriginalCaption || textBlock == TranslatedCaption))
                return;
            if (string.IsNullOrWhiteSpace(textBlock.Text))
                return;
            if (await Shell.CopyText(textBlock.Text))
                SnackbarHost.Show("已复制。", textBlock.Text, SnackbarType.Info);
            else
                SnackbarHost.Show("没能复制：剪贴板被其他程序占用，请再点一次。", string.Empty, SnackbarType.Warning, timeout: 2);
        }

        private void ApplyFontSizes()
        {
            OriginalCaption.FontSize = Translator.Setting.MainWindow.OriginalFontSize;
            TranslatedCaption.FontSize = Translator.Setting.MainWindow.TranslatedFontSize;
        }

        // Ctrl + wheel changes a font size.
        private void AdjustFontOnWheel(PointerWheelEventArgs e, Func<int> get, Action<int> set)
        {
            if (e.KeyModifiers != KeyModifiers.Control)
                return;
            set(AdjustFontSize(get(), e.Delta.Y));
            ApplyFontSizes();
            e.Handled = true;
        }

        private static int AdjustFontSize(int current, double wheelDelta)
        {
            int next = current + (wheelDelta > 0 ? StyleConsts.DELTA_FONT_SIZE : -StyleConsts.DELTA_FONT_SIZE);
            return Math.Clamp(next, StyleConsts.MIN_FONT_SIZE, StyleConsts.MAX_FONT_SIZE);
        }
    }
}
