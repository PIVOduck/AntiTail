using AntiTail.Interfaces;
using AntiTail.Models;
using AntiTail.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        _logger = logger;
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _sessions = sessions;
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
                await bot.SendMessage(chatId, "Дію скасовано. Натисніть /start.", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
                return;
            }

            // 1. Обробка етапу вибору групи студентом
            if (session.Step == RegistrationStep.AwaitingGroupSelect)
            {
                await HandleGroupSelectionInputAsync(bot, chatId, text, session, ct);
                return;
            }

            // 2. Обробка створення групи Адміном (Крок 1: Назва)
            if (session.Step == RegistrationStep.AwaitingNewGroupCipher)
            {
                session.PendingName = text.Trim(); 
                session.Step = RegistrationStep.AwaitingNewGroupYear;
                _sessions.Save(chatId, session);
                await bot.SendMessage(chatId, "Тепер введіть курс (число від 1 до 6):", cancellationToken: ct);
                return;
            }

            // 3. Обробка створення групи Адміном (Крок 2: Курс)
            if (session.Step == RegistrationStep.AwaitingNewGroupYear)
            {
                if (int.TryParse(text.Trim(), out int year))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
                    
                    var newGroup = await adminService.CreateGroupAsync(session.PendingName, year);
                    
                    session.Step = RegistrationStep.Completed;
                    _sessions.Save(chatId, session);
                    
                    await bot.SendMessage(chatId, 
                        $"✅ Групу {newGroup.Cipher} успішно створено!\nКод для запрошення студентів: {newGroup.InviteLink}", 
                        replyMarkup: AdminMainMenu(), cancellationToken: ct);
                }
                else
                {
                    await bot.SendMessage(chatId, "Будь ласка, введіть коректне число для курсу:", cancellationToken: ct);
                }
                return;
            }

            // 4. ОБРОБКА КНОПОК ГОЛОВНОГО МЕНЮ
            if (session.Step == RegistrationStep.Completed)
            {
                switch (text)
                {
                    // --- Спільні кнопки ---
                    case "📚 Мої курси":
                        await bot.SendMessage(chatId, "Функція 'Мої курси' в розробці 🛠", cancellationToken: ct);
                        break;

                    // --- Кнопки Студента ---
                    case "⏰ Дедлайни":
                        await bot.SendMessage(chatId, "Функція 'Дедлайни' в розробці 🛠", cancellationToken: ct);
                        break;
                    case "📊 Заборгованість":
                        await bot.SendMessage(chatId, "Функція 'Заборгованість' в розробці 🛠", cancellationToken: ct);
                        break;
                    case "✉️ Написати викладачу":
                        await bot.SendMessage(chatId, "Функція 'Написати викладачу' в розробці 🛠", cancellationToken: ct);
                        break;

                    // --- Кнопки Викладача ---
                    case "👥 Групи":
                        await ShowGroupsListAsync(bot, chatId, ct);
                        break;
                    case "📋 Черга здачі":
                        await bot.SendMessage(chatId, "Функція 'Черга здачі' в розробці 🛠", cancellationToken: ct);
                        break;
                    case "📣 Оголошення":
                        await bot.SendMessage(chatId, "Функція 'Оголошення' в розробці 🛠", cancellationToken: ct);
                        break;
                    case "🔄 Синхронізація з Classroom":
                        await bot.SendMessage(chatId, "🔄 Починаю синхронізацію з Google Classroom... (Заглушка)", cancellationToken: ct);
                        break;

                    // --- Кнопки Адміна ---
                    case "📋 Список груп":
                        await ShowGroupsListAsync(bot, chatId, ct);
                        break;
                    case "➕ Створити групу":
                        if (session.Role == UserRole.Admin)
                        {
                            session.Step = RegistrationStep.AwaitingNewGroupCipher;
                            _sessions.Save(chatId, session);
                            await bot.SendMessage(chatId, "Введіть шифр нової групи (наприклад, КН-21):\n\nАбо введіть /cancel щоб скасувати.", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
                        }
                        break;
                    case "❌ Видалити групу":
                        await bot.SendMessage(chatId, "Функція 'Видалити групу' в розробці 🛠", cancellationToken: ct);
                        break;

                    // --- Невідома команда ---
                    default:
                        await bot.SendMessage(chatId, "Невідома команда. Будь ласка, оберіть дію в меню 👇", cancellationToken: ct);
                        break;
                    case "👨‍🏫 Викладачі":
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
                            var teachers = await adminService.GetAllTeachersAsync();
                            
                            if (!teachers.Any()) 
                            {
                                await bot.SendMessage(chatId, "Жодного викладача ще не зареєстровано.", cancellationToken: ct);
                            } 
                            else 
                            {
                                var msg = "👨‍🏫 Зареєстровані викладачі:\n\n";
                                foreach (var t in teachers) 
                                {
                                    // Перевіряємо, чи є викладач куратором якихось груп
                                    string curatedGroups = t.Group != null && t.Group.Any() 
                                        ? string.Join(", ", t.Group.Select(g => g.Cipher)) 
                                        : "немає";
                                        
                                    msg += $"👤 {t.FullName}\n📧 {t.CorporateEmail}\n🛡 Куратор груп: {curatedGroups}\n\n";
                                }
                                await bot.SendMessage(chatId, msg, cancellationToken: ct);
                            }
                        }
                        break;

                    case "👨‍🎓 Студенти":
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                            var groups = await reg.GetAllGroupsAsync();
                            
                            if (!groups.Any()) 
                            {
                                await bot.SendMessage(chatId, "В базі ще немає груп.", cancellationToken: ct);
                            } 
                            else 
                            {
                                // Генеруємо інлайн-кнопки з існуючих груп
                                var buttons = groups.Select(g => 
                                    InlineKeyboardButton.WithCallbackData(g.Cipher, $"view_students:{g.Id}")
                                ).Chunk(2).Select(r => r.ToArray()).ToArray();
                                
                                await bot.SendMessage(chatId, "Оберіть групу для перегляду студентів:", 
                                    replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
                            }
                        }
                        break;
                }
                return; 
            }

            // Якщо користувач ще не завершив реєстрацію і пише щось незрозуміле
            await bot.SendMessage(chatId, "Я розумію /start та /cancel.", cancellationToken: ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "Помилка обробки оновлення"); }
    }

    private async Task HandleStartAsync(ITelegramBotClient bot, long chatId, UserSession session, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        
        // Перевіряємо чи це Адмін
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var admin = await adminService.FindAdminByTelegramIdAsync(chatId);
        if (admin is not null)
        {
            session.Step = RegistrationStep.Completed; session.Role = UserRole.Admin;
            _sessions.Save(chatId, session);
            await bot.SendMessage(chatId,
                $"👑 Вітаю, {admin.FullName}! Ви авторизовані як Адміністратор.",
                replyMarkup: AdminMainMenu(), cancellationToken: ct);
            return;
        }

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
            "👋 Вітаємо в AntiTail Bot!\n\nСистема для відстеження дедлайнів та академічної заборгованості.\n\nОберіть вашу роль:",
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
        {
            await HandleGroupSelectedAsync(bot, chatId, gid, session, ct);
            return;
        }
        
        // --- НОВИЙ КОД ДЛЯ ПЕРЕГЛЯДУ СТУДЕНТІВ ---
        if (data.StartsWith("view_students:") && int.TryParse(data.Replace("view_students:", ""), out int viewGroupId))
        {
            using var scope = _scopeFactory.CreateScope();
            var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();
            
            var students = await adminService.GetStudentsByGroupIdAsync(viewGroupId);
            
            if (!students.Any()) 
            {
                await bot.SendMessage(chatId, "У цій групі ще немає зареєстрованих студентів.", cancellationToken: ct);
            } 
            else 
            {
                var msg = $"👨‍🎓 Список студентів групи:\n\n";
                for (int i = 0; i < students.Count; i++) 
                {
                    msg += $"{i + 1}. {students[i].FullName} ({students[i].CorporateEmail})\n";
                }
                await bot.SendMessage(chatId, msg, cancellationToken: ct);
            }
        }
    }

    private async Task HandleRoleSelectedAsync(ITelegramBotClient bot, long chatId, UserRole role, UserSession session, CancellationToken ct)
    {
        session.Role = role;
        session.Step = RegistrationStep.AwaitingGoogleAuth;
        _sessions.Save(chatId, session);

        using var scope = _scopeFactory.CreateScope();
        var classroomService = scope.ServiceProvider.GetRequiredService<IBaseClassroomService>();

        string state  = $"{(role == UserRole.Teacher ? "teacher" : "student")}:{chatId}";
        string authUrl = classroomService.GetAuthorizationUrl(state);
        string label   = role == UserRole.Teacher ? "Викладач 👨‍🏫" : "Студент 👨‍🎓";

        await bot.SendMessage(chatId,
            $"Роль обрано: {label}\n\n" +
            "Натисніть кнопку нижче для авторизації через Google акаунт коледжу.\n" +
            "⚠️ Використовуйте лише корпоративну пошту коледжу.",
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("🔑 Увійти через Google", authUrl)),
            cancellationToken: ct);
    }

    private async Task HandleGroupSelectionInputAsync(ITelegramBotClient bot, long chatId, string text, UserSession session, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var group = await reg.GetGroupByInviteTokenAsync(text.Trim());
        if (group is null)
        {
            await bot.SendMessage(chatId, "Групу не знайдено. Спробуйте ще раз або оберіть зі списку:", cancellationToken: ct);
            await ShowGroupListForSelectionAsync(bot, chatId, reg, ct);
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

    // Метод для відображення списку груп для викладача та адміна
    private async Task ShowGroupsListAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var groups = await reg.GetAllGroupsAsync();

        if (!groups.Any())
        {
            await bot.SendMessage(chatId, "В базі поки немає жодної групи.", cancellationToken: ct);
        }
        else
        {
            var groupsList = string.Join("\n", groups.Select(g => $"📌 {g.Cipher} ({g.CourseYear} курс) — Код: {g.InviteLink}"));
            await bot.SendMessage(chatId, $"📚 Список існуючих груп:\n\n{groupsList}", cancellationToken: ct);
        }
    }

    // Метод для відображення груп кнопками (при реєстрації студента)
    private async Task ShowGroupListForSelectionAsync(ITelegramBotClient bot, long chatId, IRegistrationService reg, CancellationToken ct)
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

    private static ReplyKeyboardMarkup AdminMainMenu() =>
        new(new[] { 
                new[] { new KeyboardButton("➕ Створити групу"), new KeyboardButton("❌ Видалити групу") },
                new[] { new KeyboardButton("📋 Список груп"), new KeyboardButton("👨‍🏫 Викладачі") },
                new[] { new KeyboardButton("👨‍🎓 Студенти") } 
            })
            { ResizeKeyboard = true };

    private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        _logger.LogError("Polling error: {Msg}", exception is ApiRequestException api ? $"[{api.ErrorCode}] {api.Message}" : exception.ToString());
        return Task.CompletedTask;
    }
}