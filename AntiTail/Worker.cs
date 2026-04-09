using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using AntiTail.Interfaces; 

namespace AntiTail; // Або ваш namespace

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly IBaseClassroomService _classroomService;

    public Worker(ILogger<Worker> logger, ITelegramBotClient botClient, IBaseClassroomService classroomService)
    {
        _logger = logger;
        _botClient = botClient;
        _classroomService = classroomService;
    }

    // ОСЬ ЦЕЙ МЕТОД ЗНИК, повертаємо його на місце:
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ReceiverOptions receiverOptions = new()
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        _logger.LogInformation("Бот запускається...");

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken
        );

        var me = await _botClient.GetMe(stoppingToken);
        _logger.LogInformation($"Бот @{me.Username} успішно підключений і слухає повідомлення!");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message) return;
        if (message.Text is not { } messageText) return;

        long chatId = message.Chat.Id;
        _logger.LogInformation($"Отримано повідомлення '{messageText}' від чату {chatId}.");

        if (messageText.StartsWith("/start"))
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: "Привіт! Я бот для спостерігання за дедлайнами. Твій Chat ID: " + chatId,
                cancellationToken: cancellationToken);
        }
        else if (messageText.StartsWith("/login")) 
        {
            // Генеруємо унікальне посилання для цього користувача
            string authUrl = _classroomService.GetAuthorizationUrl(chatId.ToString());

            var inlineKeyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("🔑 Увійти в Google Classroom", authUrl));

            await botClient.SendMessage(
                chatId: chatId,
                text: "Натисніть кнопку нижче, щоб безпечно авторизуватися через ваш університетський акаунт Google:",
                replyMarkup: inlineKeyboard, 
                cancellationToken: cancellationToken);
        }
        else
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: $"Ти написав: {messageText}. Але я поки розумію тільки /start та /login",
                cancellationToken: cancellationToken);
        }
    }

    // Ваш оригінальний обробник помилок теж на місці
    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Помилка Telegram API:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogError(ErrorMessage);
        return Task.CompletedTask;
    }
}