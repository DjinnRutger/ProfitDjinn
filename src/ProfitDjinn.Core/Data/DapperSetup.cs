using System.Data;
using Dapper;

namespace ProfitDjinn.Core.Data;

/// <summary>
/// Teaches Dapper this database's conventions: snake_case columns onto PascalCase
/// properties, and DATE columns stored as "YYYY-MM-DD" text onto DateOnly.
/// </summary>
internal static class DapperSetup
{
    private static int _done;

    internal static void Ensure()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1) return;
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = SqlFormat.Date(value);
        }

        public override DateOnly Parse(object value) =>
            SqlFormat.ParseDate(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
            ?? throw new DataException("A required date in the database is empty.");
    }

    private sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value is { } d ? SqlFormat.Date(d) : DBNull.Value;
        }

        public override DateOnly? Parse(object value) =>
            value is null or DBNull ? null : SqlFormat.ParseDate(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
    }
}
