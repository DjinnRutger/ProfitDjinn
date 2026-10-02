using System.Globalization;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Services;

/// <summary>
/// The "back up your data" reminder shown at start. It is due when it is turned on and the
/// chosen number of days has passed since the countdown last restarted. The countdown
/// restarts when the reminder is answered either way, and whenever a backup is made.
/// A database that has never had a countdown starts one on first check rather than asking
/// straight away, so a new install is not greeted with a reminder.
/// </summary>
public sealed class BackupReminder
{
    public const int DefaultDays = 7;
    public const int MaxDays = 365;

    private readonly SettingsService _settings;
    private readonly Func<DateOnly> _today;

    public BackupReminder(SettingsService settings, Func<DateOnly> today)
    {
        _settings = settings;
        _today = today;
    }

    public bool Enabled => Setting.AsBool(_settings.Get(SettingKeys.BackupReminderEnabled, "true"));

    /// <summary>The saved interval. A blank or broken value falls back to 7 days.</summary>
    public int Days
    {
        get
        {
            string raw = _settings.Get(SettingKeys.BackupReminderDays).Trim();
            return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int d) && d is >= 1 and <= MaxDays ? d : DefaultDays;
        }
    }

    /// <summary>True when the reminder should be shown now. Starts the countdown if none is running.</summary>
    public bool IsDue()
    {
        if (!Enabled) return false;
        if (LastRestart() is not { } last)
        {
            Restart();
            return false;
        }
        return _today().DayNumber - last.DayNumber >= Days;
    }

    /// <summary>Restarts the countdown from today: the reminder was answered, or a backup was made.</summary>
    public void Restart() =>
        _settings.Set(SettingKeys.BackupReminderLast, _today().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>Checks a typed interval before Settings saves it. Returns null when it is fine.</summary>
    public static string? Validate(string days) =>
        int.TryParse(days.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int d) && d is >= 1 and <= MaxDays
            ? null
            : $"Days between backup reminders must be a whole number from 1 to {MaxDays}.";

    private DateOnly? LastRestart() =>
        DateOnly.TryParseExact(_settings.Get(SettingKeys.BackupReminderLast).Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
