using AntiTail.DBContext;
using AntiTail.Entitys;
using AntiTail.Interfaces;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Oauth2.v2.Data;
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
        _db.Teachers.FirstOrDefaultAsync(t => t.TelegramChatId == telegramChatId);

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
}