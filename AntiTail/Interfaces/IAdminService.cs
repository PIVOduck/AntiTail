using AntiTail.Entities;
using AntiTail.Entitys;

namespace AntiTail.Interfaces;

public interface IAdminService
{
    Task<Admin?> FindAdminByTelegramIdAsync(long telegramChatId);
    Task<Group> CreateGroupAsync(string cipher, int courseYear);
    Task DeleteGroupAsync(int groupId);
    Task<List<Teacher>> GetAllTeachersAsync();
    Task<List<Student>> GetStudentsByGroupIdAsync(int groupId);
    // --- Видалення ---
    Task DeleteTeacherAsync(int id);
    Task DeleteStudentAsync(int id);

    // --- Редагування ---
    Task UpdateGroupAsync(int id, string? cipher = null, int? year = null, int? curatorId = null);
    Task UpdateTeacherAsync(int id, string? fullName = null, string? email = null);
    Task UpdateStudentAsync(int id, string? fullName = null, string? email = null, int? groupId = null);
}