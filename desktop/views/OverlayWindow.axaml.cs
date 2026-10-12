using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using ColorEnum = LiveCaptionsTranslator.utils.Color;

namespace LiveCaptionsTranslator
{
    // The floating caption window: always on top, no frame, resized from its edges.
    public partial class OverlayWindow : Window
    {
        private static readonly Dictionary<ColorEnum, Avalonia.Media.Color> colorMap = new()
        {
            { ColorEnum.White, Colors.White },
            { ColorEnum.Yellow, Colors.Yellow },
            { ColorEnum.LimeGreen, Colors.LimeGreen },
            { ColorEnum.Aqua, Colors.Aqua },
            { ColorEnum.Blue, Colors.Blue },
            { ColorEnum.DeepPink, Colors.DeepPink },
            { ColorEnum.Red, Colors.Red },
            { ColorEnum.Black, Colors.Black },
        };
        private CaptionVisible onlyMode = CaptionVisible.Both;

        public CaptionVisible OnlyMode
        {
            get => onlyMode;
            set
            {
                onlyMode = value;
                ResizeForOnlyMode();
            }
        }
        public CaptionLocation SwitchMode { get; set; } = CaptionLocation.TranslationTop;

        private static OverlayWindowState Look => Translator.Setting.OverlayWindow;

        public OverlayWindow()
        {
            InitializeComponent();

            WindowBounds.Restore(this, "OverlayWindow", new Size(480, 100));
            // A width of exactly 650 counts as unset and gets the default width.
            if (Math.Abs(Width - 650) < 1)
                Width = 480;
            WindowBounds.Track(this, "OverlayWindow");

            Opened += (s, e) => Translator.Caption.PropertyChanged += OnCaptionChanged;
            Closed += (s, e) => Translator.Caption.PropertyChanged -= OnCaptionChanged;

            PointerEntered += (s, e) => ControlPanel.IsVisible = true;
            PointerExited += (s, e) => ControlPanel.IsVisible = false;
            CaptionGrid.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button)
                    BeginMoveDrag(e);
            };
            Resizer(TopEdge, WindowEdge.North);
            Resizer(BottomEdge, WindowEdge.South);
            Resizer(LeftEdge, WindowEdge.West);
            Resizer(RightEdge, WindowEdge.East);
            Resizer(TopLeftEdge, WindowEdge.NorthWest);
            Resizer(TopRightEdge, WindowEdge.NorthEast);
            Resizer(BottomLeftEdge, WindowEdge.SouthWest);
            Resizer(BottomRightEdge, WindowEdge.SouthEast);

