using CMIS_IyaSoft.Services;
using Microsoft.AspNetCore.Mvc;

namespace CMIS_IyaSoft.Controllers;

[ApiController]
[Route("browser")]
public class CmisController : ControllerBase
{
    private readonly ICmisService _cmisService;

    public CmisController(ICmisService cmisService)
    {
        _cmisService = cmisService;
    }

    // GET /browser
    // Returns repository info or type definitions based on ?cmisselector
    [HttpGet]
    public async Task<IActionResult> GetRepository([FromQuery] string? cmisselector)
    {
        if (string.Equals(cmisselector, "types", StringComparison.OrdinalIgnoreCase))
        {
            var types = await _cmisService.GetTypesAsync();
            return Ok(new { typeDefinitions = types });
        }

        // Default Repository Info Response (CMIS 1.1 compliant)
        var repoInfo = new
        {
            defaultInfo = new
            {
                repositoryId = "mini-cmis-repo",
                repositoryName = "Mini CMIS Repository",
                repositoryDescription = "IyaSoft Mini CMIS Engine",
                vendorName = "IyaSoft",
                productName = "MiniCMIS API",
                productVersion = "1.0.0",
                rootFolderId = "root-folder",
                cmisVersionSupported = "1.1"
            }
        };

        return Ok(repoInfo);
    }

    // GET /browser/{repositoryId}/root (or any path/object)
    // Handles ?cmisselector=children and ?cmisselector=content
    [HttpGet("{repositoryId}/{objectId}")]
    public async Task<IActionResult> GetObject(
        string repositoryId,
        string objectId,
        [FromQuery] string? cmisselector)
    {
        // 1. Selector: children -> list folder content
        if (string.Equals(cmisselector, "children", StringComparison.OrdinalIgnoreCase))
        {
            var children = await _cmisService.GetChildrenAsync(objectId);
            return Ok(new { objects = children });
        }

        // 2. Selector: content -> download binary stream
        if (string.Equals(cmisselector, "content", StringComparison.OrdinalIgnoreCase))
        {
            var streamResult = await _cmisService.GetContentStreamAsync(objectId);
            if (streamResult == null || streamResult.Value.Content == null)
            {
                return NotFound(new { error = "Content stream not found for this object." });
            }

            var (content, mimeType, fileName) = streamResult.Value;
            return File(content, mimeType, fileName);
        }

        // 3. Default: return object metadata
        var cmisObject = await _cmisService.GetObjectByIdAsync(objectId);
        if (cmisObject == null)
        {
            return NotFound(new { error = $"Object with ID '{objectId}' was not found." });
        }

        return Ok(cmisObject);
    }

    // POST /browser/{repositoryId}/{folderId}
    // Handles cmisaction=createDocument and cmisaction=createFolder
    [HttpPost("{repositoryId}/{folderId}")]
    public async Task<IActionResult> PostObject(
        [FromRoute] string repositoryId,
        [FromRoute] string folderId,
        [FromForm] string cmisaction,
        [FromForm] string? name,
        IFormFile? file)
    {
        if (string.Equals(cmisaction, "createDocument", StringComparison.OrdinalIgnoreCase))
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "File content is required for createDocument action." });
            }

            var docName = string.IsNullOrWhiteSpace(name) ? file.FileName : name;
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            var createdDoc = await _cmisService.CreateDocumentAsync(
                folderId,
                docName,
                file.ContentType ?? "application/octet-stream",
                fileBytes
            );

            return CreatedAtAction(nameof(GetObject), new { repositoryId, objectId = createdDoc.Id }, createdDoc);
        }

        if (string.Equals(cmisaction, "createFolder", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { error = "Folder name is required for createFolder action." });
            }

            var createdFolder = await _cmisService.CreateFolderAsync(folderId, name);
            return CreatedAtAction(nameof(GetObject), new { repositoryId, objectId = createdFolder.Id }, createdFolder);
        }

        return BadRequest(new { error = $"Unsupported cmisaction '{cmisaction}'." });
    }
}