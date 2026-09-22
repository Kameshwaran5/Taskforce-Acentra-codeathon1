using Acentra.Models;
using Microsoft.EntityFrameworkCore;

namespace Acentra.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        if (!await context.Resources.AnyAsync())
        {
            var resources = new List<Resource>
            {
                new()
                {
                    Name = "Conference Room Apollo",
                    Type = "Meeting Room",
                    Location = "2nd Floor, West Wing",
                    Active = true
                },
                new()
                {
                    Name = "Boardroom Horizon",
                    Type = "Executive Boardroom",
                    Location = "5th Floor, Penthouse Suite",
                    Active = true
                },
                new()
                {
                    Name = "IoT & Robotics Lab",
                    Type = "Engineering Lab",
                    Location = "Ground Floor, Innovation Hub",
                    Active = true
                },
                new()
                {
                    Name = "4K Laser Presentation Rig",
                    Type = "AV Equipment",
                    Location = "Equipment Depot - Rack B",
                    Active = true
                }
            };

            await context.Resources.AddRangeAsync(resources);
            await context.SaveChangesAsync();

            // Seed demo bookings for today so calendar has visible sample blocks
            var todayUtc = DateTime.UtcNow.Date;
            var apollo = resources.First(r => r.Name == "Conference Room Apollo");
            var horizon = resources.First(r => r.Name == "Boardroom Horizon");
            var lab = resources.First(r => r.Name == "IoT & Robotics Lab");

            var sampleBookings = new List<Booking>
            {
                new()
                {
                    ResourceId = apollo.Id,
                    UserName = "Sarah Connor",
                    UserEmail = "sarah.c@sky.net",
                    StartUtc = todayUtc.AddHours(9).AddMinutes(0),
                    EndUtc = todayUtc.AddHours(10).AddMinutes(30),
                    Status = "Confirmed",
                    Version = Guid.NewGuid(),
                    OwnerToken = Guid.NewGuid(),
                    CreatedUtc = DateTime.UtcNow
                },
                new()
                {
                    ResourceId = apollo.Id,
                    UserName = "Alex Rivera",
                    UserEmail = "alex.r@venture.io",
                    StartUtc = todayUtc.AddHours(13).AddMinutes(0),
                    EndUtc = todayUtc.AddHours(14).AddMinutes(30),
                    Status = "Confirmed",
                    Version = Guid.NewGuid(),
                    OwnerToken = Guid.NewGuid(),
                    CreatedUtc = DateTime.UtcNow
                },
                new()
                {
                    ResourceId = horizon.Id,
                    UserName = "Marcus Vance",
                    UserEmail = "m.vance@acme.corp",
                    StartUtc = todayUtc.AddHours(10).AddMinutes(30),
                    EndUtc = todayUtc.AddHours(12).AddMinutes(0),
                    Status = "Confirmed",
                    Version = Guid.NewGuid(),
                    OwnerToken = Guid.NewGuid(),
                    CreatedUtc = DateTime.UtcNow
                },
                new()
                {
                    ResourceId = lab.Id,
                    UserName = "Dr. Elena Rostova",
                    UserEmail = "e.rostova@quantum.edu",
                    StartUtc = todayUtc.AddHours(14).AddMinutes(0),
                    EndUtc = todayUtc.AddHours(16).AddMinutes(30),
                    Status = "Confirmed",
                    Version = Guid.NewGuid(),
                    OwnerToken = Guid.NewGuid(),
                    CreatedUtc = DateTime.UtcNow
                }
            };

            await context.Bookings.AddRangeAsync(sampleBookings);
            await context.SaveChangesAsync();
        }
    }
}
