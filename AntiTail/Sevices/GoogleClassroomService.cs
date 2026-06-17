using AntiTail.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Classroom.v1;
using Google.Apis.Oauth2.v2.Data;
using LightMonitorBot.DTO;
using Microsoft.Extensions.Configuration;
using Google.Apis.Classroom.v1.Data;

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
                    // --- ДОЗВОЛИ ДЛЯ СТУДЕНТА ---
                    ClassroomService.Scope.ClassroomCourseworkMeReadonly,
                    ClassroomService.Scope.ClassroomStudentSubmissionsMeReadonly,
    
                    // --- НОВІ ДОЗВОЛИ ДЛЯ ВИКЛАДАЧА ---
                    ClassroomService.Scope.ClassroomCourseworkStudents, // Дає право бачити завдання і ставити оцінки!
                    ClassroomService.Scope.ClassroomAnnouncements,      // Дає право створювати оголошення
    
                    Google.Apis.Oauth2.v2.Oauth2Service.Scope.UserinfoEmail,
                    Google.Apis.Oauth2.v2.Oauth2Service.Scope.UserinfoProfile
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
        
        public async Task<List<CourseDto>> GetUserCoursesAsync(string accessToken)
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var classroomService = new ClassroomService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AntiTailBot"
            });

            // Формуємо запит до Google Classroom
            var request = classroomService.Courses.List();
            request.CourseStates = CoursesResource.ListRequest.CourseStatesEnum.ACTIVE;           
            //request.TeacherId = "me"; // Тільки ті, де власник токена є викладачем

            var response = await request.ExecuteAsync();
            var result = new List<CourseDto>();

            if (response.Courses != null)
            {
                foreach (var c in response.Courses)
                {
                    result.Add(new CourseDto
                    {
                        Id = c.Id,      // Це буде наш GoogleCourseId
                        Name = c.Name   // Назва предмета
                    });
                }
            }
            return result;
        }

        public async Task<List<AssignmentDto>> GetCourseAssignmentsAsync(string courseId)
        {
            throw new NotImplementedException("Див. оновлений варіант з accessToken нижче");
        }
        public async Task<List<AssignmentDto>> GetCourseAssignmentsAsync(string courseId, string accessToken)
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var classroomService = new ClassroomService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AntiTailBot"
            });

            var request = classroomService.Courses.CourseWork.List(courseId);
            var response = await request.ExecuteAsync();
            var result = new List<AssignmentDto>();

            if (response.CourseWork != null)
            {
                foreach (var work in response.CourseWork)
                {
                    result.Add(new AssignmentDto
                    {
                        Id = work.Id,
                        Title = work.Title,
                        Description = work.Description,
                        MaxPoints = work.MaxPoints
                    });
                }
            }
            return result;
        }
        public async Task<List<AnnouncementDto>> GetCourseAnnouncementsAsync(string courseId, string accessToken)
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var classroomService = new ClassroomService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AntiTailBot"
            });

            var result = new List<AnnouncementDto>();

            // Отримуємо список викладачів курсу щоб показувати їх імена
            var teacherNames = new Dictionary<string, string>(); // userId -> fullName
            try
            {
                var teachersRequest = classroomService.Courses.Teachers.List(courseId);
                teachersRequest.Fields = "teachers(userId,profile(name,emailAddress))";
                var teachersResponse = await teachersRequest.ExecuteAsync();
                if (teachersResponse.Teachers != null)
                {
                    foreach (var t in teachersResponse.Teachers)
                    {
                        var name = t.Profile?.Name?.FullName ?? t.Profile?.EmailAddress ?? "Викладач";
                        teacherNames[t.UserId] = name;
                    }
                }
            }
            catch { }

            // Отримуємо Announcements (оголошення від викладача)
            try
            {
                var annRequest = classroomService.Courses.Announcements.List(courseId);
                annRequest.AnnouncementStates = CoursesResource.AnnouncementsResource.ListRequest.AnnouncementStatesEnum.PUBLISHED;
                annRequest.PageSize = 20;
                var annResponse = await annRequest.ExecuteAsync();

                if (annResponse.Announcements != null)
                {
                    foreach (var a in annResponse.Announcements)
                    {
                        string authorName = (!string.IsNullOrEmpty(a.CreatorUserId) && teacherNames.TryGetValue(a.CreatorUserId, out var n)) ? n : "Викладач";
                        result.Add(new AnnouncementDto
                        {
                            Id = a.Id,
                            Text = $"👤 <i>{authorName}</i>\n{a.Text ?? "(без тексту)"}",
                            UpdateTime = (a.UpdateTime?.ToString() ?? a.CreationTime?.ToString() ?? "")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Якщо немає прав на Announcements — продовжуємо, спробуємо матеріали
            }

            int beforeMaterials = result.Count;

            // Також отримуємо матеріали (MATERIAL CourseWork)
            try
            {
                var matRequest = classroomService.Courses.CourseWork.List(courseId);
                matRequest.CourseWorkStates = CoursesResource.CourseWorkResource.ListRequest.CourseWorkStatesEnum.PUBLISHED;
                matRequest.PageSize = 20;
                var matResponse = await matRequest.ExecuteAsync();

                if (matResponse.CourseWork != null)
                {
                    foreach (var m in matResponse.CourseWork.Where(w =>
                                 w.WorkType == "MATERIAL" ||
                                 (w.Title != null && w.Title.Contains("Оголошення"))))
                    {
                        string body = m.Description ?? "";
                        result.Add(new AnnouncementDto
                        {
                            Id = m.Id,
                            Text = $"<b>{m.Title}</b>" + (string.IsNullOrEmpty(body) ? "" : $"\n{body}"),
                            UpdateTime = (m.UpdateTime?.ToString() ?? m.CreationTime?.ToString() ?? "")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
            }

            // Сортуємо від найновіших
            return result.OrderByDescending(a => a.UpdateTime).ToList();
        }

        public Task<List<AnnouncementDto>> GetCourseAnnouncementsAsync(string courseId) => throw new NotImplementedException();
        public async Task<Userinfo> GetUserInfoAsync(TokenResponse tokens)
        {
            // Створюємо тимчасовий доступ (Credential) за допомогою токена доступу
            var credential = GoogleCredential.FromAccessToken(tokens.AccessToken);
    
            // Створюємо сервіс для роботи з Oauth2 API
            var oauth2Service = new Google.Apis.Oauth2.v2.Oauth2Service(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AntiTailBot"
            });

            // Робимо запит до Google, щоб отримати інформацію про профіль (Email, Name, Id)
            return await oauth2Service.Userinfo.Get().ExecuteAsync();
        }

        public Task<List<StudentDto>> GetCourseStudentsAsync(string courseId, string accessToken) => throw new NotImplementedException();

        public Task InviteStudentToCourseAsync(string courseId, string studentEmail) => throw new NotImplementedException();

        public async Task<List<SubmissionDto>> GetAllSubmissionsAsync(string courseId, string assignmentId, string accessToken)
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var classroomService = new ClassroomService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AntiTailBot"
            });

            var result = new List<SubmissionDto>();

            // Просимо в Google всі здані роботи для цього конкретного завдання
            var request = classroomService.Courses.CourseWork.StudentSubmissions.List(courseId, assignmentId);
            var response = await request.ExecuteAsync();

            if (response.StudentSubmissions != null)
            {
                foreach (var sub in response.StudentSubmissions)
                {
                    result.Add(new SubmissionDto
                    {
                        Id = sub.Id,
                        AssignmentId = sub.CourseWorkId,
                        State = sub.State, // "NEW", "TURNED_IN", "RETURNED"
                        AssignedGrade = sub.AssignedGrade,
                        UserId = sub.UserId,
                        Title = $"Робота від студента (ID: {sub.UserId})"
                    });
                }
            }
            return result;
        }

        public async Task GradeStudentSubmissionAsync(string courseId, string assignmentId, string submissionId, double grade, string accessToken)
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var classroomService = new ClassroomService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AntiTailBot"
            });

            var submission = new Google.Apis.Classroom.v1.Data.StudentSubmission
            {
                AssignedGrade = grade
            };

            // Виставляємо оцінку в Classroom
            var patchRequest = classroomService.Courses.CourseWork.StudentSubmissions.Patch(
                submission, courseId, assignmentId, submissionId);
            patchRequest.UpdateMask = "assignedGrade";
            await patchRequest.ExecuteAsync();
    
            // Рядки з Return ми просто прибрали!
        }
        
        public async Task CreateAnnouncementAsync(string courseId, string text, List<string> targetStudentIds = null)
        {
            throw new InvalidOperationException("Використовуйте CreateAnnouncementAsync(courseId, text, accessToken).");
        }

        public async Task CreateAnnouncementAsync(string courseId, string text, string accessToken, string teacherName = "Викладач")
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var classroomService = new ClassroomService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AntiTailBot"
            });

            var material = new Google.Apis.Classroom.v1.Data.CourseWork
            {
                Title = $"📣 Оголошення від {teacherName}",
                Description = $"👤 {teacherName}\n\n{text}",
                WorkType = "ASSIGNMENT",
                State = "PUBLISHED"
            };

            await classroomService.Courses.CourseWork.Create(material, courseId).ExecuteAsync();
        }

        public async Task<List<SubmissionDto>> GetMySubmissionsAsync(string courseId, string accessToken)
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var classroomService = new ClassroomService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AntiTailBot"
            });

            var result = new List<SubmissionDto>();

            // 1. Отримуємо всі завдання (CourseWork) курсу, щоб знати їх назви та дедлайни
            var workRequest = classroomService.Courses.CourseWork.List(courseId);
            var workResponse = await workRequest.ExecuteAsync();
            var courseWorks = workResponse.CourseWork != null ? workResponse.CourseWork.ToList() : new List<CourseWork>();

            // 2. Отримуємо всі здачі студента
            var subRequest = classroomService.Courses.CourseWork.StudentSubmissions.List(courseId, "-"); 
            subRequest.UserId = "me";
            var subResponse = await subRequest.ExecuteAsync();

            if (subResponse.StudentSubmissions != null)
            {
                foreach (var sub in subResponse.StudentSubmissions)
                {
                    // Шукаємо відповідне завдання, щоб взяти з нього Назву і Дедлайн
                    var relatedWork = courseWorks.FirstOrDefault(w => w.Id == sub.CourseWorkId);
                    
                    bool isLate = false;
                    
                    // Самостійно вираховуємо дедлайн, не чекаючи на прапорці Google
                    if (relatedWork != null && relatedWork.DueDate != null)
                    {
                        var dueDate = new DateTime(
                            relatedWork.DueDate.Year ?? DateTime.UtcNow.Year,
                            relatedWork.DueDate.Month ?? DateTime.UtcNow.Month,
                            relatedWork.DueDate.Day ?? DateTime.UtcNow.Day,
                            relatedWork.DueTime?.Hours ?? 23,
                            relatedWork.DueTime?.Minutes ?? 59,
                            0, DateTimeKind.Utc);

                        // Якщо дедлайн пройшов, а робота не здана - це борг!
                        if (dueDate < DateTime.UtcNow && sub.State != "TURNED_IN" && sub.State != "RETURNED")
                        {
                            isLate = true;
                        }
                    }
                    else if (sub.Late.HasValue && sub.Late.Value)
                    {
                        isLate = true;
                    }

                    result.Add(new SubmissionDto
                    {
                        Id = sub.Id,
                        AssignmentId = sub.CourseWorkId,
                        State = sub.State,
                        AssignedGrade = sub.AssignedGrade,
                        Late = isLate, // Тепер це 100% точний показник боргу
                        UserId = sub.UserId,
                        Title = relatedWork != null ? relatedWork.Title : $"Завдання (ID: {sub.CourseWorkId})"
                    });
                }
            }

            return result;
        }

        public Task<string> GetGradingCriteriaAsync(string courseId, string assignmentId) => throw new NotImplementedException();
    }
}