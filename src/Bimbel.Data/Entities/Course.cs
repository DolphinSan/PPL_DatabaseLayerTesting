namespace Bimbel.Data.Entities;

public class Course
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }

    public int MentorId { get; set; }
    public Mentor Mentor { get; set; } = null!;

    public List<CourseSession> Sessions { get; set; } = new();
    public List<Enrollment> Enrollments { get; set; } = new();
}
