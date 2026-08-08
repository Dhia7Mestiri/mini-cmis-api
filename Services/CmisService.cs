using CMIS_IyaSoft.Data;
using CMIS_IyaSoft.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMIS_IyaSoft.Services;

public class CmisService : ICmisService
{
    private readonly AppDbContext _context;

    public CmisService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<CmisType>> GetTypesAsync()
    {
        return await _context.Types.ToListAsync();
    }

    public async Task<IEnumerable<CmisObject>> GetChildrenAsync(string folderId)
    {
        return await _context.Objects
            .Where(o => o.ParentId == folderId)
            .ToListAsync();
    }

    public async Task<CmisObject?> GetObjectByIdAsync(string objectId)
    {
        return await _context.Objects
            .FirstOrDefaultAsync(o => o.Id == objectId);
    }

    public async Task<(byte[]? Content, string? MimeType, string Name)?> GetContentStreamAsync(string objectId)
    {
        var doc = await _context.Objects.FirstOrDefaultAsync(o => o.Id == objectId);

        if (doc == null || doc.ContentStream == null)
        {
            return null;
        }

        return (doc.ContentStream, doc.MimeType ?? "application/octet-stream", doc.Name);
    }
}