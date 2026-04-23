using AntiTail.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Classroom.v1;
using LightMonitorBot.DTO;

namespace AntiTail.Services
{
    public class GoogleClassroomService : ITeacherClassroomService, IStudentClassroomService
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri;

        public GoogleClassroomService(IConfiguration configuration)
        {
            _clientId = configuration["GoogleAuth:ClientId"];
            _clientSecret = configuration["GoogleAuth:ClientSecret"];
            _redirectUri = configuration["GoogleAuth:RedirectUri"];
        }

        public string GetAuthorizationUrl(string chatId)
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret
                },
                // 1. ВАЖЛИВО: Додали дозволи на завдання та оцінки!
                Scopes = new[] 
                { 
                    ClassroomService.Scope.ClassroomCoursesReadonly,
                    ClassroomService.Scope.ClassroomCourseworkMeReadonly,
                    ClassroomService.Scope.ClassroomStudentSubmissionsMeReadonly
                }
            });

            var request = (Google.Apis.Auth.OAuth2.Requests.GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(_redirectUri);
            request.State = chatId;
            
            // 2. ВАЖЛИВО: Просимо довгостроковий Refresh Token
            request.AccessType = "offline"; 
            request.Prompt = "consent";

            return request.Build().AbsoluteUri;
        }

        // 3. ВАЖЛИВО: Метод для обміну коду на справжні токени
        public async Task<TokenResponse> AuthenticateUserAsync(string authCode)
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret
                }
            });

            var tokenResponse = await flow.ExchangeCodeForTokenAsync(
                userId: "user", 
                code: authCode, 
                redirectUri: _redirectUri, 
                taskCancellationToken: CancellationToken.None
            );

            return tokenResponse;
        }

        // --- Заглушки для інших методів (їх ми реалізуємо пізніше, коли токени будуть у БД) ---

        public Task RevokeUserTokensAsync(string userId) => throw new NotImplementedException();
        
        public Task<List<CourseDto>> GetUserCoursesAsync(string userId) => throw new NotImplementedException();

        public Task<List<AssignmentDto>> GetCourseAssignmentsAsync(string courseId) => throw new NotImplementedException();

        public Task<List<AnnouncementDto>> GetCourseAnnouncementsAsync(string courseId) => throw new NotImplementedException();

        public Task<List<StudentDto>> GetCourseStudentsAsync(string courseId) => throw new NotImplementedException();

        public Task InviteStudentToCourseAsync(string courseId, string studentEmail) => throw new NotImplementedException();

        public Task<List<SubmissionDto>> GetAllSubmissionsAsync(string courseId, string assignmentId) => throw new NotImplementedException();

        public Task GradeStudentSubmissionAsync(string courseId, string assignmentId, string submissionId, double grade) => throw new NotImplementedException();

        public Task CreateAnnouncementAsync(string courseId, string text, List<string> targetStudentIds = null) => throw new NotImplementedException();

        public Task<List<SubmissionDto>> GetMySubmissionsAsync(string courseId, string studentId) => throw new NotImplementedException();

        public Task<string> GetGradingCriteriaAsync(string courseId, string assignmentId) => throw new NotImplementedException();
    }
}