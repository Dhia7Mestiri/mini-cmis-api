using CMIS_IyaSoft.Data;
using CMIS_IyaSoft.Entities;
using CMIS_IyaSoft.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CMIS_IyaSoft.Tests.UnitTests;

// Real tests against the actual CmisService - grounded in the real repo code,
// not guessed method names. Each test creates a fresh isolated InMemory DB.
public class CmisServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly CmisService _sut;

    public CmisServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _sut = new CmisService(_context);
    }

    public void Dispose() => _context.Dispose();

    private async Task<CmisObject> SeedRootFolderAsync()
    {
        var root = new CmisObject
        {
            Id = "root-folder",
            Name = "root-folder",
            TypeId = "cmis:folder",
            ParentId = null,
            Path = "/"
        };
        _context.Objects.Add(root);
        await _context.SaveChangesAsync();
        return root;
    }

    // ---------------- CreateFolderAsync ----------------

    [Fact]
    public async Task CreateFolderAsync_Creates_Folder_With_Correct_Path()
    {
        var root = await SeedRootFolderAsync();

        var folder = await _sut.CreateFolderAsync(root.Id, "Reports");

        Assert.Equal("Reports", folder.Name);
        Assert.Equal("cmis:folder", folder.TypeId);
        Assert.Equal(root.Id, folder.ParentId);
        Assert.Equal("/Reports", folder.Path);
    }

    [Fact]
    public async Task CreateFolderAsync_Throws_When_Name_Already_Exists_In_Same_Parent()
    {
        var root = await SeedRootFolderAsync();
        await _sut.CreateFolderAsync(root.Id, "Reports");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateFolderAsync(root.Id, "Reports"));
    }

    [Fact]
    public async Task CreateFolderAsync_Allows_Same_Name_In_Different_Parents()
    {
        var root = await SeedRootFolderAsync();
        var folderA = await _sut.CreateFolderAsync(root.Id, "A");
        var folderB = await _sut.CreateFolderAsync(root.Id, "B");

        var nested1 = await _sut.CreateFolderAsync(folderA.Id, "Shared");
        var nested2 = await _sut.CreateFolderAsync(folderB.Id, "Shared");

        Assert.Equal("/A/Shared", nested1.Path);
        Assert.Equal("/B/Shared", nested2.Path);
    }

    // ---------------- CreateDocumentAsync ----------------

    [Fact]
    public async Task CreateDocumentAsync_Stores_Content_And_Metadata_Correctly()
    {
        var root = await SeedRootFolderAsync();
        var bytes = System.Text.Encoding.UTF8.GetBytes("hello world");

        var doc = await _sut.CreateDocumentAsync(root.Id, "notes.txt", "text/plain", bytes);

        Assert.Equal("notes.txt", doc.Name);
        Assert.Equal("cmis:document", doc.TypeId);
        Assert.Equal("text/plain", doc.MimeType);
        Assert.Equal(bytes.Length, doc.ContentStreamLength);
        Assert.Equal(bytes, doc.ContentStream);
    }

    [Fact]
    public async Task CreateDocumentAsync_Throws_When_Name_Already_Exists_In_Same_Parent()
    {
        var root = await SeedRootFolderAsync();
        await _sut.CreateDocumentAsync(root.Id, "notes.txt", "text/plain", new byte[] { 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateDocumentAsync(root.Id, "notes.txt", "text/plain", new byte[] { 2 }));
    }

    // ---------------- GetChildrenAsync ----------------

    [Fact]
    public async Task GetChildrenAsync_Returns_Only_Direct_Children()
    {
        var root = await SeedRootFolderAsync();
        await _sut.CreateFolderAsync(root.Id, "A");
        await _sut.CreateFolderAsync(root.Id, "B");
        var nestedParent = await _sut.CreateFolderAsync(root.Id, "C");
        await _sut.CreateFolderAsync(nestedParent.Id, "Grandchild - should not appear");

        var children = await _sut.GetChildrenAsync(root.Id);

        Assert.Equal(3, children.Count());
        Assert.Contains(children, c => c.Name == "A");
        Assert.Contains(children, c => c.Name == "B");
        Assert.Contains(children, c => c.Name == "C");
        Assert.DoesNotContain(children, c => c.Name.Contains("Grandchild"));
    }

    // ---------------- UpdateObjectAsync (rename) ----------------

    [Fact]
    public async Task UpdateObjectAsync_Renames_Object_And_Updates_Own_Path()
    {
        var root = await SeedRootFolderAsync();
        var folder = await _sut.CreateFolderAsync(root.Id, "OldName");

        var renamed = await _sut.UpdateObjectAsync(folder.Id, "NewName");

        Assert.Equal("NewName", renamed.Name);
        Assert.Equal("/NewName", renamed.Path);
    }

    [Fact]
    public async Task UpdateObjectAsync_Rewrites_Descendant_Paths_At_Any_Depth()
    {
        var root = await SeedRootFolderAsync();
        var reports = await _sut.CreateFolderAsync(root.Id, "Reports");
        var year = await _sut.CreateFolderAsync(reports.Id, "2026");
        var doc = await _sut.CreateDocumentAsync(year.Id, "summary.pdf", "application/pdf", new byte[] { 1 });

        await _sut.UpdateObjectAsync(reports.Id, "Reports2026");

        var updatedYear = await _sut.GetObjectByIdAsync(year.Id);
        var updatedDoc = await _sut.GetObjectByIdAsync(doc.Id);

        Assert.Equal("/Reports2026/2026", updatedYear!.Path);
        Assert.Equal("/Reports2026/2026/summary.pdf", updatedDoc!.Path);
    }

    [Fact]
    public async Task UpdateObjectAsync_Throws_When_Object_Not_Found()
    {
        await SeedRootFolderAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateObjectAsync("nonexistent-id", "NewName"));
    }

    [Fact]
    public async Task UpdateObjectAsync_Throws_When_Renaming_Root_Folder()
    {
        var root = await SeedRootFolderAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateObjectAsync(root.Id, "NewRootName"));
    }

    [Fact]
    public async Task UpdateObjectAsync_Throws_When_New_Name_Collides_With_Sibling()
    {
        var root = await SeedRootFolderAsync();
        await _sut.CreateFolderAsync(root.Id, "Existing");
        var toRename = await _sut.CreateFolderAsync(root.Id, "ToRename");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateObjectAsync(toRename.Id, "Existing"));
    }

    // ---------------- MoveObjectAsync ----------------

    [Fact]
    public async Task MoveObjectAsync_Moves_Object_And_Updates_Path()
    {
        var root = await SeedRootFolderAsync();
        var source = await _sut.CreateFolderAsync(root.Id, "Source");
        var target = await _sut.CreateFolderAsync(root.Id, "Target");
        var doc = await _sut.CreateDocumentAsync(source.Id, "file.txt", "text/plain", new byte[] { 1 });

        var moved = await _sut.MoveObjectAsync(doc.Id, target.Id);

        Assert.Equal(target.Id, moved.ParentId);
        Assert.Equal("/Target/file.txt", moved.Path);
    }

    [Fact]
    public async Task MoveObjectAsync_Rewrites_Descendant_Paths()
    {
        var root = await SeedRootFolderAsync();
        var source = await _sut.CreateFolderAsync(root.Id, "Source");
        var target = await _sut.CreateFolderAsync(root.Id, "Target");
        var subfolder = await _sut.CreateFolderAsync(source.Id, "Sub");
        var doc = await _sut.CreateDocumentAsync(subfolder.Id, "file.txt", "text/plain", new byte[] { 1 });

        await _sut.MoveObjectAsync(source.Id, target.Id);

        var movedSub = await _sut.GetObjectByIdAsync(subfolder.Id);
        var movedDoc = await _sut.GetObjectByIdAsync(doc.Id);

        Assert.Equal("/Target/Source/Sub", movedSub!.Path);
        Assert.Equal("/Target/Source/Sub/file.txt", movedDoc!.Path);
    }

    [Fact]
    public async Task MoveObjectAsync_Throws_When_Moving_Folder_Into_Own_Descendant()
    {
        var root = await SeedRootFolderAsync();
        var parent = await _sut.CreateFolderAsync(root.Id, "Parent");
        var child = await _sut.CreateFolderAsync(parent.Id, "Child");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.MoveObjectAsync(parent.Id, child.Id));
    }

    [Fact]
    public async Task MoveObjectAsync_Throws_When_Moving_Root_Folder()
    {
        var root = await SeedRootFolderAsync();
        var target = await _sut.CreateFolderAsync(root.Id, "Target");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.MoveObjectAsync(root.Id, target.Id));
    }

    [Fact]
    public async Task MoveObjectAsync_Throws_When_Target_Is_Not_A_Folder()
    {
        var root = await SeedRootFolderAsync();
        var doc = await _sut.CreateDocumentAsync(root.Id, "file.txt", "text/plain", new byte[] { 1 });
        var otherDoc = await _sut.CreateDocumentAsync(root.Id, "other.txt", "text/plain", new byte[] { 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.MoveObjectAsync(doc.Id, otherDoc.Id));
    }

    // ---------------- DeleteObjectAsync ----------------

    [Fact]
    public async Task DeleteObjectAsync_Returns_False_When_Object_Not_Found()
    {
        await SeedRootFolderAsync();

        var result = await _sut.DeleteObjectAsync("nonexistent-id");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteObjectAsync_Deletes_Empty_Folder()
    {
        var root = await SeedRootFolderAsync();
        var folder = await _sut.CreateFolderAsync(root.Id, "Empty");

        var result = await _sut.DeleteObjectAsync(folder.Id);

        Assert.True(result);
        Assert.Null(await _sut.GetObjectByIdAsync(folder.Id));
    }

    [Fact]
    public async Task DeleteObjectAsync_Throws_When_Folder_Has_Children()
    {
        var root = await SeedRootFolderAsync();
        var folder = await _sut.CreateFolderAsync(root.Id, "NotEmpty");
        await _sut.CreateDocumentAsync(folder.Id, "file.txt", "text/plain", new byte[] { 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteObjectAsync(folder.Id));
    }

    [Fact]
    public async Task DeleteObjectAsync_Throws_When_Deleting_Root_Folder()
    {
        var root = await SeedRootFolderAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteObjectAsync(root.Id));
    }

    // ---------------- DeleteTreeAsync ----------------

    [Fact]
    public async Task DeleteTreeAsync_Removes_Folder_And_All_Descendants()
    {
        var root = await SeedRootFolderAsync();
        var folder = await _sut.CreateFolderAsync(root.Id, "ToDelete");
        var sub = await _sut.CreateFolderAsync(folder.Id, "Sub");
        var doc = await _sut.CreateDocumentAsync(sub.Id, "file.txt", "text/plain", new byte[] { 1 });

        var deletedCount = await _sut.DeleteTreeAsync(folder.Id);

        Assert.Equal(3, deletedCount); // folder + sub + doc
        Assert.Null(await _sut.GetObjectByIdAsync(folder.Id));
        Assert.Null(await _sut.GetObjectByIdAsync(sub.Id));
        Assert.Null(await _sut.GetObjectByIdAsync(doc.Id));
    }

    [Fact]
    public async Task DeleteTreeAsync_Throws_When_Target_Is_Not_A_Folder()
    {
        var root = await SeedRootFolderAsync();
        var doc = await _sut.CreateDocumentAsync(root.Id, "file.txt", "text/plain", new byte[] { 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteTreeAsync(doc.Id));
    }

    [Fact]
    public async Task DeleteTreeAsync_Throws_When_Deleting_Root_Folder()
    {
        var root = await SeedRootFolderAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteTreeAsync(root.Id));
    }

    // ---------------- SearchObjectsAsync ----------------

    [Fact]
    public async Task SearchObjectsAsync_Returns_Empty_For_Blank_Term()
    {
        await SeedRootFolderAsync();

        var results = await _sut.SearchObjectsAsync("   ");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchObjectsAsync_Finds_Partial_Name_Matches()
    {
        var root = await SeedRootFolderAsync();
        await _sut.CreateFolderAsync(root.Id, "Quarterly Reports");
        await _sut.CreateFolderAsync(root.Id, "Archive");

        var results = await _sut.SearchObjectsAsync("report");

        Assert.Single(results);
        Assert.Equal("Quarterly Reports", results.First().Name);
    }

    // ---------------- ExecuteQueryAsync (CMIS-SQL) ----------------

    [Fact]
    public async Task ExecuteQueryAsync_Filters_By_Type_And_InFolder()
    {
        var root = await SeedRootFolderAsync();
        var folder = await _sut.CreateFolderAsync(root.Id, "Docs");
        await _sut.CreateDocumentAsync(folder.Id, "a.txt", "text/plain", new byte[] { 1 });
        await _sut.CreateDocumentAsync(root.Id, "b.txt", "text/plain", new byte[] { 1 });

        var (results, numItems, hasMore) = await _sut.ExecuteQueryAsync(
            $"SELECT * FROM cmis:document WHERE IN_FOLDER('{folder.Id}')");

        Assert.Equal(1, numItems);
        Assert.False(hasMore);
        Assert.Equal("a.txt", results.First().Name);
    }

    [Fact]
    public async Task ExecuteQueryAsync_Respects_Pagination()
    {
        var root = await SeedRootFolderAsync();
        for (int i = 0; i < 5; i++)
        {
            await _sut.CreateDocumentAsync(root.Id, $"doc{i}.txt", "text/plain", new byte[] { 1 });
        }

        var (page, numItems, hasMore) = await _sut.ExecuteQueryAsync(
            "SELECT * FROM cmis:document", maxItems: 2, skipCount: 0);

        Assert.Equal(5, numItems);
        Assert.Equal(2, page.Count());
        Assert.True(hasMore);
    }

    [Fact]
    public async Task ExecuteQueryAsync_Orders_Results_Descending()
    {
        var root = await SeedRootFolderAsync();
        await _sut.CreateDocumentAsync(root.Id, "b.txt", "text/plain", new byte[] { 1 });
        await _sut.CreateDocumentAsync(root.Id, "a.txt", "text/plain", new byte[] { 1 });

        var (results, _, _) = await _sut.ExecuteQueryAsync(
            "SELECT * FROM cmis:document ORDER BY cmis:name DESC");

        Assert.Equal("b.txt", results.First().Name);
    }
}
