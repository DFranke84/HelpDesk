using HelpDesk.Books.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Books.Models.Data;

/// <summary>
/// Books library EF Core context for the external Books SQL database.
/// </summary>
public sealed class BooksDbContext : DbContext
{
    /// <summary>
    /// Create Books database context.
    /// </summary>
    /// <param name="options">EF Core options <see cref="DbContextOptions{TContext}"/>.</param>
    public BooksDbContext(DbContextOptions<BooksDbContext> options)
        : base(options)
    {
    }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Publisher> Publishers => Set<Publisher>();

    /// <summary>
    /// Configure Books and Publisher table mappings.
    /// </summary>
    /// <param name="modelBuilder">EF Core model builder <see cref="ModelBuilder"/>.</param>
    /// <returns>This method does not return a value.</returns>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Publisher>(e =>
        {
            e.ToTable("Publishers");
            e.HasKey(x => x.PublisherId);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<Book>(e =>
        {
            e.ToTable("Books");
            e.HasKey(x => x.BookId);
            e.Property(x => x.Isbn13).IsRequired().HasMaxLength(13).IsFixedLength();
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Subtitle).HasMaxLength(400);
            e.Property(x => x.LanguageCode).IsRequired().HasMaxLength(2).IsFixedLength();
            e.HasIndex(x => x.Isbn13).IsUnique();
            e.HasOne(x => x.Publisher)
                .WithMany()
                .HasForeignKey(x => x.PublisherId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}