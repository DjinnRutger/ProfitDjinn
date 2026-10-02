using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ProfitDjinn.Core.Data;

namespace ProfitDjinn.Core.Services;

/// <summary>A release found on GitHub that is newer than the running version.</summary>
public sealed record AvailableUpdate(string Version, string Url);

/// <summary>What one check found, for Settings > Updates.</summary>
public sealed record UpdateCheck(bool Ok, AvailableUpdate? Update, string Message);

/// <summary>
/// 2.3. "Is there a newer ProfitDjinn?" Asks GitHub for the latest published release of the
/// public repo, at most once a day, at start, and only while the update_check_enabled
/// setting is on. Nothing about the user or their data is sent: it is one anonymous GET.
/// The answer is kept in settings, so the footer badge shows without asking again.
/// </summary>
public sealed class UpdateService
{
    public const string Repo = "DjinnRutger/ProfitDjinn";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
    private const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

    private readonly SettingsService _settings;
    private readonly Func<DateOnly> _today;
    private readonly string _running;
    private readonly Func<CancellationToken, Task<string>> _fetch;

    /// <param name="fetch">Returns the GitHub API's JSON. Tests pass a fake; the app uses <see cref="FetchFromGitHub"/>.</param>
    public UpdateService(SettingsService settings, Func<DateOnly> today, string runningVersion, Func<CancellationToken, Task<string>>? fetch = null)
    {
        _settings = settings;
        _today = today;
        _running = runningVersion;
        _fetch = fetch ?? FetchFromGitHub;
    }

    public bool Enabled => Model.Setting.AsBool(_settings.Get(SettingKeys.UpdateCheckEnabled, "true"));

    public string RunningVersion => _running;

    /// <summary>The newer release found by the last check, if it is still newer than this build.</summary>
    public AvailableUpdate? Known
    {
        get
        {
            string v = _settings.Get(SettingKeys.UpdateLatestVersion).Trim();
            string url = _settings.Get(SettingKeys.UpdateLatestUrl).Trim();
            if (v.Length == 0 || !IsNewer(v, _running)) return null;
            return new AvailableUpdate(Clean(v), url.StartsWith("https://github.com/", StringComparison.Ordinal) ? url : ReleasesPage);
        }
    }

    public DateOnly? LastChecked =>
        DateOnly.TryParseExact(_settings.Get(SettingKeys.UpdateLastCheck).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>True when the daily check at start should run now.</summary>
    public bool DueToday => Enabled && LastChecked != _today();

    /// <summary>
    /// Asks GitHub now. Never throws: a network problem comes back as a message and leaves the
    /// last known answer in place.
    /// </summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken cancel = default)
    {
        string json;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            json = await _fetch(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return new UpdateCheck(false, Known, "Could not reach GitHub to check for updates. Check the internet connection and try again.");
        }

        string? tag, url;
        try
        {
            using var doc = JsonDocument.Parse(json);
            tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        }
        catch (JsonException)
        {
            return new UpdateCheck(false, Known, "GitHub's answer could not be read. Try again later.");
        }
        if (string.IsNullOrWhiteSpace(tag) || ParseVersion(tag) is null)
            return new UpdateCheck(false, Known, "GitHub did not say which version is the latest. Try again later.");

        _settings.Set(SettingKeys.UpdateLastCheck, _today().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        _settings.Set(SettingKeys.UpdateLatestVersion, tag.Trim());
        _settings.Set(SettingKeys.UpdateLatestUrl, url ?? "");
        var update = Known;
        return new UpdateCheck(true, update, update is null
            ? $"You have the latest version ({Clean(_running)})."
            : $"Version {update.Version} is available. You have {Clean(_running)}.");
    }

    /// <summary>The real request: one anonymous GET to the GitHub API, which needs a User-Agent.</summary>
    public static async Task<string> FetchFromGitHub(CancellationToken cancel)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"ProfitDjinn/{Clean(AppInfo.Version)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.GetAsync(LatestApi, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ versions

    /// <summary>"v2.2.0-beta" to "2.2.0-beta"; build metadata after "+" dropped.</summary>
    public static string Clean(string v) => v.Trim().TrimStart('v', 'V').Split('+')[0];

    /// <summary>
    /// Semantic version order: numbers first, then a pre-release ("-beta") sorts before the
    /// same numbers without one, and pre-release labels compare as text (beta2 after beta).
    /// </summary>
    public static bool IsNewer(string candidate, string running)
    {
        var a = ParseVersion(candidate);
        var b = ParseVersion(running);
        if (a is null || b is null) return false;
        for (int i = 0; i < 3; i++)
            if (a.Value.Numbers[i] != b.Value.Numbers[i]) return a.Value.Numbers[i] > b.Value.Numbers[i];
        string pa = a.Value.Pre, pb = b.Value.Pre;
        if (pa == pb) return false;
        if (pa.Length == 0) return true;
        if (pb.Length == 0) return false;
        return string.CompareOrdinal(pa, pb) > 0;
    }

    private static (int[] Numbers, string Pre)? ParseVersion(string v)
    {
        string s = Clean(v);
        int dash = s.IndexOf('-');
        string core = dash >= 0 ? s[..dash] : s;
        string pre = dash >= 0 ? s[(dash + 1)..].ToLowerInvariant() : "";
        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3) return null;
        var nums = new int[3];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i])) return null;
        return (nums, pre);
    }
}
