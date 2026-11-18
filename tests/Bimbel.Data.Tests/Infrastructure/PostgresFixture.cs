using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Bimbel.Data.Tests.Infrastructure;

/// <summary>
/// Menyalakan SATU container PostgreSQL asli untuk seluruh test,
/// lalu menjalankan semua migration dari database kosong.
/// Container otomatis dibuang setelah test selesai.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Kalau ada migration yang rusak, SEMUA test gagal di sini.
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public BimbelDbContext CreateContext() => CreateContext(ConnectionString);

    public static BimbelDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<BimbelDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new BimbelDbContext(options);
    }
}

[CollectionDefinition(Name)]
public class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "database";
}
