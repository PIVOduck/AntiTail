using LightMonitorBot.DTO;

namespace AntiTail.Interfaces;

public interface ITeacherClassroomService: IBaseClassroomService
{
    // --- Керування студентами ---
    Task<List<StudentDto>> GetCourseStudentsAsync(string courseId, string accessToken);
    Task InviteStudentToCourseAsync(string courseId, string studentEmail);

    // --- Перевірка робіт ---
    // Перегляд черги здачі (всі роботи всіх студентів)
    Task<List<SubmissionDto>> GetAllSubmissionsAsync(string courseId, string assignmentId,string accessToken);
        
    // Позначення роботи як перевіреної (виставлення оцінки)
    Task GradeStudentSubmissionAsync(string courseId, string assignmentId, string submissionId, double grade, string accessToken);

    // --- Комунікація ---
    // Створення оголошення для групи
    Task CreateAnnouncementAsync(string courseId, string text, List<string> targetStudentIds = null);

}