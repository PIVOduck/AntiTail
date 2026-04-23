using LightMonitorBot.DTO;

namespace AntiTail.Interfaces;

public interface IDebtAnalyzerService
{

    public Task<List<SubmissionDto>> CalculateStudentDebtsAsync(string chatId);
}