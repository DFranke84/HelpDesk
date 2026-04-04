using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HelpDesk.Models.Books;

[Table("Publishers")]
public sealed class Publisher
{
    public int PublisherId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";
}
