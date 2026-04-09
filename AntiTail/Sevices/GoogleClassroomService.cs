using AntiTail.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Classroom.v1;
using LightMonitorBot.DTO;


namespace AntiTail.Services // Ваш namespace
{
    public class GoogleClassroomService : ITeacherClassroomService, IStudentClassroomService
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri;

        // IConfiguration дозволяє читати дані з appsettings.json
        public GoogleClassroomService(IConfiguration configuration)
        {
            _clientId = configuration["GoogleAuth:ClientId"];
            _clientSecret = configuration["GoogleAuth:ClientSecret"];
            _redirectUri = configuration["GoogleAuth:RedirectUri"];
        }

        public string GetAuthorizationUrl(string chatId)
        {
            // Налаштовуємо "потік" авторизації Google
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret
                },
                // Вказуємо, що нам потрібен доступ до курсів
                Scopes = new[] { ClassroomService.Scope.ClassroomCoursesReadonly }
            });

            // Генеруємо посилання. 
            // Параметр state дуже важливий — туди ми "ховаємо" ChatId користувача!
            // 1. Спочатку створюємо об'єкт запиту
            var request = flow.CreateAuthorizationCodeRequest(_redirectUri);

            // 2. Ховаємо наш ChatId у властивість State
            request.State = chatId;

            // 3. ТІЛЬКИ ТЕПЕР генеруємо (будуємо) фінальне посилання
            var url = request.Build();

            return url.AbsoluteUri;
        }

        // ... Інші методи (AuthenticateUserAsync, GetUserCoursesAsync) 
        // поки що можна залишити порожніми або кидати виняток NotImplementedException

        public string GetAuthorizationUrl()
        {
            throw new NotImplementedException();
        }

        public Task<TokenResponse> AuthenticateUserAsync(string authCode) => throw new NotImplementedException();
        public Task RevokeUserTokensAsync(string userId) => throw new NotImplementedException();
        public Task<List<CourseDto>> GetUserCoursesAsync(string userId) => throw new NotImplementedException();

        public Task<List<AssignmentDto>> GetCourseAssignmentsAsync(string courseId)
        {
            throw new NotImplementedException();
        }

        public Task<List<AnnouncementDto>> GetCourseAnnouncementsAsync(string courseId)
        {
            throw new NotImplementedException();
        }

        // ... додайте заглушки для інших методів з інтерфейсів
        public Task<List<StudentDto>> GetCourseStudentsAsync(string courseId)
        {
            throw new NotImplementedException();
        }

        public Task InviteStudentToCourseAsync(string courseId, string studentEmail)
        {
            throw new NotImplementedException();
        }

        public Task<List<SubmissionDto>> GetAllSubmissionsAsync(string courseId, string assignmentId)
        {
            throw new NotImplementedException();
        }

        public Task GradeStudentSubmissionAsync(string courseId, string assignmentId, string submissionId, double grade)
        {
            throw new NotImplementedException();
        }

        public Task CreateAnnouncementAsync(string courseId, string text, List<string> targetStudentIds = null)
        {
            throw new NotImplementedException();
        }

        public Task<List<SubmissionDto>> GetMySubmissionsAsync(string courseId, string studentId)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetGradingCriteriaAsync(string courseId, string assignmentId)
        {
            throw new NotImplementedException();
        }
    }
}