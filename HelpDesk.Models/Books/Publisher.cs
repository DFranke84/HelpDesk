using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models.Books;

/// <summary>Book publisher entity from the external Books database.</summary>
public sealed class Publisher
{
    public int PublisherId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";
}
