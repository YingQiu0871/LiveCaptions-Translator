using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LiveCaptionsTranslator.utils
{
    public record AudioDevice(string Id, string Name, bool IsDefault)
    {
        public override string ToString() => IsDefault ? $"{Name}（当前默认）" : Name;
    }

    // Same names as NAudio's DataFlow, which the Windows version of this class takes.
    public enum DataFlow
    {
        Render,
        Capture,
        All,
    }

    // Linux: PulseAudio / PipeWire through `pactl`. macOS: the output devices known to `say`.
    public static class AudioDevices
    {
        public static List<AudioDevice> List(DataFlow flow) => ListAsync(flow).GetAwaiter().GetResult();

        public static async Task<List<AudioDevice>> ListAsync(DataFlow flow)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                    return flow == DataFlow.Capture ? new List<AudioDevice>() : await ListMacOutputs().ConfigureAwait(false);
                return await ListPulse(flow).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return new List<AudioDevice>();
            }
        }

        // The id of the default device; null if it cannot be told (macOS: `say` does not mark it).
        public static string? DefaultId(DataFlow flow) => DefaultIdAsync(flow).GetAwaiter().GetResult();

        public static async Task<string?> DefaultIdAsync(DataFlow flow)
        {
            if (!OperatingSystem.IsLinux() || flow == DataFlow.All)
                return null;
            string command = flow == DataFlow.Capture ? "get-default-source" : "get-default-sink";
            return (await Exec.Run("pactl", new[] { command }).ConfigureAwait(false))?.Trim();
        }

        // Makes the device the default one for the whole system (Linux only).
        public static void SetDefault(string id)
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("请在系统设置的“声音”里更换默认设备。");
            var sources = Exec.Run("pactl", new[] { "list", "short", "sources" }).GetAwaiter().GetResult() ?? string.Empty;
            bool isSource = sources.Split('\n').Any(line => line.Split('\t').ElementAtOrDefault(1) == id);
            var process = Process.Start(Exec.Create("pactl", new[] { isSource ? "set-default-source" : "set-default-sink", id }));
            process?.WaitForExit(5000);
        }

        // `pactl list short sources`: index, name, driver, sample spec, state. Monitors of the outputs are the
        // sources named *.monitor: they carry what the computer plays.
        private static async Task<List<AudioDevice>> ListPulse(DataFlow flow)
        {
            bool capture = flow == DataFlow.Capture;
            string? text = await Exec.Run("pactl", new[] { "list", "short", capture ? "sources" : "sinks" }).ConfigureAwait(false);
            if (text == null)
                return new List<AudioDevice>();
            string? defaultId = await DefaultIdAsync(flow).ConfigureAwait(false);

            var result = new List<AudioDevice>();
            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split('\t');
                if (fields.Length < 2)
                    continue;
                string name = fields[1];
                if (capture && name.EndsWith(".monitor", StringComparison.Ordinal))
                    continue;
                result.Add(new AudioDevice(name, name, name == defaultId));
            }
            return result;
        }

        // The sources that carry the sound the computer plays, one per output (`pactl ... sources` *.monitor).
        public static async Task<List<AudioDevice>> ListMonitors()
        {
            string? text = await Exec.Run("pactl", new[] { "list", "short", "sources" }).ConfigureAwait(false);
            if (text == null)
                return new List<AudioDevice>();
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t'))
                .Where(fields => fields.Length >= 2 && fields[1].EndsWith(".monitor", StringComparison.Ordinal))
                .Select(fields => new AudioDevice(fields[1], fields[1], false))
                .ToList();
        }

        // `say -a '?'` prints "  73 MacBook Pro Speakers": a CoreAudio id that changes between sessions, then the
        // name. `say -a` also takes the name, so that is what is stored and passed.
        private static async Task<List<AudioDevice>> ListMacOutputs()
        {
            string? text = await Exec.Run("say", new[] { "-a", "?" }).ConfigureAwait(false);
            var result = new List<AudioDevice>();
            if (text == null)
                return result;
            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(line, @"^\s*(\d+)\s+(.+?)\s*$");
                if (match.Success)
                    result.Add(new AudioDevice(match.Groups[2].Value, match.Groups[2].Value, false));
            }
            return result;
        }
    }
}
