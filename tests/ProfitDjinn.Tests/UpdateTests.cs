using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("v2.3.0-beta", "2.2.0-beta", true)]
    [InlineData("v2.2.0", "2.2.0-beta", true)]       // the release after a beta of the same numbers
    [InlineData("v2.2.0-beta", "2.2.0", false)]
    [InlineData("v2.2.0-beta", "2.2.0-beta+abc123", false)]
    [InlineData("v2.2.0-beta2", "2.2.0-beta", true)]
    [InlineData("v2.10.0", "2.9.9", true)]
    [InlineData("v1.9.0", "2.0.0-beta", false)]
    [InlineData("nonsense", "2.0.0", false)]
    public void Versions_compare_like_semantic_versions(string candidate, string running, bool newer) =>
        Assert.Equal(newer, UpdateService.IsNewer(candidate, running));

    private static UpdateService Service(ProfitDjinn.Core.Store s, string running, Func<CancellationToken, Task<string>> fetch, DateOnly day) =>
        new(s.Settings, () => day, running, fetch);

    [Fact]
    public async Task A_newer_release_is_remembered_and_checked_at_most_once_a_day()
    {
        var day = new DateOnly(2026, 10, 2);
        var s = Fixture.FreshStore(day);
        int calls = 0;
        var u = Service(s, "2.2.0-beta+abc", _ => { calls++; return Task.FromResult("""{"tag_name":"v2.3.0-beta","html_url":"https://github.com/DjinnRutger/ProfitDjinn/releases/tag/v2.3.0-beta"}"""); }, day);
        Assert.True(u.DueToday);
        Assert.Null(u.Known);

        var r = await u.CheckAsync();
        Assert.True(r.Ok);
        Assert.Equal("2.3.0-beta", r.Update!.Version);
        Assert.Equal("Version 2.3.0-beta is available. You have 2.2.0-beta.", r.Message);
        Assert.False(u.DueToday);
        Assert.Equal(1, calls);

        // Next start, same day, no network: the badge still knows.
        var later = Service(s, "2.2.0-beta", _ => throw new HttpRequestException("offline"), day);
        Assert.Equal("https://github.com/DjinnRutger/ProfitDjinn/releases/tag/v2.3.0-beta", later.Known!.Url);
        Assert.False(later.DueToday);

        // After updating, the stored answer is no longer newer.
        Assert.Null(Service(s, "2.3.0-beta", _ => Task.FromResult("{}"), day).Known);
    }

    [Fact]
    public async Task Offline_or_odd_answers_are_reported_not_thrown_and_the_switch_turns_it_off()
    {
        var day = new DateOnly(2026, 10, 2);
        var s = Fixture.FreshStore(day);
        var offline = await Service(s, "2.2.0", _ => throw new HttpRequestException("no route"), day).CheckAsync();
        Assert.False(offline.Ok);
        Assert.Contains("Could not reach GitHub", offline.Message);
        Assert.False((await Service(s, "2.2.0", _ => Task.FromResult("<html>"), day).CheckAsync()).Ok);
        Assert.False((await Service(s, "2.2.0", _ => Task.FromResult("{\"name\":\"x\"}"), day).CheckAsync()).Ok);
        Assert.Equal("", s.Settings.Get(SettingKeys.UpdateLastCheck));     // a failed check is retried next start

        var latest = await Service(s, "2.2.0", _ => Task.FromResult("""{"tag_name":"v2.2.0","html_url":"https://evil.example/x"}"""), day).CheckAsync();
        Assert.Equal("You have the latest version (2.2.0).", latest.Message);

        s.Settings.Set(SettingKeys.UpdateLatestVersion, "v9.0.0");
        s.Settings.Set(SettingKeys.UpdateLatestUrl, "https://evil.example/x");
        Assert.Equal(UpdateService.ReleasesPage, Service(s, "2.2.0", _ => Task.FromResult("{}"), day).Known!.Url);   // only github.com links open

        s.Settings.Set(SettingKeys.UpdateCheckEnabled, "false");
        Assert.False(Service(s, "2.2.0", _ => Task.FromResult("{}"), day.AddDays(1)).DueToday);
    }
}
