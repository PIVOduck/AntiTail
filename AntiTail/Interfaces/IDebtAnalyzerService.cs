using LightMonitorBot.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AntiTail.Interfaces;

public interface IDebtAnalyzerService
{
    Task<List<SubmissionDto>> CalculateStudentDebtsAsync(string chatId);
}