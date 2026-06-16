namespace LightMonitorBot.DTO;

public class AnnouncementDto
{
    public string Id { get; set; }
    public string Text { get; set; }
    public DateTime CreationTime { get; set; }
    public string? UpdateTime { get; set; }  // ← додати
}