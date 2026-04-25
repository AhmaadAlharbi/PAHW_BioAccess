using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using FingerprintManagementSystem.ApiAdapter.Persistence.Entities;

namespace FingerprintManagementSystem.ApiAdapter.Persistence;

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
            .HasKey(x => x.TerminalId); // كل جهاز له منطقة وحدة

        // 🔗 العلاقة (هذا هو الكود اللي سألت عنه)
        modelBuilder.Entity<TerminalRegionMap>()
            .HasOne(x => x.Region)          // TerminalRegionMap فيه Region
            .WithMany()                     // Region ما نحتاج List داخلها
            .HasForeignKey(x => x.RegionId) // المفتاح الأجنبي
            .OnDelete(DeleteBehavior.Restrict);



        // ===== Seed Regions =====
        modelBuilder.Entity<Region>().HasData(
       new Region { Id = 1, Name = "المبنى الرئيسي" },
       new Region { Id = 2, Name = "المطلاع" },
       new Region { Id = 3, Name = "برج التحرير" },
       new Region { Id = 4, Name = "صباح السالم" },
       new Region { Id = 5, Name = "الجهراء - حكومة مول" },
       new Region { Id = 6, Name = "الجهراء - تيماء" },
       new Region { Id = 7, Name = "جابر الأحمد" },
       new Region { Id = 8, Name = "سعد العبدالله" },
       new Region { Id = 9, Name = "الصليبية" },
       new Region { Id = 10, Name = "القرين - حكومة مول" },
       new Region { Id = 11, Name = "مبارك الكبير" },
       new Region { Id = 12, Name = "النهضة" },
       new Region { Id = 13, Name = "غرب الجليب" },
       new Region { Id = 14, Name = "مواقع أخرى" },
         new Region { Id = 15, Name = "السالمي" }
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
                FullName = "أحمد زيد الحربي",
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
