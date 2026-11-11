using Bimbel.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bimbel.Data.Configurations;

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("enrollments");
        builder.HasKey(e => e.Id);

        // Enum disimpan sebagai teks agar mudah dibaca di database.
        builder.Property(e => e.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        // Satu siswa tidak boleh mendaftar ke kursus yang sama dua kali.
        builder.HasIndex(e => new { e.StudentId, e.CourseId }).IsUnique();
    }
}
