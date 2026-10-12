using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LiveCaptionsTranslator.utils
{
    // Avalonia on X11 scales by Xft.dpi / 96 but finds nothing on some HiDPI setups (GDK_SCALE, GNOME integer scaling),
    // which makes everything tiny. Before the UI starts, the scale is worked out from the user's choice or the desktop's
    // settings and handed to Avalonia through AVALONIA_GLOBAL_SCALE_FACTOR, which multiplies Avalonia's own factor.
    internal static class LinuxScale
    {
        // The choices of the settings page; the first one means "follow the system".
        public static readonly string[] Choices = { "auto", "1", "1.25", "1.5", "1.75", "2", "2.5", "3" };

        public static string Label(string choice) =>
            choice == "auto" ? "自动（跟随系统）" : $"{double.Parse(choice, CultureInfo.InvariantCulture) * 100:0}%";

        public static void Apply()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR")))
                    return;
                string choice = DesktopSettings.UiScale;
                if (choice != "auto" && double.TryParse(choice, NumberStyles.Float, CultureInfo.InvariantCulture, out double chosen))
                {
                    // Avalonia's final scale is its own base factor times AVALONIA_GLOBAL_SCALE_FACTOR, so the
                    // factor handed over is target / base and the result is exactly what the user picked.
                    double target = Normalize(chosen);
                    Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR",
                        (target / AvaloniaBase()).ToString("0.####", CultureInfo.InvariantCulture));
                }
                else if (AutoScale() is double scale)
                {
                    Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR",
                        scale.ToString("0.##", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception)
            {
                // Scaling is a convenience: without it the UI is just drawn at 1.0.
            }
        }

        // The scale Avalonia's X11 backend applies by itself (before AVALONIA_GLOBAL_SCALE_FACTOR): the per-screen
        // variable if set, otherwise Xft.dpi / 96 from the root window's RESOURCE_MANAGER, otherwise 1. Once
        // AVALONIA_GLOBAL_SCALE_FACTOR is set Avalonia ignores every QT_* variable, so those do not count here.
        private static double AvaloniaBase()
        {
            try
            {
                string? list = Environment.GetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS");
                if (!string.IsNullOrWhiteSpace(list))
                {
                    // "name=1.5;name2=2" or "1.5;2": which screen the window lands on is not known here,
                    // so the first entry is the best guess.
                    string first = list.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];
                    if (Number(first[(first.IndexOf('=') + 1)..]) is double perScreen)
                        return perScreen;
                    return 1.0;
                }
                return XftDpi() is double dpi ? dpi / 96 : 1.0;
            }
            catch (Exception)
            {
                return 1.0;
            }
        }

        // Without an explicit choice nothing is changed when the desktop already scales (Avalonia's own base above 1,
        // or any scale variable set) or on Wayland, where the compositor may be scaling XWayland windows itself.
        private static double? AutoScale()
        {
            foreach (string name in new[] { "QT_SCALE_FACTOR", "QT_SCREEN_SCALE_FACTORS", "AVALONIA_SCREEN_SCALE_FACTORS" })
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                    return null;
            }
            if (AvaloniaBase() > 1.0)
                return null;
            if (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland"
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return null;

            foreach (var source in new Func<double?>[] { FromGdkEnvironment, FromGnome })
            {
                try
                {
                    if (source() is double value && Normalize(value) > 1.0)
                        return Normalize(value);
                }
                catch (Exception)
                {
                    // Try the next source.
                }
            }
            return null;
        }

        private static double Normalize(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 1.0;
            return Math.Round(Math.Clamp(value, 1.0, 4.0) * 20) / 20;
        }

        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(IntPtr name);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XResourceManagerString(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern int XCloseDisplay(IntPtr display);

        // Xft.dpi from the X server's resource database, read the way Avalonia reads it.
        private static double? XftDpi()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                return null;
            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                return null;
            try
            {
                string? resources = Marshal.PtrToStringUTF8(XResourceManagerString(display));
                var match = Regex.Match(resources ?? string.Empty, @"^Xft\.dpi:\s*([0-9.]+)", RegexOptions.Multiline);
                return match.Success ? Number(match.Groups[1].Value) : null;
            }
            finally
            {
                XCloseDisplay(display);
            }
        }

        private static double? FromGdkEnvironment() => Number(Environment.GetEnvironmentVariable("GDK_SCALE"));

        private static double? FromGnome()
        {
            double integer = Number(Run("gsettings", "get", "org.gnome.desktop.interface", "scaling-factor")?.Replace("uint32", "")) ?? 0;
            return integer > 0 ? integer : null;
        }

        private static double? Number(string? text) =>
            double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
                ? value
                : null;

        // The output of a tool, or null if it is missing, fails or takes longer than a second.
        private static string? Run(string tool, params string[] arguments)
        {
            string? path = Exec.Find(tool);
            if (path == null)
                return null;
            try
            {
                var info = new ProcessStartInfo(path)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (string argument in arguments)
                    info.ArgumentList.Add(argument);
                using var process = Process.Start(info);
                if (process == null)
                    return null;
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(1000))
                {
                    process.Kill(true);
                    return null;
                }
                return process.ExitCode == 0 && output.Wait(1000) ? output.Result : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
