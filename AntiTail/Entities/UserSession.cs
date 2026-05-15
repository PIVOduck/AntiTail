namespace AntiTail.Models;
 
public class UserSession
{
    public RegistrationStep Step { get; set; } = RegistrationStep.None;
    public UserRole? Role { get; set; }
 
    public string? PendingFullName { get; set; }
    public string? PendingGroupInviteToken { get; set; } 
 
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
    AwaitingRole,           
    AwaitingGoogleAuth,     
    AwaitingGroupSelect,    
    Completed,              
    AwaitingNewGroupCipher, // Для адміна: введення назви групи
    AwaitingNewGroupYear    // Для адміна: введення курсу
}
 
public enum UserRole
{
    Student,
    Teacher,
    Admin // Додали адміна
}