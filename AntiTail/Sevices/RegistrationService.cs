using AntiTail.DBContext;
using AntiTail.Entities;
using AntiTail.Entitys;
using AntiTail.Interfaces;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Oauth2.v2.Data;
using LightMonitorBot.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AntiTail.Services;

public class RegistrationService : IRegistrationService
{
    private readonly AppDBContext _db;
    private readonly ILogger<RegistrationService> _logger;

    public RegistrationService(AppDBContext db, ILogger<RegistrationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── Пошук існуючих ───────────────────────────────────────────────────────

    public Task<Teacher?> FindTeacherByTelegramIdAsync(long telegramChatId) =>
        _db.Teachers
            .Include(t => t.Courses)           // Підтягуємо всі курси цього викладача
            .ThenInclude(c => c.Group)         // Одразу підтягуємо групи для цих курсів (щоб у бота коректно виводився шифр групи)
            .FirstOrDefaultAsync(t => t.TelegramChatId == telegramChatId);

    public Task<Student?> FindStudentByTelegramIdAsync(long telegramChatId) =>
        _db.Students.FirstOrDefaultAsync(s => s.TelegramChatId == telegramChatId);

    public Task<Teacher?> FindTeacherByGoogleIdAsync(string googleId) =>
        _db.Teachers.FirstOrDefaultAsync(t => t.GoogleId == googleId);

    public Task<Student?> FindStudentByGoogleIdAsync(string googleId) =>
        _db.Students.FirstOrDefaultAsync(s => s.GoogleId == googleId);

    // ─── Реєстрація ──────────────────────────────────────────────────────────

    public async Task<Teacher> RegisterTeacherAsync(
        long telegramChatId,
        Userinfo googleUser,
        TokenResponse tokens)
    {
        // Перевіряємо чи викладач вже існує
        var existing = await _db.Teachers.FirstOrDefaultAsync(t =>
            t.GoogleId == googleUser.Id ||
            t.CorporateEmail == googleUser.Email ||
            t.TelegramChatId == telegramChatId);

        if (existing != null)
        {
            existing.AccessToken    = tokens.AccessToken;
            existing.RefreshToken   = tokens.RefreshToken ?? existing.RefreshToken;
            existing.TokenExpiresAt = tokens.IssuedUtc.AddSeconds(tokens.ExpiresInSeconds ?? 3600);
            existing.TelegramChatId = telegramChatId;
            existing.GoogleId       = googleUser.Id;

            await _db.SaveChangesAsync();

            _logger.LogInformation("🔄 Оновлено існуючого викладача {Email} (TelegramId={TgId})",
                existing.CorporateEmail, telegramChatId);

            return existing;
        }

        var teacher = new Teacher
        {
            FullName        = googleUser.Name ?? googleUser.Email,
            CorporateEmail  = googleUser.Email,
            TelegramChatId  = telegramChatId,
            GoogleId        = googleUser.Id,
            AccessToken     = tokens.AccessToken,
            RefreshToken    = tokens.RefreshToken,
            TokenExpiresAt  = tokens.IssuedUtc.AddSeconds(tokens.ExpiresInSeconds ?? 3600)
        };

        _db.Teachers.Add(teacher);
        await _db.SaveChangesAsync();

        _logger.LogInformation("✅ Зареєстровано викладача {Email} (TelegramId={TgId})",
            teacher.CorporateEmail, telegramChatId);

        return teacher;
    }

    public async Task<Student> RegisterStudentAsync(
        long telegramChatId,
        Userinfo googleUser,
        TokenResponse tokens,
        int groupId)
    {
        // Перевіряємо чи студент вже існує по GoogleId або email
        var existing = await _db.Students.FirstOrDefaultAsync(s =>
            s.GoogleId == googleUser.Id ||
            s.CorporateEmail == googleUser.Email ||
            s.TelegramChatId == telegramChatId);

        if (existing != null)
        {
            // Оновлюємо токени і групу замість створення дубліката
            existing.AccessToken    = tokens.AccessToken;
            existing.RefreshToken   = tokens.RefreshToken ?? existing.RefreshToken;
            existing.TokenExpiresAt = tokens.IssuedUtc.AddSeconds(tokens.ExpiresInSeconds ?? 3600);
            existing.TelegramChatId = telegramChatId;
            existing.GroupId        = groupId;
            existing.GoogleId       = googleUser.Id;

            await _db.SaveChangesAsync();

            _logger.LogInformation("🔄 Оновлено існуючого студента {Email} (TelegramId={TgId})",
                existing.CorporateEmail, telegramChatId);

            return existing;
        }

        // Новий студент
        var student = new Student
        {
            FullName        = googleUser.Name ?? googleUser.Email,
            CorporateEmail  = googleUser.Email,
            TelegramChatId  = telegramChatId,
            GoogleId        = googleUser.Id,
            AccessToken     = tokens.AccessToken,
            RefreshToken    = tokens.RefreshToken,
            TokenExpiresAt  = tokens.IssuedUtc.AddSeconds(tokens.ExpiresInSeconds ?? 3600),
            GroupId         = groupId
        };

        _db.Students.Add(student);
        await _db.SaveChangesAsync();

        _logger.LogInformation("✅ Зареєстровано студента {Email} у групі #{GroupId} (TelegramId={TgId})",
            student.CorporateEmail, groupId, telegramChatId);

        return student;
    }

    // ─── Оновлення токенів (re-login) ─────────────────────────────────────────

    public async Task UpdateTeacherTokensAsync(int teacherId, TokenResponse tokens, string googleId)
    {
        var teacher = await _db.Teachers.FindAsync(teacherId)
            ?? throw new KeyNotFoundException($"Teacher #{teacherId} not found");

        teacher.GoogleId       = googleId;
        teacher.AccessToken    = tokens.AccessToken;
        teacher.RefreshToken   = tokens.RefreshToken ?? teacher.RefreshToken; // RefreshToken може не повернутись повторно
        teacher.TokenExpiresAt = tokens.IssuedUtc.AddSeconds(tokens.ExpiresInSeconds ?? 3600);

        await _db.SaveChangesAsync();
        _logger.LogInformation("🔄 Оновлено токени викладача #{Id}", teacherId);
    }

    public async Task UpdateStudentTokensAsync(int studentId, TokenResponse tokens, string googleId)
    {
        var student = await _db.Students.FindAsync(studentId)
            ?? throw new KeyNotFoundException($"Student #{studentId} not found");

        student.GoogleId       = googleId;
        student.AccessToken    = tokens.AccessToken;
        student.RefreshToken   = tokens.RefreshToken ?? student.RefreshToken;
        student.TokenExpiresAt = tokens.IssuedUtc.AddSeconds(tokens.ExpiresInSeconds ?? 3600);

        await _db.SaveChangesAsync();
        _logger.LogInformation("🔄 Оновлено токени студента #{Id}", studentId);
    }

    // ─── Групи ────────────────────────────────────────────────────────────────

    public Task<List<Group>> GetAllGroupsAsync() =>
        _db.Groups
           .Include(g => g.Curator)
           .OrderBy(g => g.Cipher)
           .ToListAsync();

    public Task<Group?> GetGroupByInviteTokenAsync(string inviteToken) =>
        _db.Groups.FirstOrDefaultAsync(g => g.InviteLink == inviteToken);
    
    // 1. Метод синхронізації курсів
    public async Task<List<Course>> SyncTeacherCoursesAsync(int teacherId, List<CourseDto> googleCourses)
    {
        var teacher = await _db.Teachers.Include(t => t.Courses).FirstOrDefaultAsync(t => t.Id == teacherId);
        if (teacher == null) return new List<Course>();

        foreach (var gc in googleCourses)
        {
            var existing = teacher.Courses.FirstOrDefault(c => c.GoogleCourseId == gc.Id);
            if (existing == null)
            {
                // Перевіряємо глобально в БД — щоб не дублювати курс
                var globalExisting = await _db.Courses.FirstOrDefaultAsync(c => c.GoogleCourseId == gc.Id && c.TeacherId == teacherId);
                if (globalExisting == null)
                {
                    var newCourse = new Course
                    {
                        Name = gc.Name,
                        GoogleCourseId = gc.Id,
                        Status = "Active",
                        GroupId = null
                    };
                    teacher.Courses.Add(newCourse);
                }
                else
                {
                    if (!teacher.Courses.Contains(globalExisting))
                        teacher.Courses.Add(globalExisting);
                    globalExisting.Name = gc.Name;
                }
            }
            else
            {
                existing.Name = gc.Name;
            }
        }
        await _db.SaveChangesAsync();
        return teacher.Courses.ToList();
    }

// 2. Метод прив'язки курсу до групи
    public async Task BindCourseToGroupAsync(int courseId, int groupId)
    {
        var course = await _db.Courses.FindAsync(courseId);
        if (course != null)
        {
            course.GroupId = groupId;
            await _db.SaveChangesAsync();
        }
    }
}