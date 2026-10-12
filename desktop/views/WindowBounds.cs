using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace LiveCaptionsTranslator
{
    // Remembers where a window was and how big, in the same "left, top, width, height" text the Windows version
    // keeps in Setting.WindowBounds (the position is in pixels, the size in device independent units).
    public static class WindowBounds
    {
        public static Rect? Load(string name)
        {
            try
            {
                if (Translator.Setting?.WindowBounds.TryGetValue(name, out var text) != true)
                    return null;
                var parts = text!.Split(',').Select(p => double.Parse(p.Trim(), CultureInfo.InvariantCulture)).ToArray();
                return parts.Length == 4 && parts[2] > 0 && parts[3] > 0
                    ? new Rect(parts[0], parts[1], parts[2], parts[3])
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Applies the saved bounds; the window is centered if it was never placed or would be off every screen.
        public static void Restore(Window window, string name, Size fallback)
        {
            var saved = Load(name);
            var size = saved is Rect rect ? new Size(rect.Width, rect.Height) : fallback;
            window.Width = Math.Max(size.Width, window.MinWidth);
            window.Height = Math.Max(size.Height, window.MinHeight);
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (saved is not Rect position)
                return;
            try
            {
                var topLeft = new PixelPoint((int)position.X, (int)position.Y);
                if (window.Screens.All.Any(screen => screen.WorkingArea.Contains(topLeft)))
                {
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Position = topLeft;
                }
            }
            catch (Exception)
            {
            }
        }

        // Saves the bounds shortly after the last move or resize.
        public static void Track(Window window, string name)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Save(window, name);
            };
            void Changed()
            {
                timer.Stop();
                timer.Start();
            }
            window.PositionChanged += (s, e) => Changed();
            window.PropertyChanged += (s, e) =>
            {
                if (e.Property == Window.ClientSizeProperty)
                    Changed();
            };
        }

        public static void Save(Window window, string name)
        {
            var setting = Translator.Setting;
            if (setting == null || window.WindowState != WindowState.Normal || !window.IsVisible)
                return;
            setting.WindowBounds[name] = string.Format(CultureInfo.InvariantCulture, "{0}, {1}, {2}, {3}",
                window.Position.X, window.Position.Y, Math.Round(window.ClientSize.Width, 1),
                Math.Round(window.ClientSize.Height, 1));
            setting.Save();
        }
    }
}
