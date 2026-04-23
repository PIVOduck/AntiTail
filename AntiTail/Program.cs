using AntiTail;
using AntiTail.DBContext;
using LightMonitorBot;
using AntiTail.Interfaces;
using AntiTail.Services;
using AntiTail.Sevices;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

var ConectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDBContext>(options => options.UseMySql(ConectionString, ServerVersion.AutoDetect(ConectionString)));
// 1. Читаємо токен з appsettings.json
var botToken = builder.Configuration.GetSection("BotConfig:BotToken").Value;

if (string.IsNullOrEmpty(botToken) || botToken == "Insert your token here")
{
    throw new ArgumentNullException("BotConfig:BotToken", "Токен бота не знайдено! Перевір appsettings.Development.json");
}

// 2. Реєструємо клієнт Telegram як Singleton (один на весь додаток)
builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));

builder.Services.AddSingleton<IBaseClassroomService, GoogleClassroomService>();

// 3. Реєструємо твій власний сервіс
builder.Services.AddSingleton<ITelegramService, TelegramService>();

// 4. Реєструємо фоновий процес (Worker)
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();