# Bimbel Testing: Database Layer Testing dengan Testcontainers, EF Core & PostgreSQL

Tugas Eksplorasi PPL (M) - Kelompok 1
Anggota: Oktavian Ramadhan, Kadek Angga Wistara, Danendra Nayottama H., M. Khalid Ash Shidiqqi

## 1. Pengenalan Singkat

Tutorial ini menguji **lapisan database** (persistence, migrations, mapping, relationships, constraints) memakai **Testcontainers**, **EF Core**, dan **PostgreSQL asli** di dalam container Docker. Studi kasusnya sistem bimbingan belajar (Bimbel) dengan 5 tabel: `mentors`, `courses`, `course_sessions`, `students`, `enrollments`

Database in-memory dan SQLite tidak cukup karena foreign key, check constraint, dan delete behavior tidak berperilaku seperti PostgreSQL, dan migration tidak benar-benar dijalankan. Akibatnya test bisa hijau di laptop tetapi rusak di production

- **Testcontainers**: menyalakan container PostgreSQL lewat Docker, memakai port acak, menunggu database siap, lalu menghapus container setelah test selesai
- **EF Core + Npgsql**: ORM, migration, dan mapping (Fluent API) ke PostgreSQL
- **xUnit**: test framework dan pengelola fixture

Alur: satu container PostgreSQL dinyalakan, migration dijalankan, semua test berjalan (tabel dikosongkan dengan `TRUNCATE` sebelum tiap test), lalu container dihapus otomatis

## 2. Persiapan Lingkungan

- **.NET SDK 10.0+** (`dotnet --version`)
- **Docker Desktop** (Windows/macOS) atau Docker Engine (Linux), harus **sedang berjalan** (`docker info` tidak error)
- **EF Core CLI**: `dotnet tool install --global dotnet-ef`
- IDE: VS Code, Visual Studio, atau Rider

Windows/macOS: buka Docker Desktop dan tunggu status **Engine running**

> Run pertama butuh internet untuk mengunduh image `postgres:16-alpine`.

## 3. Instalasi

```bash
git clone https://github.com/DolphinSan/PPL_DatabaseLayerTesting.git
cd PPL_DatabaseLayerTesting
dotnet restore
dotnet build
```

Source code aplikasi ada di `src/Bimbel.Data/` dan source code pengujian di `tests/Bimbel.Data.Tests/`

## 4. Langkah Implementasi Pengujian

**Langkah 1. Buat solution dan paket.**
```bash
dotnet new sln -n BimbelTesting
dotnet new classlib -n Bimbel.Data -o src/Bimbel.Data
dotnet new classlib -n Bimbel.Data.Tests -o tests/Bimbel.Data.Tests
dotnet sln add src/Bimbel.Data tests/Bimbel.Data.Tests
dotnet add tests/Bimbel.Data.Tests reference src/Bimbel.Data

dotnet add src/Bimbel.Data package Microsoft.EntityFrameworkCore
dotnet add src/Bimbel.Data package Microsoft.EntityFrameworkCore.Design
dotnet add src/Bimbel.Data package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add tests/Bimbel.Data.Tests package Microsoft.NET.Test.Sdk
dotnet add tests/Bimbel.Data.Tests package xunit --version 2.9.3
dotnet add tests/Bimbel.Data.Tests package xunit.runner.visualstudio
dotnet add tests/Bimbel.Data.Tests package Testcontainers.PostgreSql
```

**Langkah 2. Buat entitas** `Mentor`, `Course`, `CourseSession`, `Student`, `Enrollment`, dan enum `EnrollmentStatus` di `src/Bimbel.Data/Entities/`. Contoh:
```csharp
public class Course
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int MentorId { get; set; }
    public Mentor Mentor { get; set; } = null!;
    public List<CourseSession> Sessions { get; set; } = new();
    public List<Enrollment> Enrollments { get; set; } = new();
}
```

