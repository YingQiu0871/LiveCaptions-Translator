using System.Diagnostics;
using System.Text;

namespace LiveCaptionsTranslator.utils
{
    // Runs the small command-line tools the Linux / macOS version relies on (parec, pactl, say, spd-say,
    // pdftoppm, tesseract).
    internal static class Exec
    {
        // A program started from the macOS Finder or a Linux launcher gets a minimal PATH, without Homebrew.
        private static readonly string[] ExtraDirectories =
        {
            "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin", "/snap/bin",
        };

        public static string? Find(string name)
        {
            var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Concat(ExtraDirectories);
            foreach (string directory in directories)
            {
                try
                {
                    string path = Path.Combine(directory, name);
                    if (File.Exists(path))
                        return path;
                }
                catch (Exception)
                {
                    // An unreadable directory in PATH is skipped.
                }
            }
            return null;
        }

        public static bool Exists(string name) => Find(name) != null;

        private static void ExtendPath(ProcessStartInfo info)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            info.Environment["PATH"] = string.Join(Path.PathSeparator, ExtraDirectories.Prepend(path));
        }

        public static ProcessStartInfo Create(string fileName, IEnumerable<string> arguments)
        {
            var info = new ProcessStartInfo(Find(fileName) ?? fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            ExtendPath(info);
            return info;
        }

        // Starts a GUI tool (a settings page, the file manager) and leaves it alone: nothing is redirected, so it
        // cannot block on an output pipe nobody reads.
        public static void Launch(string fileName, IEnumerable<string> arguments)
        {
            var info = new ProcessStartInfo(Find(fileName) ?? fileName) { UseShellExecute = false };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            ExtendPath(info);
            using var process = Process.Start(info);
        }

        // Runs a tool to the end and returns its standard output; null if it is missing, fails or times out.
        public static async Task<string?> Run(string fileName, IEnumerable<string> arguments, int timeoutMs = 10000)
        {
            if (!Exists(fileName))
                return null;
            try
            {
                using var process = Process.Start(Create(fileName, arguments));
                if (process == null)
                    return null;
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(timeoutMs);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    return null;
                }
                string text = await output.ConfigureAwait(false);
                await error.ConfigureAwait(false);
                return process.ExitCode == 0 ? text : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Kill(Process? process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill(true);
            }
            catch (Exception)
            {
                // The process has already exited.
            }
        }
    }
}
