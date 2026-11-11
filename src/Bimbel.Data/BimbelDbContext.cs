using Bimbel.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bimbel.Data;

public class BimbelDbContext : DbContext
{
    public BimbelDbContext(DbContextOptions<BimbelDbContext> options) : base(options)
    {
    }

    public DbSet<Mentor> Mentors => Set<Mentor>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<CourseSession> CourseSessions => Set<CourseSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BimbelDbContext).Assembly);
    }
}
