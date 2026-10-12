using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace LiveCaptionsTranslator
{
    // Wrapping text made of colored runs, drawn with an optional outline (the overlay's caption text).
    public class StrokeTextBlock : Control
    {
        private IReadOnlyList<(string Text, IBrush Brush)> runs = Array.Empty<(string, IBrush)>();
        private double fontSize = 15;
        private FontWeight fontWeight = FontWeight.Normal;
        private double strokeThickness = 0;
        private IBrush stroke = Brushes.Black;
        private FormattedText? formatted;
        private double formattedWidth = -1;

        public void SetText(IReadOnlyList<(string Text, IBrush Brush)> value)
        {
            runs = value;
            Rebuild();
        }

        public double FontSize
        {
            get => fontSize;
            set
            {
                fontSize = value;
                Rebuild();
            }
        }

        public FontWeight FontWeight
        {
            get => fontWeight;
            set
            {
                fontWeight = value;
                Rebuild();
            }
        }

        public double StrokeThickness
        {
            get => strokeThickness;
            set
            {
                strokeThickness = value;
                InvalidateVisual();
            }
        }

        public IBrush Stroke
        {
            get => stroke;
            set
            {
                stroke = value;
                InvalidateVisual();
            }
        }

        private void Rebuild()
        {
            formatted = null;
            InvalidateMeasure();
            InvalidateVisual();
        }

        private FormattedText Format(double width)
        {
            if (formatted != null && Math.Abs(formattedWidth - width) < 0.5)
                return formatted;

            string text = string.Concat(runs.Select(run => run.Text));
            var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, fontWeight);
            var result = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
                fontSize, Brushes.White)
            {
                MaxTextWidth = double.IsInfinity(width) ? 100000 : Math.Max(width, 1),
            };
            int start = 0;
            foreach (var (runText, brush) in runs)
            {
                if (runText.Length > 0)
                    result.SetForegroundBrush(brush, start, runText.Length);
                start += runText.Length;
            }
            formatted = result;
            formattedWidth = width;
            return result;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var text = Format(availableSize.Width);
            return new Size(Math.Min(text.Width, availableSize.Width), text.Height);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var text = Format(Bounds.Width);
            if (strokeThickness > 0)
            {
                var geometry = text.BuildGeometry(new Point(0, 0));
                if (geometry != null)
                    context.DrawGeometry(null, new Pen(stroke, strokeThickness, lineJoin: PenLineJoin.Round), geometry);
            }
            context.DrawText(text, new Point(0, 0));
        }
    }
}
