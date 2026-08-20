using CMIS_IyaSoft.Data;
using CMIS_IyaSoft.Entities;
using CMIS_IyaSoft.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CMIS_IyaSoft.Tests.UnitTests;

public class TypeManagementTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly CmisService _sut;

    public TypeManagementTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);

        SeedBaseTypes();

        _sut = new CmisService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private void SeedBaseTypes()
    {
        _context.Types.AddRange(
            new CmisType
            {
                Id = "cmis:document",
                BaseId = "cmis:document",
                DisplayName = "Document",
                Description = "CMIS Document Type",
                ParentTypeId = null
            },
            new CmisType
            {
                Id = "cmis:folder",
                BaseId = "cmis:folder",
                DisplayName = "Folder",
                Description = "CMIS Folder Type",
                ParentTypeId = null
            });

        _context.SaveChanges();
    }

    private async Task<CmisObject> SeedRootFolderAsync()
    {
        var root = new CmisObject
        {
            Id = "root-folder",
            Name = "root-folder",
            TypeId = "cmis:folder",
            ParentId = null,
            Path = "/",
            CreatedBy = "test",
            CreationDate = DateTime.UtcNow,
            LastModificationDate = DateTime.UtcNow
        };

        _context.Objects.Add(root);
        await _context.SaveChangesAsync();

        return root;
    }

    private static CreateCmisTypeRequest ContractRequest(
        string parentTypeId = "cmis:document")
    {
        return new CreateCmisTypeRequest
        {
            Id = "custom:contract",
            DisplayName = "Contract",
            Description = "Contract document type",
            ParentTypeId = parentTypeId,
            PropertyDefinitions =
            [
                new CmisTypePropertyRequest
                {
                    PropertyId = "custom:contractNumber",
                    LocalName = "contractNumber",
                    PropertyType = "string",
                    Cardinality = "single",
                    Updatability = "readwrite",
                    Required = true
                },
                new CmisTypePropertyRequest
                {
                    PropertyId = "custom:expiryDate",
                    LocalName = "expiryDate",
                    PropertyType = "datetime",
                    Cardinality = "single",
                    Updatability = "readwrite",
                    Required = false
                }
            ]
        };
    }

    // ---------------------------------------------------------
    // createType
    // ---------------------------------------------------------

    [Fact]
    public async Task CreateTypeAsync_Creates_Derived_Type_And_Own_Properties()
    {
        var created = await _sut.CreateTypeAsync(ContractRequest());

        Assert.Equal("custom:contract", created.Id);
        Assert.Equal("cmis:document", created.BaseId);
        Assert.Equal("cmis:document", created.ParentTypeId);
        Assert.Equal("Contract", created.DisplayName);

        var storedType = await _context.Types
            .SingleAsync(t => t.Id == "custom:contract");

        Assert.Equal("cmis:document", storedType.BaseId);
        Assert.Equal("cmis:document", storedType.ParentTypeId);

        var ownProperties = await _context.TypePropertyDefinitions
            .Where(p => p.TypeId == "custom:contract")
            .OrderBy(p => p.PropertyId)
            .ToListAsync();

        Assert.Equal(2, ownProperties.Count);
        Assert.Contains(
            ownProperties,
            p => p.PropertyId == "custom:contractNumber" && p.Required);

        Assert.Contains(
            ownProperties,
            p => p.PropertyId == "custom:expiryDate" &&
                 p.PropertyType == "datetime");
    }

    [Fact]
    public async Task CreateTypeAsync_Rejects_Duplicate_Type_Id()
    {
        await _sut.CreateTypeAsync(ContractRequest());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateTypeAsync(ContractRequest()));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task CreateTypeAsync_Rejects_Missing_Parent()
    {
        var request = ContractRequest("custom:missingParent");

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.CreateTypeAsync(request));

        Assert.Contains("Parent type", ex.Message);
    }

    [Fact]
    public async Task CreateTypeAsync_Rejects_Reserved_Cmis_Namespace()
    {
        var request = ContractRequest();
        request.Id = "cmis:contract";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateTypeAsync(request));

        Assert.Contains("cmis:", ex.Message);
    }

    [Fact]
    public async Task CreateTypeAsync_Rejects_Property_Already_Inherited()
    {
        _context.TypePropertyDefinitions.Add(
            new TypePropertyDefinition
            {
                TypeId = "cmis:document",
                PropertyId = "custom:department",
                LocalName = "department",
                PropertyType = "string",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = false
            });

        await _context.SaveChangesAsync();

        var request = ContractRequest();
        request.PropertyDefinitions.Add(
            new CmisTypePropertyRequest
            {
                PropertyId = "custom:department",
                LocalName = "department",
                PropertyType = "string",
                Cardinality = "single",
                Updatability = "readwrite"
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateTypeAsync(request));

        Assert.Contains("inherited", ex.Message);
    }

    // ---------------------------------------------------------
    // inheritance / read side
    // ---------------------------------------------------------

    [Fact]
    public async Task GetTypeChildrenAsync_Returns_Dynamically_Created_Child()
    {
        await _sut.CreateTypeAsync(ContractRequest());

        var children = (await _sut.GetTypeChildrenAsync("cmis:document"))
            .ToList();

        Assert.Contains(children, t => t.Id == "custom:contract");
    }

    [Fact]
    public async Task GetTypeDefinitionAsync_Includes_Parent_And_Own_Properties()
    {
        _context.TypePropertyDefinitions.Add(
            new TypePropertyDefinition
            {
                TypeId = "cmis:document",
                PropertyId = "custom:department",
                LocalName = "department",
                PropertyType = "string",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = false
            });

        await _context.SaveChangesAsync();

        await _sut.CreateTypeAsync(ContractRequest());

        var definition =
            await _sut.GetTypeDefinitionAsync("custom:contract");

        Assert.NotNull(definition);
        Assert.Equal("cmis:document", definition!.ParentTypeId);

        Assert.Contains(
            definition.PropertyDefinitions,
            p => p.Id == "custom:department");

        Assert.Contains(
            definition.PropertyDefinitions,
            p => p.Id == "custom:contractNumber");

        Assert.Contains(
            definition.PropertyDefinitions,
            p => p.Id == "custom:expiryDate");
    }

    // ---------------------------------------------------------
    // updateType
    // ---------------------------------------------------------

    [Fact]
    public async Task UpdateTypeAsync_Updates_Metadata_And_Own_Properties()
    {
        await _sut.CreateTypeAsync(ContractRequest());

        var update = new UpdateCmisTypeRequest
        {
            DisplayName = "Business Contract",
            Description = "Updated description",
            ParentTypeId = "cmis:document",
            PropertyDefinitions =
            [
                new CmisTypePropertyRequest
                {
                    PropertyId = "custom:contractNumber",
                    LocalName = "contractNumber",
                    PropertyType = "string",
                    Cardinality = "single",
                    Updatability = "readwrite",
                    Required = true
                },
                new CmisTypePropertyRequest
                {
                    PropertyId = "custom:status",
                    LocalName = "status",
                    PropertyType = "string",
                    Cardinality = "single",
                    Updatability = "readwrite",
                    Required = false
                }
            ]
        };

        var updated = await _sut.UpdateTypeAsync(
            "custom:contract",
            update);

        Assert.Equal("Business Contract", updated.DisplayName);
        Assert.Equal("Updated description", updated.Description);

        var ownProperties = await _context.TypePropertyDefinitions
            .Where(p => p.TypeId == "custom:contract")
            .ToListAsync();

        Assert.Equal(2, ownProperties.Count);
        Assert.Contains(
            ownProperties,
            p => p.PropertyId == "custom:contractNumber");
        Assert.Contains(
            ownProperties,
            p => p.PropertyId == "custom:status");
        Assert.DoesNotContain(
            ownProperties,
            p => p.PropertyId == "custom:expiryDate");
    }

    [Theory]
    [InlineData("cmis:document")]
    [InlineData("cmis:folder")]
    public async Task UpdateTypeAsync_Rejects_Base_Cmis_Types(
        string typeId)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateTypeAsync(
                typeId,
                new UpdateCmisTypeRequest
                {
                    DisplayName = "Changed"
                }));

        Assert.Contains("cannot be modified", ex.Message);
    }

    [Fact]
    public async Task UpdateTypeAsync_Rejects_Inheritance_Cycle()
    {
        await _sut.CreateTypeAsync(ContractRequest());

        await _sut.CreateTypeAsync(
            new CreateCmisTypeRequest
            {
                Id = "custom:specialContract",
                DisplayName = "Special Contract",
                ParentTypeId = "custom:contract"
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateTypeAsync(
                "custom:contract",
                new UpdateCmisTypeRequest
                {
                    ParentTypeId = "custom:specialContract"
                }));

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------
    // deleteType
    // ---------------------------------------------------------

    [Fact]
    public async Task DeleteTypeAsync_Deletes_Unused_Leaf_Type_And_Its_Properties()
    {
        await _sut.CreateTypeAsync(ContractRequest());

        await _sut.DeleteTypeAsync("custom:contract");

        Assert.False(
            await _context.Types.AnyAsync(
                t => t.Id == "custom:contract"));

        Assert.False(
            await _context.TypePropertyDefinitions.AnyAsync(
                p => p.TypeId == "custom:contract"));
    }

    [Fact]
    public async Task DeleteTypeAsync_Rejects_Type_With_Children()
    {
        await _sut.CreateTypeAsync(ContractRequest());

        await _sut.CreateTypeAsync(
            new CreateCmisTypeRequest
            {
                Id = "custom:specialContract",
                DisplayName = "Special Contract",
                ParentTypeId = "custom:contract"
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteTypeAsync("custom:contract"));

        Assert.Contains("child types", ex.Message);
    }

    [Fact]
    public async Task DeleteTypeAsync_Rejects_Type_Used_By_Repository_Object()
    {
        var root = await SeedRootFolderAsync();

        await _sut.CreateTypeAsync(ContractRequest());

        await _sut.CreateDocumentAsync(
            root.Id,
            "contract.pdf",
            "application/pdf",
            [1, 2, 3],
            "custom:contract",
            """
            {
                "custom:contractNumber": "CTR-001"
            }
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteTypeAsync("custom:contract"));

        Assert.Contains("objects use it", ex.Message);
    }

    [Theory]
    [InlineData("cmis:document")]
    [InlineData("cmis:folder")]
    public async Task DeleteTypeAsync_Rejects_Base_Cmis_Types(
        string typeId)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteTypeAsync(typeId));

        Assert.Contains("cannot be deleted", ex.Message);
    }

    // ---------------------------------------------------------
    // real use of dynamically-created type
    // ---------------------------------------------------------

    [Fact]
    public async Task CreateDocumentAsync_Can_Use_Dynamically_Created_Type()
    {
        var root = await SeedRootFolderAsync();

        await _sut.CreateTypeAsync(ContractRequest());

        var document = await _sut.CreateDocumentAsync(
            root.Id,
            "contract.pdf",
            "application/pdf",
            [10, 20, 30],
            "custom:contract",
            """
            {
                "custom:contractNumber": "CTR-2026-001",
                "custom:expiryDate": "2027-08-20T10:00:00Z"
            }
            """);

        Assert.Equal("custom:contract", document.TypeId);

        var values = await _context.ObjectProperties
            .Where(p => p.ObjectId == document.Id)
            .ToDictionaryAsync(p => p.PropertyId, p => p.Value);

        Assert.Equal("CTR-2026-001", values["custom:contractNumber"]);
        Assert.Contains("2027-08-20", values["custom:expiryDate"]);
    }

    [Fact]
    public async Task CreateDocumentAsync_Enforces_Required_Property_From_Dynamic_Type()
    {
        var root = await SeedRootFolderAsync();

        await _sut.CreateTypeAsync(ContractRequest());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateDocumentAsync(
                root.Id,
                "contract.pdf",
                "application/pdf",
                [1],
                "custom:contract"));

        Assert.Contains("custom:contractNumber", ex.Message);
    }
}