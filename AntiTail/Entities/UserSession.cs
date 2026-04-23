namespace AntiTail.Models;
 
/// <summary>
/// Зберігає тимчасовий стан реєстрації/авторизації для конкретного Telegram-чату.
/// Зберігається в пам'яті (IMemoryCache) — не потрібна таблиця в БД.
/// </summary>
public class UserSession
{
    public RegistrationStep Step { get; set; } = RegistrationStep.None;
    public UserRole? Role { get; set; }
 
    // Дані, зібрані під час реєстрації (до підтвердження Google)
    public string? PendingFullName { get; set; }
    public string? PendingGroupInviteToken { get; set; } // для студента
 
    // Google-токени, отримані після OAuth callback — чекаємо на збереження
    public string? PendingAccessToken { get; set; }
    public string? PendingRefreshToken { get; set; }
    public DateTime? PendingTokenExpiresAt { get; set; }
    public string? PendingGoogleId { get; set; }
    public string? PendingEmail { get; set; }
    public string? PendingName { get; set; }
}
 
public enum RegistrationStep
{
    None,
    AwaitingRole,           // /start → запитуємо: студент чи викладач?
    AwaitingGoogleAuth,     // Відправили посилання — чекаємо callback
    AwaitingGroupSelect,    // Студент: список груп для вибору
    Completed               // Авторизований
}
 
public enum UserRole
{
    Student,
    Teacher
}