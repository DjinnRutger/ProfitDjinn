using System.Globalization;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Services;

/// <summary>A receipt file copied into the receipts folder, before its database row exists.</summary>
public sealed record SavedReceipt(string FileName, string RelPath, string Folder, long SizeBytes);

/// <summary>How a "move receipts to the new folder" went.</summary>
public sealed record MoveResult(int Moved, int Missing, IReadOnlyList<string> Failed);

/// <summary>
/// 2.2. Receipt files on disk. They live in the receipts folder: the receipts_folder setting,
/// or &lt;data&gt;\receipts when that is empty. Each file is stored at yyyy\E{expense}-{name}; the
/// database keeps that relative path plus the folder it was saved under, so a receipt still
/// opens after the folder setting changes without moving the files.
/// </summary>
public sealed class ReceiptStore
{
    public const long MaxBytes = 25L * 1024 * 1024;

    public static readonly IReadOnlyList<string> Extensions = new[]
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic",
    };

    private readonly SettingsService _settings;
    private readonly AppPaths _paths;

    public ReceiptStore(SettingsService settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
    }

    public string DefaultFolder => _paths.ReceiptsFolder;

    /// <summary>Where new receipts go.</summary>
    public string CurrentFolder => Effective(_settings.Get(SettingKeys.ReceiptsFolder));

    /// <summary>The folder a receipts_folder value means: itself, or the default when empty.</summary>
    public string Effective(string? setting) =>
        string.IsNullOrWhiteSpace(setting) ? DefaultFolder : Path.GetFullPath(setting.Trim());

    /// <summary>Checks a file can be attached. Returns a message for the user, or null.</summary>
    public static string? Check(string source)
    {
        string name = Path.GetFileName(source);
        if (!File.Exists(source)) return $"'{name}' was not found.";
        if (!Extensions.Contains(Path.GetExtension(source).ToLowerInvariant()))
            return $"'{name}' is not a receipt type ProfitDjinn takes. Use a PDF or an image ({string.Join(", ", Extensions)}).";
        long size = new FileInfo(source).Length;
        if (size > MaxBytes) return $"'{name}' is larger than 25 MB.";
        if (size == 0) return $"'{name}' is empty.";
        return null;
    }

    /// <summary>Copies <paramref name="source"/> into the receipts folder for expense <paramref name="expenseId"/>.</summary>
    public SavedReceipt Save(long expenseId, string source, DateOnly expenseDate)
    {
        if (Check(source) is { } problem) throw new UserFacingException(problem);
        string folder = CurrentFolder;
        string name = Path.GetFileName(source);
        string year = expenseDate.Year.ToString(CultureInfo.InvariantCulture);
        string stem = $"E{expenseId}-{Sanitize(Path.GetFileNameWithoutExtension(name))}";
        string ext = Path.GetExtension(name).ToLowerInvariant();
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, year));
            string rel = Path.Combine(year, stem + ext);
            for (int n = 2; File.Exists(Path.Combine(folder, rel)); n++)
                rel = Path.Combine(year, $"{stem}-{n}{ext}");
            File.Copy(source, Path.Combine(folder, rel));
            return new SavedReceipt(name, rel, folder, new FileInfo(Path.Combine(folder, rel)).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UserFacingException($"'{name}' could not be copied to the receipts folder\n{folder}\n\n{ex.Message}", ex);
        }
    }

    /// <summary>The file on disk: in the current folder first, then the folder it was saved under. Null when it is in neither.</summary>
    public string? Resolve(ExpenseReceipt r)
    {
        foreach (string folder in new[] { CurrentFolder, r.Folder }.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(folder, r.RelPath);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>The path to open, or a message saying where it was looked for.</summary>
    public string PathFor(ExpenseReceipt r) => Resolve(r) ?? throw new UserFacingException(
        $"The receipt '{r.FileName}' was not found in\n{CurrentFolder}\nor\n{r.Folder}\n\nIt may have been moved or deleted outside ProfitDjinn.");

    /// <summary>
    /// Moves each receipt's file to <paramref name="newFolder"/>, keeping its relative path.
    /// <paramref name="moved"/> runs after each successful move so the caller can update that
    /// row at once; a failure part way leaves every row pointing at where its file really is.
    /// </summary>
    public MoveResult MoveAll(IEnumerable<ExpenseReceipt> receipts, string newFolder, Action<ExpenseReceipt> moved)
    {
        newFolder = Path.GetFullPath(newFolder);
        int done = 0, missing = 0;
        var failed = new List<string>();
        foreach (var r in receipts)
        {
            string? from = Resolve(r);
            if (from is null) { missing++; continue; }
            string to = Path.Combine(newFolder, r.RelPath);
            try
            {
                if (!string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    if (File.Exists(to)) throw new IOException($"A different file named {r.RelPath} is already in the new folder.");
                    File.Move(from, to);
                }
                moved(r);
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{r.FileName}: {ex.Message}");
            }
        }
        return new MoveResult(done, missing, failed);
    }

    /// <summary>Checks a folder can hold receipts by creating it and writing a test file. Returns a message, or null.</summary>
    public static string? ValidateFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Choose a folder for receipts.";
        try
        {
            string full = Path.GetFullPath(folder.Trim());
            Directory.CreateDirectory(full);
            string probe = Path.Combine(full, $".profitdjinn-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"ProfitDjinn cannot save receipts in\n{folder}\n\n{ex.Message}";
        }
    }

    /// <summary>Deletes a receipt's file if it can be found. Failing to delete is not an error.</summary>
    public void TryDelete(ExpenseReceipt r)
    {
        try
        {
            if (Resolve(r) is { } path) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Sanitize(string stem)
    {
        var bad = Path.GetInvalidFileNameChars();
        string clean = new string(stem.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (clean.Length > 80) clean = clean[..80];
        return clean.Length == 0 ? "receipt" : clean;
    }
}
