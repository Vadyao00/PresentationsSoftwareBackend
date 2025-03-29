using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PresentationsSoftware.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PresentationsSoftware.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PresentationsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly JsonSerializerOptions _jsonOptions;

    public PresentationsController(ApplicationDbContext context)
    {
        _context = context;
        _jsonOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Presentation>>> GetPresentations()
    {
        return await _context.Presentations
            .OrderByDescending(p => p.UploadDate)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Presentation>> GetPresentation(int id)
    {
        var presentation = await _context.Presentations
            .Include(p => p.Slides)
                .ThenInclude(s => s.Elements)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (presentation == null)
        {
            return NotFound();
        }

        var json = JsonSerializer.Serialize(presentation, _jsonOptions);
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<ActionResult<Presentation>> CreatePresentation(Presentation presentation)
    {
        presentation.UploadDate = DateTime.UtcNow;
            
        presentation.Slides.Add(new Slide { Order = 1 });

        _context.Presentations.Add(presentation);
        await _context.SaveChangesAsync();

        var json = JsonSerializer.Serialize(presentation, _jsonOptions);
        return Created($"/api/presentations/{presentation.Id}", json);
    }
}