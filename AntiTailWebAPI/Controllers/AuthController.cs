using Microsoft.AspNetCore.Mvc;
using AntiTail.Interfaces;
using AntiTail.Models;
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

        var stateParts = state.Split(':');
        if (stateParts.Length != 2 || !long.TryParse(stateParts[1], out long chatId))
            return BadRequest("Некоректний параметр state.");

        string role = stateParts[0];

        var tokens = await _classroomService.AuthenticateUserAsync(code);
        var googleUser = await _classroomService.GetUserInfoAsync(tokens);

        // ─── ВАЛІДАЦІЯ: перевіряємо конфлікт ролей ───────────────────────────
        if (role == "teacher")
        {
            var existingAsStudent = await _registrationService.FindStudentByGoogleIdAsync(googleUser.Id);
            if (existingAsStudent != null)
            {
                await _botClient.SendMessage(chatId,
                    "❌ Цей Google-акаунт вже зареєстрований як <b>студент</b>. " +
                    "Неможливо зареєструватись як викладач з тим самим акаунтом.",
                    Telegram.Bot.Types.Enums.ParseMode.Html);
                return Content(ErrorHtml("Цей акаунт вже зареєстрований як студент."), "text/html");
            }
        }
        else if (role == "student")
        {
            var existingAsTeacher = await _registrationService.FindTeacherByGoogleIdAsync(googleUser.Id);
            if (existingAsTeacher != null)
            {
                await _botClient.SendMessage(chatId,
                    "❌ Цей Google-акаунт вже зареєстрований як <b>викладач</b>. " +
                    "Неможливо зареєструватись як студент з тим самим акаунтом.",
                    Telegram.Bot.Types.Enums.ParseMode.Html);
                return Content(ErrorHtml("Цей акаунт вже зареєстрований як викладач."), "text/html");
            }

            var existingStudentByChatId = await _registrationService.FindStudentByTelegramIdAsync(chatId);
            if (existingStudentByChatId != null && existingStudentByChatId.GoogleId != googleUser.Id)
            {
                await _botClient.SendMessage(chatId,
                    "❌ Цей Telegram-акаунт вже прив'язаний до іншого студента. " +
                    "Зверніться до адміністратора.");
                return Content(ErrorHtml("Цей Telegram вже прив'язаний до іншого студента."), "text/html");
            }
        }
        // ─────────────────────────────────────────────────────────────────────

        if (role == "teacher")
        {
            var existingTeacher = await _registrationService.FindTeacherByGoogleIdAsync(googleUser.Id);
            if (existingTeacher != null)
                await _registrationService.UpdateTeacherTokensAsync(existingTeacher.Id, tokens, googleUser.Id);
            else
                await _registrationService.RegisterTeacherAsync(chatId, googleUser, tokens);

            var session = _sessions.GetOrCreate(chatId);
            session.Step = RegistrationStep.Completed;
            session.Role = UserRole.Teacher;
            _sessions.Save(chatId, session);

            var replyMarkup = new ReplyKeyboardMarkup(new[] {
                new[] { new KeyboardButton("📚 Мої курси"), new KeyboardButton("👥 Групи") },
                new[] { new KeyboardButton("📋 Черга здачі"), new KeyboardButton("📣 Оголошення") },
                new[] { new KeyboardButton("🔄 Синхронізація з Classroom") }
            }) { ResizeKeyboard = true };

            await _botClient.SendMessage(chatId, "✅ Авторизація успішна! Ви зареєстровані як викладач.", replyMarkup: replyMarkup);
        }
        else if (role == "student")
        {
            var session = _sessions.GetOrCreate(chatId);
            session.PendingAccessToken = tokens.AccessToken;
            session.PendingRefreshToken = tokens.RefreshToken;
            session.PendingTokenExpiresAt = tokens.IssuedUtc.AddSeconds(tokens.ExpiresInSeconds ?? 3600);
            session.PendingGoogleId = googleUser.Id;
            session.PendingEmail = googleUser.Email;
            session.PendingName = googleUser.Name ?? googleUser.Email;
            session.Step = RegistrationStep.AwaitingGroupSelect;
            _sessions.Save(chatId, session);

            var groups = await _registrationService.GetAllGroupsAsync();
            var buttons = groups.Select(g =>
                InlineKeyboardButton.WithCallbackData($"{g.Cipher} ({g.CourseYear} курс)", $"group:{g.Id}")
            ).Chunk(2).Select(r => r.ToArray()).ToArray();

            await _botClient.SendMessage(chatId,
                "✅ Google акаунт успішно підключено!\n\nОстанній крок: оберіть вашу групу зі списку нижче:",
                replyMarkup: new InlineKeyboardMarkup(buttons));
        }

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

    private static string ErrorHtml(string message) => $@"
        <html>
        <head>
            <meta charset='utf-8'> <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            <style>
                body {{ font-family: 'Segoe UI', sans-serif; text-align: center; background-color: #f4f7f6; padding-top: 15vh; }}
                .card {{ background: white; padding: 40px; border-radius: 10px; box-shadow: 0 4px 8px rgba(0,0,0,0.1); display: inline-block; }}
                h1 {{ color: #e74c3c; }}
                p {{ color: #555; font-size: 18px; }}
            </style>
        </head>
        <body>
            <div class='card'>
                <h1>Помилка ❌</h1>
                <p>{message}</p>
                <p>Поверніться в Telegram-бот та спробуйте ще раз.</p>
            </div>
        </body>
        </html>";
}