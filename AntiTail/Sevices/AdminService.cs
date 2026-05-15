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

    public async Task DeleteGroupAsync(int groupId)
    {
        var group = await _db.Groups.FindAsync(groupId);
        if (group != null)
        {
            _db.Groups.Remove(group);
            await _db.SaveChangesAsync();
        }
    }
}