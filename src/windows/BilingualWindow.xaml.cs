using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using Color = System.Windows.Media.Color;

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

        private static readonly Brush OriginalBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
        private static readonly Brush TranslationBrush = Brushes.White;

        private readonly BilingualFeed feed;

        private static BilingualWindowState Look => Translator.Setting.BilingualWindow;

        public BilingualWindow()
        {
            InitializeComponent();
            Topmost = Look.Topmost;
            Look.Sentences = Math.Clamp(Look.Sentences, BilingualFeed.MIN_SENTENCES, BilingualFeed.MAX_SENTENCES);
            feed = new BilingualFeed(Look.Sentences);
            feed.Changed += () => Dispatcher.BeginInvoke(new Action(Render), DispatcherPriority.Background);

            Loaded += (s, e) =>
            {
                feed.Start();
                Render();
            };
            Closed += (s, e) => feed.Dispose();

            RestorePlace();
            LimitHeight();
            LocationChanged += (s, e) =>
            {
                WindowHandler.SaveState(this, Translator.Setting);
                LimitHeight();
            };
            // The height follows the text, so only a change of the width is worth saving.
            SizeChanged += (s, e) =>
            {
                if (e.WidthChanged)
                    WindowHandler.SaveState(this, Translator.Setting);
            };

            ShowPinState();
            ShowSentences();
            ApplyBackgroundOpacity();
        }

        private void RestorePlace()
        {
            double left, top, width = Width;
            try
            {
                var bound = WindowHandler.LoadState(this, Translator.Setting);
                left = bound.Left;
                top = bound.Top;
                width = bound.Width;
            }
            catch (Exception)
            {
                left = double.NaN;
                top = double.NaN;
            }

            Width = Math.Clamp(width, MinWidth, SystemParameters.WorkArea.Width);
            double screenLeft = SystemParameters.VirtualScreenLeft;
            double screenTop = SystemParameters.VirtualScreenTop;
            bool onScreen = !double.IsNaN(left) && !double.IsNaN(top) &&
                left + Width > screenLeft + 50 && left < screenLeft + SystemParameters.VirtualScreenWidth - 50 &&
                top >= screenTop && top < screenTop + SystemParameters.VirtualScreenHeight - 50;
            if (onScreen)
            {
                Left = left;
                Top = top;
            }
            else
            {
                Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
                Top = 40;
            }
        }

        // The window may grow down to the bottom of the work area; longer content scrolls to its newest line.
        private void LimitHeight()
        {
            MaxHeight = Math.Max(120, SystemParameters.WorkArea.Bottom - Top);
            // Panel margin (2 x 5) and scroll area margin (2 x 12).
            PairScroll.MaxHeight = Math.Max(40, MaxHeight - 34);
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
                HintText.Visibility = Visibility.Visible;
                PairScroll.ScrollToTop();
                return;
            }
            HintText.Visibility = Visibility.Collapsed;

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
            PairScroll.ScrollToEnd();
        }

        private void ShowPinState()
        {
            Topmost = Look.Topmost;
            if (PinButton.Icon is Wpf.Ui.Controls.SymbolIcon icon)
                icon.Filled = Look.Topmost;
        }

        private void ShowSentences()
        {
            SentencesButton.Content = $"{Look.Sentences} 句";
        }

        private void ApplyBackgroundOpacity()
        {
            PanelBorder.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(Look.Opacity, 0, 255), 0, 0, 0));
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            ControlPanel.Visibility = Visibility.Visible;
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            ControlPanel.Visibility = Visibility.Collapsed;
        }

        private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            Look.Topmost = !Look.Topmost;
            ShowPinState();
        }

        private void FontIncrease_Click(object sender, RoutedEventArgs e)
        {
            Look.FontSize = Math.Min(Look.FontSize + 2, MAX_FONT_SIZE);
            Render();
        }

        private void FontDecrease_Click(object sender, RoutedEventArgs e)
        {
            Look.FontSize = Math.Max(Look.FontSize - 2, MIN_FONT_SIZE);
            Render();
        }

        private void OpacityIncrease_Click(object sender, RoutedEventArgs e)
        {
            Look.Opacity = Math.Min(Look.Opacity + StyleConsts.DELTA_OPACITY, MAX_OPACITY);
            ApplyBackgroundOpacity();
        }

        private void OpacityDecrease_Click(object sender, RoutedEventArgs e)
        {
            Look.Opacity = Math.Max(Look.Opacity - StyleConsts.DELTA_OPACITY, MIN_OPACITY);
            ApplyBackgroundOpacity();
        }

        private void SentencesButton_Click(object sender, RoutedEventArgs e)
        {
            Look.Sentences = Look.Sentences >= BilingualFeed.MAX_SENTENCES
                ? BilingualFeed.MIN_SENTENCES
                : Look.Sentences + 1;
            feed.Sentences = Look.Sentences;
            ShowSentences();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LeftThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = Width - e.HorizontalChange;
            if (newWidth >= MinWidth)
            {
                Left += e.HorizontalChange;
                Width = newWidth;
            }
        }

        private void RightThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = Width + e.HorizontalChange;
            if (newWidth >= MinWidth)
                Width = newWidth;
        }
    }
}
