using AntiTail.Interfaces;
using AntiTail.Models;
using AntiTail.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AntiTail;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionService _sessions;

    public Worker(
        ILogger<Worker> logger,
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        SessionService sessions)
    {
        _logger           = logger;
        _botClient        = botClient;
        _scopeFactory     = scopeFactory;
        _sessions         = sessions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
        _logger.LogInformation("Бот запускається...");
        _botClient.StartReceiving(HandleUpdateAsync, HandlePollingErrorAsync, receiverOptions, stoppingToken);
        var me = await _botClient.GetMe(stoppingToken);
        _logger.LogInformation("Бот @{Username} підключений!", me.Username);
        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(1000, stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is { } cbq) { await HandleCallbackQueryAsync(bot, cbq, ct); return; }
            if (update.Message is not { } message) return;
            if (message.Text is not { } text) return;

            long chatId = message.Chat.Id;
            _logger.LogInformation("Повідомлення '{Text}' від {ChatId}", text, chatId);
            var session = _sessions.GetOrCreate(chatId);

            if (text == "/start") { await HandleStartAsync(bot, chatId, session, ct); return; }
            if (text == "/cancel")
            {
                _sessions.Remove(chatId);
                await bot.SendMessage(chatId, "Реєстрацію скасовано. Натисніть /start.", cancellationToken: ct);
                return;
            }

            if (session.Step == RegistrationStep.AwaitingGroupSelect)
                await HandleGroupSelectionInputAsync(bot, chatId, text, session, ct);
            else
                await bot.SendMessage(chatId, "Я розумію /start та /cancel.", cancellationToken: ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "Помилка обробки оновлення"); }
    }

    private async Task HandleStartAsync(ITelegramBotClient bot, long chatId, UserSession session, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();

        var teacher = await reg.FindTeacherByTelegramIdAsync(chatId);
        if (teacher is not null)
        {
            session.Step = RegistrationStep.Completed; session.Role = UserRole.Teacher;
            _sessions.Save(chatId, session);
            await bot.SendMessage(chatId,
                $"👋 З поверненням, {teacher.FullName}!\nВи авторизовані як Викладач.",
                replyMarkup: TeacherMainMenu(), cancellationToken: ct);
            return;
        }

        var student = await reg.FindStudentByTelegramIdAsync(chatId);
        if (student is not null)
        {
            session.Step = RegistrationStep.Completed; session.Role = UserRole.Student;
            _sessions.Save(chatId, session);
            await bot.SendMessage(chatId,
                $"👋 З поверненням, {student.FullName}!\nВи авторизовані як Студент.",
                replyMarkup: StudentMainMenu(), cancellationToken: ct);
            return;
        }

        session.Step = RegistrationStep.AwaitingRole;
        _sessions.Save(chatId, session);
        await bot.SendMessage(chatId,
            "👋 Вітаємо в AntiTil Bot!\n\nСистема для відстеження дедлайнів та академічної заборгованості.\n\nОберіть вашу роль:",
            replyMarkup: RoleSelectionKeyboard(), cancellationToken: ct);
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery cbq, CancellationToken ct)
    {
        long chatId = cbq.Message!.Chat.Id;
        string data = cbq.Data ?? "";
        await bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ct);
        var session = _sessions.GetOrCreate(chatId);

        if (data == "role:student") { await HandleRoleSelectedAsync(bot, chatId, UserRole.Student, session, ct); return; }
        if (data == "role:teacher") { await HandleRoleSelectedAsync(bot, chatId, UserRole.Teacher, session, ct); return; }
        if (data.StartsWith("group:") && int.TryParse(data.Replace("group:", ""), out int gid))
            await HandleGroupSelectedAsync(bot, chatId, gid, session, ct);
    }

    private async Task HandleRoleSelectedAsync(ITelegramBotClient bot, long chatId, UserRole role, UserSession session, CancellationToken ct)
    {
        session.Role = role;
        session.Step = RegistrationStep.AwaitingGoogleAuth;
        _sessions.Save(chatId, session);

        // СТВОРЮЄМО СКОУП ТА ДІСТАЄМО СЕРВІС ТУТ
        using var scope = _scopeFactory.CreateScope();
        var classroomService = scope.ServiceProvider.GetRequiredService<IBaseClassroomService>();

        string state  = $"{(role == UserRole.Teacher ? "teacher" : "student")}:{chatId}";
    
        // Використовуємо локальний classroomService замість глобального _classroomService
        string authUrl = classroomService.GetAuthorizationUrl(state);
        string label   = role == UserRole.Teacher ? "Викладач 👨‍🏫" : "Студент 👨‍🎓";

        await bot.SendMessage(chatId,
            $"Роль обрано: {label}\n\n" +
            "Натисніть кнопку нижче для авторизації через Google акаунт коледжу.\n" +
            "⚠️ Використовуйте лише корпоративну пошту коледжу.",
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("🔑 Увійти через Google", authUrl)),
            cancellationToken: ct);
        //todo продовження реєстрації - група стедуента і вся інша інфа
    }

    private async Task HandleGroupSelectionInputAsync(ITelegramBotClient bot, long chatId, string text, UserSession session, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var group = await reg.GetGroupByInviteTokenAsync(text.Trim());
        if (group is null)
        {
            await bot.SendMessage(chatId, "Групу не знайдено. Спробуйте ще раз або оберіть зі списку:", cancellationToken: ct);
            await ShowGroupListAsync(bot, chatId, reg, ct);
            return;
        }
        await CompleteStudentRegistrationAsync(bot, chatId, group.Id, session, ct);
    }

    private async Task HandleGroupSelectedAsync(ITelegramBotClient bot, long chatId, int groupId, UserSession session, CancellationToken ct)
        => await CompleteStudentRegistrationAsync(bot, chatId, groupId, session, ct);

    private async Task CompleteStudentRegistrationAsync(ITelegramBotClient bot, long chatId, int groupId, UserSession session, CancellationToken ct)
    {
        if (session.PendingAccessToken is null)
        {
            await bot.SendMessage(chatId, "Сесія закінчилась. Натисніть /start.", cancellationToken: ct);
            _sessions.Remove(chatId);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();

        var tokens = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            AccessToken      = session.PendingAccessToken,
            RefreshToken     = session.PendingRefreshToken,
            ExpiresInSeconds = session.PendingTokenExpiresAt.HasValue
                ? (long?)(session.PendingTokenExpiresAt.Value - DateTime.UtcNow).TotalSeconds : 3600,
            IssuedUtc = DateTime.UtcNow
        };
        var googleUser = new Google.Apis.Oauth2.v2.Data.Userinfo
        {
            Id = session.PendingGoogleId, Email = session.PendingEmail, Name = session.PendingName
        };

        var student = await reg.RegisterStudentAsync(chatId, googleUser, tokens, groupId);
        session.Step = RegistrationStep.Completed; session.Role = UserRole.Student;
        _sessions.Save(chatId, session);

        await bot.SendMessage(chatId,
            $"🎉 Реєстрацію завершено!\n\nІм'я: {student.FullName}\nEmail: {student.CorporateEmail}\n\n" +
            "Тепер ви можете переглядати свої дедлайни та заборгованість.",
            replyMarkup: StudentMainMenu(), cancellationToken: ct);
    }

    private async Task ShowGroupListAsync(ITelegramBotClient bot, long chatId, IRegistrationService reg, CancellationToken ct)
    {
        var groups = await reg.GetAllGroupsAsync();
        if (!groups.Any()) { await bot.SendMessage(chatId, "В системі ще немає груп. Зверніться до куратора.", cancellationToken: ct); return; }
        var buttons = groups
            .Select(g => InlineKeyboardButton.WithCallbackData($"{g.Cipher} ({g.CourseYear} курс)", $"group:{g.Id}"))
            .Chunk(2).Select(r => r.ToArray()).ToArray();
        await bot.SendMessage(chatId, "Оберіть свою групу:", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private static InlineKeyboardMarkup RoleSelectionKeyboard() =>
        new(new[] { new[] { InlineKeyboardButton.WithCallbackData("👨‍🎓 Студент", "role:student") },
                    new[] { InlineKeyboardButton.WithCallbackData("👨‍🏫 Викладач", "role:teacher") } });

    private static ReplyKeyboardMarkup StudentMainMenu() =>
        new(new[] { new[] { new KeyboardButton("📚 Мої курси"), new KeyboardButton("⏰ Дедлайни") },
                    new[] { new KeyboardButton("📊 Заборгованість"), new KeyboardButton("✉️ Написати викладачу") } })
        { ResizeKeyboard = true };

    private static ReplyKeyboardMarkup TeacherMainMenu() =>
        new(new[] { new[] { new KeyboardButton("📚 Мої курси"), new KeyboardButton("👥 Групи") },
                    new[] { new KeyboardButton("📋 Черга здачі"), new KeyboardButton("📣 Оголошення") },
                    new[] { new KeyboardButton("🔄 Синхронізація з Classroom") } })
        { ResizeKeyboard = true };

    private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        _logger.LogError("Polling error: {Msg}", exception is ApiRequestException api ? $"[{api.ErrorCode}] {api.Message}" : exception.ToString());
        return Task.CompletedTask;
    }
}