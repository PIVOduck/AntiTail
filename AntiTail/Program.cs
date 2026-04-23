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
builder.Services.AddMemoryCache();
// 1. БАЗА ДАНИХ (Вона автоматично стає Scoped)
builder.Services.AddDbContext<AppDBContext>(/* твої налаштування */);

// 2. СЕРВІСИ РОБОТИ З БД (МАЮТЬ БУТИ SCOPED)
// ВИПРАВЛЕННЯ 1: Зміни AddSingleton на AddScoped
builder.Services.AddScoped<IRegistrationService, RegistrationService>();

// 3. СЕРВІСИ ДЛЯ БОТА
// ВИПРАВЛЕННЯ 2: Додаємо відсутній SessionService як Singleton
builder.Services.AddSingleton<SessionService>();

// Твій Google Classroom Service (краще теж Scoped або Transient)
builder.Services.AddScoped<IBaseClassroomService, GoogleClassroomService>();

// Telegram Bot
builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));

// Твій Worker (Він завжди HostedService / Singleton)
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();