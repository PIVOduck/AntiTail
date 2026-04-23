

using AntiTail.Entities;

namespace AntiTail.Entitys;

public class Teacher
{
    public int Id { get; set; }
    public string FullName { get; set; }
    public string CorporateEmail { get; set; }
    public long TelegramChatId { get; set; }
    public string? GoogleId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public virtual ICollection<Course> Courses { get; set; }
    public virtual ICollection<Group> Group { get; set; }
    
}