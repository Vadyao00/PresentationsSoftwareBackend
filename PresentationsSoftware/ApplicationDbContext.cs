using Microsoft.EntityFrameworkCore;
using PresentationsSoftware.Models;

namespace PresentationsSoftware;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Presentation> Presentations { get; set; }
    public DbSet<Slide> Slides { get; set; }
    public DbSet<SlideElement> SlideElements { get; set; }
    public DbSet<PresentationUser> PresentationUsers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<SlideElement>()
            .Property(e => e.Type)
            .HasConversion<int>();
            
        modelBuilder.Entity<PresentationUser>()
            .Property(e => e.Role)
            .HasConversion<int>();
        
        modelBuilder.Entity<Presentation>()
            .HasMany(p => p.Slides)
            .WithOne(s => s.Presentation)
            .HasForeignKey(s => s.PresentationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Slide>()
            .HasMany(s => s.Elements)
            .WithOne(e => e.Slide)
            .HasForeignKey(e => e.SlideId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SlideElement>()
            .Property(e => e.Style)
            .HasColumnType("json");
            
        modelBuilder.Entity<Slide>()
            .Property(s => s.Order)
            .HasColumnName("Order");
    }
}