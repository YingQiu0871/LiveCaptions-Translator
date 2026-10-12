using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LiveCaptionsTranslator.utils
{
    // Avalonia on X11 (and XWayland) often reports a scale of 1.0 on a HiDPI screen, which makes everything tiny.
    // Before the UI starts, the scale is worked out from the user's choice or the desktop's settings and handed to
    // Avalonia through AVALONIA_GLOBAL_SCALE_FACTOR.
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
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR"))
                    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS")))
                    return;
                string choice = DesktopSettings.UiScale;
                double scale = Detect(choice);
                // An explicit choice (even 100%) is passed on; "auto" only when something bigger than 1.0 was found.
                if (scale > 1.0 || choice != "auto")
                    Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR",
                        scale.ToString("0.##", CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                // Scaling is a convenience: without it the UI is just drawn at 1.0.
            }
        }

        // The scale for a settings choice ("auto" or a number); 1.0 means leave Avalonia alone.
        public static double Detect(string choice)
        {
            if (choice != "auto" && double.TryParse(choice, NumberStyles.Float, CultureInfo.InvariantCulture, out double chosen))
                return Normalize(chosen);

            var sources = new Func<double?>[] { FromXftDpi, FromGdkEnvironment, FromQtEnvironment, FromGnome, FromKde };
            foreach (var source in sources)
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
            return 1.0;
        }

        private static double Normalize(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 1.0;
            return Math.Round(Math.Clamp(value, 1.0, 4.0) * 20) / 20;
        }

        private static double? FromXftDpi()
        {
            var match = Regex.Match(Run("xrdb", "-query") ?? string.Empty, @"^Xft\.dpi:\s*([0-9.]+)", RegexOptions.Multiline);
            return match.Success ? Number(match.Groups[1].Value) / 96 : null;
        }

        private static double? FromGdkEnvironment()
        {
            double? scale = Number(Environment.GetEnvironmentVariable("GDK_SCALE"));
            double? dpi = Number(Environment.GetEnvironmentVariable("GDK_DPI_SCALE"));
            return scale == null && dpi == null ? null : (scale ?? 1) * (dpi ?? 1);
        }

        private static double? FromQtEnvironment() => Number(Environment.GetEnvironmentVariable("QT_SCALE_FACTOR"));

        private static double? FromGnome()
        {
            string schema = "org.gnome.desktop.interface";
            double integer = Number(Run("gsettings", "get", schema, "scaling-factor")?.Replace("uint32", "")) ?? 0;
            double text = Number(Run("gsettings", "get", schema, "text-scaling-factor")) ?? 1;
            double scale = Math.Max(integer, 1) * text;
            return integer > 0 || text > 1 ? scale : null;
        }

        private static double? FromKde()
        {
            foreach (string tool in new[] { "kreadconfig6", "kreadconfig5" })
            {
                if (Number(Run(tool, "--group", "KScreen", "--key", "ScaleFactor")) is double value)
                    return value;
            }
            return null;
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
