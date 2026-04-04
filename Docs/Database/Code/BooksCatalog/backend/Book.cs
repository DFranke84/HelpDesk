/* Template: copy to HelpDesk.Models/Books/Book.cs */
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HelpDesk.Models.Books;

/// <summary>Row in Books.dbo.Books (library titles).</summary>
[Table("Books")]
public sealed class Book
{
    public int BookId { get; set; }

    [Required, MaxLength(13)]
    public string Isbn13 { get; set; } = "";

    [Required, MaxLength(300)]
    public string Title { get; set; } = "";

    [MaxLength(400)]
    public string? Subtitle { get; set; }

    public short PublicationYear { get; set; }

    public int PublisherId { get; set; }

    public Publisher? Publisher { get; set; }

    public int? Pages { get; set; }

    [Required, MaxLength(2)]
    public string LanguageCode { get; set; } = "en";

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
