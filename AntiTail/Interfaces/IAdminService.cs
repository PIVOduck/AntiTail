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
}