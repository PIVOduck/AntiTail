using AntiTail.Entities;
using AntiTail.Models;
using AntiTail.Entitys;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Oauth2.v2.Data;
using LightMonitorBot.DTO;

namespace AntiTail.Interfaces;

public interface IRegistrationService
{
    // --- Перевірка існуючих користувачів ---
    Task<Teacher?> FindTeacherByTelegramIdAsync(long telegramChatId);
    Task<Student?> FindStudentByTelegramIdAsync(long telegramChatId);
    Task<Teacher?> FindTeacherByGoogleIdAsync(string googleId);
    Task<Student?> FindStudentByGoogleIdAsync(string googleId);

    // --- Реєстрація нових ---
    /// <summary>
    /// Реєструє нового викладача після успішного Google OAuth.
    /// </summary>
    Task<Teacher> RegisterTeacherAsync(long telegramChatId, Userinfo googleUser, TokenResponse tokens);

    /// <summary>
    /// Реєструє нового студента після успішного Google OAuth та вибору групи.
    /// </summary>
    Task<Student> RegisterStudentAsync(long telegramChatId, Userinfo googleUser, TokenResponse tokens, int groupId);

    // --- Оновлення токенів (повторна авторизація) ---
    Task UpdateTeacherTokensAsync(int teacherId, TokenResponse tokens, string googleId);
    Task UpdateStudentTokensAsync(int studentId, TokenResponse tokens, string googleId);

    // --- Групи ---
    Task<List<Group>> GetAllGroupsAsync();
    Task<Group?> GetGroupByInviteTokenAsync(string inviteToken);

    Task<List<Course>> SyncTeacherCoursesAsync(int teacherId, List<LightMonitorBot.DTO.CourseDto> googleCourses);
    Task BindCourseToGroupAsync(int courseId, int groupId);}