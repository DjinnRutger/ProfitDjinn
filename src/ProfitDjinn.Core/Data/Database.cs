using Microsoft.Data.Sqlite;

namespace ProfitDjinn.Core.Data;

/// <summary>
/// The SQLite file. Every call opens and closes its own connection with pooling off, so
/// nothing holds app.db open between calls and a backup or restore never finds it locked.
///
/// Foreign keys stay OFF, as they always have been for this file (PRAGMA foreign_keys = 0).
/// Turning them on could reject rows an older build wrote. Cascades are done in code by the
/// services, the way SQLAlchemy did them in 1.x.
/// </summary>
public sealed class Database
{
    public string Path { get; }

    private readonly string _connectionString;

    public Database(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            ForeignKeys = false,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    public T Run<T>(Func<SqliteConnection, T> work)
    {
        using var db = Open();
        return work(db);
    }

    public void Run(Action<SqliteConnection> work)
    {
        using var db = Open();
        work(db);
    }

    /// <summary>All of <paramref name="work"/> is saved, or none of it.</summary>
    public T InTransaction<T>(Func<SqliteConnection, SqliteTransaction, T> work)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        T result = work(db, tx);
        tx.Commit();
        return result;
    }

    public void InTransaction(Action<SqliteConnection, SqliteTransaction> work) =>
        InTransaction<object?>((db, tx) => { work(db, tx); return null; });
}
