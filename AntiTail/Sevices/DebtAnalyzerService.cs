using LightMonitorBot.DTO;
using AntiTail.Interfaces;
using AntiTail.Models; 
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AntiTail.DBContext;

namespace AntiTail.Services;

public class DebtAnalyzerService : IDebtAnalyzerService
{
    private readonly IStudentClassroomService _classroomApi;
    private readonly AppDBContext _dbContext;
    private readonly IConfiguration _configuration;

    public DebtAnalyzerService(IStudentClassroomService classroomApi, AppDBContext dbContext, IConfiguration configuration)
    {
        _classroomApi = classroomApi;
        _dbContext = dbContext;
        _configuration = configuration;
    }

   public async Task<List<SubmissionDto>> CalculateStudentDebtsAsync(string chatId)
{
    long parsedChatId = long.Parse(chatId);
    var student = await _dbContext.Students.FirstOrDefaultAsync(s => s.TelegramChatId == parsedChatId);

    // 1. Оновлення токенів (залишається без змін)
    if (student != null && !string.IsNullOrEmpty(student.RefreshToken))
    {
        if (student.TokenExpiresAt == null || student.TokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
        {
            var newTokens = await RefreshGoogleTokenAsync(student.RefreshToken);
            if (newTokens != null && !string.IsNullOrEmpty(newTokens.AccessToken))
            {
                student.AccessToken = newTokens.AccessToken;
                student.TokenExpiresAt = DateTime.UtcNow.AddSeconds(newTokens.ExpiresIn); 
                
                _dbContext.Students.Update(student);
                await _dbContext.SaveChangesAsync();
            }
        }
    }

    var debts = new List<SubmissionDto>();

    // Передаємо AccessToken замість ChatId
    var courses = await _classroomApi.GetUserCoursesAsync(student.AccessToken);

    foreach (var course in courses)
    {
        // Отримуємо роботи. Метод вже сам перевірив дедлайни і підтягнув назви!
        var submissions = await _classroomApi.GetMySubmissionsAsync(course.Id, student.AccessToken);
        
        // Просто беремо ті, де Late == true
        var overdueSubmissions = submissions.Where(s => s.Late == true).ToList();
        overdueSubmissions.ForEach(s => s.CourseName = course.Name);
        debts.AddRange(overdueSubmissions);
    }

    return debts;
}

    private async Task<GoogleTokenResponse?> RefreshGoogleTokenAsync(string refreshToken)
    {
        // БЕЗПЕЧНО ДІСТАЄМО КЛЮЧІ З КОНФІГУРАЦІЇ
        var clientId = _configuration["GoogleAuth:ClientId"];
        var clientSecret = _configuration["GoogleAuth:ClientSecret"];

        using var httpClient = new HttpClient();
        var request = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("grant_type", "refresh_token")
        });

        var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", request);

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GoogleTokenResponse>(json);
        }

        return null;
    }
}

public class GoogleTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}