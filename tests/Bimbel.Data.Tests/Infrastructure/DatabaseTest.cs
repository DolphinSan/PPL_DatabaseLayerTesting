using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bimbel.Data.Tests.Infrastructure;

/// <summary>
/// Base class semua test. Sebelum tiap test, semua tabel dikosongkan
/// sehingga test tidak saling memengaruhi.
/// </summary>
[Collection(DatabaseCollection.Name)]
public abstract class DatabaseTest : IAsyncLifetime
{
    protected DatabaseTest(PostgresFixture fixture) => Fixture = fixture;

    protected PostgresFixture Fixture { get; }

    /// <summary>
    /// Selalu buat DbContext BARU untuk membaca hasil.
    /// Dengan begitu data benar-benar diambil dari database, bukan dari change tracker.
    /// </summary>
    protected BimbelDbContext NewContext() => Fixture.CreateContext();

    public async Task InitializeAsync()
    {
        await using var context = NewContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE enrollments, course_sessions, courses, students, mentors RESTART IDENTITY CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Menjalankan aksi yang diharapkan melanggar constraint,
    /// lalu mengembalikan error asli dari PostgreSQL.
    /// </summary>
    protected static async Task<PostgresException> AssertViolationAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(action);
        return Assert.IsType<PostgresException>(ex.InnerException);
    }
}
