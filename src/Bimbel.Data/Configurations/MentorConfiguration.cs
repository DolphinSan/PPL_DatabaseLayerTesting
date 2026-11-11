using Bimbel.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bimbel.Data.Configurations;

public class MentorConfiguration : IEntityTypeConfiguration<Mentor>
{
    public void Configure(EntityTypeBuilder<Mentor> builder)
    {
        builder.ToTable("mentors");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Email).IsRequired().HasMaxLength(150);

        builder.HasIndex(m => m.Email).IsUnique();

        // Mentor yang masih punya kursus tidak boleh dihapus.
        builder.HasMany(m => m.Courses)
               .WithOne(c => c.Mentor)
               .HasForeignKey(c => c.MentorId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
