using System.Text.Json;
using CMIS_IyaSoft.Entities;
using CMIS_IyaSoft.Services;
using Microsoft.AspNetCore.Authorization;
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
    // cmisselector=repositoryInfo|types|typeDefinition|query (default = discovery / repository info)
    [HttpGet]
    public async Task<IActionResult> GetRepository(
        [FromQuery] string? cmisselector,
        [FromQuery] string? typeId,
        [FromQuery] string? q,
        [FromQuery] int maxItems = 100,
        [FromQuery] int skipCount = 0)
    {
        if (
            string.Equals(cmisselector, "typeChildren", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cmisselector, "types", StringComparison.OrdinalIgnoreCase)
        )
        {
            try
            {
                var types = await _cmisService.GetTypeChildrenAsync(typeId);

                return Ok(new
                {
                    types
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    exception = "objectNotFound",
                    message = ex.Message
                });
            }
        }

        if (string.Equals(cmisselector, "typeDefinition", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(typeId))
            {
                return BadRequest(new { exception = "invalidArgument", message = "'typeId' query parameter is required for typeDefinition selector." });
            }

            // Merges the fixed system properties with any DB-driven custom
            // TypePropertyDefinitions for this type.
            var typeDefinition = await _cmisService.GetTypeDefinitionAsync(typeId);
            if (typeDefinition == null)
            {
                return NotFound(new { exception = "objectNotFound", message = $"Type '{typeId}' was not found." });
            }

            return Ok(typeDefinition);
        }

        if (string.Equals(cmisselector, "query", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest(new { exception = "invalidArgument", message = "Search query parameter 'q' is required for query selector." });
            }

            var results = await _cmisService.SearchObjectsAsync(q);
            return Ok(new { results });
        }

        // Default: repository discovery info (getRepositories equivalent).
        // Exposes both working URLs the spec asks the client to use for all subsequent calls.
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
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
                cmisVersionSupported = "1.1",
                repositoryUrl = $"{baseUrl}/browser",
                rootFolderUrl = $"{baseUrl}/browser/mini-cmis-repo/root-folder"
            }
        };

        return Ok(repoInfo);
    }


    // GET /browser/{repositoryId}/{objectId}
    // cmisselector=children|content|parents|object (default)
    [HttpGet("{repositoryId}/{objectId}")]
    [Authorize(Roles = "Admin,Manager,User")]
    public async Task<IActionResult> GetObject(
        [FromRoute] string repositoryId,
        [FromRoute] string objectId,
        [FromQuery] string? cmisselector)
    {
        if (string.Equals(cmisselector, "children", StringComparison.OrdinalIgnoreCase))
        {
            var children = await _cmisService.GetChildrenEnvelopesAsync(objectId);
            return Ok(new { objects = children });
        }

        if (string.Equals(cmisselector, "content", StringComparison.OrdinalIgnoreCase))
        {
            var streamResult = await _cmisService.GetContentStreamAsync(objectId);
            if (streamResult == null || streamResult.Value.Content == null)
            {
                return NotFound(new { exception = "objectNotFound", message = "Content stream not found for this object." });
            }

            var (content, mimeType, fileName) = streamResult.Value;
            return File(content, mimeType, fileName);
        }

        if (string.Equals(cmisselector, "parents", StringComparison.OrdinalIgnoreCase))
        {
            var parents = await _cmisService.GetParentsEnvelopesAsync(objectId);
            return Ok(new { objects = parents });
        }

        var envelope = await _cmisService.GetObjectEnvelopeAsync(objectId);
        if (envelope == null)
        {
            return NotFound(new { exception = "objectNotFound", message = $"Object with ID '{objectId}' was not found." });
        }

        return Ok(envelope);
    }

    // POST /browser/{repositoryId}/{objectId}
    // cmisaction=createDocument|createFolder|update|move|delete|deleteTree|setContentStream
    //
    // 'properties' is an optional form field containing a JSON object of custom
    // property id -> value (or array of values for multi-valued properties), e.g.
    // properties={"custom:department":"Finance"}. A null/empty value clears the
    // property on update ("vider une propriété").
    [HttpPost("{repositoryId}/{objectId}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> PostObject(
        [FromRoute] string repositoryId,
        [FromRoute] string objectId,
        [FromForm] string cmisaction,
[FromForm] string? name,
[FromForm] string? targetFolderId,
[FromForm] string? objectTypeId,
[FromForm] string? properties,
IFormFile? file)
    {
        if (string.Equals(cmisaction, "delete", StringComparison.OrdinalIgnoreCase))
        {
            if (!User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var success = await _cmisService.DeleteObjectAsync(objectId);
            if (!success)
            {
                return NotFound(new { exception = "objectNotFound", message = $"Object with ID '{objectId}' was not found." });
            }

            return NoContent();
        }

        if (string.Equals(cmisaction, "deleteTree", StringComparison.OrdinalIgnoreCase))
        {
            if (!User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var deletedCount = await _cmisService.DeleteTreeAsync(objectId);
            return Ok(new { deletedCount });
        }

        if (string.Equals(cmisaction, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(properties))
            {
                return BadRequest(new { exception = "invalidArgument", message = "Provide 'name' and/or 'properties' for update action." });
            }

            var updated = await _cmisService.UpdateObjectAsync(objectId, name, properties);
            return Ok(updated);
        }

        if (string.Equals(cmisaction, "setContentStream", StringComparison.OrdinalIgnoreCase))
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { exception = "invalidArgument", message = "File content is required for setContentStream action." });
            }

            using var replaceStream = new MemoryStream();
            await file.CopyToAsync(replaceStream);

            var updatedDoc = await _cmisService.SetContentStreamAsync(
                objectId, file.ContentType ?? "application/octet-stream", replaceStream.ToArray());

            return Ok(updatedDoc);
        }

        if (string.Equals(cmisaction, "move", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(targetFolderId))
            {
                return BadRequest(new { exception = "invalidArgument", message = "'targetFolderId' is required for move action." });
            }

            var moved = await _cmisService.MoveObjectAsync(objectId, targetFolderId);
            return Ok(moved);
        }

        if (string.Equals(cmisaction, "createDocument", StringComparison.OrdinalIgnoreCase))
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { exception = "invalidArgument", message = "File content is required for createDocument action." });
            }

            var docName = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileName(file.FileName)
                : name;

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            var requestedTypeId =
                string.IsNullOrWhiteSpace(objectTypeId)
                    ? "cmis:document"
                    : objectTypeId;

            var createdDoc = await _cmisService.CreateDocumentAsync(
                objectId,
                docName,
                file.ContentType ?? "application/octet-stream",
                fileBytes,
                requestedTypeId,
                properties);

            return CreatedAtAction(nameof(GetObject), new { repositoryId, objectId = createdDoc.Id }, createdDoc);
        }

        if (string.Equals(cmisaction, "createFolder", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { exception = "invalidArgument", message = "Folder name is required for createFolder action." });
            }

            var createdFolder = await _cmisService.CreateFolderAsync(objectId, name, properties);
            return CreatedAtAction(nameof(GetObject), new { repositoryId, objectId = createdFolder.Id }, createdFolder);
        }

        if (string.Equals(cmisaction, "query", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { exception = "invalidArgument", message = "Use POST /browser?cmisaction=query with a 'statement' form field for CMIS-SQL queries." });
        }

        return BadRequest(new { exception = "notSupported", message = $"Unsupported cmisaction '{cmisaction}'." });
    }

    // POST /browser
    // cmisaction=query - CMIS-SQL query endpoint, on the repository URL per spec
    [HttpPost]
    [Authorize(Roles = "Admin,Manager,User")]
    public async Task<IActionResult> PostRepository(
        [FromForm] string cmisaction,
        [FromForm] string? statement,
        [FromForm] string? typeId,
        [FromForm] string? type,
        [FromForm] int maxItems = 100,
        [FromForm] int skipCount = 0)
    {
        try
        {
            if (string.Equals(cmisaction, "query", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(statement))
                {
                    return BadRequest(new { exception = "invalidArgument", message = "'statement' form field is required for query action." });
                }

                var (results, numItems, hasMoreItems) =
                    await _cmisService.ExecuteQueryAsync(statement, maxItems, skipCount);

                return Ok(new { results, numItems, hasMoreItems });
            }

            if (string.Equals(cmisaction, "createType", StringComparison.OrdinalIgnoreCase))
            {
                if (!User.IsInRole("Admin")) return Forbid();
                if (string.IsNullOrWhiteSpace(type))
                    return BadRequest(new { exception = "invalidArgument", message = "'type' form field is required and must contain a JSON type definition." });

                CreateCmisTypeRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<CreateCmisTypeRequest>(
                        type,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException ex)
                {
                    return BadRequest(new { exception = "invalidArgument", message = $"Invalid type JSON: {ex.Message}" });
                }

                if (request == null)
                    return BadRequest(new { exception = "invalidArgument", message = "Type definition could not be parsed." });

                var created = await _cmisService.CreateTypeAsync(request);
                return StatusCode(StatusCodes.Status201Created, created);
            }

            if (string.Equals(cmisaction, "updateType", StringComparison.OrdinalIgnoreCase))
            {
                if (!User.IsInRole("Admin")) return Forbid();
                if (string.IsNullOrWhiteSpace(typeId))
                    return BadRequest(new { exception = "invalidArgument", message = "'typeId' form field is required for updateType." });
                if (string.IsNullOrWhiteSpace(type))
                    return BadRequest(new { exception = "invalidArgument", message = "'type' form field is required and must contain a JSON update definition." });

                UpdateCmisTypeRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<UpdateCmisTypeRequest>(
                        type,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException ex)
                {
                    return BadRequest(new { exception = "invalidArgument", message = $"Invalid type JSON: {ex.Message}" });
                }

                if (request == null)
                    return BadRequest(new { exception = "invalidArgument", message = "Type update definition could not be parsed." });

                var updated = await _cmisService.UpdateTypeAsync(typeId, request);
                return Ok(updated);
            }

            if (string.Equals(cmisaction, "deleteType", StringComparison.OrdinalIgnoreCase))
            {
                if (!User.IsInRole("Admin")) return Forbid();
                if (string.IsNullOrWhiteSpace(typeId))
                    return BadRequest(new { exception = "invalidArgument", message = "'typeId' form field is required for deleteType." });

                await _cmisService.DeleteTypeAsync(typeId);
                return NoContent();
            }

            return BadRequest(new { exception = "notSupported", message = $"Unsupported cmisaction '{cmisaction}' on repository URL." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { exception = "objectNotFound", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { exception = "constraint", message = ex.Message });
        }
    }

}