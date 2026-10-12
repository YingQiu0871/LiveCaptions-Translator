using Avalonia;
using Avalonia.Media;

namespace LiveCaptionsTranslator
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Headless check of the core (settings, databases, slides, tools); also used by CI.
            if (args.Contains("--selftest"))
                return SelfTest.Run();

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Chinese text needs a CJK font; the first one installed is used for the glyphs the main font lacks.
            .With(new FontManagerOptions
            {
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("Noto Sans CJK SC") },
                    new FontFallback { FontFamily = new FontFamily("PingFang SC") },
                    new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") },
                    new FontFallback { FontFamily = new FontFamily("WenQuanYi Zen Hei") },
                    new FontFallback { FontFamily = new FontFamily("WenQuanYi Micro Hei") },
                },
            })
            .LogToTrace();
    }
}
