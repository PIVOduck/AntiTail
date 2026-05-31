using AntiTail.Interfaces;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace AntiTail.Sevices;

public class TelegramService : ITelegramService
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<TelegramService> _logger; 
    

    // Додаємо конструктор! Сюди автоматично "прилетять" залежності з Program.cs
    public TelegramService(ITelegramBotClient botClient, ILogger<TelegramService> logger)
    {
        _botClient = botClient;
        _logger = logger;
    }

    public async Task SendMessageAsync(string chatId, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Метод телеграму вимагає ChatId у форматі long
            long parsedChatId = long.Parse(chatId);

            await _botClient.SendMessage(
                chatId: parsedChatId,
                text: message,
                cancellationToken: cancellationToken
            );

            _logger.LogInformation($"Повідомлення успішно відправлено користувачу {chatId}.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Помилка відправки повідомлення: {ex.Message}");
        }
    }
   
    

    
}