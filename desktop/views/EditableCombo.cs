using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LiveCaptionsTranslator
{
    // A text box with a drop-down list of suggestions (Avalonia's ComboBox cannot be edited).
    public class EditableCombo : Grid
    {
        private readonly TextBox box = new() { VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
        private readonly Button arrow = new()
        {
            // Drawn, not a "▾" character: not every Linux font has that glyph.
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Avalonia.Media.Geometry.Parse("M0,0 L4,4 L8,0"),
                Width = 8,
                Height = 4,
                StrokeThickness = 1.5,
                StrokeLineCap = Avalonia.Media.PenLineCap.Round,
                StrokeJoin = Avalonia.Media.PenLineJoin.Round,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
            Padding = new Thickness(10, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
            Focusable = false,
        };
        private IReadOnlyList<string> items = Array.Empty<string>();
        private bool setting = false;

        // A suggestion was picked from the list.
        public event Action<string>? ItemPicked;

        // The text changed, by typing or by picking.
        public event Action<string>? TextEdited;

        public EditableCombo()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto");
            // The chevron follows the button's content color (hover, disabled) (light and dark theme).
            ((Avalonia.Controls.Shapes.Path)arrow.Content!).Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty,
                new Avalonia.Data.Binding(nameof(Avalonia.Controls.Presenters.ContentPresenter.Foreground))
                {
                    RelativeSource = new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor)
                    {
                        AncestorType = typeof(Avalonia.Controls.Presenters.ContentPresenter),
                    },
                });
            SetColumn(arrow, 1);
            Children.Add(box);
            Children.Add(arrow);
            box.TextChanged += (s, e) =>
            {
                if (!setting)
                    TextEdited?.Invoke(box.Text ?? string.Empty);
            };
            arrow.Click += (s, e) => ShowItems();
        }

        public string Text
        {
            get => box.Text ?? string.Empty;
            set
            {
                setting = true;
                box.Text = value;
                setting = false;
            }
        }

        public IEnumerable<string> ItemsSource
        {
            get => items;
            set => items = value.ToList();
        }

        private void ShowItems()
        {
            var menu = new ContextMenu();
            foreach (string item in items)
            {
                var entry = new MenuItem { Header = item };
                entry.Click += (s, e) =>
                {
                    Text = item;
                    ItemPicked?.Invoke(item);
                    TextEdited?.Invoke(item);
                };
                menu.Items.Add(entry);
            }
            if (items.Count > 0)
                menu.Open(arrow);
        }
    }
}