**Langkah 3. Buat `BimbelDbContext`** dan konfigurasi Fluent API per entitas di `src/Bimbel.Data/Configurations/`:
```csharp
// DbContext memuat semua konfigurasi sekaligus
protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.ApplyConfigurationsFromAssembly(typeof(BimbelDbContext).Assembly);
```
Konfigurasi penting:
```csharp
// Mapping: presisi harga
builder.Property(c => c.Price).HasColumnType("numeric(12,2)");

// Mapping: enum disimpan sebagai teks + unique index komposit
builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
builder.HasIndex(e => new { e.StudentId, e.CourseId }).IsUnique();

builder.HasIndex(m => m.Email).IsUnique();

// Relationship: hapus mentor ditolak bila masih punya course
builder.HasMany(m => m.Courses).WithOne(c => c.Mentor)
       .HasForeignKey(c => c.MentorId).OnDelete(DeleteBehavior.Restrict);

// Relationship: hapus course ikut menghapus sessions
builder.HasMany(c => c.Sessions).WithOne(s => s.Course)
       .HasForeignKey(s => s.CourseId).OnDelete(DeleteBehavior.Cascade);

builder.ToTable("course_sessions", t => t.HasCheckConstraint(
    "ck_course_sessions_duration_positive", "\"DurationMinutes\" > 0"));
```

**Langkah 4. Buat migration.** Tambahkan `BimbelDbContextFactory` (`IDesignTimeDbContextFactory<BimbelDbContext>`) agar `dotnet ef` bisa membuat DbContext tanpa database berjalan, lalu:
```bash
dotnet ef migrations add InitialCreate --project src/Bimbel.Data
```

**Langkah 5. Buat fixture Testcontainers** (`tests/Bimbel.Data.Tests/Infrastructure/PostgresFixture.cs`). Satu container dipakai semua test:
```csharp
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();   // migration rusak = semua test gagal di sini
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public BimbelDbContext CreateContext() => CreateContext(ConnectionString);

    public static BimbelDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<BimbelDbContext>().UseNpgsql(connectionString).Options);
}

[CollectionDefinition(Name)]
public class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "database";
}
```

**Langkah 6. Buat base class** (`DatabaseTest.cs`) agar tiap test mulai dari tabel kosong dan bisa menangkap error asli PostgreSQL:
```csharp
[Collection(DatabaseCollection.Name)]
public abstract class DatabaseTest(PostgresFixture fixture) : IAsyncLifetime
{
    protected PostgresFixture Fixture { get; } = fixture;

    // Context BARU = data dibaca dari database, bukan dari change tracker
    protected BimbelDbContext NewContext() => Fixture.CreateContext();

    public async Task InitializeAsync()
    {
        await using var context = NewContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE enrollments, course_sessions, courses, students, mentors RESTART IDENTITY CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected static async Task<PostgresException> AssertViolationAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(action);
        return Assert.IsType<PostgresException>(ex.InnerException);
    }
}
```
Tambahkan juga `TestData.cs` (pembuat `Mentor`, `Student`, `Course`, `CourseSession` dengan email unik) seperti di repo.

**Langkah 7. Tulis test.** Class test mewarisi `DatabaseTest`:
```csharp
public class PersistenceTests(PostgresFixture fixture) : DatabaseTest(fixture) { ... }
public class ConstraintTests(PostgresFixture fixture) : DatabaseTest(fixture) { ... }
```

*Persistence*: simpan dengan satu context, baca dengan context baru
```csharp
await using (var write = NewContext())
{
    write.Courses.Add(course);       // mentor dan sessions ikut tersimpan lewat relasi
    await write.SaveChangesAsync();
}
await using var read = NewContext();
var saved = await read.Courses.Include(c => c.Mentor).Include(c => c.Sessions).SingleAsync();
Assert.True(saved.Id > 0);           // Id dibuat oleh PostgreSQL
Assert.Equal(2, saved.Sessions.Count);
```

