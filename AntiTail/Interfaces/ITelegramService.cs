namespace AntiTail.Interfaces;

public interface ITelegramService
{
    Task SendMessageAsync(string chatId, string message, CancellationToken cancellationToken = default);
}