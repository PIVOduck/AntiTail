using Microsoft.AspNetCore.Mvc;
using AntiTail.Interfaces;
using AntiTail.Models; // Твій простір імен для UserRole, RegistrationStep
using AntiTail.Services;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace AntiTailWebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITelegramBotClient _botClient;
    private readonly IBaseClassroomService _classroomService;
    private readonly IRegistrationService _registrationService;
    private readonly SessionService _sessions;

    public AuthController(
        ITelegramBotClient botClient,
        IBaseClassroomService classroomService,
        IRegistrationService registrationService,
        SessionService sessions)
    {
        _botClient = botClient;
        _classroomService = classroomService;
        _registrationService = registrationService;
        _sessions = sessions;
    }

    [HttpGet("callback")]
    public async Task<IActionResult> GoogleCallback([FromQuery] string code, [FromQuery] string state)
    {
        if (string.IsNullOrEmpty(code))
            return BadRequest("Помилка авторизації: Google не повернув код доступу.");

        // 1. Розбираємо state, який ми передали раніше (формат "role:chatId")
        var stateParts = state.Split(':');
        if (stateParts.Length != 2 || !long.TryParse(stateParts[1], out long chatId))
            return BadRequest("Некоректний параметр state.");
            
        string role = stateParts[0];

        // 2. Обмінюємо code на токени та отримуємо інформацію про користувача
        // (Тобі потрібно реалізувати метод ExchangeCodeForTokensAsync у GoogleClassroomService)
        var tokens = await _classroomService.AuthenticateUserAsync(code);
        var googleUser = await _classroomService.GetUserInfoAsync(tokens);

        // 3. Логіка для ВИКЛАДАЧА
        if (role == "teacher")
        {
            var existingTeacher = await _registrationService.FindTeacherByGoogleIdAsync(googleUser.Id);
            if (existingTeacher != null)
                await _registrationService.UpdateTeacherTokensAsync(existingTeacher.Id, tokens, googleUser.Id);
            else
                await _registrationService.RegisterTeacherAsync(chatId, googleUser, tokens);

            // Оновлюємо сесію
            var session = _sessions.GetOrCreate(chatId);
            session.Step = RegistrationStep.Completed;
            session.Role = UserRole.Teacher;
            _sessions.Save(chatId, session);

            // Відправляємо успішне повідомлення та головне меню викладача
            var replyMarkup = new ReplyKeyboardMarkup(new[] { 
                new[] { new KeyboardButton("📚 Мої курси"), new KeyboardButton("👥 Групи") },
                new[] { new KeyboardButton("📋 Черга здачі"), new KeyboardButton("📣 Оголошення") },
                new[] { new KeyboardButton("🔄 Синхронізація з Classroom") } 
            }) { ResizeKeyboard = true };

            await _botClient.SendMessage(chatId, "✅ Авторизація успішна! Ви зареєстровані як викладач.", replyMarkup: replyMarkup);
        }
        
        // 4. Логіка для СТУДЕНТА
        else if (role == "student")
        {
            // Студенту ще потрібно обрати групу. Зберігаємо токени в сесії (у спільній пам'яті з ботом)
            var session = _sessions.GetOrCreate(chatId);
            session.PendingAccessToken = tokens.AccessToken;
            session.PendingRefreshToken = tokens.RefreshToken;
            session.PendingTokenExpiresAt = tokens.IssuedUtc.AddSeconds(tokens.ExpiresInSeconds ?? 3600);
            session.PendingGoogleId = googleUser.Id;
            session.PendingEmail = googleUser.Email;
            session.PendingName = googleUser.Name ?? googleUser.Email;
            
            session.Step = RegistrationStep.AwaitingGroupSelect;
            _sessions.Save(chatId, session);

            // Отримуємо список груп з БД та формуємо кнопки для Telegram
            var groups = await _registrationService.GetAllGroupsAsync();
            var buttons = groups.Select(g => 
                InlineKeyboardButton.WithCallbackData($"{g.Cipher} ({g.CourseYear} курс)", $"group:{g.Id}")
            ).Chunk(2).Select(r => r.ToArray()).ToArray();

            await _botClient.SendMessage(chatId, 
                "✅ Google акаунт успішно підключено!\n\nОстанній крок: оберіть вашу групу зі списку нижче:", 
                replyMarkup: new InlineKeyboardMarkup(buttons));
        }

        // 5. Показуємо красиву сторінку успіху в браузері
        string htmlResponse = @"
            <html>
            <head>
                <meta charset='utf-8'> <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <style>
                    body { font-family: 'Segoe UI', sans-serif; text-align: center; background-color: #f4f7f6; padding-top: 15vh; }
                    .card { background: white; padding: 40px; border-radius: 10px; box-shadow: 0 4px 8px rgba(0,0,0,0.1); display: inline-block; }
                    h1 { color: #4CAF50; }
                    p { color: #555; font-size: 18px; }
                </style>
            </head>
            <body>
                <div class='card'>
                    <h1>Успіх! 🎉</h1>
                    <p>Акаунт успішно підключено.</p>
                    <p>Можете закрити цю вкладку та повернутися в Telegram-бот.</p>
                </div>
            </body>
            </html>";

        return Content(htmlResponse, "text/html");
    }
}