using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using Terminals.Web.Persistence.Entities;

namespace Terminals.Web.Persistence;

public class LocalAppDbContext : DbContext
{
    public LocalAppDbContext(DbContextOptions<LocalAppDbContext> options)
        : base(options)
    {
    }
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<DelegationTerminal> DelegationTerminals => Set<DelegationTerminal>();

    public DbSet<Region> Regions => Set<Region>();
    public DbSet<TerminalRegionMap> TerminalRegionMaps => Set<TerminalRegionMap>();
    public DbSet<AllowedUser> AllowedUsers => Set<AllowedUser>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ===== Region =====
        modelBuilder.Entity<Region>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<Region>()
            .Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        // ===== TerminalRegionMap =====
        modelBuilder.Entity<TerminalRegionMap>()
            .HasKey(x => x.TerminalId); // ÙƒÙ„ Ø¬Ù‡Ø§Ø² Ù„Ù‡ Ù…Ù†Ø·Ù‚Ø© ÙˆØ­Ø¯Ø©

        // ðŸ”— Ø§Ù„Ø¹Ù„Ø§Ù‚Ø© (Ù‡Ø°Ø§ Ù‡Ùˆ Ø§Ù„ÙƒÙˆØ¯ Ø§Ù„Ù„ÙŠ Ø³Ø£Ù„Øª Ø¹Ù†Ù‡)
        modelBuilder.Entity<TerminalRegionMap>()
            .HasOne(x => x.Region)          // TerminalRegionMap ÙÙŠÙ‡ Region
            .WithMany()                     // Region Ù…Ø§ Ù†Ø­ØªØ§Ø¬ List Ø¯Ø§Ø®Ù„Ù‡Ø§
            .HasForeignKey(x => x.RegionId) // Ø§Ù„Ù…ÙØªØ§Ø­ Ø§Ù„Ø£Ø¬Ù†Ø¨ÙŠ
            .OnDelete(DeleteBehavior.Restrict);



        // ===== Seed Regions =====
        modelBuilder.Entity<Region>().HasData(
       new Region { Id = 1, Name = "Ø§Ù„Ù…Ø¨Ù†Ù‰ Ø§Ù„Ø±Ø¦ÙŠØ³ÙŠ" },
       new Region { Id = 2, Name = "Ø§Ù„Ù…Ø·Ù„Ø§Ø¹" },
       new Region { Id = 3, Name = "Ø¨Ø±Ø¬ Ø§Ù„ØªØ­Ø±ÙŠØ±" },
       new Region { Id = 4, Name = "ØµØ¨Ø§Ø­ Ø§Ù„Ø³Ø§Ù„Ù…" },
       new Region { Id = 5, Name = "Ø§Ù„Ø¬Ù‡Ø±Ø§Ø¡ - Ø­ÙƒÙˆÙ…Ø© Ù…ÙˆÙ„" },
       new Region { Id = 6, Name = "Ø§Ù„Ø¬Ù‡Ø±Ø§Ø¡ - ØªÙŠÙ…Ø§Ø¡" },
       new Region { Id = 7, Name = "Ø¬Ø§Ø¨Ø± Ø§Ù„Ø£Ø­Ù…Ø¯" },
       new Region { Id = 8, Name = "Ø³Ø¹Ø¯ Ø§Ù„Ø¹Ø¨Ø¯Ø§Ù„Ù„Ù‡" },
       new Region { Id = 9, Name = "Ø§Ù„ØµÙ„ÙŠØ¨ÙŠØ©" },
       new Region { Id = 10, Name = "Ø§Ù„Ù‚Ø±ÙŠÙ† - Ø­ÙƒÙˆÙ…Ø© Ù…ÙˆÙ„" },
       new Region { Id = 11, Name = "Ù…Ø¨Ø§Ø±Ùƒ Ø§Ù„ÙƒØ¨ÙŠØ±" },
       new Region { Id = 12, Name = "Ø§Ù„Ù†Ù‡Ø¶Ø©" },
       new Region { Id = 13, Name = "ØºØ±Ø¨ Ø§Ù„Ø¬Ù„ÙŠØ¨" },
       new Region { Id = 14, Name = "Ù…ÙˆØ§Ù‚Ø¹ Ø£Ø®Ø±Ù‰" },
         new Region { Id = 15, Name = "Ø§Ù„Ø³Ø§Ù„Ù…ÙŠ" }
   );
        
        // ===== Delegation =====
        modelBuilder.Entity<Delegation>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<Delegation>()
            .Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired();

        modelBuilder.Entity<Delegation>()
            .HasMany(x => x.Terminals)
            .WithOne(x => x.Delegation)
            .HasForeignKey(x => x.DelegationId)
            .OnDelete(DeleteBehavior.Cascade);

// ===== DelegationTerminal =====
        modelBuilder.Entity<DelegationTerminal>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<DelegationTerminal>()
            .Property(x => x.TerminalId)
            .HasMaxLength(50)
            .IsRequired();
        
        modelBuilder.Entity<AllowedUser>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<AllowedUser>()
            .HasIndex(x => x.EmployeeId)
            .IsUnique();

        modelBuilder.Entity<AllowedUser>()
            .Property(x => x.FullName)
            .HasMaxLength(200);

        modelBuilder.Entity<AllowedUser>()
            .Property(x => x.Email)
            .HasMaxLength(200);

        modelBuilder.Entity<AllowedUser>()
            .Property(x => x.Department)
            .HasMaxLength(200);

        modelBuilder.Entity<AllowedUser>().HasData(
            new AllowedUser
            {
                Id = 1,
                EmployeeId = 7300,
                FullName = "Ø£Ø­Ù…Ø¯ Ø²ÙŠØ¯ Ø§Ù„Ø­Ø±Ø¨ÙŠ",
                Email = "admin@admin.com",
                Department = "",
                IsActive = true,
                IsAdmin = true
            }
        );

        // ===== ActivityLog =====
        modelBuilder.Entity<ActivityLog>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<ActivityLog>()
            .Property(x => x.ActorType)
            .HasMaxLength(20)
            .IsRequired();

        modelBuilder.Entity<ActivityLog>()
            .Property(x => x.ActorName)
            .HasMaxLength(200);

        modelBuilder.Entity<ActivityLog>()
            .Property(x => x.Action)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<ActivityLog>()
            .Property(x => x.EntityType)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<ActivityLog>()
            .Property(x => x.EntityId)
            .HasMaxLength(100);

        modelBuilder.Entity<ActivityLog>()
            .Property(x => x.Summary)
            .HasMaxLength(500)
            .IsRequired();

        modelBuilder.Entity<ActivityLog>()
            .Property(x => x.Severity)
            .HasMaxLength(20);

        modelBuilder.Entity<ActivityLog>()
            .HasIndex(x => x.CreatedAt);

    }

}
