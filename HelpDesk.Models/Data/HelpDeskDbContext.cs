using System.Text.Json;
using HelpDesk.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HelpDesk.Models.Data;

public class HelpDeskDbContext : DbContext
{
    private readonly ICurrentUserContext? _currentUser;

    public HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options, ICurrentUserContext? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<DeskApplication> Applications => Set<DeskApplication>();
    public DbSet<DeskPage> Pages => Set<DeskPage>();
    public DbSet<DeskGroup> Groups => Set<DeskGroup>();
    public DbSet<DeskApplicationGroup> DeskApplicationGroups => Set<DeskApplicationGroup>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<NLogEntry> NLogEntries => Set<NLogEntry>();
    public DbSet<EntityChangeHistory> EntityChangeHistories => Set<EntityChangeHistory>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var pending = BuildPendingHistories();
        if (pending.Count == 0)
            return base.SaveChanges(acceptAllChangesOnSuccess);

        var ownsTransaction = Database.CurrentTransaction is null;
        var transaction = Database.CurrentTransaction ?? Database.BeginTransaction();
        try
        {
            var n = base.SaveChanges(acceptAllChangesOnSuccess);
            foreach (var (row, entry) in pending)
            {
                row.EntityId = ResolveEntityId(entry);
                EntityChangeHistories.Add(row);
            }
            base.SaveChanges(acceptAllChangesOnSuccess);
            if (ownsTransaction) transaction.Commit();
            return n;
        }
        catch
        {
            if (ownsTransaction) transaction.Rollback();
            throw;
        }
        finally
        {
            if (ownsTransaction) transaction.Dispose();
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var pending = BuildPendingHistories();
        if (pending.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);

        var ownsTransaction = Database.CurrentTransaction is null;
        var transaction = Database.CurrentTransaction
            ?? await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var n = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            foreach (var (row, entry) in pending)
            {
                row.EntityId = ResolveEntityId(entry);
                EntityChangeHistories.Add(row);
            }
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            if (ownsTransaction) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return n;
        }
        catch
        {
            if (ownsTransaction) await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (ownsTransaction) await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    private List<(EntityChangeHistory Row, EntityEntry Entry)> BuildPendingHistories()
    {
        var list = new List<(EntityChangeHistory, EntityEntry)>();
        var userId = _currentUser?.UserId;
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                     .ToList())
        {
            if (entry.Entity is EntityChangeHistory or NLogEntry or PasswordResetToken)
                continue;

            if (!TryMapEntity(entry, out var entityType, out var action))
                continue;

            var json = BuildJsonSnapshot(entry);
            if (entry.State == EntityState.Modified && string.IsNullOrEmpty(json))
                continue;

            var row = new EntityChangeHistory
            {
                EntityType = entityType,
                EntityId = "",
                Action = action,
                JsonChanges = json,
                ChangedByUserId = userId,
                ChangedAt = now
            };
            list.Add((row, entry));
        }

        return list;
    }

    private static string ResolveEntityId(EntityEntry entry)
    {
        return entry.Entity switch
        {
            User u => u.Id.ToString(),
            DeskApplication a => a.Id.ToString(),
            DeskPage p => p.Id.ToString(),
            DeskGroup g => g.Id.ToString(),
            DeskApplicationGroup ag => $"{ag.DeskApplicationId}:{ag.DeskGroupId}",
            UserGroup ug => $"{ug.UserId}:{ug.GroupId}",
            _ => ""
        };
    }

    private static bool TryMapEntity(EntityEntry entry, out string entityType, out EntityChangeAction action)
    {
        entityType = "";
        action = EntityChangeAction.Update;
        switch (entry.Entity)
        {
            case User:
                entityType = TrackedEntityType.User;
                break;
            case DeskApplication:
                entityType = TrackedEntityType.DeskApplication;
                break;
            case DeskPage:
                entityType = TrackedEntityType.DeskPage;
                break;
            case DeskGroup:
                entityType = TrackedEntityType.DeskGroup;
                break;
            case DeskApplicationGroup:
                entityType = TrackedEntityType.DeskApplicationGroup;
                break;
            case UserGroup:
                entityType = TrackedEntityType.UserGroup;
                break;
            default:
                return false;
        }

        action = entry.State switch
        {
            EntityState.Added => EntityChangeAction.Insert,
            EntityState.Deleted => EntityChangeAction.Delete,
            _ => EntityChangeAction.Update
        };
        return true;
    }

