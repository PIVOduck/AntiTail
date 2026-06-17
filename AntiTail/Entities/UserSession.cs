namespace AntiTail.Models;
 
public class UserSession
{
    public RegistrationStep Step { get; set; } = RegistrationStep.None;
    public UserRole? Role { get; set; }
 
    public string? PendingFullName { get; set; }
    public string? PendingGroupInviteToken { get; set; }
    public int? EditTargetId { get; set; }

    public string? PendingAccessToken { get; set; }
    public string? PendingRefreshToken { get; set; }
    public DateTime? PendingTokenExpiresAt { get; set; }
    public string? PendingGoogleId { get; set; }
    public string? PendingEmail { get; set; }
    public string? PendingName { get; set; }
    public string? PendingCourseId { get; set; }
    public string? PendingAssignmentId { get; set; }
    public string? PendingSubmissionId { get; set; }
    public int? PendingTeacherId { get; set; }
    public long? PendingStudentChatId { get; set; }
}
 
public enum RegistrationStep
{
    None,
    AwaitingRole,           
    AwaitingGoogleAuth,     
    AwaitingGroupSelect,    
    Completed,              
    AwaitingNewGroupCipher, 
    AwaitingNewGroupYear,
    
    // НОВІ КРОКИ ДЛЯ РЕДАГУВАННЯ:
    AwaitingEditGroupCipher,
    AwaitingEditGroupYear,
    AwaitingEditTeacherName,
    AwaitingEditTeacherEmail,
    AwaitingEditStudentName,
    AwaitingEditStudentEmail,   // Для адміна: введення курсу
    // --- НОВІ КРОКИ ДЛЯ ВИКЛАДАЧА ---
       // Очікуємо оцінку для студента
       // Додати до вашого переліку станів:
       TeacherAwaitingAnnouncementCourse,
       TeacherAwaitingAnnouncementText,
       TeacherAwaitingGradingCourse,
       TeacherAwaitingGradingAssignment,
       TeacherAwaitingGradingSubmission,
       TeacherAwaitingGradeValue,
       TeacherAwaitingCourseBindingSelectCourse,
       TeacherAwaitingCourseBindingSelectGroup,
       StudentAwaitingTeacherMessage,
       TeacherAwaitingReplyToStudent
}
 
public enum UserRole
{
    Student,
    Teacher,
    Admin // Додали адміна
}