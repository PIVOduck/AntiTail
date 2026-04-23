namespace AntiTail.Entities;

public class Assignment
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string? GoogleAssignmentId { get; set; }
    public DateTime? DueDate { get; set; }
    public float MaxPoints { get; set; }
    public int CourseId { get; set; }
    public virtual Course Course { get; set; }
}