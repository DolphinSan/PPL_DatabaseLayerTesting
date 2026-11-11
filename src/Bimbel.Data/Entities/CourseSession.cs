namespace Bimbel.Data.Entities;

public class CourseSession
{
    public int Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public int DurationMinutes { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
}