            ApplyBackgroundOpacity();
            ShowCaption();
        }

        private void Resizer(Border edge, WindowEdge direction)
        {
            edge.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    BeginResizeDrag(direction, e);
            };
        }

        // The captions change on the translation threads: draw them on the UI thread.
        private void OnCaptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(ShowCaption);
        }

        private void ShowCaption()
        {
            var caption = Translator.Caption;
            var fontBrush = new SolidColorBrush(colorMap[Look.FontColor]);
            OriginalCaption.FontSize = Look.FontSize;
            TranslatedCaption.FontSize = (int)(Look.FontSize * 1.25);
            ApplyFontBold();
            OriginalCaption.StrokeThickness = TranslatedCaption.StrokeThickness = Look.FontStroke;

            OriginalCaption.SetText(new (string, IBrush)[] { (caption.OverlayOriginalCaption, fontBrush) });
            TranslatedCaption.SetText(new (string, IBrush)[]
            {
                (caption.OverlayNoticePrefix, fontBrush),
                (caption.OverlayPreviousTranslation, fontBrush),
                (caption.OverlayCurrentTranslation, new SolidColorBrush(TranslationColor(fontBrush.Color))),
            });
        }

        // The current translation is a little off the text color, so it stands out from the previous ones.
        private static Avalonia.Media.Color TranslationColor(Avalonia.Media.Color color)
        {
            double target = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B > 127 ? 0 : 255;
            byte r = (byte)Math.Clamp(color.R + (target - color.R) * 0.3, 0, 255);
            byte g = (byte)Math.Clamp(color.G + (target - color.G) * 0.4, 0, 255);
            byte b = (byte)Math.Clamp(color.B + (target - color.B) * 0.3, 0, 255);
            return Avalonia.Media.Color.FromRgb(r, g, b);
        }

        private void FontIncrease_Click(object? sender, RoutedEventArgs e)
        {
            if (Look.FontSize + StyleConsts.DELTA_FONT_SIZE < StyleConsts.MAX_FONT_SIZE)
            {
                Look.FontSize += StyleConsts.DELTA_FONT_SIZE;
                ShowCaption();
            }
        }

        private void FontDecrease_Click(object? sender, RoutedEventArgs e)
        {
            if (Look.FontSize - StyleConsts.DELTA_FONT_SIZE > StyleConsts.MIN_FONT_SIZE)
            {
                Look.FontSize -= StyleConsts.DELTA_FONT_SIZE;
                ShowCaption();
            }
        }

        private void FontBold_Click(object? sender, RoutedEventArgs e)
        {
            Look.FontBold++;
            if (Look.FontBold > utils.FontBold.Both)
                Look.FontBold = utils.FontBold.None;
            ApplyFontBold();
        }

        private void ApplyFontBold()
        {
            bool original = Look.FontBold is utils.FontBold.SubtitleOnly or utils.FontBold.Both;
            bool translation = Look.FontBold is utils.FontBold.TranslationOnly or utils.FontBold.Both;
            OriginalCaption.FontWeight = original ? FontWeight.Bold : FontWeight.Normal;
            TranslatedCaption.FontWeight = translation ? FontWeight.Bold : FontWeight.Normal;
        }

        private void FontStrokeIncrease_Click(object? sender, RoutedEventArgs e)
        {
            if (Look.FontStroke + StyleConsts.DELTA_STROKE > StyleConsts.MAX_STROKE)
                return;
            Look.FontStroke += StyleConsts.DELTA_STROKE;
            ApplyFontStroke();
        }

        private void FontStrokeDecrease_Click(object? sender, RoutedEventArgs e)
        {
            if (Look.FontStroke - StyleConsts.DELTA_STROKE < StyleConsts.MIN_STROKE)
                return;
            Look.FontStroke -= StyleConsts.DELTA_STROKE;
            ApplyFontStroke();
        }

        private void FontColorCycle_Click(object? sender, RoutedEventArgs e)
        {
            Look.FontColor++;
            if (Look.FontColor > ColorEnum.Black)
                Look.FontColor = ColorEnum.White;
            ShowCaption();
        }

        private void BackgroundOpacityIncrease_Click(object? sender, RoutedEventArgs e) =>
            StepBackgroundOpacity(StyleConsts.DELTA_OPACITY);

        private void BackgroundOpacityDecrease_Click(object? sender, RoutedEventArgs e) =>
            StepBackgroundOpacity(-StyleConsts.DELTA_OPACITY);

        private void StepBackgroundOpacity(int delta)
        {
            Look.Opacity = Math.Clamp(Look.Opacity + delta, StyleConsts.MIN_OPACITY, StyleConsts.MAX_OPACITY);
            ApplyBackgroundOpacity();
        }

        private void BackgroundColorCycle_Click(object? sender, RoutedEventArgs e)
        {
            Look.BackgroundColor++;
            if (Look.BackgroundColor > ColorEnum.Black)
                Look.BackgroundColor = ColorEnum.White;
            ApplyBackgroundOpacity();
        }

        private void OnlyModeButton_Click(object? sender, RoutedEventArgs e)
        {
            if (onlyMode == CaptionVisible.SubtitleOnly)
            {
                // (0) Subtitle + Translation
                OnlyModeButton.Content = "全显";
                OnlyMode = CaptionVisible.Both;
            }
            else if (onlyMode == CaptionVisible.Both)
            {
                // (1) Translation Only
                OnlyModeButton.Content = "译文";
                OnlyMode = CaptionVisible.TranslationOnly;
            }
            else
            {
                // (2) Subtitle Only
                OnlyModeButton.Content = "原文";
                OnlyMode = CaptionVisible.SubtitleOnly;
            }
        }

        private void SwitchModeButton_Click(object? sender, RoutedEventArgs e)
        {
            if (SwitchMode == CaptionLocation.TranslationTop)
            {
                Grid.SetRow(TranslatedCaptionCard, 1);
                Grid.SetRow(OriginalCaptionCard, 0);
                SwitchMode = CaptionLocation.SubtitleTop;
            }
            else
            {
                Grid.SetRow(TranslatedCaptionCard, 0);
                Grid.SetRow(OriginalCaptionCard, 1);
                SwitchMode = CaptionLocation.TranslationTop;
            }
        }

        public void ResizeForOnlyMode()
        {
            double delta = StyleConsts.DELTA_OVERLAY_HEIGHT;
            void MoveTop(double amount) => Position = new PixelPoint(Position.X, Position.Y + (int)(amount * RenderScaling));

            if (onlyMode == CaptionVisible.TranslationOnly)
            {
                // (1) Translation Only
                OriginalCaptionCard.IsVisible = false;
                MinHeight -= delta;
                Height -= delta;
                MoveTop(delta);
            }
            if (onlyMode == CaptionVisible.SubtitleOnly)
            {
                // restore
                OriginalCaptionCard.IsVisible = true;
                MoveTop(-delta);
                Height += delta;
                MinHeight += delta;

                // (2) Subtitle Only
                TranslatedCaptionCard.IsVisible = false;
                MinHeight -= delta;
                Height -= delta;
            }
            else if (onlyMode == CaptionVisible.Both)
            {
                // restore
                TranslatedCaptionCard.IsVisible = true;
                Height += delta;
                MinHeight += delta;
            }
        }

        public void ApplyFontStroke()
        {
            OriginalCaption.StrokeThickness = Look.FontStroke;
            TranslatedCaption.StrokeThickness = Look.FontStroke;
        }

        public void ApplyBackgroundOpacity()
        {
            var color = colorMap[Look.BackgroundColor];
            BorderBackground.Background = new SolidColorBrush(
                Avalonia.Media.Color.FromArgb((byte)Math.Clamp(Look.Opacity, 0, 255), color.R, color.G, color.B));
        }
    }
}
