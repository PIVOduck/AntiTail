using Microsoft.AspNetCore.Mvc;
using AntiTail.Interfaces;

namespace AntiTailWebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITelegramService _telegramService;
    // Пізніше сюди додамо IBaseClassroomService для обміну коду на токени

    // Контролер приймає сервіс телеграму, який ми створили раніше
    public AuthController(ITelegramService telegramService)
    {
        _telegramService = telegramService;
    }

    // Цей метод відповідає за маршрут: /api/auth/callback
    [HttpGet("callback")]
    public async Task<IActionResult> GoogleCallback([FromQuery] string code, [FromQuery] string state)
    {
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest("Помилка авторизації: Google не повернув код доступу.");
        }

        // У змінній state ми раніше заховали Chat ID користувача!
        string chatId = state;

        // ВАЖЛИВО: Пізніше тут буде код для обміну 'code' на справжні токени 
        // та збереження їх у базу даних (SQLite/PostgreSQL).

        // Відправляємо повідомлення студенту прямо в Telegram
        await _telegramService.SendMessageAsync(chatId, "✅ Авторизація пройшла успішно! Тепер я маю доступ до твоїх курсів Google Classroom.");

        // Генеруємо красиву HTML-сторінку, яку побачить користувач у браузері
        string htmlResponse = @"
            <html>
            <head>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <style>
                    body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; text-align: center; background-color: #f4f7f6; padding-top: 15vh; }
                    .card { background: white; padding: 40px; border-radius: 10px; box-shadow: 0 4px 8px rgba(0,0,0,0.1); display: inline-block; }
                    h1 { color: #4CAF50; }
                    p { color: #555; font-size: 18px; }
                </style>
            </head>
            <body>
                <div class='card'>
                    <h1>Успіх! 🎉</h1>
                    <p>Ваш акаунт Google успішно підключено.</p>
                    <p>Можете закрити цю сторінку та повернутися в Telegram.</p>
                </div>
            </body>
            </html>";

        return Content(htmlResponse, "text/html");
    }
}