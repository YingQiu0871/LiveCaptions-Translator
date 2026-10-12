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
            Content = "▾",
            Padding = new Thickness(8, 0),
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
