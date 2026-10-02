using System.Text.Json;
using ProfitDjinn.Core;

namespace ProfitDjinn.Tests;

/// <summary>Test helpers: fixture files, and throwaway data folders that never touch the real database.</summary>
internal static class Fixture
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static JsonElement Json(string name) => JsonDocument.Parse(File.ReadAllText(PathOf(name))).RootElement;

    /// <summary>An empty data folder in the temp directory.</summary>
    public static AppPaths TempPaths()
    {
        string dir = Path.Combine(Path.GetTempPath(), "profitdjinn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new AppPaths(dir);
    }

    /// <summary>A data folder holding a copy of a fixture database, so tests can change it freely.</summary>
    public static AppPaths CopyOf(string fixtureDb)
    {
        var paths = TempPaths();
        File.Copy(PathOf(fixtureDb), paths.DatabasePath);
        return paths;
    }

    public static Store FreshStore(DateOnly? today = null) =>
        new(TempPaths(), today is { } d ? () => d : null);
}
