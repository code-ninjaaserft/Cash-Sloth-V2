using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Data;

public sealed class ServerDbContext(DbContextOptions<ServerDbContext> options)
    : IdentityDbContext<ServerUser, IdentityRole, string>(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PairingCode> PairingCodes => Set<PairingCode>();
    public DbSet<DeviceChallenge> DeviceChallenges => Set<DeviceChallenge>();
    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<Preset> Presets => Set<Preset>();
    public DbSet<PresetCategory> PresetCategories => Set<PresetCategory>();
    public DbSet<PresetItem> PresetItems => Set<PresetItem>();
    public DbSet<ExchangeRateSnapshot> ExchangeRateSnapshots => Set<ExchangeRateSnapshot>();
    public DbSet<TranslationEntry> TranslationEntries => Set<TranslationEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ServerMetadata> ServerMetadata => Set<ServerMetadata>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Device>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Name).HasMaxLength(100);
            entity.Property(value => value.PublicKey).IsRequired();
            entity.Property(value => value.PublicKeyFingerprint).HasMaxLength(128);
            entity.HasIndex(value => value.PublicKeyFingerprint).IsUnique();
        });

        builder.Entity<PairingCode>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.CodeHash).HasMaxLength(128);
            entity.HasIndex(value => value.CodeHash).IsUnique();
        });

        builder.Entity<DeviceChallenge>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Nonce).HasMaxLength(128);
            entity.Property(value => value.Purpose).HasMaxLength(32);
            entity.HasOne(value => value.Device)
                .WithMany(value => value.Challenges)
                .HasForeignKey(value => value.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.DeviceId, value.ExpiresAtUtc });
        });

        builder.Entity<LoginSession>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.RefreshTokenHash).HasMaxLength(128);
            entity.HasIndex(value => value.RefreshTokenHash).IsUnique();
            entity.HasOne(value => value.User)
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Device)
                .WithMany(value => value.Sessions)
                .HasForeignKey(value => value.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Preset>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.Property(value => value.Version).IsConcurrencyToken();
        });

        builder.Entity<PresetCategory>(entity =>
        {
            entity.HasKey(value => new { value.PresetId, value.Name });
            entity.Property(value => value.Name).HasMaxLength(100);
            entity.HasOne(value => value.Preset)
                .WithMany(value => value.Categories)
                .HasForeignKey(value => value.PresetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PresetItem>(entity =>
        {
            entity.HasKey(value => new { value.PresetId, value.Id });
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.Property(value => value.Category).HasMaxLength(100);
            entity.HasOne(value => value.Preset)
                .WithMany(value => value.Items)
                .HasForeignKey(value => value.PresetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ExchangeRateSnapshot>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.BaseCurrency).HasMaxLength(3);
            entity.HasIndex(value => new { value.BaseCurrency, value.FetchedAtUtc });
        });

        builder.Entity<TranslationEntry>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.SourceLanguage).HasMaxLength(12);
            entity.Property(value => value.TargetLanguage).HasMaxLength(12);
            entity.Property(value => value.SourceTextNormalized).HasMaxLength(500);
            entity.HasIndex(value => new
            {
                value.SourceLanguage,
                value.TargetLanguage,
                value.SourceTextNormalized
            }).IsUnique();
        });

        builder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Actor).HasMaxLength(200);
            entity.Property(value => value.Action).HasMaxLength(100);
            entity.Property(value => value.TargetType).HasMaxLength(100);
            entity.HasIndex(value => value.CreatedAtUtc);
        });

        builder.Entity<ServerMetadata>(entity =>
        {
            entity.HasKey(value => value.Key);
            entity.Property(value => value.Key).HasMaxLength(100);
        });
    }
}
