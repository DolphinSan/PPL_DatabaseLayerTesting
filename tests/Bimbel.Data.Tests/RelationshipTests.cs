using Bimbel.Data.Entities;
using Bimbel.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bimbel.Data.Tests;

// TOPIK 4: RELATIONSHIPS
// Navigation property, Include, dan perilaku delete (cascade vs restrict).
public class RelationshipTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Navigation_is_empty_without_Include_and_filled_with_it()
    {
        await using (var write = NewContext())
        {
            var course = TestData.NewCourse(TestData.NewMentor());
            course.Sessions.Add(TestData.NewSession("Limit"));
            course.Sessions.Add(TestData.NewSession("Integral"));
            write.Courses.Add(course);
            await write.SaveChangesAsync();
        }

        await using var withoutInclude = NewContext();
        Assert.Empty((await withoutInclude.Courses.SingleAsync()).Sessions); // tidak ada lazy loading

        await using var withInclude = NewContext();
        var loaded = await withInclude.Courses.Include(c => c.Sessions).SingleAsync();
        Assert.Equal(2, loaded.Sessions.Count);
    }

    [Fact]
    public async Task Students_of_a_course_are_reached_through_enrollments()
    {
        var course = TestData.NewCourse(TestData.NewMentor());
        await using (var write = NewContext())
        {
            write.Enrollments.Add(new Enrollment { Course = course, Student = TestData.NewStudent() });
            write.Enrollments.Add(new Enrollment { Course = course, Student = TestData.NewStudent() });
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var loaded = await read.Courses
            .Include(c => c.Enrollments).ThenInclude(e => e.Student)
            .SingleAsync();

        Assert.Equal(2, loaded.Enrollments.Count);
        Assert.All(loaded.Enrollments, e => Assert.NotNull(e.Student));
    }

    [Fact]
    public async Task Deleting_a_course_cascades_to_sessions_and_enrollments_in_the_database()
    {
        int courseId;
        await using (var write = NewContext())
        {
            var course = TestData.NewCourse(TestData.NewMentor());
            course.Sessions.Add(TestData.NewSession());
            course.Enrollments.Add(new Enrollment { Student = TestData.NewStudent() });
            write.Courses.Add(course);
            await write.SaveChangesAsync();
            courseId = course.Id;
        }

        await using (var delete = NewContext())
        {
            // ExecuteDelete langsung ke SQL, EF tidak ikut campur,
            // jadi yang bekerja di sini adalah ON DELETE CASCADE milik PostgreSQL.
            await delete.Courses.Where(c => c.Id == courseId).ExecuteDeleteAsync();
        }

        await using var read = NewContext();
        Assert.Equal(0, await read.CourseSessions.CountAsync());
        Assert.Equal(0, await read.Enrollments.CountAsync());
        Assert.Equal(1, await read.Students.CountAsync()); // siswa tidak ikut terhapus
    }

    [Fact]
    public async Task Deleting_a_student_cascades_to_their_enrollments()
    {
        await using (var write = NewContext())
        {
            write.Enrollments.Add(new Enrollment
            {
                Student = TestData.NewStudent(),
                Course = TestData.NewCourse(TestData.NewMentor())
            });
            await write.SaveChangesAsync();
        }

        await using (var delete = NewContext())
        {
            delete.Students.Remove(await delete.Students.SingleAsync());
            await delete.SaveChangesAsync();
        }

        await using var read = NewContext();
        Assert.Equal(0, await read.Enrollments.CountAsync());
        Assert.Equal(1, await read.Courses.CountAsync()); // kursus tetap ada
    }

    [Fact]
    public async Task Deleting_a_mentor_who_still_has_courses_is_blocked()
    {
        await using (var write = NewContext())
        {
            write.Courses.Add(TestData.NewCourse(TestData.NewMentor()));
            await write.SaveChangesAsync();
        }

        await using var delete = NewContext();
        delete.Mentors.Remove(await delete.Mentors.SingleAsync());

        var error = await AssertViolationAsync(() => delete.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }
}
