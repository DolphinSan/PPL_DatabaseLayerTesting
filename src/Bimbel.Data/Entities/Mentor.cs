namespace Bimbel.Data.Entities;

public class Mentor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public List<Course> Courses { get; set; } = new();
}
