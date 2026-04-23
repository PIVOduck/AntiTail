namespace AntiTail.Entitys;

public class Student
{
    public int Id { get; set; }
    public string FullName { get; set; }
    public string CorporateEmail { get; set; }
    public long TelegramChatId { get; set; }
    public string? GoogleId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public int GroupId { get; set; }
    public virtual Group Group { get; set; }
}