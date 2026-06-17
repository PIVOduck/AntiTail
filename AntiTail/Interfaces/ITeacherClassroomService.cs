using LightMonitorBot.DTO;

namespace AntiTail.Interfaces;

public interface ITeacherClassroomService: IBaseClassroomService
{
// --- Керування студентами ---
    Task<List<StudentDto>> GetCourseStudentsAsync(string courseId, string accessToken);
    Task InviteStudentToCourseAsync(string courseId, string studentEmail);

    // --- Перевірка робіт ---
    Task<List<SubmissionDto>> GetAllSubmissionsAsync(string courseId, string assignmentId, string accessToken);
    Task GradeStudentSubmissionAsync(string courseId, string assignmentId, string submissionId, double grade, string accessToken);

    // --- Комунікація ---
    Task CreateAnnouncementAsync(string courseId, string text, List<string> targetStudentIds = null);
    Task CreateAnnouncementAsync(string courseId, string text, string accessToken, string teacherName = "Викладач");
}