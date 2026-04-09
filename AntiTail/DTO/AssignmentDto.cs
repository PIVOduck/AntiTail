namespace LightMonitorBot.DTO;

public class AssignmentDto
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime? DueDate { get; set; } // Може бути null, якщо дедлайну немає
    public double? MaxPoints { get; set; }
}