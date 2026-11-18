using Bimbel.Data.Entities;
using Bimbel.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bimbel.Data.Tests;

// TOPIK 5: CONSTRAINTS
// Database menolak data yang salah. Inilah yang TIDAK dilakukan EF InMemory
// dan sering berbeda di H2/SQLite.
public class ConstraintTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Duplicate_mentor_email_is_rejected()
    {
        await using (var seed = NewContext())
        {
            seed.Mentors.Add(TestData.NewMentor("budi@bimbel.test"));
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        context.Mentors.Add(TestData.NewMentor("budi@bimbel.test"));

        var error = await AssertViolationAsync(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("IX_mentors_Email", error.ConstraintName);
    }

    [Fact]
    public async Task Duplicate_student_email_is_rejected()
    {
        await using (var seed = NewContext())
        {
            seed.Students.Add(TestData.NewStudent("siti@bimbel.test"));
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        context.Students.Add(TestData.NewStudent("siti@bimbel.test"));

        var error = await AssertViolationAsync(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [Fact]
    public async Task Student_cannot_enroll_in_the_same_course_twice()
    {
        int studentId, courseId;
        await using (var seed = NewContext())
        {
            var enrollment = new Enrollment
            {
                Student = TestData.NewStudent(),
                Course = TestData.NewCourse(TestData.NewMentor())
            };
            seed.Enrollments.Add(enrollment);
            await seed.SaveChangesAsync();
            studentId = enrollment.StudentId;
            courseId = enrollment.CourseId;
        }

        await using var context = NewContext();
        context.Enrollments.Add(new Enrollment { StudentId = studentId, CourseId = courseId });

        var error = await AssertViolationAsync(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("IX_enrollments_StudentId_CourseId", error.ConstraintName);
    }

    [Fact]
    public async Task Required_column_cannot_be_null()
    {
        await using var context = NewContext();
        context.Mentors.Add(new Mentor { Name = null!, Email = "x@bimbel.test" });

        var error = await AssertViolationAsync(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.NotNullViolation, error.SqlState);
    }

    [Fact]
    public async Task Text_longer_than_max_length_is_rejected()
    {
        await using var context = NewContext();
        var course = TestData.NewCourse(TestData.NewMentor());
        course.Name = new string('x', 151); // batas: 150
        context.Courses.Add(course);

        var error = await AssertViolationAsync(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, error.SqlState);
    }

    [Fact]
    public async Task Foreign_key_must_point_to_an_existing_row()
    {
        await using var context = NewContext();
        context.Courses.Add(new Course { Name = "Fisika", Price = 100_000m, MentorId = 9999 });

        var error = await AssertViolationAsync(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public async Task Session_duration_must_be_positive(int minutes)
    {
        await using var context = NewContext();
        var course = TestData.NewCourse(TestData.NewMentor());
        course.Sessions.Add(TestData.NewSession(minutes: minutes));
        context.Courses.Add(course);

        var error = await AssertViolationAsync(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_course_sessions_duration_positive", error.ConstraintName);
    }
}