    private static string? BuildJsonSnapshot(EntityEntry entry)
    {
        try
        {
            if (entry.State == EntityState.Deleted)
            {
                var dict = entry.Properties
                    .Where(p => !p.Metadata.IsPrimaryKey())
                    .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
                return JsonSerializer.Serialize(dict);
            }

            if (entry.State == EntityState.Added)
            {
                var dict = entry.Properties
                    .Where(p => !p.Metadata.IsPrimaryKey())
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                return JsonSerializer.Serialize(dict);
            }

            var changes = entry.Properties
                .Where(p => p.IsModified && !p.Metadata.IsPrimaryKey())
                .Select(p => new { p.Metadata.Name, Old = p.OriginalValue, New = p.CurrentValue })
                .ToList();
            if (changes.Count == 0)
                return null;
            return JsonSerializer.Serialize(changes);
        }
        catch
        {
            return "{}";
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasIndex(x => x.LoginId).IsUnique();
            e.Property(x => x.LoginId).HasMaxLength(256);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.MustChangePassword).HasDefaultValue(false);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.DomainSamAccountName).HasMaxLength(256);
            e.Property(x => x.DomainUserPrincipalName).HasMaxLength(256);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<DeskApplication>(e =>
        {
            e.ToTable("Applications");
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ExternalConnectionStringName).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<DeskPage>(e =>
        {
            e.ToTable("Pages");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Route).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.DeskApplication)
                .WithMany()
                .HasForeignKey(x => x.DeskApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeskGroup>(e =>
        {
            e.ToTable("Groups");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        // SQL Server: two CASCADE FKs on a junction table + UserGroups->Groups CASCADE causes
        // "multiple cascade paths". Cascade from Application only; Group/Page use NO ACTION — EF deletes join rows first when removing Group/Page.
        modelBuilder.Entity<DeskApplicationGroup>(e =>
        {
            e.ToTable("ApplicationGroups");
            e.HasKey(x => new { x.DeskApplicationId, x.DeskGroupId });
            e.HasOne(x => x.DeskApplication)
                .WithMany(a => a.ApplicationGroups)
                .HasForeignKey(x => x.DeskApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DeskGroup)
                .WithMany(g => g.ApplicationGroups)
                .HasForeignKey(x => x.DeskGroupId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<UserGroup>(e =>
        {
            e.ToTable("UserGroups");
            e.HasKey(x => new { x.UserId, x.GroupId });
            e.HasOne(x => x.User)
                .WithMany(u => u.UserGroups)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group)
                .WithMany(g => g.UserGroups)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NLogEntry>(e =>
        {
            e.ToTable("NLOG");
            e.HasIndex(x => x.Logged);
            e.HasIndex(x => x.Level);
            e.HasIndex(x => x.ActivityCategory);
            e.HasIndex(x => x.CorrelationId);
            e.Property(x => x.MachineName).HasMaxLength(200);
            e.Property(x => x.Level).HasMaxLength(16);
            e.Property(x => x.Logger).HasMaxLength(300);
            e.Property(x => x.ActivityCategory).HasMaxLength(30);
            e.Property(x => x.LoginId).HasMaxLength(256);
            e.Property(x => x.DomainLogin).HasMaxLength(256);
            e.Property(x => x.ElapsedTime).HasColumnType("decimal(11,5)");
        });

        modelBuilder.Entity<EntityChangeHistory>(e =>
        {
            e.ToTable("EntityChangeHistories");
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ChangedAt);
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(64);
            e.HasOne(x => x.ChangedByUser)
                .WithMany()
                .HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("PasswordResetTokens");
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.TokenHash);
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

