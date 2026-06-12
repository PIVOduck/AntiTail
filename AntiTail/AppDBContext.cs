using AntiTail.Entities;
using AntiTail.Entitys;
using Microsoft.EntityFrameworkCore;

namespace AntiTail.DBContext;

public class AppDBContext : DbContext
{
    public AppDBContext(DbContextOptions<AppDBContext> options) : base(options)
    {
    }

    // Твої існуючі таблиці
    public DbSet<Teacher> Teachers { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<Course> Courses { get; set; }
    public DbSet<Assignment> Assignments { get; set; }
    public DbSet<Student> Students { get; set; }

    // 1. Додаємо нову таблицю для Адмінів
    public DbSet<Admin> Admins { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Зв'язок Студент -> Група
        modelBuilder.Entity<Student>()
            .HasOne(S => S.Group)
            .WithMany(G => G.Students)
            .HasForeignKey(S => S.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // Зв'язок Група -> Куратор (Викладач)
        // ВАЖЛИВО: У самому класі Group.cs треба змінити public int? CuratorId { get; set; }
        modelBuilder.Entity<Group>()
            .HasOne(G => G.Curator)
            .WithMany(T => T.Group)
            .HasForeignKey(G => G.CuratorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Зв'язок Курс -> Викладач
        modelBuilder.Entity<Course>()
            .HasOne(C => C.Teacher)
            .WithMany(T => T.Courses)
            .HasForeignKey(C => C.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        // Зв'язок Курс -> Група
        modelBuilder.Entity<Course>()
            .HasOne(C => C.Group)
            .WithMany(G => G.Courses)
            .HasForeignKey(C => C.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Зв'язок Завдання -> Курс
        modelBuilder.Entity<Assignment>()
            .HasOne(A => A.Course)
            .WithMany(C => C.Assignments)
            .HasForeignKey(A => A.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Налаштування типів даних
        modelBuilder.Entity<Assignment>()
            .Property(A => A.MaxPoints)
            .HasColumnType("float");

        // --- Унікальні індекси для безпеки даних ---

        // Для Студентів
        modelBuilder.Entity<Student>()
            .HasIndex(S => S.GoogleId)
            .IsUnique();
        modelBuilder.Entity<Student>()
            .HasIndex(S => S.CorporateEmail)
            .IsUnique();

        // Для Викладачів
        modelBuilder.Entity<Teacher>()
            .HasIndex(T => T.GoogleId)
            .IsUnique();
        modelBuilder.Entity<Teacher>()
            .HasIndex(T => T.CorporateEmail)
            .IsUnique();
        
        modelBuilder.Entity<Course>()
            .HasIndex(c => c.GoogleCourseId)
            .IsUnique();

        // 2. Додаємо унікальний індекс для Telegram ID Адміна
        modelBuilder.Entity<Admin>()
            .HasIndex(A => A.TelegramChatId)
            .IsUnique();
    }
}