using AntiTail.Interfaces;
using AntiTail.Models;
using AntiTail.Services;
using AntiTail.Entities;
using AntiTail.Entitys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.EntityFrameworkCore;

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
            if (update.CallbackQuery is { } cbq)
            {
                await HandleCallbackQueryAsync(bot, cbq, ct);
                return;
            }

            if (update.Message is not { } message) return;
            if (message.Text is not { } text) return;

            long chatId = message.Chat.Id;
            _logger.LogInformation("Повідомлення '{Text}' від {ChatId}", text, chatId);
            var session = _sessions.GetOrCreate(chatId);

            // Перевірка адміна при кожному запиті
            if (session.Role == UserRole.Admin)
            {
                using var scopeCheck = _scopeFactory.CreateScope();
                var adminCheck = scopeCheck.ServiceProvider.GetRequiredService<IAdminService>();
                if (await adminCheck.FindAdminByTelegramIdAsync(chatId) == null)
                {
                    _sessions.Remove(chatId);
                    await bot.SendMessage(chatId,
                        "❌ Доступ заборонено. Вас було видалено зі списку адміністраторів.",
                        replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
                    return;
                }
            }

            // /start
            if (text == "/start")
            {
                await HandleStartAsync(bot, chatId, session, ct);
                return;
            }

            // /cancel
            if (text == "/cancel")
            {
                _sessions.Remove(chatId);
                await bot.SendMessage(chatId, "Дію скасовано. Натисніть /start.",
                    replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
                return;
            }

            // ── FSM КРОКИ ──────────────────────────────────────────────

            // Студент пише повідомлення викладачу
            if (session.Step == RegistrationStep.StudentAwaitingTeacherMessage)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();

                var student = await db.Students
                    .Include(s => s.Group)
                    .FirstOrDefaultAsync(s => s.TelegramChatId == chatId, ct);

                Teacher? chosenTeacher = null;
                if (session.PendingTeacherId.HasValue)
                    chosenTeacher = await db.Teachers.FindAsync(new object[] { session.PendingTeacherId.Value }, ct);

                if (chosenTeacher != null && chosenTeacher.TelegramChatId != 0)
                {
                    string groupInfo = student?.Group != null ? $" (Група {student.Group.Cipher})" : "";
                    string msgToTeacher =
                        $"📩 <b>Нове повідомлення від студента!</b>\n" +
                        $"👤 {student?.FullName ?? "Студент"}{groupInfo}\n" +
                        $"💬: {text}";
                    try
                    {
                        await bot.SendMessage(chosenTeacher.TelegramChatId, msgToTeacher,
                            parseMode: ParseMode.Html, cancellationToken: ct);
                        await bot.SendMessage(chatId,
                            $"✅ Повідомлення надіслано викладачу {chosenTeacher.FullName}!",
                            replyMarkup: StudentMainMenu(), cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Не вдалося надіслати повідомлення викладачу {TeacherId}",
                            chosenTeacher.TelegramChatId);
                        await bot.SendMessage(chatId,
                            "❌ Не вдалося доставити повідомлення. Можливо, викладач ще не запустив бота.",
                            replyMarkup: StudentMainMenu(), cancellationToken: ct);
                    }
                }
                else
                {
                    await bot.SendMessage(chatId,
                        "❌ Викладача не знайдено або він не зареєстрований у боті.",
                        replyMarkup: StudentMainMenu(), cancellationToken: ct);
                }

                session.Step = RegistrationStep.Completed;
                session.PendingTeacherId = null;
                _sessions.Save(chatId, session);
                return;
            }

            // Студент вибирає групу
            if (session.Step == RegistrationStep.AwaitingGroupSelect)
            {
                await HandleGroupSelectionInputAsync(bot, chatId, text, session, ct);
                return;
            }

            // Адмін: ввів шифр нової групи
            if (session.Step == RegistrationStep.AwaitingNewGroupCipher)
            {
                session.PendingName = text.Trim();
                session.Step = RegistrationStep.AwaitingNewGroupYear;
                _sessions.Save(chatId, session);
                await bot.SendMessage(chatId, "Тепер введіть курс (число від 1 до 6):", cancellationToken: ct);
                return;
            }

            // Адмін: ввів курс нової групи
            if (session.Step == RegistrationStep.AwaitingNewGroupYear)
            {
                if (int.TryParse(text.Trim(), out int year))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var adminSvc = scope.ServiceProvider.GetRequiredService<IAdminService>();
                    var newGroup = await adminSvc.CreateGroupAsync(session.PendingName, year);

                    session.Step = RegistrationStep.Completed;
                    _sessions.Save(chatId, session);

                    await bot.SendMessage(chatId,
                        $"✅ Групу {newGroup.Cipher} успішно створено!\nКод для запрошення студентів: {newGroup.InviteLink}",
                        replyMarkup: AdminMainMenu(), cancellationToken: ct);
                }
                else
                {
                    await bot.SendMessage(chatId, "Будь ласка, введіть коректне число для курсу:",
                        cancellationToken: ct);
                }
                return;
            }

            // Викладач: вводить оцінку
            if (session.Step == RegistrationStep.TeacherAwaitingGradeValue)
            {
                if (double.TryParse(text.Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double grade))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var regService = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                    var classroomSvc = scope.ServiceProvider.GetRequiredService<ITeacherClassroomService>();
                    var config = scope.ServiceProvider
                        .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                    var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();

                    var teacher = await regService.FindTeacherByTelegramIdAsync(chatId);
                    await RefreshTeacherTokenIfNeededAsync(teacher, config, db);

                    if (teacher?.AccessToken == null)
                    {
                        await bot.SendMessage(chatId,
                            "❌ Помилка авторизації (немає токена). Спробуйте /start.",
                            cancellationToken: ct);
                        return;
                    }

                    try
                    {
                        await classroomSvc.GradeStudentSubmissionAsync(
                            session.PendingCourseId,
                            session.PendingAssignmentId,
                            session.PendingSubmissionId,
                            grade,
                            teacher.AccessToken);

                        session.Step = RegistrationStep.Completed;
                        session.PendingCourseId = null;
                        session.PendingAssignmentId = null;
                        session.PendingSubmissionId = null;
                        _sessions.Save(chatId, session);

                        await bot.SendMessage(chatId,
                            $"✅ Оцінку {grade} успішно виставлено! Студент отримає сповіщення.",
                            replyMarkup: TeacherMainMenu(), cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Помилка при виставленні оцінки в Classroom");
                        await bot.SendMessage(chatId,
                            $"❌ Не вдалося відправити оцінку в Google Classroom. Помилка: {ex.Message}",
                            replyMarkup: TeacherMainMenu(), cancellationToken: ct);
                    }
                }
                else
                {
                    await bot.SendMessage(chatId,
                        "❌ Будь ласка, введіть коректне число (наприклад, 95 або 100):",
                        cancellationToken: ct);
                }
                return;
            }

            // Викладач: вводить текст оголошення
            if (session.Step == RegistrationStep.TeacherAwaitingAnnouncementText)
            {
                using var scope = _scopeFactory.CreateScope();
                var classroomSvc = scope.ServiceProvider.GetRequiredService<ITeacherClassroomService>();
                var regService = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                var config = scope.ServiceProvider
                    .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();

                var teacher = await regService.FindTeacherByTelegramIdAsync(chatId);
                await RefreshTeacherTokenIfNeededAsync(teacher, config, db);

                if (teacher?.AccessToken == null)
                {
                    await bot.SendMessage(chatId, "❌ Помилка авторизації. Спробуйте /start.",
                        replyMarkup: TeacherMainMenu(), cancellationToken: ct);
                    return;
                }

                try
                {
                    await classroomSvc.CreateAnnouncementAsync(session.PendingCourseId, text, teacher.AccessToken);
                    session.Step = RegistrationStep.Completed;
                    session.PendingCourseId = null;
                    _sessions.Save(chatId, session);
                    await bot.SendMessage(chatId,
                        "✅ Оголошення успішно опубліковано в Google Classroom!",
                        replyMarkup: TeacherMainMenu(), cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Помилка при створенні оголошення");
                    await bot.SendMessage(chatId,
                        $"❌ Не вдалося опублікувати оголошення: {ex.Message}",
                        replyMarkup: TeacherMainMenu(), cancellationToken: ct);
                }
                return;
            }

            // ── ГОЛОВНЕ МЕНЮ ────────────────────────────────────────────

            if (session.Step == RegistrationStep.Completed)
            {
                switch (text)
                {
                    // ── СТУДЕНТ ──────────────────────────────────────────

                    case "📚 Мої курси" when session.Role == UserRole.Student:
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                        var clSvc = scope.ServiceProvider.GetRequiredService<IStudentClassroomService>();
                        var student = await regSvc.FindStudentByTelegramIdAsync(chatId);
                        var courses = await clSvc.GetUserCoursesAsync(student.AccessToken);

                        if (!courses.Any())
                            await bot.SendMessage(chatId,
                                "У вас поки немає активних курсів у Google Classroom.", cancellationToken: ct);
                        else
                        {
                            string msg = "📚 Ваші курси:\n\n";
                            foreach (var c in courses) msg += $"🔹 {c.Name}\n";
                            await bot.SendMessage(chatId, msg, cancellationToken: ct);
                        }
                        break;
                    }

                    case "⏰ Дедлайни" when session.Role == UserRole.Student:
                    {
                        await bot.SendMessage(chatId,
                            "⏳ Збираю інформацію про ваші активні завдання...", cancellationToken: ct);
                        using var scope = _scopeFactory.CreateScope();
                        var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                        var clSvc = scope.ServiceProvider.GetRequiredService<IStudentClassroomService>();
                        var student = await regSvc.FindStudentByTelegramIdAsync(chatId);
                        var courses = await clSvc.GetUserCoursesAsync(student.AccessToken);
                        var upcomingTasks = new List<string>();

                        foreach (var course in courses)
                        {
                            var submissions = await clSvc.GetMySubmissionsAsync(course.Id, student.AccessToken);
                            var active = submissions
                                .Where(s => s.State != "TURNED_IN" && s.State != "RETURNED" && s.Late == false)
                                .ToList();
                            foreach (var sub in active)
                                upcomingTasks.Add(
                                    $"📌 <b>{course.Name}</b>\n   🔹 {sub.Title}");
                        }

                        if (!upcomingTasks.Any())
                            await bot.SendMessage(chatId,
                                "🎉 У вас немає активних незданих завдань! Ви все зробили.",
                                cancellationToken: ct);
                        else
                            await bot.SendMessage(chatId,
                                "⏰ Ваші активні завдання (ще є час здати):\n\n" +
                                string.Join("\n\n", upcomingTasks),
                                parseMode: ParseMode.Html, cancellationToken: ct);
                        break;
                    }

                    case "📊 Заборгованість" when session.Role == UserRole.Student:
                    {
                        await bot.SendMessage(chatId,
                            "⏳ Перевіряю ваші борги в Google Classroom... Зачекайте.",
                            cancellationToken: ct);
                        using var scope = _scopeFactory.CreateScope();
                        var debtSvc = scope.ServiceProvider.GetRequiredService<IDebtAnalyzerService>();
                        var debts = await debtSvc.CalculateStudentDebtsAsync(chatId.ToString());

                        if (!debts.Any())
                            await bot.SendMessage(chatId, "🎉 У вас немає прострочених завдань!",
                                cancellationToken: ct);
                        else
                        {
                            string msg = "📊 Ваші заборгованості:\n\n";
                            for (int i = 0; i < debts.Count; i++)
                            {
                                string coursePart = !string.IsNullOrEmpty(debts[i].CourseName)
                                    ? $"<b>{debts[i].CourseName}</b>\n   "
                                    : "";
                                msg += $"{i + 1}. {coursePart}🔹 {debts[i].Title}\n\n";
                            }
                            await bot.SendMessage(chatId, msg,
                                parseMode: ParseMode.Html, cancellationToken: ct);
                        }
                        break;
                    }

                    case "✉️ Написати викладачу" when session.Role == UserRole.Student:
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var adminSvc = scope.ServiceProvider.GetRequiredService<IAdminService>();
                        var allTeachers = await adminSvc.GetAllTeachersAsync();

                        var registeredTeachers = allTeachers.Where(t => t.TelegramChatId != 0).ToList();
                        if (!registeredTeachers.Any())
                        {
                            await bot.SendMessage(chatId,
                                "Жоден викладач ще не зареєстрував бота. Спробуйте пізніше.",
                                cancellationToken: ct);
                            break;
                        }

                        var buttons = registeredTeachers
                            .Select(t => InlineKeyboardButton.WithCallbackData(
                                $"👤 {t.FullName}", $"msg_teacher:{t.Id}"))
                            .Chunk(1).Select(r => r.ToArray()).ToArray();

                        await bot.SendMessage(chatId, "Оберіть викладача, якому хочете написати:",
                            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
                        break;
                    }

                    case "📣 Оголошення" when session.Role == UserRole.Student:
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                        var student = await regSvc.FindStudentByTelegramIdAsync(chatId);

                        if (student?.AccessToken == null)
                        {
                            await bot.SendMessage(chatId, "❌ Помилка авторизації. Спробуйте /start.",
                                cancellationToken: ct);
                            break;
                        }

                        var clSvc = scope.ServiceProvider.GetRequiredService<IStudentClassroomService>();
                        var courses = await clSvc.GetUserCoursesAsync(student.AccessToken);

                        if (!courses.Any())
                        {
                            await bot.SendMessage(chatId, "У вас немає активних курсів.",
                                cancellationToken: ct);
                            break;
                        }

                        var annButtons = courses
                            .Select(c => InlineKeyboardButton.WithCallbackData(
                                c.Name, $"student_course_announce:{c.Id}"))
                            .Chunk(1).Select(r => r.ToArray()).ToArray();

                        await bot.SendMessage(chatId, "Оберіть курс для перегляду оголошень:",
                            replyMarkup: new InlineKeyboardMarkup(annButtons), cancellationToken: ct);
                        break;
                    }

                    // ── ВИКЛАДАЧ ─────────────────────────────────────────

                    case "📚 Мої курси" when session.Role == UserRole.Teacher:
                    {
                        await bot.SendMessage(chatId,
                            "⏳ Синхронізація ваших курсів з Google Classroom... Зачекайте.",
                            cancellationToken: ct);
                        using var scope = _scopeFactory.CreateScope();
                        var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                        var clSvc = scope.ServiceProvider.GetRequiredService<ITeacherClassroomService>();
                        var config = scope.ServiceProvider
                            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                        var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();

                        var teacher = await regSvc.FindTeacherByTelegramIdAsync(chatId);
                        await RefreshTeacherTokenIfNeededAsync(teacher, config, db);

                        if (teacher?.AccessToken == null)
                        {
                            await bot.SendMessage(chatId,
                                "❌ Помилка токена. Будь ласка, авторизуйтесь наново (/start).",
                                cancellationToken: ct);
                            break;
                        }

                        var googleCourses = await clSvc.GetUserCoursesAsync(teacher.AccessToken);
                        await regSvc.SyncTeacherCoursesAsync(teacher.Id, googleCourses);

                        var updatedTeacher = await db.Teachers
                            .Include(t => t.Courses).ThenInclude(c => c.Group)
                            .FirstOrDefaultAsync(t => t.TelegramChatId == chatId, ct);

                        if (updatedTeacher?.Courses == null || !updatedTeacher.Courses.Any())
                            await bot.SendMessage(chatId,
                                "У вас поки немає активних курсів у Google Classroom.",
                                cancellationToken: ct);
                        else
                        {
                            string msg = "📚 Ваші актуальні курси:\n\n";
                            foreach (var course in updatedTeacher.Courses)
                            {
                                string groupInfo = course.Group != null
                                    ? $"Група: {course.Group.Cipher}"
                                    : "❌ не прив'язано до групи";
                                msg += $"🔹 {course.Name} ({groupInfo})\n";
                            }
                            await bot.SendMessage(chatId, msg, cancellationToken: ct);
                        }
                        break;
                    }

                    case "👥 Групи":
                        await ShowGroupsListAsync(bot, chatId, ct);
                        break;

                    case "📋 Черга здачі":
                        await ShowTeacherCoursesForSelectionAsync(bot, chatId, session, "grade_course:",
                            RegistrationStep.TeacherAwaitingGradingCourse, ct);
                        break;

                    case "📣 Оголошення" when session.Role == UserRole.Teacher:
                        await ShowTeacherCoursesForSelectionAsync(bot, chatId, session, "announce_course:",
                            RegistrationStep.TeacherAwaitingAnnouncementCourse, ct);
                        break;

                    case "🔄 Синхронізація з Classroom" when session.Role == UserRole.Teacher:
                    {
                        await bot.SendMessage(chatId, "⏳ Звертаюсь до Google Classroom... Зачекайте.",
                            cancellationToken: ct);
                        using var scope = _scopeFactory.CreateScope();
                        var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                        var clSvc = scope.ServiceProvider.GetRequiredService<ITeacherClassroomService>();
                        var config = scope.ServiceProvider
                            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                        var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();

                        var teacher = await regSvc.FindTeacherByTelegramIdAsync(chatId);
                        await RefreshTeacherTokenIfNeededAsync(teacher, config, db);

                        if (teacher?.AccessToken == null)
                        {
                            await bot.SendMessage(chatId,
                                "❌ Помилка токена. Будь ласка, авторизуйтесь наново (/start).",
                                cancellationToken: ct);
                            break;
                        }

                        var googleCourses = await clSvc.GetUserCoursesAsync(teacher.AccessToken);
                        var savedCourses = await regSvc.SyncTeacherCoursesAsync(teacher.Id, googleCourses);
                        var unboundCourses = savedCourses.Where(c => c.GroupId == null).ToList();

                        string syncMsg =
                            $"✅ Синхронізацію завершено!\nЗнайдено курсів: {googleCourses.Count}\n\n";

                        if (unboundCourses.Any())
                        {
                            syncMsg +=
                                $"⚠️ У вас є {unboundCourses.Count} курсів, які ще не прив'язані до груп. Оберіть курс для налаштування:";
                            session.Step = RegistrationStep.TeacherAwaitingCourseBindingSelectCourse;
                            _sessions.Save(chatId, session);

                            var buttons = unboundCourses
                                .Select(c => InlineKeyboardButton.WithCallbackData(c.Name, $"bind_crs:{c.Id}"))
                                .Chunk(1).Select(r => r.ToArray()).ToArray();

                            await bot.SendMessage(chatId, syncMsg,
                                replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
                        }
                        else
                        {
                            await bot.SendMessage(chatId,
                                syncMsg + "Усі ваші курси вже успішно прив'язані до груп!",
                                cancellationToken: ct);
                        }
                        break;
                    }

                    // ── АДМІН ────────────────────────────────────────────

                    case "📋 Список груп":
                        await ShowGroupsListAsync(bot, chatId, ct);
                        break;

                    case "➕ Створити групу" when session.Role == UserRole.Admin:
                    {
                        session.Step = RegistrationStep.AwaitingNewGroupCipher;
                        _sessions.Save(chatId, session);
                        await bot.SendMessage(chatId,
                            "Введіть шифр нової групи (наприклад, КН-21):\n\nАбо введіть /cancel щоб скасувати.",
                            replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
                        break;
                    }

                    case "❌ Видалити групу" when session.Role == UserRole.Admin:
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                        var groups = await regSvc.GetAllGroupsAsync();

                        if (!groups.Any())
                        {
                            await bot.SendMessage(chatId, "Немає груп для видалення.", cancellationToken: ct);
                            break;
                        }

                        var buttons = groups
                            .Select(g => InlineKeyboardButton.WithCallbackData($"❌ {g.Cipher}", $"del_grp:{g.Id}"))
                            .Chunk(2).Select(r => r.ToArray()).ToArray();

                        await bot.SendMessage(chatId, "Оберіть групу для видалення:",
                            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
                        break;
                    }

                    case "👨‍🏫 Викладачі" when session.Role == UserRole.Admin:
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var adminSvc = scope.ServiceProvider.GetRequiredService<IAdminService>();
                        var teachers = await adminSvc.GetAllTeachersAsync();

                        if (!teachers.Any())
                            await bot.SendMessage(chatId,
                                "Жодного викладача ще не зареєстровано.", cancellationToken: ct);
                        else
                        {
                            string msg = "👨‍🏫 Зареєстровані викладачі:\n\n";
                            foreach (var t in teachers)
                            {
                                string curatedGroups = t.Group != null && t.Group.Any()
                                    ? string.Join(", ", t.Group.Select(g => g.Cipher))
                                    : "немає";
                                msg += $"👤 {t.FullName}\n📧 {t.CorporateEmail}\n🛡 Куратор груп: {curatedGroups}\n\n";
                            }
                            await bot.SendMessage(chatId, msg, cancellationToken: ct);
                        }
                        break;
                    }

                    case "👨‍🎓 Студенти" when session.Role == UserRole.Admin:
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                        var groups = await regSvc.GetAllGroupsAsync();

                        if (!groups.Any())
                        {
                            await bot.SendMessage(chatId, "В базі ще немає груп.", cancellationToken: ct);
                            break;
                        }

                        var buttons = groups
                            .Select(g => InlineKeyboardButton.WithCallbackData(g.Cipher, $"view_students:{g.Id}"))
                            .Chunk(2).Select(r => r.ToArray()).ToArray();

                        await bot.SendMessage(chatId, "Оберіть групу для перегляду студентів:",
                            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
                        break;
                    }

                    default:
                        await bot.SendMessage(chatId,
                            "Невідома команда. Будь ласка, оберіть дію в меню 👇",
                            cancellationToken: ct);
                        break;
                }

                return;
            }

            await bot.SendMessage(chatId, "Я розумію /start та /cancel.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка обробки оновлення");
        }
    }

    // ── HANDLE START ────────────────────────────────────────────────────

    private async Task HandleStartAsync(ITelegramBotClient bot, long chatId, UserSession session,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var adminSvc = scope.ServiceProvider.GetRequiredService<IAdminService>();
        var admin = await adminSvc.FindAdminByTelegramIdAsync(chatId);
        if (admin is not null)
        {
            session.Step = RegistrationStep.Completed;
            session.Role = UserRole.Admin;
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
            session.Step = RegistrationStep.Completed;
            session.Role = UserRole.Teacher;
            _sessions.Save(chatId, session);
            await bot.SendMessage(chatId,
                $"👋 З поверненням, {teacher.FullName}!\nВи авторизовані як Викладач.",
                replyMarkup: TeacherMainMenu(), cancellationToken: ct);
            return;
        }

        var student = await reg.FindStudentByTelegramIdAsync(chatId);
        if (student is not null)
        {
            session.Step = RegistrationStep.Completed;
            session.Role = UserRole.Student;
            _sessions.Save(chatId, session);
            await bot.SendMessage(chatId,
                $"👋 З поверненням, {student.FullName}!\nВи авторизовані як Студент.",
                replyMarkup: StudentMainMenu(), cancellationToken: ct);
            return;
        }

        session.Step = RegistrationStep.AwaitingRole;
        _sessions.Save(chatId, session);
        await bot.SendMessage(chatId,
            "👋 Вітаємо в AntiTail Bot!\n\n" +
            "Система для відстеження дедлайнів та академічної заборгованості.\n\n" +
            "Оберіть вашу роль:",
            replyMarkup: RoleSelectionKeyboard(), cancellationToken: ct);
    }

    // ── HANDLE CALLBACK ─────────────────────────────────────────────────

    private async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery cbq, CancellationToken ct)
    {
        long chatId = cbq.Message!.Chat.Id;
        string data = cbq.Data ?? "";
        await bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ct);
        var session = _sessions.GetOrCreate(chatId);

        // Перевірка адміна
        if (session.Role == UserRole.Admin)
        {
            using var scopeCheck = _scopeFactory.CreateScope();
            var adminCheck = scopeCheck.ServiceProvider.GetRequiredService<IAdminService>();
            if (await adminCheck.FindAdminByTelegramIdAsync(chatId) == null)
            {
                _sessions.Remove(chatId);
                await bot.SendMessage(chatId,
                    "❌ Доступ заборонено. Дія скасована, оскільки вас було видалено.",
                    cancellationToken: ct);
                return;
            }
        }

        // Вибір ролі
        if (data == "role:student")
        {
            await HandleRoleSelectedAsync(bot, chatId, UserRole.Student, session, ct);
            return;
        }

        if (data == "role:teacher")
        {
            await HandleRoleSelectedAsync(bot, chatId, UserRole.Teacher, session, ct);
            return;
        }

        // Вибір групи студентом
        if (data.StartsWith("group:") && int.TryParse(data["group:".Length..], out int gid))
        {
            await HandleGroupSelectedAsync(bot, chatId, gid, session, ct);
            return;
        }

        // Адмін: перегляд студентів групи
        if (data.StartsWith("view_students:") &&
            int.TryParse(data["view_students:".Length..], out int viewGroupId))
        {
            using var scope = _scopeFactory.CreateScope();
            var adminSvc = scope.ServiceProvider.GetRequiredService<IAdminService>();
            var students = await adminSvc.GetStudentsByGroupIdAsync(viewGroupId);

            if (!students.Any())
                await bot.SendMessage(chatId,
                    "У цій групі ще немає зареєстрованих студентів.", cancellationToken: ct);
            else
            {
                string msg = "👨‍🎓 Список студентів групи:\n\n";
                for (int i = 0; i < students.Count; i++)
                    msg += $"{i + 1}. {students[i].FullName} ({students[i].CorporateEmail})\n";
                await bot.SendMessage(chatId, msg, cancellationToken: ct);
            }
            return;
        }

        // Викладач: обрав курс для оголошення
        if (data.StartsWith("announce_course:"))
        {
            string courseDbIdStr = data["announce_course:".Length..];
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();
            var course = await db.Courses.FindAsync(new object[] { int.Parse(courseDbIdStr) }, ct);

            if (course != null && !string.IsNullOrEmpty(course.GoogleCourseId))
            {
                session.PendingCourseId = course.GoogleCourseId; // Зберігаємо ID курсу в сесії
                session.Step = RegistrationStep.TeacherAwaitingAnnouncementText; // Переводимо в стан очікування тексту
                _sessions.Save(chatId, session);
        
                await bot.SendMessage(chatId,
                    "Надішліть текст оголошення, який отримають студенти цього курсу:\n\n(або /cancel для відміни)",
                    cancellationToken: ct);
            }
            else
            {
                await bot.SendMessage(chatId, "❌ Помилка: курс не знайдено в базі.", cancellationToken: ct);
            }
            return;
        }

        // Студент: обрав викладача для повідомлення
        if (data.StartsWith("msg_teacher:") &&
            int.TryParse(data["msg_teacher:".Length..], out int chosenTeacherId))
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();
            var teacher = await db.Teachers.FindAsync(new object[] { chosenTeacherId }, ct);

            if (teacher == null)
            {
                await bot.SendMessage(chatId, "❌ Викладача не знайдено.", cancellationToken: ct);
                return;
            }

            session.PendingTeacherId = chosenTeacherId;
            session.Step = RegistrationStep.StudentAwaitingTeacherMessage;
            _sessions.Save(chatId, session);
            await bot.SendMessage(chatId,
                $"Напишіть ваше повідомлення для {teacher.FullName}:\n\n(або /cancel для відміни)",
                cancellationToken: ct);
            return;
        }

        // ── ВИПРАВЛЕНО: студент переглядає оголошення курсу ──────────────
        // Студент: перегляд оголошень курсу
        if (data.StartsWith("student_course_announce:"))
        {
            string googleCourseId = data["student_course_announce:".Length..];
            using var scope = _scopeFactory.CreateScope();
            var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
            var clSvc = scope.ServiceProvider.GetRequiredService<IStudentClassroomService>(); // ВИПРАВЛЕНО: перевірте, чи ви використовуєте IStudentClassroomService
            var student = await regSvc.FindStudentByTelegramIdAsync(chatId);

            if (student?.AccessToken == null)
            {
                await bot.SendMessage(chatId, "❌ Помилка авторизації. Спробуйте /start.", cancellationToken: ct);
                return;
            }

            try
            {
                // Переконайтеся, що метод GetCourseAnnouncementsAsync існує у вашому IStudentClassroomService
                var announcements = await clSvc.GetCourseAnnouncementsAsync(googleCourseId, student.AccessToken);

                if (announcements == null || !announcements.Any())
                {
                    await bot.SendMessage(chatId, "📭 У цьому курсі ще немає оголошень.", cancellationToken: ct);
                }
                else
                {
                    string msg = "📣 <b>Оголошення курсу:</b>\n\n";
                    foreach (var ann in announcements.Take(5))
                    {
                        msg += $"🔹 {ann.Text}\n"; // Перевірте, чи об'єкт announcement має властивість Text
                        msg += "\n";
                    }
                    await bot.SendMessage(chatId, msg, parseMode: ParseMode.Html, cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Помилка при отриманні оголошень: {Msg}", ex.Message);
                await bot.SendMessage(chatId, "❌ Не вдалося отримати оголошення. Перевірте, чи є доступ до Classroom.", cancellationToken: ct);
            }
            return;
        }

        // Викладач: обрав курс для черги здачі (оцінювання)
        if (data.StartsWith("grade_course:"))
        {
            string courseDbIdStr = data["grade_course:".Length..];
            using var scope = _scopeFactory.CreateScope();
            var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
            var clSvc = scope.ServiceProvider.GetRequiredService<ITeacherClassroomService>();
            var config = scope.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();

            var course = await db.Courses.FindAsync(new object[] { int.Parse(courseDbIdStr) }, ct);
            if (course == null || string.IsNullOrEmpty(course.GoogleCourseId))
            {
                await bot.SendMessage(chatId, "❌ Помилка: курс не знайдено.", cancellationToken: ct);
                return;
            }

            session.PendingCourseId = course.GoogleCourseId;
            session.Step = RegistrationStep.TeacherAwaitingGradingAssignment;
            _sessions.Save(chatId, session);

            var teacher = await regSvc.FindTeacherByTelegramIdAsync(chatId);
            await RefreshTeacherTokenIfNeededAsync(teacher, config, db);

            if (teacher?.AccessToken == null)
            {
                await bot.SendMessage(chatId,
                    "❌ Помилка токена. Будь ласка, авторизуйтесь наново (/start).",
                    cancellationToken: ct);
                return;
            }

            var assignments = await clSvc.GetCourseAssignmentsAsync(course.GoogleCourseId, teacher.AccessToken);

            if (!assignments.Any())
            {
                await bot.SendMessage(chatId, "📭 У цьому курсі ще немає створених завдань.",
                    replyMarkup: TeacherMainMenu(), cancellationToken: ct);
                session.Step = RegistrationStep.Completed;
                _sessions.Save(chatId, session);
                return;
            }

            var buttons = assignments
                .Select(a => InlineKeyboardButton.WithCallbackData(a.Title, $"grade_assign:{a.Id}"))
                .Chunk(1).Select(r => r.ToArray()).ToArray();

            await bot.SendMessage(chatId,
                "📋 Оберіть завдання для перегляду зданих робіт та оцінювання:",
                replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
            return;
        }

        // Викладач: обрав завдання
        if (data.StartsWith("grade_assign:"))
        {
            string assignmentId = data["grade_assign:".Length..];
            session.PendingAssignmentId = assignmentId;
            session.Step = RegistrationStep.TeacherAwaitingGradingSubmission;
            _sessions.Save(chatId, session);

            using var scope = _scopeFactory.CreateScope();
            var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
            var clSvc = scope.ServiceProvider.GetRequiredService<ITeacherClassroomService>();
            var db = scope.ServiceProvider.GetRequiredService<AntiTail.DBContext.AppDBContext>();
            var teacher = await regSvc.FindTeacherByTelegramIdAsync(chatId);

            var submissions = await clSvc.GetAllSubmissionsAsync(
                session.PendingCourseId, assignmentId, teacher.AccessToken);
            var turnedIn = submissions.Where(s => s.State == "TURNED_IN").ToList();

            if (!turnedIn.Any())
            {
                await bot.SendMessage(chatId,
                    "🤷‍♂️ Наразі немає нових зданих робіт для перевірки.", cancellationToken: ct);
                session.Step = RegistrationStep.Completed;
                _sessions.Save(chatId, session);
                return;
            }

            var userIds = turnedIn
                .Where(s => !string.IsNullOrEmpty(s.UserId))
                .Select(s => s.UserId).Distinct().ToList();

            var knownStudents = await db.Students
                .Where(st => userIds.Contains(st.GoogleId))
                .ToDictionaryAsync(st => st.GoogleId!, st => st.FullName, ct);

            var buttonsList = new List<InlineKeyboardButton[]>();
            int queueNumber = 1;
            foreach (var s in turnedIn)
            {
                string name = (!string.IsNullOrEmpty(s.UserId) &&
                               knownStudents.TryGetValue(s.UserId, out var n))
                    ? n
                    : $"Студент (ID: {s.UserId?[..Math.Min(8, s.UserId?.Length ?? 0)]}...)";
                buttonsList.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData($"{queueNumber}. 📝 {name}", $"grade_sub:{s.Id}")
                });
                queueNumber++;
            }

            await bot.SendMessage(chatId,
                $"📋 Черга здачі ({turnedIn.Count} студент(ів)). Оберіть роботу для оцінювання:",
                replyMarkup: new InlineKeyboardMarkup(buttonsList), cancellationToken: ct);
            return;
        }

        // Викладач: обрав роботу студента для оцінювання
        if (data.StartsWith("grade_sub:"))
        {
            string submissionId = data["grade_sub:".Length..];
            session.PendingSubmissionId = submissionId;
            session.Step = RegistrationStep.TeacherAwaitingGradeValue;
            _sessions.Save(chatId, session);
            await bot.SendMessage(chatId,
                "Введіть оцінку (числом) для цієї роботи:\n\n(або /cancel для відміни)",
                cancellationToken: ct);
            return;
        }

        // Прив'язка курсу до групи — Крок 1
        if (data.StartsWith("bind_crs:") && int.TryParse(data["bind_crs:".Length..], out int courseIdToBind))
        {
            session.PendingCourseId = courseIdToBind.ToString();
            session.Step = RegistrationStep.TeacherAwaitingCourseBindingSelectGroup;
            _sessions.Save(chatId, session);

            using var scope = _scopeFactory.CreateScope();
            var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
            var groups = await regSvc.GetAllGroupsAsync();

            var buttons = groups
                .Select(g => InlineKeyboardButton.WithCallbackData(
                    $"{g.Cipher} ({g.CourseYear} курс)", $"bind_grp:{g.Id}"))
                .Chunk(2).Select(r => r.ToArray()).ToArray();

            await bot.SendMessage(chatId, "Оберіть групу, у якій викладається цей предмет:",
                replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
            return;
        }

        // Прив'язка курсу до групи — Крок 2
        if (data.StartsWith("bind_grp:") && int.TryParse(data["bind_grp:".Length..], out int groupIdToBind))
        {
            if (session.PendingCourseId != null && int.TryParse(session.PendingCourseId, out int parsedCourseId))
            {
                using var scope = _scopeFactory.CreateScope();
                var regSvc = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
                await regSvc.BindCourseToGroupAsync(parsedCourseId, groupIdToBind);
                await bot.SendMessage(chatId,
                    "✅ Курс успішно закріплено за групою!\n" +
                    "Щоб прив'язати інші курси, натисніть '🔄 Синхронізація з Classroom' ще раз.",
                    replyMarkup: TeacherMainMenu(), cancellationToken: ct);
            }
            else
            {
                await bot.SendMessage(chatId, "❌ Помилка сесії.",
                    replyMarkup: TeacherMainMenu(), cancellationToken: ct);
            }

            session.Step = RegistrationStep.Completed;
            session.PendingCourseId = null;
            _sessions.Save(chatId, session);
            return;
        }

        // Адмін: видалення групи
        if (data.StartsWith("del_grp:") && int.TryParse(data["del_grp:".Length..], out int groupIdToDelete))
        {
            using var scope = _scopeFactory.CreateScope();
            var adminSvc = scope.ServiceProvider.GetRequiredService<IAdminService>();
            await adminSvc.DeleteGroupAsync(groupIdToDelete);
            await bot.DeleteMessage(chatId, cbq.Message!.MessageId, cancellationToken: ct);
            await bot.SendMessage(chatId, "✅ Групу успішно видалено з бази даних!", cancellationToken: ct);
            return;
        }
    }

    // ── HELPERS ─────────────────────────────────────────────────────────

    /// <summary>
    /// Оновлює access token викладача якщо він закінчився або закінчується.
    /// Виділено в окремий метод щоб не дублювати 50 рядків скрізь.
    /// </summary>
    private async Task RefreshTeacherTokenIfNeededAsync(
        Teacher? teacher,
        Microsoft.Extensions.Configuration.IConfiguration config,
        AntiTail.DBContext.AppDBContext db)
    {
        if (teacher == null || string.IsNullOrEmpty(teacher.RefreshToken)) return;
        if (teacher.TokenExpiresAt != null && teacher.TokenExpiresAt > DateTime.UtcNow.AddMinutes(5)) return;

        try
        {
            using var http = new System.Net.Http.HttpClient();
            var req = new System.Net.Http.FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", config["GoogleAuth:ClientId"]!),
                new KeyValuePair<string, string>("client_secret", config["GoogleAuth:ClientSecret"]!),
                new KeyValuePair<string, string>("refresh_token", teacher.RefreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token")
            });

            var resp = await http.PostAsync("https://oauth2.googleapis.com/token", req);
            if (!resp.IsSuccessStatusCode) return;

            var json = await resp.Content.ReadAsStringAsync();
            var newTokens = System.Text.Json.JsonSerializer
                .Deserialize<AntiTail.Services.GoogleTokenResponse>(json);

            if (newTokens != null && !string.IsNullOrEmpty(newTokens.AccessToken))
            {
                teacher.AccessToken = newTokens.AccessToken;
                teacher.TokenExpiresAt = DateTime.UtcNow.AddSeconds(newTokens.ExpiresIn);
                db.Teachers.Update(teacher);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка оновлення токена викладача");
        }
    }

    private async Task HandleRoleSelectedAsync(ITelegramBotClient bot, long chatId, UserRole role,
        UserSession session, CancellationToken ct)
    {
        session.Role = role;
        session.Step = RegistrationStep.AwaitingGoogleAuth;
        _sessions.Save(chatId, session);

        using var scope = _scopeFactory.CreateScope();
        var clSvc = scope.ServiceProvider.GetRequiredService<IBaseClassroomService>();

        string state = $"{(role == UserRole.Teacher ? "teacher" : "student")}:{chatId}";
        string authUrl = clSvc.GetAuthorizationUrl(state);
        string label = role == UserRole.Teacher ? "Викладач 👨‍🏫" : "Студент 👨‍🎓";

        await bot.SendMessage(chatId,
            $"Роль обрано: {label}\n\n" +
            "Натисніть кнопку нижче для авторизації через Google акаунт коледжу.\n" +
            "⚠️ Використовуйте лише корпоративну пошту коледжу.",
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithUrl("🔑 Увійти через Google", authUrl)),
            cancellationToken: ct);
    }

    private async Task HandleGroupSelectionInputAsync(ITelegramBotClient bot, long chatId, string text,
        UserSession session, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var group = await reg.GetGroupByInviteTokenAsync(text.Trim());
        if (group is null)
        {
            await bot.SendMessage(chatId, "Групу не знайдено. Спробуйте ще раз або оберіть зі списку:",
                cancellationToken: ct);
            await ShowGroupListForSelectionAsync(bot, chatId, reg, ct);
            return;
        }

        await CompleteStudentRegistrationAsync(bot, chatId, group.Id, session, ct);
    }

    private async Task HandleGroupSelectedAsync(ITelegramBotClient bot, long chatId, int groupId,
        UserSession session, CancellationToken ct)
        => await CompleteStudentRegistrationAsync(bot, chatId, groupId, session, ct);

    private async Task CompleteStudentRegistrationAsync(ITelegramBotClient bot, long chatId, int groupId,
        UserSession session, CancellationToken ct)
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
            AccessToken = session.PendingAccessToken,
            RefreshToken = session.PendingRefreshToken,
            ExpiresInSeconds = session.PendingTokenExpiresAt.HasValue
                ? (long?)(session.PendingTokenExpiresAt.Value - DateTime.UtcNow).TotalSeconds
                : 3600,
            IssuedUtc = DateTime.UtcNow
        };
        var googleUser = new Google.Apis.Oauth2.v2.Data.Userinfo
        {
            Id = session.PendingGoogleId,
            Email = session.PendingEmail,
            Name = session.PendingName
        };

        var student = await reg.RegisterStudentAsync(chatId, googleUser, tokens, groupId);
        session.Step = RegistrationStep.Completed;
        session.Role = UserRole.Student;
        _sessions.Save(chatId, session);

        await bot.SendMessage(chatId,
            $"🎉 Реєстрацію завершено!\n\nІм'я: {student.FullName}\nEmail: {student.CorporateEmail}\n\n" +
            "Тепер ви можете переглядати свої дедлайни та заборгованість.",
            replyMarkup: StudentMainMenu(), cancellationToken: ct);
    }

    private async Task ShowGroupsListAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var groups = await reg.GetAllGroupsAsync();

        if (!groups.Any())
            await bot.SendMessage(chatId, "В базі поки немає жодної групи.", cancellationToken: ct);
        else
        {
            var list = string.Join("\n",
                groups.Select(g => $"📌 {g.Cipher} ({g.CourseYear} курс) — Код: {g.InviteLink}"));
            await bot.SendMessage(chatId, $"📚 Список існуючих груп:\n\n{list}", cancellationToken: ct);
        }
    }

    private async Task ShowGroupListForSelectionAsync(ITelegramBotClient bot, long chatId,
        IRegistrationService reg, CancellationToken ct)
    {
        var groups = await reg.GetAllGroupsAsync();
        if (!groups.Any())
        {
            await bot.SendMessage(chatId, "В системі ще немає груп. Зверніться до куратора.",
                cancellationToken: ct);
            return;
        }

        var buttons = groups
            .Select(g => InlineKeyboardButton.WithCallbackData(
                $"{g.Cipher} ({g.CourseYear} курс)", $"group:{g.Id}"))
            .Chunk(2).Select(r => r.ToArray()).ToArray();

        await bot.SendMessage(chatId, "Оберіть свою групу:",
            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task ShowTeacherCoursesForSelectionAsync(ITelegramBotClient bot, long chatId,
        UserSession session, string callbackPrefix, RegistrationStep nextStep, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reg = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var teacher = await reg.FindTeacherByTelegramIdAsync(chatId);

        var activeCourses = teacher?.Courses?
            .Where(c => c.Status == "Active" || string.IsNullOrEmpty(c.Status))
            .ToList();

        if (teacher == null || activeCourses == null || !activeCourses.Any())
        {
            await bot.SendMessage(chatId,
                "У вас немає активних курсів.\n\n💡 Натисніть '🔄 Синхронізація з Classroom' щоб завантажити курси.",
                cancellationToken: ct);
            return;
        }

        session.Step = nextStep;
        _sessions.Save(chatId, session);

        var buttons = activeCourses
            .Select(c => InlineKeyboardButton.WithCallbackData(c.Name, $"{callbackPrefix}{c.Id}"))
            .Chunk(1).Select(r => r.ToArray()).ToArray();

        await bot.SendMessage(chatId, "Оберіть курс:",
            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    // ── KEYBOARDS ────────────────────────────────────────────────────────

    private static InlineKeyboardMarkup RoleSelectionKeyboard() =>
        new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("👨‍🎓 Студент", "role:student") },
            new[] { InlineKeyboardButton.WithCallbackData("👨‍🏫 Викладач", "role:teacher") }
        });

    private static ReplyKeyboardMarkup StudentMainMenu() =>
        new(new[]
        {
            new[] { new KeyboardButton("📚 Мої курси"), new KeyboardButton("⏰ Дедлайни") },
            new[] { new KeyboardButton("📊 Заборгованість"), new KeyboardButton("✉️ Написати викладачу") },
            new[] { new KeyboardButton("📣 Оголошення") }
        }) { ResizeKeyboard = true };

    private static ReplyKeyboardMarkup TeacherMainMenu() =>
        new(new[]
        {
            new[] { new KeyboardButton("📚 Мої курси"), new KeyboardButton("👥 Групи") },
            new[] { new KeyboardButton("📋 Черга здачі"), new KeyboardButton("📣 Оголошення") },
            new[] { new KeyboardButton("🔄 Синхронізація з Classroom") }
        }) { ResizeKeyboard = true };

    private static ReplyKeyboardMarkup AdminMainMenu() =>
        new(new[]
        {
            new[] { new KeyboardButton("➕ Створити групу"), new KeyboardButton("❌ Видалити групу") },
            new[] { new KeyboardButton("📋 Список груп"), new KeyboardButton("👨‍🏫 Викладачі") },
            new[] { new KeyboardButton("👨‍🎓 Студенти") }
        }) { ResizeKeyboard = true };

    private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        _logger.LogError("Polling error: {Msg}",
            exception is ApiRequestException api
                ? $"[{api.ErrorCode}] {api.Message}"
                : exception.ToString());
        return Task.CompletedTask;
    }
}