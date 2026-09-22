using Acentra.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acentra.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResourcesController : ControllerBase
{
    private readonly AppDbContext _context;

    public ResourcesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetActiveResources()
    {
        var resources = await _context.Resources
            .AsNoTracking()
            .Where(r => r.Active)
            .OrderBy(r => r.Name)
            .ToListAsync();

        return Ok(resources);
    }
}
