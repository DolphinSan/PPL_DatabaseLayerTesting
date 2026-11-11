using Bimbel.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bimbel.Data.Configurations;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("courses");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.Price).HasColumnType("numeric(12,2)");

        // Hapus kursus => semua pertemuan ikut terhapus.
        builder.HasMany(c => c.Sessions)
               .WithOne(s => s.Course)
               .HasForeignKey(s => s.CourseId)
               .OnDelete(DeleteBehavior.Cascade);

        // Hapus kursus => semua pendaftaran ikut terhapus.
        builder.HasMany(c => c.Enrollments)
               .WithOne(e => e.Course)
               .HasForeignKey(e => e.CourseId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
