using LightMonitorBot.DTO;

namespace AntiTail.Interfaces;

public interface ITeacherClassroomService: IBaseClassroomService
{
    // --- Керування студентами ---
    Task<List<StudentDto>> GetCourseStudentsAsync(string courseId);
    Task InviteStudentToCourseAsync(string courseId, string studentEmail);

    // --- Перевірка робіт ---
    // Перегляд черги здачі (всі роботи всіх студентів)
    Task<List<SubmissionDto>> GetAllSubmissionsAsync(string courseId, string assignmentId);
        
    // Позначення роботи як перевіреної (виставлення оцінки)
    Task GradeStudentSubmissionAsync(string courseId, string assignmentId, string submissionId, double grade);

    // --- Комунікація ---
    // Створення оголошення для групи
    Task CreateAnnouncementAsync(string courseId, string text, List<string> targetStudentIds = null);

}