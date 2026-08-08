using CMIS_IyaSoft.Services;
using Microsoft.AspNetCore.Mvc;

namespace CMIS_IyaSoft.Controllers;

[ApiController]
[Route("api/cmis")]
public class CmisController : ControllerBase
{
    private readonly ICmisService _cmisService;

    // Injecting ICmisService via Constructor Injection
    public CmisController(ICmisService cmisService)
    {
        _cmisService = cmisService;
    }

    // 1. Get Repository Info / Types
    // GET: api/cmis/types
    [HttpGet("types")]
    public async Task<IActionResult> GetTypes()
    {
        var types = await _cmisService.GetTypesAsync();
        return Ok(types);
    }

    // 2. Get Object by ID
    // GET: api/cmis/objects/{id}
    [HttpGet("objects/{id}")]
    public async Task<IActionResult> GetObjectById(string id)
    {
        var cmisObject = await _cmisService.GetObjectByIdAsync(id);

        if (cmisObject == null)
        {
            return NotFound(new { error = "ObjectNotFound", message = $"Object with ID '{id}' was not found." });
        }

        return Ok(cmisObject);
    }

    // 3. Get Folder Children (CMIS Navigation)
    // GET: api/cmis/objects/{id}/children
    [HttpGet("objects/{id}/children")]
    public async Task<IActionResult> GetChildren(string id)
    {
        // First check if parent folder exists
        var folder = await _cmisService.GetObjectByIdAsync(id);
        if (folder == null)
        {
            return NotFound(new { error = "ObjectNotFound", message = $"Folder with ID '{id}' was not found." });
        }

        var children = await _cmisService.GetChildrenAsync(id);
        return Ok(children);
    }
}