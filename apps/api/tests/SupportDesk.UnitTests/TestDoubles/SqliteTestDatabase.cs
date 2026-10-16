using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>
/// A throwaway SQLite in-memory database with the real EF model. The connection stays open
/// for the lifetime of the object, because an in-memory SQLite database disappears when its
/// connection closes.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<SupportDbContext> _options;

    public SqliteTestDatabase()
    {
        _connection.Open();

        _options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public SupportDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
