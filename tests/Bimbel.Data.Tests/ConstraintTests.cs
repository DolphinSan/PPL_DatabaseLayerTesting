using Bimbel.Data.Entities;
using Bimbel.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Bimbel.Data.Tests;

/// <summary>
/// Pengujian Relationships dan Constraints pada Database Bimbel.
/// Menggunakan shared container via <see cref="DatabaseTest"/> dan Testcontainers PostgreSQL.
///
/// Disusun oleh: DANEN
/// Cakupan:
/// 1. Foreign Key Constraints (Course -> Mentor, Enrollment -> Student/Course, CourseSession -> Course)
/// 2. Unique Constraints (Mentor Email, Student Email, Enrollment [StudentId, CourseId])
/// 3. Cascade Delete (Course -> Sessions & Enrollments, Student -> Enrollments)
/// 4. Restrict Delete (Mentor -> Courses)
/// 5. Check Constraints (CourseSession DurationMinutes > 0)
/// 6. Skenario Test Gagal untuk Keperluan Demo
/// </summary>
public class ConstraintTests(PostgresFixture fixture) : DatabaseTest(fixture)
{

    [Fact]
    public async Task Course_dengan_mentor_yang_tidak_ada_ditolak_oleh_foreign_key()
    {
        await using var write = NewContext();

        var course = new Course
        {
            Name = "Golang Backend",
            Description = "Belajar Golang",
            Price = 450_000m,
            MentorId = 99999 // MentorId fiktif tidak ada di tabel mentors
        };
        write.Courses.Add(course);

        var error = await AssertViolationAsync(() => write.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task Enrollment_dengan_student_atau_course_tidak_valid_ditolak_oleh_foreign_key()
    {
        await using var write = NewContext();

        var enrollment = new Enrollment
        {
            StudentId = 88888, // Student tidak ada
            CourseId = 77777,  // Course tidak ada
            Status = EnrollmentStatus.Active,
            EnrolledAt = DateTime.UtcNow
        };
        write.Enrollments.Add(enrollment);

        var error = await AssertViolationAsync(() => write.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task Session_dengan_course_tidak_valid_ditolak_oleh_foreign_key()
    {
        await using var write = NewContext();

        var session = new CourseSession
        {
            Topic = "Arsitektur Microservices",
            DurationMinutes = 90,
            StartTime = DateTime.UtcNow.AddDays(1),
            CourseId = 55555 // Course tidak ada
        };
        write.CourseSessions.Add(session);

        var error = await AssertViolationAsync(() => write.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }





    [Fact]
    public async Task Dua_mentor_dengan_email_sama_ditolak_oleh_unique_index()
    {
        const string duplicateEmail = "mentor_sama@bimbel.test";

        await using (var write1 = NewContext())
        {
            write1.Mentors.Add(TestData.NewMentor(duplicateEmail));
            await write1.SaveChangesAsync();
        }

        await using var write2 = NewContext();
        write2.Mentors.Add(TestData.NewMentor(duplicateEmail));

        var error = await AssertViolationAsync(() => write2.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [Fact]
    public async Task Dua_student_dengan_email_sama_ditolak_oleh_unique_index()
    {
        const string duplicateEmail = "student_sama@bimbel.test";

        await using (var write1 = NewContext())
        {
            write1.Students.Add(TestData.NewStudent(duplicateEmail));
            await write1.SaveChangesAsync();
        }

        await using var write2 = NewContext();
        write2.Students.Add(TestData.NewStudent(duplicateEmail));

        var error = await AssertViolationAsync(() => write2.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [Fact]
    public async Task Student_mendaftar_ke_course_yang_sama_dua_kali_ditolak_oleh_unique_index()
    {
        int studentId;
        int courseId;

        await using (var setup = NewContext())
        {
            var mentor = TestData.NewMentor();
            var student = TestData.NewStudent();
            var course = TestData.NewCourse(mentor);

            setup.Students.Add(student);
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();

            studentId = student.Id;
            courseId = course.Id;

            // Pendaftaran pertama (berhasil)
            setup.Enrollments.Add(new Enrollment
            {
                StudentId = studentId,
                CourseId = courseId,
                Status = EnrollmentStatus.Active
            });
            await setup.SaveChangesAsync();
        }

        // Pendaftaran kedua ke kursus yang sama persis (harus ditolak)
        await using var duplicateWrite = NewContext();
        duplicateWrite.Enrollments.Add(new Enrollment
        {
            StudentId = studentId,
            CourseId = courseId,
            Status = EnrollmentStatus.Active
        });

        var error = await AssertViolationAsync(() => duplicateWrite.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }





    [Fact]
    public async Task Menghapus_course_menghapus_semua_sessions_dan_enrollments_terkait()
    {
        int courseId;
        int mentorId;
        int studentId;

        await using (var setup = NewContext())
        {
            var mentor = TestData.NewMentor();
            var student = TestData.NewStudent();
            var course = TestData.NewCourse(mentor);
            course.Sessions.Add(TestData.NewSession("Pertemuan 1"));
            course.Sessions.Add(TestData.NewSession("Pertemuan 2"));

            setup.Students.Add(student);
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();

            setup.Enrollments.Add(new Enrollment
            {
                StudentId = student.Id,
                CourseId = course.Id,
                Status = EnrollmentStatus.Active
            });
            await setup.SaveChangesAsync();

            courseId = course.Id;
            mentorId = mentor.Id;
            studentId = student.Id;
        }

        // Hapus Course
        await using (var delete = NewContext())
        {
            var courseToDelete = await delete.Courses.SingleAsync(c => c.Id == courseId);
            delete.Courses.Remove(courseToDelete);
            await delete.SaveChangesAsync();
        }

        // Verifikasi hasil cascade di PostgreSQL
        await using var verify = NewContext();
        Assert.Null(await verify.Courses.FindAsync(courseId));
        Assert.Equal(0, await verify.CourseSessions.CountAsync(s => s.CourseId == courseId));
        Assert.Equal(0, await verify.Enrollments.CountAsync(e => e.CourseId == courseId));

        // Mentor dan Student tidak boleh ikut terhapus
        Assert.NotNull(await verify.Mentors.FindAsync(mentorId));
        Assert.NotNull(await verify.Students.FindAsync(studentId));
    }

    [Fact]
    public async Task Menghapus_student_menghapus_semua_enrollment_siswa_tersebut()
    {
        int studentId;
        int courseId;

        await using (var setup = NewContext())
        {
            var mentor = TestData.NewMentor();
            var student = TestData.NewStudent();
            var course = TestData.NewCourse(mentor);

            setup.Students.Add(student);
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();

            setup.Enrollments.Add(new Enrollment
            {
                StudentId = student.Id,
                CourseId = course.Id,
                Status = EnrollmentStatus.Active
            });
            await setup.SaveChangesAsync();

            studentId = student.Id;
            courseId = course.Id;
        }

        // Hapus Student
        await using (var delete = NewContext())
        {
            var studentToDelete = await delete.Students.SingleAsync(s => s.Id == studentId);
            delete.Students.Remove(studentToDelete);
            await delete.SaveChangesAsync();
        }

        // Verifikasi pendaftaran terhapus tapi course tetap ada
        await using var verify = NewContext();
        Assert.Null(await verify.Students.FindAsync(studentId));
        Assert.Equal(0, await verify.Enrollments.CountAsync(e => e.StudentId == studentId));
        Assert.NotNull(await verify.Courses.FindAsync(courseId));
    }





    [Fact]
    public async Task Menghapus_mentor_yang_masih_memiliki_course_ditolak_oleh_restrict()
    {
        int mentorId;

        await using (var setup = NewContext())
        {
            var mentor = TestData.NewMentor();
            var course = TestData.NewCourse(mentor);
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();
            mentorId = mentor.Id;
        }

        // Coba hapus mentor yang masih terhubung ke course
        await using var delete = NewContext();
        var mentorToDelete = await delete.Mentors.SingleAsync(m => m.Id == mentorId);
        delete.Mentors.Remove(mentorToDelete);

        var error = await AssertViolationAsync(() => delete.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);

        // Verifikasi mentor tetap ada di database
        await using var verify = NewContext();
        Assert.NotNull(await verify.Mentors.FindAsync(mentorId));
    }

    [Fact]
    public async Task Menghapus_mentor_yang_tidak_memiliki_course_diizinkan()
    {
        int mentorId;

        await using (var setup = NewContext())
        {
            var mentor = TestData.NewMentor();
            setup.Mentors.Add(mentor);
            await setup.SaveChangesAsync();
            mentorId = mentor.Id;
        }

        await using (var delete = NewContext())
        {
            var mentorToDelete = await delete.Mentors.SingleAsync(m => m.Id == mentorId);
            delete.Mentors.Remove(mentorToDelete);
            await delete.SaveChangesAsync();
        }

        await using var verify = NewContext();
        Assert.Null(await verify.Mentors.FindAsync(mentorId));
    }





    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public async Task Session_dengan_durasi_nol_atau_negatif_ditolak_oleh_check_constraint(int invalidDuration)
    {
        int courseId;

        await using (var setup = NewContext())
        {
            var course = TestData.NewCourse(TestData.NewMentor());
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();
            courseId = course.Id;
        }

        await using var write = NewContext();
        var session = TestData.NewSession(minutes: invalidDuration);
        session.CourseId = courseId;
        write.CourseSessions.Add(session);

        var error = await AssertViolationAsync(() => write.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task Session_dengan_durasi_positif_berhasil_disimpan()
    {
        int sessionId;

        await using (var write = NewContext())
        {
            var course = TestData.NewCourse(TestData.NewMentor());
            var session = TestData.NewSession("Pemrograman C#", minutes: 120);
            course.Sessions.Add(session);

            write.Courses.Add(course);
            await write.SaveChangesAsync();
            sessionId = session.Id;
        }

        await using var read = NewContext();
        var savedSession = await read.CourseSessions.FindAsync(sessionId);
        Assert.NotNull(savedSession);
        Assert.Equal(120, savedSession.DurationMinutes);
    }





    [Fact]
    public async Task Demo_SkenarioGagal_MemasukkanDurasiSesiTidakValid_TanpaMenanganiConstraint()
    {
        await using var write = NewContext();
        var course = TestData.NewCourse(TestData.NewMentor());
        write.Courses.Add(course);
        await write.SaveChangesAsync();

        // Data tidak valid: Durasi negatif (-45 menit)
        var invalidSession = TestData.NewSession("Demo Sesi Gagal", minutes: -45);
        invalidSession.CourseId = course.Id;

        write.CourseSessions.Add(invalidSession);

        // Langsung panggil SaveChangesAsync() tanpa menangani exception:
        // Test AKAN GAGAL karena melempar DbUpdateException akibat CHECK CONSTRAINT PostgreSQL!
        await write.SaveChangesAsync();

        Assert.True(invalidSession.Id > 0, "Baris ini tidak akan pernah tercapai saat demo.");
    }
}
