using Google.Apis.Auth.OAuth2.Responses;
using LightMonitorBot.DTO;

namespace AntiTail.Interfaces;

public interface IBaseClassroomService
{
        // --- Авторизація ---
        string GetAuthorizationUrl(string chatId);
        Task<TokenResponse> AuthenticateUserAsync(string authCode);
        Task RevokeUserTokensAsync(string userId);

        // --- Загальні дані ---
        Task<List<CourseDto>> GetUserCoursesAsync(string accessToken);
        Task<List<AssignmentDto>> GetCourseAssignmentsAsync(string courseId);
        Task<List<AnnouncementDto>> GetCourseAnnouncementsAsync(string courseId);
        Task<Google.Apis.Oauth2.v2.Data.Userinfo> GetUserInfoAsync(TokenResponse tokens);
    
}