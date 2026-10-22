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
            "https://api.github.com/repos/YingQiu0871/LiveCaptions-Translator/releases?per_page=10";
        private static readonly TimeSpan CHECK_INTERVAL = TimeSpan.FromHours(24);
        private static readonly TimeSpan RENOTIFY_INTERVAL = TimeSpan.FromDays(3);

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

        // The result of the last successful check in this run, for the About page to show again.
        public static UpdateInfo? Latest { get; private set; }

        // The highest build among the (non-draft, non-prerelease) lecture releases of a GitHub releases list;
        // other tags are ignored. Returns null when there is none.
        public static UpdateInfo? PickLatest(JsonElement releases, int? current)
        {
            if (releases.ValueKind != JsonValueKind.Array)
                return null;
            UpdateInfo? best = null;
            foreach (var release in releases.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object)
                    continue;
                if (IsTrue(release, "draft") || IsTrue(release, "prerelease"))
                    continue;
                string tag = release.TryGetProperty("tag_name", out var name) ? name.GetString() ?? "" : "";
                if (ParseTag(tag) is not int build || (best != null && build <= best.LatestBuild))
                    continue;
                string url = release.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() ?? "" : "";
                DateTime? published = null;
                if (release.TryGetProperty("published_at", out var date) && date.ValueKind == JsonValueKind.String &&
                    date.TryGetDateTime(out var parsed))
                    published = parsed;
                best = new UpdateInfo
                {
                    LatestBuild = build,
                    Tag = tag.Trim(),
                    Url = url.StartsWith("https://") ? url : RELEASES_URL,
                    PublishedAt = published,
                    IsNewer = IsNewer(current, build)
                };
            }
            return best;
        }

        private static bool IsTrue(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

        // Whether the periodic check is due; a timestamp in the future (clock change) counts as due.
        public static bool IsCheckDue(DateTime lastCheckUtc, DateTime nowUtc) =>
            lastCheckUtc > nowUtc || nowUtc - lastCheckUtc >= CHECK_INTERVAL;

        // Whether this build should be announced: not yet announced, or the last announcement is a few days old.
        public static bool ShouldAnnounce(int notifiedBuild, DateTime noticeUtc, int latest, DateTime nowUtc) =>
            notifiedBuild < latest || noticeUtc > nowUtc || nowUtc - noticeUtc >= RENOTIFY_INTERVAL;

        // Called by the About page after its own check found a newer build: the startup toast need not repeat it.
        public static void MarkNotified(UpdateInfo info)
        {
            var lecture = Translator.Setting?.Lecture;
            if (lecture == null || !info.IsNewer)
                return;
            lecture.LastNotifiedUpdateBuild = info.LatestBuild;
            lecture.LastUpdateNotice = DateTime.UtcNow;
        }

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
                var info = PickLatest(document.RootElement, CurrentBuild);
                if (info != null)
                    Latest = info;
                return info;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // At startup: check once a day, and tell about each new build once (again after a few days). Does nothing in a development build.
        public static async Task AutoCheckAsync()
        {
            var lecture = Translator.Setting?.Lecture;
            if (lecture == null || CurrentBuild == null || !lecture.AutoCheckUpdate)
                return;
            if (!IsCheckDue(lecture.LastUpdateCheck, DateTime.UtcNow))
                return;

            var info = await CheckAsync();
            if (info == null)
                return;
            lecture.LastUpdateCheck = DateTime.UtcNow;
            if (!info.IsNewer || !ShouldAnnounce(lecture.LastNotifiedUpdateBuild, lecture.LastUpdateNotice,
                                                  info.LatestBuild, DateTime.UtcNow))
                return;
            // Only a toast that was really shown counts as told (the main window may not be there yet).
            if (!SnackbarHost.CanShow)
                return;
            SnackbarHost.Show($"发现新版本 {info.Tag}", "到「关于」页查看并下载。", SnackbarType.Info,
                              timeout: 8, closeButton: true);
            MarkNotified(info);
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