*Migrations*: jalankan migration ke database kosong, lalu cek tabel
```csharp
var cs = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Database = "bimbel_from_scratch" }.ConnectionString;
await using var context = PostgresFixture.CreateContext(cs);
await context.Database.MigrateAsync();

var tables = await context.Database
    .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
    .ToListAsync();
Assert.Contains("mentors", tables);
```
Deteksi migration yang terlupa: `Assert.False(context.Database.HasPendingModelChanges());`

*Mapping*: tanya PostgreSQL tipe kolom aslinya
```csharp
[Theory]
[InlineData("courses", "Price", "numeric(12,2)")]
[InlineData("course_sessions", "StartTime", "timestamp with time zone")]
[InlineData("enrollments", "Status", "character varying(20)")]
public async Task Column_has_the_expected_postgres_type(string table, string column, string expectedType)
{
    await using var context = NewContext();
    var actualType = await context.Database.SqlQueryRaw<string>(
        """
        SELECT format_type(a.atttypid, a.atttypmod) AS "Value"
        FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
        WHERE c.relname = {0} AND a.attname = {1} AND c.relnamespace = 'public'::regnamespace
        """, table, column).SingleAsync();
    Assert.Equal(expectedType, actualType);
}
```

*Relationships*: hapus mentor yang masih punya course harus ditolak
```csharp
delete.Mentors.Remove(mentorToDelete);
var error = await AssertViolationAsync(() => delete.SaveChangesAsync());
Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);   // 23503
```
Untuk cascade: hapus `Course`, lalu pastikan `CourseSession` dan `Enrollment` miliknya hilang sementara `Mentor` dan `Student` tetap ada

*Constraints*: database harus menolak data tidak valid.
```csharp
// Unique: email mentor kembar
var error = await AssertViolationAsync(() => second.SaveChangesAsync());
Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);       // 23505

// Check: durasi sesi 0 atau negatif
var error = await AssertViolationAsync(() => write.SaveChangesAsync());
Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);        // 23514
```
Kode lengkap semua test ada di `PersistenceTests.cs` dan `ConstraintTests.cs`

**Langkah 8. Skenario gagal** Test ini sengaja merah karena menyimpan sesi berdurasi `-45` tanpa menangani constraint:
```csharp
write.CourseSessions.Add(invalidSession);   // DurationMinutes = -45
await write.SaveChangesAsync();             // melempar DbUpdateException -> FAILED
```
Perbaikannya test `Session_dengan_durasi_nol_atau_negatif_ditolak_oleh_check_constraint`: ia mengharapkan penolakan database dan memeriksa SQLSTATE `23514`

## 5. Cara Menjalankan

Pastikan Docker Desktop menyala, lalu jalankan dari direktori root:

| Tujuan | Perintah |
|---|---|
| Semua test normal | `dotnet test --filter "FullyQualifiedName!~Demo_SkenarioGagal"` |
| Hanya skenario demo gagal | `dotnet test --filter "FullyQualifiedName~Demo_SkenarioGagal"` |
| Semua test termasuk demo | `dotnet test` |
| Persistence, Migrations, Mapping | `dotnet test --filter "FullyQualifiedName~PersistenceTests"` |
| Relationships & Constraints | `dotnet test --filter "FullyQualifiedName~ConstraintTests&FullyQualifiedName!~Demo_SkenarioGagal"` |

Total **26 test**: 25 normal dan 1 demo gagal yang disengaja

## 6. Hasil yang Diharapkan

Test normal:
```text
Passed!  - Failed: 0, Passed: 25, Skipped: 0, Total: 25
```

Skenario gagal:
```text
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1

Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes.
---- Npgsql.PostgresException : 23514: new row for relation "course_sessions"
     violates check constraint "ck_course_sessions_duration_positive"
```

Semua test (`dotnet test`): 25 Passed, 1 Failed (test demo)

Selama test berjalan, container `postgres:16-alpine` muncul di Docker Desktop lalu hilang otomatis setelah selesai. Run pertama bisa beberapa menit karena mengunduh image

Kode error PostgreSQL yang diverifikasi: `23503` (foreign key), `23505` (unique), `23514` (check)
