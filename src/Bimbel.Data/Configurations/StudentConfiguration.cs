using Bimbel.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bimbel.Data.Configurations;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.FullName).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Email).IsRequired().HasMaxLength(150);

        builder.HasIndex(s => s.Email).IsUnique();

        // Hapus siswa => pendaftarannya ikut terhapus.
        builder.HasMany(s => s.Enrollments)
               .WithOne(e => e.Student)
               .HasForeignKey(e => e.StudentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
