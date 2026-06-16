namespace LightMonitorBot.DTO;

public class SubmissionDto
{
    public string Id { get; set; }
    public string AssignmentId { get; set; }
    public string State { get; set; } // Наприклад: "NEW", "TURNED_IN", "RETURNED"
    public double? AssignedGrade { get; set; } // Виставлена оцінка
    public string UserId { get; set; }
    
    
    // ВАЖЛИВО: Додаємо властивість Late
    public bool? Late { get; set; } 
    public string Title { get; set; }
    public string? CourseName { get; set; }
}