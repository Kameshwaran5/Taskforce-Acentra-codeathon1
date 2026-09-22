# ReservePulse — Concurrency-Safe Resource Booking System

A high-performance MVP Resource Booking System built with **ASP.NET Core 8**, **Entity Framework Core**, **PostgreSQL 16**, and a **Vanilla JS Calendar Grid UI**.

This project was built to solve a critical real-world problem: **preventing two users from double-booking the same resource at the same time, even under simultaneous/concurrent race conditions**, and proving it with an automated test suite and a live interactive judge demo.

---

## Quick Start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker & Docker Compose](https://www.docker.com/) (or a local PostgreSQL 16 instance)

### 1. Start PostgreSQL with Docker Compose
```bash
docker compose up -d
```
*Spins up a single `postgres:16` container on port `5432` with database `resourcebooking`, username `postgres`, password `postgres`.*

### 2. Run the Web API & Calendar
```bash
dotnet run
```
On startup:
- EF Core automatically creates the database schema (`EnsureCreatedAsync`).
- Seeds 4 sample resources and active demo bookings (`DbSeeder.SeedAsync`).
- Serves the frontend at [http://localhost:5000](http://localhost:5000).

### 3. Run the Automated Concurrency Conflict Test
In a separate terminal, execute the test script:
```bash
./test_concurrency.sh
```
This script fires two simultaneous `curl` requests in parallel for the exact same resource and overlapping time window. It verifies that:
- Exactly **one request succeeds with HTTP 200 OK**.
- Exactly **one request is rejected with HTTP 409 Conflict** (SqlState 40001 serialization failure).
- **Zero HTTP 500 errors occur**.
- Cancelling the same booking concurrently also yields **1x 200 OK and 1x 409 Conflict**.

---

## How This Prevents Double-Booking

### 1. The Concurrency Challenge
In a typical booking system using the default database isolation level (`Read Committed`), two simultaneous requests for the same time slot can experience a **Read-Write Race Condition**:
1. User A checks if Resource 1 is booked at 14:00 &rarr; Database says *No*.
2. User B checks if Resource 1 is booked at 14:00 &rarr; Database says *No*.
3. User A inserts a booking & commits.
4. User B inserts a booking & commits.
5. **Result: Double booking disaster!**

### 2. PostgreSQL Serializable Snapshot Isolation (SSI)
To eliminate race conditions without crude global application locks, we run booking creation inside an explicit transaction set to `IsolationLevel.Serializable`:
```csharp
await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
```
PostgreSQL's Serializable isolation monitors the read/write dependencies between concurrent transactions. If two transactions read the same data and attempt conflicting writes, PostgreSQL identifies the cycle in the Serialization Graph and automatically aborts one of the transactions.

### 3. Commit-Time Exception Handling (SqlState `40001`)
In PostgreSQL, serialization conflicts often surface **at `COMMIT` time**, not when executing the `INSERT` or `SELECT`. When this happens:
- It throws a raw `Npgsql.PostgresException` with `SqlState = "40001"` (`serialization_failure`).
- It does **not** throw `DbUpdateConcurrencyException` or `DbUpdateException`.

If an application only catches standard EF Core exceptions, a simultaneous booking race will surface as an **unhandled 500 Internal Server Error**.

In `BookingsController.cs`, we explicitly filter on Postgres SqlState `40001` and `23505`:
```csharp
catch (Npgsql.PostgresException ex) when (ex.SqlState is "40001" or "23505")
{
    await transaction.RollbackAsync();
    return Conflict(new
    {
        message = "Conflict: Concurrent booking detected. Another user booked this slot simultaneously.",
        code = ex.SqlState
    });
}
```
This guarantees that whenever two requests race, the loser always receives a clean **HTTP 409 Conflict**, never a 500.

### 4. Optimistic Concurrency Token on Cancellation
For booking cancellations, each booking has a `Guid Version` configured in EF Core:
```csharp
modelBuilder.Entity<Booking>().Property(b => b.Version).IsConcurrencyToken();
```
- When a client sends a cancellation request, it provides the booking's last-known `Version`.
- **Fast-Path Check**: If the stored version does not match the client's version, the API immediately returns `409 Conflict`.
- **Race Check**: If two clients race past the check simultaneously, EF Core issues `UPDATE ... WHERE "Id" = @id AND "Version" = @originalVersion`. The second update matches 0 rows, triggering `DbUpdateConcurrencyException`, which is caught and returned as `409 Conflict`.

### 5. Token-Based Cancellation Access Control (No Accounts Required)
To secure cancellations without requiring full authentication or account systems:
- **Private Per-Booking Secret**: At creation time, the server generates a separate `OwnerToken` (`Guid.NewGuid()`).
- **Never Exposed via Public API**: `GET /api/bookings` strictly projects into `BookingResponseDto`, omitting `OwnerToken`. Other users inspecting public schedules cannot see or forge this secret.
- **Returned Exactly Once**: The `OwnerToken` is returned only once in the HTTP 200 response of `POST /api/bookings`.
- **Client Storage**: The creating browser saves `{ [bookingId]: ownerToken }` to `localStorage`.
- **Authorization Enforcement (HTTP 403)**: `DELETE /api/bookings/{id}` checks `OwnerToken`. If the token is missing or does not match the database, the server returns **HTTP 403 Forbidden** (`"You can only cancel your own bookings."`). This is kept strictly distinct from **HTTP 409 Conflict** (which indicates genuine concurrency collisions).
- **UI State**: In the calendar UI, the **"Cancel Booking"** button is rendered only if `localStorage` holds the valid token; otherwise, the reservation is presented in read-only mode (`"Booked by X"`).

---

## Live Judge Demo (UI)

Open [http://localhost:5000](http://localhost:5000):
1. **Interactive Day-View Grid**: Resources as rows, 8 AM–8 PM hourly columns, with booking blocks positioned with sub-hour minute precision (e.g. 10:30–11:30).
2. **"+ New Booking"**: Click empty slots to create bookings with real-time conflict validation.
3. **"Live Conflict Demo" Button**: Click the amber lightning button in the toolbar to launch the interactive race arena:
   - Client 1 (Alice) and Client 2 (Bob) fire simultaneous POST requests at the exact same millisecond.
   - Watch the live split-second outcome: **Alice 200 OK** vs **Bob 409 Conflict** with raw JSON responses and zero 500s!
4. **Inspect Concurrency Token**: Click any confirmed booking to view its `Guid Version` token and test optimistic concurrency cancellation.

---

## Project Structure
```
├── Program.cs                          # App entry point, static files, EF Core setup
├── ResourceBooking.csproj / Acentra.csproj # Project file (.NET 8, Npgsql EF Core 8)
├── appsettings.json                    # PostgreSQL connection string
├── docker-compose.yml                  # PostgreSQL 16 container definition
├── test_concurrency.sh                 # Concurrency bash test suite
├── Models/
│   ├── Resource.cs                     # Resource model (Unique name index)
│   └── Booking.cs                      # Booking model (Version concurrency token)
├── Data/
│   ├── AppDbContext.cs                 # EF Core DbContext with indexes and tokens
│   └── DbSeeder.cs                     # Startup seeder for 4 resources & demo bookings
├── DTOs/
│   ├── CreateBookingRequest.cs         # Creation payload validation
│   ├── CancelBookingRequest.cs         # Cancellation payload with Version token
│   └── BookingResponseDto.cs           # Serialized response model
├── Controllers/
│   ├── ResourcesController.cs          # GET /api/resources (Active only)
│   └── BookingsController.cs           # GET, POST (Serializable), DELETE (Version check)
└── wwwroot/
    ├── index.html                      # Calendar grid HTML & Live Conflict Demo modal
    ├── styles.css                      # Modern dark theme & responsive styles
    └── app.js                          # Calendar rendering & race simulator logic
```
