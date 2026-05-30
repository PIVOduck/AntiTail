using AntiTail.DBContext;
using AntiTail.Entities;
using AntiTail.Entitys;
using AntiTail.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AntiTail.Services;

public class AdminService : IAdminService
{
    private readonly AppDBContext _db;

    public AdminService(AppDBContext db)
    {
        _db = db;
    }
    public Task<List<Teacher>> GetAllTeachersAsync() =>
        // Include(t => t.Group) підтягне інформацію про групи, які курує викладач
        _db.Teachers.Include(t => t.Group).ToListAsync();

    public Task<List<Student>> GetStudentsByGroupIdAsync(int groupId) =>
        _db.Students.Where(s => s.GroupId == groupId).ToListAsync();

    public Task<Admin?> FindAdminByTelegramIdAsync(long telegramChatId) =>
        _db.Admins.FirstOrDefaultAsync(a => a.TelegramChatId == telegramChatId);

    public async Task<Group> CreateGroupAsync(string cipher, int courseYear)
    {
        var group = new Group
        {
            Cipher = cipher,
            CourseYear = courseYear,
            // InviteLink можна згенерувати автоматично (наприклад, Guid.NewGuid().ToString().Substring(0, 8))
            InviteLink = Guid.NewGuid().ToString().Substring(0, 8) 
        };

        _db.Groups.Add(group);
        await _db.SaveChangesAsync();
        return group;
    }
// --- ВИДАЛЕННЯ ---
    public async Task DeleteGroupAsync(int id)
    {
        var entity = await _db.Groups.FindAsync(id);
        if (entity != null) { _db.Groups.Remove(entity); await _db.SaveChangesAsync(); }
    }
    public async Task DeleteTeacherAsync(int id)
    {
        var entity = await _db.Teachers.FindAsync(id);
        if (entity != null) { _db.Teachers.Remove(entity); await _db.SaveChangesAsync(); }
    }
    public async Task DeleteStudentAsync(int id)
    {
        var entity = await _db.Students.FindAsync(id);
        if (entity != null) { _db.Students.Remove(entity); await _db.SaveChangesAsync(); }
    }

    // --- РЕДАГУВАННЯ ---
    public async Task UpdateGroupAsync(int id, string? cipher = null, int? year = null, int? curatorId = null)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null) return;
        
        if (cipher != null) group.Cipher = cipher;
        if (year.HasValue) group.CourseYear = year.Value;
        if (curatorId.HasValue) group.CuratorId = curatorId.Value; // Тут може бути логіка відкріплення, якщо передати спеціальне значення, але для простоти поки так
        
        await _db.SaveChangesAsync();
    }

    public async Task UpdateTeacherAsync(int id, string? fullName = null, string? email = null)
    {
        var teacher = await _db.Teachers.FindAsync(id);
        if (teacher == null) return;
        if (fullName != null) teacher.FullName = fullName;
        if (email != null) teacher.CorporateEmail = email;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateStudentAsync(int id, string? fullName = null, string? email = null, int? groupId = null)
    {
        var student = await _db.Students.FindAsync(id);
        if (student == null) return;
        if (fullName != null) student.FullName = fullName;
        if (email != null) student.CorporateEmail = email;
        if (groupId.HasValue) student.GroupId = groupId.Value;
        await _db.SaveChangesAsync();
    }
}