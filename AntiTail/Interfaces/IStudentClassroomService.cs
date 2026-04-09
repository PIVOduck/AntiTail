using LightMonitorBot.DTO;

namespace AntiTail.Interfaces;

public interface IStudentClassroomService: IBaseClassroomService
{ 
        // Отримання інформації про власну заборгованість
        Task<List<SubmissionDto>> GetMySubmissionsAsync(string courseId, string studentId);
        // Отримання критеріїв оцінювання конкретного завдання
        Task<string> GetGradingCriteriaAsync(string courseId, string assignmentId);
    
}