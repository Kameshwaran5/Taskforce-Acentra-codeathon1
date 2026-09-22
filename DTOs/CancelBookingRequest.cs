using System.ComponentModel.DataAnnotations;

namespace Acentra.DTOs;

public class CancelBookingRequest
{
    [Required]
    public Guid Version { get; set; }

    [Required]
    public Guid OwnerToken { get; set; }
}
