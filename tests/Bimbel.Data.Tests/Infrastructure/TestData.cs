using Bimbel.Data.Entities;

namespace Bimbel.Data.Tests.Infrastructure;

internal static class TestData
{
    // Email unik per pemanggilan supaya tidak bentrok dengan unique index.
    private static string UniqueEmail() => $"{Guid.NewGuid():N}@bimbel.test";

    public static Mentor NewMentor(string? email = null) =>
        new() { Name = "Budi Santoso", Email = email ?? UniqueEmail() };

    public static Student NewStudent(string? email = null) =>
        new() { FullName = "Siti Aminah", Email = email ?? UniqueEmail() };

    public static Course NewCourse(Mentor mentor, decimal price = 150_000m) =>
        new() { Name = "Matematika SMA", Description = "Persiapan UTBK", Price = price, Mentor = mentor };

    public static CourseSession NewSession(string topic = "Limit Fungsi", int minutes = 90) =>
        new()
        {
            Topic = topic,
            DurationMinutes = minutes,
            StartTime = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc)
        };
}
