using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

/// <summary>
/// One Postgres container for the tests that need real database behaviour, such as row locks.
/// Every context from <see cref="NewContextAsync"/> gets its own database, built by the migrations.
/// </summary>
public sealed class PostgresDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .WithLogger(NullLogger.Instance)
        // Each test database keeps its own idle connection pool, so a long run passes the default limit of 100.
        .WithCommand("-c", "max_connections=500")
        .Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>A context on a new, migrated database.</summary>
    public async Task<ApplicationDbContext> NewContextAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = $"test_{Guid.NewGuid():N}"
        }.ConnectionString;

        var db = Create(connectionString);
        await db.Database.MigrateAsync();
        return db;
    }

    /// <summary>A second context on the database of <paramref name="existing"/>, for concurrency tests.</summary>
    public ApplicationDbContext NewContextOn(ApplicationDbContext existing) =>
        Create(existing.Database.GetConnectionString()!);

    private static ApplicationDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options);
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresDatabase>;
