using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // The bilingual floating window: the last sentence pairs (original above, translation below) and the sentence
    // in progress, on a semi-transparent panel that can stay above everything.
    public partial class BilingualWindow : Window
    {
        private const int MIN_FONT_SIZE = 12;
        private const int MAX_FONT_SIZE = 40;
        private const int MIN_OPACITY = 25;
        private const int MAX_OPACITY = 250;
        private const int DELTA_OPACITY = 25;

        private static readonly IBrush OriginalBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xC8, 0xC8, 0xC8));
        private static readonly IBrush TranslationBrush = Brushes.White;
        private static readonly IBrush PinnedBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x8F, 0xD1, 0x8F));
        private static readonly IBrush UnpinnedBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xAA, 0xAA, 0xAA));

        private readonly BilingualFeed feed;

        private static BilingualWindowState Look => Translator.Setting.BilingualWindow;

        public BilingualWindow()
        {
            InitializeComponent();

            Look.Sentences = Math.Clamp(Look.Sentences, BilingualFeed.MIN_SENTENCES, BilingualFeed.MAX_SENTENCES);
            feed = new BilingualFeed(Look.Sentences);
            feed.Changed += () => Dispatcher.UIThread.Post(Render);

            WindowBounds.Restore(this, "BilingualWindow", new Size(720, 240));
            // The height follows the text.
            Height = double.NaN;
            WindowBounds.Track(this, "BilingualWindow");

            Opened += (s, e) =>
            {
                feed.Start();
                Render();
            };
            Closed += (s, e) => feed.Dispose();

            PointerEntered += (s, e) => ControlPanel.IsVisible = true;
            PointerExited += (s, e) => ControlPanel.IsVisible = false;
            PanelBorder.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button)
                    BeginMoveDrag(e);
            };
            Resizer(LeftEdge, WindowEdge.West);
            Resizer(RightEdge, WindowEdge.East);

            ShowPinState();
            ShowSentences();
            ApplyBackgroundOpacity();
        }

        private void Resizer(Border edge, WindowEdge direction)
        {
            edge.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    BeginResizeDrag(direction, e);
            };
        }

        private void Render()
        {
            var items = new List<BilingualFeed.Pair>(feed.Finished);
            var current = feed.Current;
            if (current != null)
                items.Add(current);

            PairPanel.Children.Clear();
            if (items.Count == 0)
            {
                HintText.FontSize = Look.FontSize * 0.8;
                HintText.Text = feed.SessionStarted ? "正在听……" : "点击「开始」后这里显示原文和译文";
                HintText.IsVisible = true;
                return;
            }
            HintText.IsVisible = false;

            bool originalOnly = feed.OriginalOnly;
            double translationSize = Look.FontSize;
            double originalSize = originalOnly ? translationSize : Math.Round(translationSize * 0.8);
            for (int i = 0; i < items.Count; i++)
            {
                var pair = items[i];
                bool hasTranslation = !originalOnly && pair.Translation.Length > 0;
                PairPanel.Children.Add(new TextBlock
                {
                    Text = pair.Original,
                    FontSize = originalSize,
                    Foreground = originalOnly ? TranslationBrush : OriginalBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, i == 0 ? 0 : 8, 0, hasTranslation ? 1 : 0),
                });
                if (hasTranslation)
                {
                    PairPanel.Children.Add(new TextBlock
                    {
                        Text = pair.Translation,
                        FontSize = translationSize,
                        Foreground = TranslationBrush,
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
            }
        }

        private void ShowPinState()
        {
            Topmost = Look.Topmost;
            PinButton.Background = Look.Topmost ? PinnedBrush : UnpinnedBrush;
        }

        private void ShowSentences()
        {
            SentencesButton.Content = $"{Look.Sentences} 句";
        }

        private void ApplyBackgroundOpacity()
        {
            PanelBorder.Background = new SolidColorBrush(
                Avalonia.Media.Color.FromArgb((byte)Math.Clamp(Look.Opacity, 0, 255), 0, 0, 0));
        }

        private void PinButton_Click(object? sender, RoutedEventArgs e)
        {
            Look.Topmost = !Look.Topmost;
            ShowPinState();
        }

        private void FontIncrease_Click(object? sender, RoutedEventArgs e)
        {
            Look.FontSize = Math.Min(Look.FontSize + 2, MAX_FONT_SIZE);
            Render();
        }

        private void FontDecrease_Click(object? sender, RoutedEventArgs e)
        {
            Look.FontSize = Math.Max(Look.FontSize - 2, MIN_FONT_SIZE);
            Render();
        }

        private void OpacityIncrease_Click(object? sender, RoutedEventArgs e)
        {
            Look.Opacity = Math.Min(Look.Opacity + DELTA_OPACITY, MAX_OPACITY);
            ApplyBackgroundOpacity();
        }

        private void OpacityDecrease_Click(object? sender, RoutedEventArgs e)
        {
            Look.Opacity = Math.Max(Look.Opacity - DELTA_OPACITY, MIN_OPACITY);
            ApplyBackgroundOpacity();
        }

        private void SentencesButton_Click(object? sender, RoutedEventArgs e)
        {
            Look.Sentences = Look.Sentences >= BilingualFeed.MAX_SENTENCES
                ? BilingualFeed.MIN_SENTENCES
                : Look.Sentences + 1;
            feed.Sentences = Look.Sentences;
            ShowSentences();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
