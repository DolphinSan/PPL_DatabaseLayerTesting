using Bimbel.Data.Entities;
using Bimbel.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bimbel.Data.Tests;

/// <summary>
/// Persistence, migrations, dan mapping.
///
/// A. Persistence : data disimpan dan dibaca kembali dari database
/// B. Migrations  : skema tabel dibangun dari database kosong
/// C. Mapping     : tipe kolom, enum, dan presisi harga
/// </summary>
public class PersistenceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{

    [Fact]
    public async Task Save_graph_then_read_it_back_with_a_new_context()
    {
        var mentor = TestData.NewMentor();
        var course = TestData.NewCourse(mentor);
        course.Sessions.Add(TestData.NewSession("Limit Fungsi"));
        course.Sessions.Add(TestData.NewSession("Turunan"));

        await using (var write = NewContext())
        {
            write.Courses.Add(course); // mentor ikut tersimpan lewat relasi
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(); // context baru = baca dari database
        var saved = await read.Courses
            .Include(c => c.Mentor)
            .Include(c => c.Sessions)
            .SingleAsync();

        Assert.True(saved.Id > 0);                    // Id dibuat oleh PostgreSQL
        Assert.Equal("Matematika SMA", saved.Name);
        Assert.Equal(mentor.Email, saved.Mentor.Email);
        Assert.Equal(2, saved.Sessions.Count);
    }

    [Fact]
    public async Task Update_is_persisted()
    {
        int id;
        await using (var write = NewContext())
        {
            var course = TestData.NewCourse(TestData.NewMentor(), price: 100_000m);
            write.Courses.Add(course);
            await write.SaveChangesAsync();
            id = course.Id;
        }

        await using (var update = NewContext())
        {
            var course = await update.Courses.FindAsync(id);
            course!.Price = 175_000m;
            await update.SaveChangesAsync();
        }

        await using var read = NewContext();
        Assert.Equal(175_000m, (await read.Courses.FindAsync(id))!.Price);
    }

    [Fact]
    public async Task Delete_removes_the_row()
    {
        int id;
        await using (var write = NewContext())
        {
            var student = TestData.NewStudent();
            write.Students.Add(student);
            await write.SaveChangesAsync();
            id = student.Id;
        }

        await using (var delete = NewContext())
        {
            delete.Students.Remove((await delete.Students.FindAsync(id))!);
            await delete.SaveChangesAsync();
        }

        await using var read = NewContext();
        Assert.Null(await read.Students.FindAsync(id));
    }


    [Fact]
    public async Task Migrations_build_the_whole_schema_from_an_empty_database()
    {
        // Database baru yang benar-benar kosong, di dalam container yang sama.
        var connection = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = "bimbel_from_scratch"
        }.ConnectionString;

        await using var context = PostgresFixture.CreateContext(connection);
        await context.Database.MigrateAsync(); // membuat database + menjalankan semua migration

        var tables = await context.Database
            .SqlQueryRaw<string>(
                "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        foreach (var expected in new[]
                 { "mentors", "courses", "students", "enrollments", "course_sessions", "__EFMigrationsHistory" })
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public async Task Every_migration_in_the_code_has_been_applied()
    {
        await using var context = NewContext();

        var inCode = context.Database.GetMigrations();
        var inDatabase = await context.Database.GetAppliedMigrationsAsync();

        Assert.Equal(inCode, inDatabase);
    }

    [Fact]
    public async Task Model_has_no_changes_that_are_missing_a_migration()
    {
        // Gagal kalau seseorang mengubah entity/configuration tapi lupa
        // menjalankan `dotnet ef migrations add`.
        await using var context = NewContext();

        Assert.False(context.Database.HasPendingModelChanges(),
            "Model berbeda dari migration terakhir. Jalankan `dotnet ef migrations add`.");
    }


    [Theory]
    [InlineData("courses", "Price", "numeric(12,2)")]
    [InlineData("course_sessions", "StartTime", "timestamp with time zone")]
    [InlineData("enrollments", "Status", "character varying(20)")]
    public async Task Column_has_the_expected_postgres_type(string table, string column, string expectedType)
    {
        await using var context = NewContext();

        // Tanya PostgreSQL langsung: tipe apa yang benar-benar dipakai kolom ini?
        var actualType = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT format_type(a.atttypid, a.atttypmod) AS "Value"
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                WHERE c.relname = {0}
                  AND a.attname = {1}
                  AND c.relnamespace = 'public'::regnamespace
                """,
                table, column)
            .SingleAsync();

        Assert.Equal(expectedType, actualType);
    }

    [Fact]
    public async Task Price_is_rounded_to_two_decimals_by_numeric_12_2()
    {
        int id;
        await using (var write = NewContext())
        {
            var course = TestData.NewCourse(TestData.NewMentor(), price: 150_000.555m);
            write.Courses.Add(course);
            await write.SaveChangesAsync();
            id = course.Id;
        }

        await using var read = NewContext();
        // Kolom numeric(12,2) membulatkan secara diam-diam: kita perlu tahu itu.
        Assert.Equal(150_000.56m, (await read.Courses.FindAsync(id))!.Price);
    }

    [Fact]
    public async Task Enrollment_status_is_stored_as_text_not_a_number()
    {
        await using (var write = NewContext())
        {
            write.Enrollments.Add(new Enrollment
            {
                Student = TestData.NewStudent(),
                Course = TestData.NewCourse(TestData.NewMentor()),
                Status = EnrollmentStatus.Completed
            });
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();

        // Lihat isi kolom aslinya, bukan hasil konversi EF.
        var raw = await read.Database
            .SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM enrollments")
            .SingleAsync();
        Assert.Equal("Completed", raw);

        // Dan EF mengubahnya kembali menjadi enum.
        Assert.Equal(EnrollmentStatus.Completed, (await read.Enrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Utc_datetime_round_trips_unchanged_through_timestamptz()
    {
        var start = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);
        var session = TestData.NewSession();
        session.StartTime = start;

        await using (var write = NewContext())
        {
            var course = TestData.NewCourse(TestData.NewMentor());
            course.Sessions.Add(session);
            write.Courses.Add(course);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var saved = await read.CourseSessions.SingleAsync();

        Assert.Equal(start, saved.StartTime);
        Assert.Equal(DateTimeKind.Utc, saved.StartTime.Kind);
    }
}