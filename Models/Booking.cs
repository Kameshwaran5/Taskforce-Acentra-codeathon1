using System.Text.Json.Serialization;

namespace Acentra.Models;

public class Booking
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource? Resource { get; set; }

    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public string Status { get; set; } = "Confirmed"; // "Confirmed" | "Cancelled"

    public Guid Version { get; set; } = Guid.NewGuid();

    // Private secret token used for cancellation access control (never exposed via GET /api/bookings)
    public Guid OwnerToken { get; set; } = Guid.NewGuid();

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
