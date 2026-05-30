using AntiTail.Entitys;

namespace AntiTail.Entities;

public class Course
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? GoogleCourseId { get; set; }
    public string Status { get; set; } = "Active"; 
    public int? GroupId { get; set; }
    public virtual Group? Group { get; set; }
    public int TeacherId { get; set; }
    public virtual Teacher Teacher { get; set; }
    public virtual ICollection<Assignment> Assignments { get; set; }
    
}