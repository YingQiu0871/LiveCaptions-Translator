using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    public class UpdateInfo
    {
        public int LatestBuild { get; init; }
        public string Tag { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public DateTime? PublishedAt { get; init; }
        public bool IsNewer { get; init; }
    }

    // Looks for a newer release on GitHub. It only tells; downloading and installing is left to the user.
    public static class UpdateChecker
    {
        public const string RELEASES_URL = "https://github.com/YingQiu0871/LiveCaptions-Translator/releases";
        private const string LATEST_URL =
            "https://api.github.com/repos/YingQiu0871/LiveCaptions-Translator/releases/latest";

        private static readonly Regex tagPattern = new(@"^v1\.7-lecture\.(\d+)$");
        private static readonly HttpClient client = CreateClient();

        // The build number the workflow passed in (-p:LectureBuild=N); null for a local or development build.
        public static int? CurrentBuild { get; } = ReadCurrentBuild();

        public static string CurrentVersionText => CurrentBuild is int build
            ? TagOf(build)
            : typeof(UpdateChecker).Assembly.GetName().Version?.ToString() ?? string.Empty;

        public static string TagOf(int build) => $"v1.7-lecture.{build}";

        // The build number in a release tag such as "v1.7-lecture.12"; null for any other tag.
        public static int? ParseTag(string? tag)
        {
            if (tag == null)
                return null;
            var match = tagPattern.Match(tag.Trim());
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int build))
                return null;
            return build;
        }

        public static bool IsNewer(int? current, int latest) => current is int value && latest > value;

        // Returns null when the check cannot be completed (offline, rate limited, unexpected reply):
        // an update check must never disturb the user, so the callers only report "failed" if they were asked.
        public static async Task<UpdateInfo?> CheckAsync()
        {
            try
            {
                using var response = await client.GetAsync(LATEST_URL);
                if (!response.IsSuccessStatusCode)
                    return null;
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = document.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                if (ParseTag(tag) is not int latest)
                    return null;
                string url = root.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() ?? "" : "";
                DateTime? published = null;
                if (root.TryGetProperty("published_at", out var date) && date.TryGetDateTime(out var parsed))
                    published = parsed;

                return new UpdateInfo
                {
                    LatestBuild = latest,
                    Tag = tag,
                    Url = url.StartsWith("https://") ? url : RELEASES_URL,
                    PublishedAt = published,
                    IsNewer = IsNewer(CurrentBuild, latest)
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        // At startup: check once a day, and tell about each new build only once. Does nothing in a development build.
        public static async Task AutoCheckAsync()
        {
            var lecture = Translator.Setting?.Lecture;
            if (lecture == null || CurrentBuild == null || !lecture.AutoCheckUpdate)
                return;
            if (DateTime.Now - lecture.LastUpdateCheck < TimeSpan.FromHours(24))
                return;

            var info = await CheckAsync();
            if (info == null)
                return;
            lecture.LastUpdateCheck = DateTime.Now;
            if (!info.IsNewer || lecture.LastNotifiedUpdateBuild >= info.LatestBuild)
                return;
            lecture.LastNotifiedUpdateBuild = info.LatestBuild;
            SnackbarHost.Show($"发现新版本 {info.Tag}", "到「关于」页查看并下载。", SnackbarType.Info,
                              timeout: 8, closeButton: true);
        }

        // Called once the app has started: waits a little so the check does not compete with the startup.
        public static void StartAutoCheck()
        {
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                await AutoCheckAsync();
            });
        }

        private static int? ReadCurrentBuild()
        {
            foreach (var attribute in typeof(UpdateChecker).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (attribute.Key == "LectureBuild" && int.TryParse(attribute.Value, out int build))
                    return build;
            }
            return null;
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LiveCaptionsTranslator-UpdateCheck");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }
    }
}
