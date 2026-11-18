using Bimbel.Data;
using Bimbel.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Bimbel.Data.Tests;

public class ConstraintTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("bimbel_constraint_test_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        // 1. Jalankan container PostgreSQL terisolasi
        await _dbContainer.StartAsync();

        // 2. Terapkan EF Core migrations untuk membuat skema tabel & constraint di PostgreSQL asli
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        // Bersihkan dan matikan container setelah seluruh pengujian selesai
        await _dbContainer.DisposeAsync();
    }

    private BimbelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BimbelDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        return new BimbelDbContext(options);
    }


    [Fact]
    public async Task ForeignKey_MenambahkanCourseDenganMentorTidakValid_HarusGagal()
    {
        await using var context = CreateDbContext();

        var invalidCourse = new Course
        {
            Name = "Golang Backend",
            Description = "Belajar Golang",
            Price = 450_000m,
            MentorId = 99999 // MentorId ini tidak ada di tabel mentors
        };

        context.Courses.Add(invalidCourse);

        // Harus melempar DbUpdateException karena foreign key violation (SQLSTATE 23503)
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        Assert.NotNull(postgresException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
    }

    [Fact]
    public async Task ForeignKey_MenambahkanEnrollmentDenganStudentDanCourseTidakValid_HarusGagal()
    {
        await using var context = CreateDbContext();

        var invalidEnrollment = new Enrollment
        {
            StudentId = 88888, // Student tidak ada
            CourseId = 77777,  // Course tidak ada
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTime.UtcNow
        };

        context.Enrollments.Add(invalidEnrollment);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        Assert.NotNull(postgresException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
    }

    [Fact]
    public async Task ForeignKey_MenambahkanCourseSessionDenganCourseTidakValid_HarusGagal()
    {
        await using var context = CreateDbContext();

        var invalidSession = new CourseSession
        {
            Topic = "Arsitektur Microservices",
            DurationMinutes = 90,
            StartTime = DateTime.UtcNow.AddDays(1),
            CourseId = 55555 // Course tidak ada
        };

        context.CourseSessions.Add(invalidSession);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        Assert.NotNull(postgresException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
    }


    [Fact]
    public async Task Unique_MenambahkanDuaMentorDenganEmailSama_HarusGagal()
    {
        await using var context = CreateDbContext();
        var uniqueEmail = $"mentor_{Guid.NewGuid():N}@bimbel.com";

        var mentor1 = new Mentor { Name = "Mentor Pertama", Email = uniqueEmail };
        context.Mentors.Add(mentor1);
        await context.SaveChangesAsync();

        var mentor2 = new Mentor { Name = "Mentor Kedua", Email = uniqueEmail };
        context.Mentors.Add(mentor2);

        // Harus melempar DbUpdateException karena duplicate key pada index IX_mentors_Email (SQLSTATE 23505)
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        Assert.NotNull(postgresException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
    }

    [Fact]
    public async Task Unique_MenambahkanDuaStudentDenganEmailSama_HarusGagal()
    {
        await using var context = CreateDbContext();
        var uniqueEmail = $"student_{Guid.NewGuid():N}@bimbel.com";

        var student1 = new Student { FullName = "Siswa Satu", Email = uniqueEmail, RegisteredAt = DateTime.UtcNow };
        context.Students.Add(student1);
        await context.SaveChangesAsync();

        var student2 = new Student { FullName = "Siswa Dua", Email = uniqueEmail, RegisteredAt = DateTime.UtcNow };
        context.Students.Add(student2);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        Assert.NotNull(postgresException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
    }

    [Fact]
    public async Task Unique_MendaftarkanSiswaKeKursusYangSamaDuaKali_HarusGagal()
    {
        await using var context = CreateDbContext();

        // 1. Buat mentor, course, dan student
        var mentor = new Mentor { Name = "Mentor Java", Email = $"mentor_java_{Guid.NewGuid():N}@bimbel.com" };
        var student = new Student { FullName = "Siswa Java", Email = $"siswa_java_{Guid.NewGuid():N}@bimbel.com" };
        context.Mentors.Add(mentor);
        context.Students.Add(student);
        await context.SaveChangesAsync();

        var course = new Course { Name = "Java Spring Boot", Price = 500_000m, MentorId = mentor.Id };
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        // 2. Pendaftaran pertama (berhasil)
        var enrollment1 = new Enrollment
        {
            StudentId = student.Id,
            CourseId = course.Id,
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTime.UtcNow
        };
        context.Enrollments.Add(enrollment1);
        await context.SaveChangesAsync();

        // 3. Pendaftaran kedua dengan StudentId dan CourseId yang sama persis (harus gagal)
        var enrollment2 = new Enrollment
        {
            StudentId = student.Id,
            CourseId = course.Id,
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTime.UtcNow
        };
        context.Enrollments.Add(enrollment2);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        Assert.NotNull(postgresException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
    }


    [Fact]
    public async Task CascadeDelete_MenghapusCourse_HarusMenghapusSemuaSessionsDanEnrollmentsTerkait()
    {
        await using var context = CreateDbContext();

        // Setup data
        var mentor = new Mentor { Name = "Mentor Python", Email = $"python_{Guid.NewGuid():N}@bimbel.com" };
        var student = new Student { FullName = "Student Python", Email = $"std_py_{Guid.NewGuid():N}@bimbel.com" };
        context.Mentors.Add(mentor);
        context.Students.Add(student);
        await context.SaveChangesAsync();

        var course = new Course { Name = "Python Data Science", Price = 350_000m, MentorId = mentor.Id };
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        var session1 = new CourseSession { Topic = "Numpy & Pandas", DurationMinutes = 90, StartTime = DateTime.UtcNow, CourseId = course.Id };
        var session2 = new CourseSession { Topic = "Matplotlib", DurationMinutes = 90, StartTime = DateTime.UtcNow.AddDays(1), CourseId = course.Id };
        var enrollment = new Enrollment { StudentId = student.Id, CourseId = course.Id, Status = EnrollmentStatus.Active };
        context.CourseSessions.AddRange(session1, session2);
        context.Enrollments.Add(enrollment);
        await context.SaveChangesAsync();

        // Verifikasi awal data tersimpan
        Assert.Equal(2, await context.CourseSessions.CountAsync(s => s.CourseId == course.Id));
        Assert.Equal(1, await context.Enrollments.CountAsync(e => e.CourseId == course.Id));

        // Hapus Course
        await using var deleteContext = CreateDbContext();
        var courseToDelete = await deleteContext.Courses.FirstAsync(c => c.Id == course.Id);
        deleteContext.Courses.Remove(courseToDelete);
        await deleteContext.SaveChangesAsync();

        // Verifikasi di DbContext baru bahwa Course, Sessions, dan Enrollments terhapus (Cascade)
        await using var verifyContext = CreateDbContext();
        var courseExists = await verifyContext.Courses.AnyAsync(c => c.Id == course.Id);
        var sessionsCount = await verifyContext.CourseSessions.CountAsync(s => s.CourseId == course.Id);
        var enrollmentsCount = await verifyContext.Enrollments.CountAsync(e => e.CourseId == course.Id);

        Assert.False(courseExists, "Course seharusnya sudah terhapus.");
        Assert.Equal(0, sessionsCount);
        Assert.Equal(0, enrollmentsCount);

        // Pastikan Mentor dan Student TIDAK ikut terhapus
        Assert.True(await verifyContext.Mentors.AnyAsync(m => m.Id == mentor.Id));
        Assert.True(await verifyContext.Students.AnyAsync(s => s.Id == student.Id));
    }

    [Fact]
    public async Task CascadeDelete_MenghapusStudent_HarusMenghapusPendaftaranSiswaTerkait()
    {
        await using var context = CreateDbContext();

        var mentor = new Mentor { Name = "Mentor Web", Email = $"web_{Guid.NewGuid():N}@bimbel.com" };
        var student = new Student { FullName = "Siswa Web", Email = $"siswa_web_{Guid.NewGuid():N}@bimbel.com" };
        context.Mentors.Add(mentor);
        context.Students.Add(student);
        await context.SaveChangesAsync();

        var course = new Course { Name = "Fullstack React & .NET", Price = 600_000m, MentorId = mentor.Id };
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        var enrollment = new Enrollment { StudentId = student.Id, CourseId = course.Id, Status = EnrollmentStatus.Active };
        context.Enrollments.Add(enrollment);
        await context.SaveChangesAsync();

        // Hapus Student
        await using var deleteContext = CreateDbContext();
        var studentToDelete = await deleteContext.Students.FirstAsync(s => s.Id == student.Id);
        deleteContext.Students.Remove(studentToDelete);
        await deleteContext.SaveChangesAsync();

        // Verifikasi pendaftaran ikut terhapus secara cascade, tapi course tetap aman
        await using var verifyContext = CreateDbContext();
        Assert.False(await verifyContext.Students.AnyAsync(s => s.Id == student.Id));
        Assert.Equal(0, await verifyContext.Enrollments.CountAsync(e => e.StudentId == student.Id));
        Assert.True(await verifyContext.Courses.AnyAsync(c => c.Id == course.Id));
    }


    [Fact]
    public async Task RestrictDelete_MenghapusMentorYangMasihMemilikiCourse_HarusGagal()
    {
        await using var context = CreateDbContext();

        // 1. Buat mentor dan berikan 1 kursus
        var mentor = new Mentor { Name = "Mentor Senior", Email = $"senior_{Guid.NewGuid():N}@bimbel.com" };
        context.Mentors.Add(mentor);
        await context.SaveChangesAsync();

        var course = new Course { Name = "Kriptografi & Security", Price = 750_000m, MentorId = mentor.Id };
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        // 2. Coba hapus Mentor yang masih memiliki Course
        await using var deleteContext = CreateDbContext();
        var mentorToDelete = await deleteContext.Mentors.FirstAsync(m => m.Id == mentor.Id);
        deleteContext.Mentors.Remove(mentorToDelete);

        // Karena DeleteBehavior.Restrict, penghapusan ditolak oleh PostgreSQL (FK violation 23503)
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => deleteContext.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        Assert.NotNull(postgresException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);

        // 3. Verifikasi data mentor masih tetap utuh di database
        await using var verifyContext = CreateDbContext();
        var mentorStillExists = await verifyContext.Mentors.AnyAsync(m => m.Id == mentor.Id);
        Assert.True(mentorStillExists, "Data mentor tidak boleh terhapus karena dilindungi oleh Restrict constraint.");
    }

    [Fact]
    public async Task RestrictDelete_MenghapusMentorYangTidakMemilikiCourse_HarusBerhasil()
    {
        await using var context = CreateDbContext();

        var mentor = new Mentor { Name = "Mentor Baru", Email = $"baru_{Guid.NewGuid():N}@bimbel.com" };
        context.Mentors.Add(mentor);
        await context.SaveChangesAsync();

        // Hapus mentor yang tidak punya relasi course sama sekali
        context.Mentors.Remove(mentor);
        await context.SaveChangesAsync();

        var exists = await context.Mentors.AnyAsync(m => m.Id == mentor.Id);
        Assert.False(exists, "Mentor tanpa relasi kursus harus berhasil dihapus.");
    }


    [Fact]
    public async Task CheckConstraint_MenambahkanSesiDenganDurasiNolAtauNegatif_HarusGagal()
    {
        await using var context = CreateDbContext();

        var mentor = new Mentor { Name = "Mentor DevOps", Email = $"devops_{Guid.NewGuid():N}@bimbel.com" };
        context.Mentors.Add(mentor);
        await context.SaveChangesAsync();

        var course = new Course { Name = "Docker & Kubernetes", Price = 400_000m, MentorId = mentor.Id };
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        // Kasus A: DurationMinutes = 0
        var invalidSessionZero = new CourseSession
        {
            Topic = "Intro Container",
            DurationMinutes = 0, // Melanggar ck_course_sessions_duration_positive ("DurationMinutes" > 0)
            StartTime = DateTime.UtcNow,
            CourseId = course.Id
        };
        context.CourseSessions.Add(invalidSessionZero);

        var exZero = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var pgExZero = exZero.InnerException as PostgresException;
        Assert.NotNull(pgExZero);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pgExZero.SqlState);

        // Detach entri yang gagal agar context bersih kembali
        context.Entry(invalidSessionZero).State = EntityState.Detached;

        // Kasus B: DurationMinutes = -30
        var invalidSessionNegative = new CourseSession
        {
            Topic = "Kubernetes Pods",
            DurationMinutes = -30, // Negatif, melanggar check constraint
            StartTime = DateTime.UtcNow,
            CourseId = course.Id
        };
        context.CourseSessions.Add(invalidSessionNegative);

        var exNegative = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var pgExNegative = exNegative.InnerException as PostgresException;
        Assert.NotNull(pgExNegative);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pgExNegative.SqlState);
    }

    [Fact]
    public async Task CheckConstraint_MenambahkanSesiDenganDurasiPositif_HarusBerhasil()
    {
        await using var context = CreateDbContext();

        var mentor = new Mentor { Name = "Mentor AI", Email = $"ai_{Guid.NewGuid():N}@bimbel.com" };
        context.Mentors.Add(mentor);
        await context.SaveChangesAsync();

        var course = new Course { Name = "Machine Learning Dasar", Price = 550_000m, MentorId = mentor.Id };
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        var validSession = new CourseSession
        {
            Topic = "Linear Regression",
            DurationMinutes = 120, // Valid (> 0)
            StartTime = DateTime.UtcNow,
            CourseId = course.Id
        };

        context.CourseSessions.Add(validSession);
        await context.SaveChangesAsync();

        Assert.True(validSession.Id > 0);
        var sessionFromDb = await context.CourseSessions.FindAsync(validSession.Id);
        Assert.NotNull(sessionFromDb);
        Assert.Equal(120, sessionFromDb.DurationMinutes);
    }


    /// <summary>
    /// SKENARIO DEMO GAGAL:
    /// Test ini sengaja dirancang untuk mendemonstrasikan kegagalan pengujian saat presentasi/demo.
    ///
    /// Penjelasan Demo:
    /// Pengembang berasumsi bahwa sesi bimbel dengan durasi 0 menit atau durasi tidak wajar
    /// bisa disimpan begitu saja tanpa melempar error.
    /// Namun karena PostgreSQL memiliki CHECK CONSTRAINT ("DurationMinutes" > 0),
    /// operasi SaveChangesAsync() akan melempar DbUpdateException yang tidak ditangani oleh pemanggil,
    /// sehingga test runner menampilkan status FAILED (MERAH).
    /// </summary>
    [Fact]
    public async Task Demo_SkenarioGagal_MemasukkanDurasiSesiTidakValid_TanpaMenanganiConstraint()
    {
        await using var context = CreateDbContext();

        var mentor = new Mentor { Name = "Mentor Demo", Email = $"demo_{Guid.NewGuid():N}@bimbel.com" };
        context.Mentors.Add(mentor);
        await context.SaveChangesAsync();

        var course = new Course { Name = "Demo Course", Price = 100_000m, MentorId = mentor.Id };
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        // Data tidak valid: Durasi minus (-45 menit)
        var invalidSession = new CourseSession
        {
            Topic = "Topik Tidak Valid untuk Demo",
            DurationMinutes = -45,
            StartTime = DateTime.UtcNow,
            CourseId = course.Id
        };

        context.CourseSessions.Add(invalidSession);

        // PERHATIKAN: Pada skenario normal pengetesan yang lulus, kita memakai Assert.ThrowsAsync<DbUpdateException>.
        // Tetapi pada test demo gagal ini, kita langsung memanggil SaveChangesAsync() dan mengecek apakah ID > 0.
        // Hasilnya: Test AKAN GAGAL karena PostgreSQL Check Constraint melempar DbUpdateException!
        await context.SaveChangesAsync();

        // Baris ini tidak akan pernah tercapai karena exception terlempar di atas
        Assert.True(invalidSession.Id > 0, "Seharusnya tidak tercapai karena operasi di atas gagal di tingkat database.");
    }
}
