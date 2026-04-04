using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models.Books;

public sealed class Publisher
{
    public int PublisherId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";
}
