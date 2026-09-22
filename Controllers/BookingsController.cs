using System.Data;
using Acentra.Data;
using Acentra.DTOs;
using Acentra.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acentra.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<BookingsController> _logger;

    public BookingsController(AppDbContext context, ILogger<BookingsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/bookings?fromUtc=&toUtc=&resourceId=
    /// Confirmed bookings overlapping the given UTC window, optionally filtered by resource, ordered by start time.
    /// Includes Version in response for client to use on cancel.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBookings(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int? resourceId)
    {
        var query = _context.Bookings
            .AsNoTracking()
            .Include(b => b.Resource)
            .Where(b => b.Status == "Confirmed");

        if (resourceId.HasValue && resourceId.Value > 0)
        {
            query = query.Where(b => b.ResourceId == resourceId.Value);
        }

        if (fromUtc.HasValue && toUtc.HasValue)
        {
            var f = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc);
            var t = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc);
            // Overlapping logic: booking starts before window ends, and ends after window starts
            query = query.Where(b => b.StartUtc < t && b.EndUtc > f);
        }
        else if (fromUtc.HasValue)
        {
            var f = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc);
            query = query.Where(b => b.EndUtc > f);
        }
        else if (toUtc.HasValue)
        {
            var t = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc);
            query = query.Where(b => b.StartUtc < t);
        }

        var results = await query
            .OrderBy(b => b.StartUtc)
            .Select(b => new BookingResponseDto
            {
                Id = b.Id,
                ResourceId = b.ResourceId,
                ResourceName = b.Resource != null ? b.Resource.Name : string.Empty,
                UserName = b.UserName,
                UserEmail = b.UserEmail,
                StartUtc = b.StartUtc,
                EndUtc = b.EndUtc,
                Status = b.Status,
                Version = b.Version,
                CreatedUtc = b.CreatedUtc
            })
            .ToListAsync();

        return Ok(results);
    }

    /// <summary>
    /// POST /api/bookings
    /// Creates a booking with strict Serializable transaction concurrency control to prevent double-booking.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var startUtc = DateTime.SpecifyKind(request.StartUtc, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(request.EndUtc, DateTimeKind.Utc);

        // Validation: end after start
        if (endUtc <= startUtc)
        {
            return BadRequest(new { message = "End time must be strictly after start time." });
        }

        // Validation: start not in the past (with 5-minute clock drift tolerance)
        if (startUtc < DateTime.UtcNow.AddMinutes(-5))
        {
            return BadRequest(new { message = "Booking start time cannot be in the past." });
        }

        // Validation: name and email present
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.UserEmail))
        {
            return BadRequest(new { message = "User name and email are required." });
        }

        var resource = await _context.Resources.FindAsync(request.ResourceId);
        if (resource == null || !resource.Active)
        {
            return BadRequest(new { message = "Selected resource does not exist or is inactive." });
        }

        // Execute inside a database transaction at IsolationLevel.Serializable
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Check for any existing Confirmed booking on the same resource whose time range overlaps
            var hasOverlap = await _context.Bookings
                .AnyAsync(b => b.Status == "Confirmed"
                            && b.ResourceId == request.ResourceId
                            && b.StartUtc < endUtc
                            && b.EndUtc > startUtc);

            if (hasOverlap)
            {
                await transaction.RollbackAsync();
                return Conflict(new
                {
                    message = "Conflict: This resource is already booked for the requested time window.",
                    resourceId = request.ResourceId,
                    startUtc,
                    endUtc
                });
            }

            var newBooking = new Booking
            {
                ResourceId = request.ResourceId,
                UserName = request.UserName.Trim(),
                UserEmail = request.UserEmail.Trim(),
                StartUtc = startUtc,
                EndUtc = endUtc,
                Status = "Confirmed",
                Version = Guid.NewGuid(),
                OwnerToken = Guid.NewGuid(),
                CreatedUtc = DateTime.UtcNow
            };

            _context.Bookings.Add(newBooking);
            await _context.SaveChangesAsync();

            // In Serializable mode in PostgreSQL, serialization conflict errors are raised at COMMIT time!
            await transaction.CommitAsync();

            _logger.LogInformation("Booking {BookingId} successfully created for Resource {ResourceId}", newBooking.Id, newBooking.ResourceId);

            return Ok(new
            {
                id = newBooking.Id,
                version = newBooking.Version,
                ownerToken = newBooking.OwnerToken,
                resourceId = newBooking.ResourceId,
                startUtc = newBooking.StartUtc,
                endUtc = newBooking.EndUtc,
                status = newBooking.Status,
                message = "Booking confirmed successfully."
            });
        }
        // CRITICAL CONCURRENCY CATCH:
        // Raw Npgsql.PostgresException (SqlState 40001 = serialization_failure, 23505 = unique_violation)
        catch (Npgsql.PostgresException ex) when (ex.SqlState is "40001" or "23505")
        {
            _logger.LogWarning("PostgreSQL serialization conflict caught (SqlState {SqlState}): {Message}", ex.SqlState, ex.Message);
            try { await transaction.RollbackAsync(); } catch { /* ignore rollback error */ }
            return Conflict(new
            {
                message = "Conflict: Concurrent booking detected. Another user booked this slot simultaneously.",
                code = ex.SqlState
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "DbUpdateConcurrencyException caught during booking creation");
            try { await transaction.RollbackAsync(); } catch { /* ignore rollback error */ }
            return Conflict(new
            {
                message = "Conflict: Concurrency token mismatch while processing booking."
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pex && pex.SqlState is "40001" or "23505")
        {
            _logger.LogWarning("DbUpdateException wrapping PostgreSQL serialization conflict (SqlState {SqlState}): {Message}", pex.SqlState, pex.Message);
            try { await transaction.RollbackAsync(); } catch { /* ignore rollback error */ }
            return Conflict(new
            {
                message = "Conflict: Concurrent booking detected. Another user booked this slot simultaneously.",
                code = pex.SqlState
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred during booking creation");
            try { await transaction.RollbackAsync(); } catch { /* ignore rollback error */ }
            throw;
        }
    }

    /// <summary>
    /// DELETE /api/bookings/{id}
    /// Cancels a booking requiring OwnerToken for access control and client's last-known Version for optimistic concurrency.
    /// Returns 403 Forbidden if OwnerToken does not match.
    /// Returns 409 Conflict if Version token does not match.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> CancelBooking(int id, [FromBody] CancelBookingRequest? request)
    {
        if (request == null || request.Version == Guid.Empty)
        {
            return BadRequest(new { message = "Client version token is required for cancellation." });
        }

        var booking = await _context.Bookings.FindAsync(id);
        if (booking == null)
        {
            return NotFound(new { message = $"Booking with ID {id} not found." });
        }

        if (booking.Status == "Cancelled")
        {
            return BadRequest(new { message = "This booking has already been cancelled." });
        }

        // ACCESS CONTROL: Verify private OwnerToken
        if (request.OwnerToken == Guid.Empty || booking.OwnerToken != request.OwnerToken)
        {
            _logger.LogWarning("Unauthorized cancellation attempt on Booking {BookingId}. Provided OwnerToken does not match.", id);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "You can only cancel your own bookings."
            });
        }

        // Fast-path check: loaded Version doesn't match client's Version
        if (booking.Version != request.Version)
        {
            return Conflict(new
            {
                message = "Conflict: Booking was modified by another session or operation. Please refresh the calendar.",
                currentVersion = booking.Version,
                requestedVersion = request.Version
            });
        }

        try
        {
            // Update status and rotate concurrency token
            booking.Status = "Cancelled";
            booking.Version = Guid.NewGuid();

            await _context.SaveChangesAsync();

            _logger.LogInformation("Booking {BookingId} successfully cancelled", id);
            return Ok(new
            {
                id = booking.Id,
                status = booking.Status,
                version = booking.Version,
                message = "Booking cancelled successfully."
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "DbUpdateConcurrencyException caught during cancellation for Booking {BookingId}", id);
            return Conflict(new
            {
                message = "Conflict: The booking was modified concurrently by another request."
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pex && pex.SqlState is "40001" or "23505")
        {
            _logger.LogWarning(ex, "Postgres serialization conflict during cancellation for Booking {BookingId}", id);
            return Conflict(new
            {
                message = "Conflict: Concurrent modification detected."
            });
        }
    }
}
