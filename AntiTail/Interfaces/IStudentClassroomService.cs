using System.Collections.Generic;
using System.Threading.Tasks;
using LightMonitorBot.DTO;

namespace AntiTail.Interfaces;

public interface IStudentClassroomService : IBaseClassroomService
{ 
    // Отримання інформації про власну заборгованість студента за допомогою токена доступу
    Task<List<SubmissionDto>> GetMySubmissionsAsync(string courseId, string accessToken);
    
    // Отримання критеріїв оцінювання конкретного завдання
    Task<string> GetGradingCriteriaAsync(string courseId, string assignmentId);
    
    // Отримання оголошень курсу (метод, якого не бачив компілятор)
    Task<List<AnnouncementDto>> GetCourseAnnouncementsAsync(string courseId, string accessToken); 
}