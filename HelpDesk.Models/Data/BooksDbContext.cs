using HelpDesk.Models.Books;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Models.Data;

public sealed class BooksDbContext : DbContext
{
    public BooksDbContext(DbContextOptions<BooksDbContext> options)
        : base(options)
    {
    }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Publisher> Publishers => Set<Publisher>();

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
