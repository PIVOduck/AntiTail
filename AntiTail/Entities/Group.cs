using AntiTail.Entities;

namespace AntiTail.Entitys;

public class Group
{
    public int Id { get; set; }
    public string Cipher { get; set; } 
    public int CourseYear { get; set; }
    public string? InviteLink { get; set; } 
    public int CuratorId { get; set; } 
    public virtual Teacher Curator { get; set; }
    
    public virtual ICollection<Student> Students { get; set; } 
    public virtual ICollection<Course> Courses { get; set; } 
}