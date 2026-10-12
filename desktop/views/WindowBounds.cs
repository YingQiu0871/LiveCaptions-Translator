using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace LiveCaptionsTranslator
{
    // Remembers where a window was and how big, as the "left, top, width, height" text of Setting.WindowBounds
    // (the position is in pixels, the size in device independent units).
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

        // Applies the saved bounds. The whole window must fit a screen's working area: it is shrunk and moved to fit,
        // and centered if it was never placed or its top left corner is on no screen (e.g. a monitor was unplugged).
        public static void Restore(Window window, string name, Size fallback)
        {
            var saved = Load(name);
            var size = saved is Rect rect ? new Size(rect.Width, rect.Height) : fallback;
            window.Width = Math.Max(size.Width, window.MinWidth);
            window.Height = Math.Max(size.Height, window.MinHeight);
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try
            {
                var screens = window.Screens;
                var topLeft = saved is Rect position ? new PixelPoint((int)position.X, (int)position.Y) : default;
                var screen = saved == null ? null : screens.All.FirstOrDefault(s => s.WorkingArea.Contains(topLeft));
                var area = (screen ?? screens.Primary ?? screens.All.FirstOrDefault())?.WorkingArea;
                if (area is not PixelRect working)
                    return;
                double scaling = (screen ?? screens.Primary)?.Scaling ?? 1;
                window.Width = Math.Max(Math.Min(window.Width, working.Width / scaling), window.MinWidth);
                window.Height = Math.Max(Math.Min(window.Height, working.Height / scaling), window.MinHeight);
                if (screen == null)
                    return;
                int width = (int)Math.Ceiling(window.Width * scaling);
                int height = (int)Math.Ceiling(window.Height * scaling);
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(
                    Math.Max(working.X, Math.Min(topLeft.X, working.Right - width)),
                    Math.Max(working.Y, Math.Min(topLeft.Y, working.Bottom - height)));
            }
            catch (Exception)
            {
                // Placement is cosmetic: the window keeps the size and position set so far.
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
