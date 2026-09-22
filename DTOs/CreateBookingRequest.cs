using System.ComponentModel.DataAnnotations;

namespace Acentra.DTOs;

public class CreateBookingRequest
{
    [Required]
    public int ResourceId { get; set; }

    [Required(ErrorMessage = "User name is required")]
    [StringLength(150)]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(150)]
    public string UserEmail { get; set; } = string.Empty;

    [Required]
    public DateTime StartUtc { get; set; }

    [Required]
    public DateTime EndUtc { get; set; }
}
