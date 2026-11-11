using Bimbel.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bimbel.Data.Configurations;

public class CourseSessionConfiguration : IEntityTypeConfiguration<CourseSession>
{
    public void Configure(EntityTypeBuilder<CourseSession> builder)
    {
        builder.ToTable("course_sessions", t => t.HasCheckConstraint(
            "ck_course_sessions_duration_positive", "\"DurationMinutes\" > 0"));
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Topic).IsRequired().HasMaxLength(200);
        builder.Property(s => s.DurationMinutes).IsRequired();
    }
}
