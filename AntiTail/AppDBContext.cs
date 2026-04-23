using AntiTail.Entities;
using AntiTail.Entitys;
using Microsoft.EntityFrameworkCore;

namespace AntiTail.DBContext;

public class AppDBContext:DbContext
{
    public AppDBContext(DbContextOptions<AppDBContext> options):base(options)
    {
        
    }
    public DbSet<Teacher> Teachers { get; set; }
    public DbSet<Group> Groups{ get; set; }
    public DbSet<Course> Courses { get; set; }
    public DbSet<Assignment> Assignments { get; set; }
    public DbSet<Student> Students { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Student>()
            .HasOne(S => S.Group)
            .WithMany(G => G.Students)
            .HasForeignKey(S => S.GroupId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Group>()
            .HasOne(G => G.Curator)
            .WithMany(T =>T.Group)
            .HasForeignKey(G => G.CuratorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Course>()
            .HasOne(C => C.Teacher)
            .WithMany(G => G.Courses)
            .HasForeignKey(C => C.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);//Teacher з курсом зв'язаний
        modelBuilder.Entity<Course>()
            .HasOne(C => C.Group)
            .WithMany(G => G.Courses)
            .HasForeignKey(C => C.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Assignment>()
            .HasOne(A => A.Course)
            .WithMany(G => G.Assignments)
            .HasForeignKey(A => A.CourseId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Assignment>()
            .Property(A => A.MaxPoints)
            .HasColumnType("float");
        modelBuilder.Entity<Student>()
            .HasIndex(S=>S.GoogleId)
            .IsUnique();
        modelBuilder.Entity<Student>()
            .HasIndex(S => S.CorporateEmail)
            .IsUnique();
        modelBuilder.Entity<Teacher>()
            .HasIndex(T => T.GoogleId)
            .IsUnique();
        modelBuilder.Entity<Teacher>()
            .HasIndex(T => T.CorporateEmail)
            .IsUnique();
    }
}