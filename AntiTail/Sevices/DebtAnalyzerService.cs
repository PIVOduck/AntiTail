using AntiTail.Interfaces;
using LightMonitorBot.DTO;

public class DebtAnalyzerService : IDebtAnalyzerService
{
    private readonly IStudentClassroomService _classroomApi;

    // Інжектимо сервіс, який ходить в Google
    public DebtAnalyzerService(IStudentClassroomService classroomApi)
    {
        _classroomApi = classroomApi;
    }

    public async Task<List<SubmissionDto>> CalculateStudentDebtsAsync(string chatId)
    {
        var debts = new List<SubmissionDto>();

        // 1. Просимо API-сервіс дати просто список курсів
        var courses = await _classroomApi.GetUserCoursesAsync(chatId);

        foreach (var course in courses)
        {
            // 2. Просимо API-сервіс дати всі роботи для цього курсу
            var submissions = await _classroomApi.GetMySubmissionsAsync(course.Id, chatId);
            
            // 3. БІЗНЕС-ЛОГІКА (Фільтрація) працює ТУТ, а не в API-сервісі
            var overdueSubmissions = submissions.Where(s => 
                (s.State == "NEW" || s.State == "CREATED" || s.State == "RECLAIMED_BY_STUDENT") 
                && s.Late.HasValue && s.Late.Value).ToList();

            debts.AddRange(overdueSubmissions);
        }

        return debts;
    }
}