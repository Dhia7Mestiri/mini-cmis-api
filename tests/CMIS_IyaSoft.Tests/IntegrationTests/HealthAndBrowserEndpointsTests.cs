using CMIS_IyaSoft.Data;
using CMIS_IyaSoft.Entities;
using CMIS_IyaSoft.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CMIS_IyaSoft.Tests.IntegrationTests;

// Integration tests against the real HTTP pipeline.
// CustomWebApplicationFactory swaps the application database for EF Core InMemory.
public class HealthAndBrowserEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string RepoId = "mini-cmis-repo";
    private const string RootFolderId = "root-folder";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HealthAndBrowserEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------------------------------------------------------
    // Health / discovery
    // ---------------------------------------------------------

    [Fact]
    public async Task Health_Endpoint_Returns_OK()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Browser_Discovery_Returns_RepositoryUrl_And_RootFolderUrl()
    {
        var response = await _client.GetAsync("/browser");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("repositoryUrl");
        content.Should().Contain("rootFolderUrl");
        content.Should().Contain(RepoId);
    }

    // ---------------------------------------------------------
    // Type discovery / inheritance
    // ---------------------------------------------------------

    [Fact]
    public async Task Browser_Types_Returns_CmisFolder_And_CmisDocument_Root_Types()
    {
        var response =
            await _client.GetAsync(
                "/browser?cmisselector=types");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("cmis:folder");
        content.Should().Contain("cmis:document");
    }

    [Fact]
    public async Task Browser_Document_TypeChildren_Returns_Custom_FinancialDocument()
    {
        var response =
            await _client.GetAsync(
                "/browser?cmisselector=typeChildren&typeId=cmis:document");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("custom:financialDocument");
    }

    [Fact]
    public async Task Browser_FinancialDocument_TypeChildren_Returns_Facture_And_Loan()
    {
        var response =
            await _client.GetAsync(
                "/browser?cmisselector=typeChildren&typeId=custom:financialDocument");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("custom:facture");
        content.Should().Contain("custom:loan");
    }

    [Fact]
    public async Task Browser_TypeDefinition_Returns_PropertyDefinitions()
    {
        var response =
            await _client.GetAsync(
                "/browser?cmisselector=typeDefinition&typeId=cmis:folder");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("propertyDefinitions");
        content.Should().Contain("cmis:name");
    }

    [Fact]
    public async Task Browser_FinancialDocument_TypeDefinition_Contains_Own_Properties()
    {
        var response =
            await _client.GetAsync(
                "/browser?cmisselector=typeDefinition&typeId=custom:financialDocument");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("custom:amount");
        content.Should().Contain("custom:currency");
        content.Should().Contain("cmis:name");
        content.Should().Contain("cmis:objectId");
    }

    [Fact]
    public async Task Browser_Facture_TypeDefinition_Contains_Inherited_And_Own_Properties()
    {
        var response =
            await _client.GetAsync(
                "/browser?cmisselector=typeDefinition&typeId=custom:facture");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        // inherited from cmis:document
        content.Should().Contain("cmis:name");

        // inherited from custom:financialDocument
        content.Should().Contain("custom:amount");
        content.Should().Contain("custom:currency");

        // owned by custom:facture
        content.Should().Contain("custom:invoiceNumber");
        content.Should().Contain("custom:invoiceDate");

        // direct parent must be visible
        content.Should().Contain("custom:financialDocument");
    }

    [Fact]
    public async Task Browser_Folder_TypeDefinition_Contains_Custom_Owner_Property()
    {
        var response =
            await _client.GetAsync(
                "/browser?cmisselector=typeDefinition&typeId=cmis:folder");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("custom:owner");
    }

    [Fact]
    public async Task Browser_TypeDefinition_Returns_NotFound_For_Unknown_Type()
    {
        var response =
            await _client.GetAsync(
                "/browser?cmisselector=typeDefinition&typeId=cmis:doesNotExist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("objectNotFound");
    }

    // ---------------------------------------------------------
    // Authorization
    // ---------------------------------------------------------

    [Fact]
    public async Task Browser_Children_Of_Root_Requires_Auth()
    {
        var response =
            await _client.GetAsync(
                $"/browser/{RepoId}/{RootFolderId}?cmisselector=children");

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateFolder_Without_Bearer_Token_Returns_Unauthorized()
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("createFolder"), "cmisaction" },
            { new StringContent("Test Folder"), "name" }
        };

        var response =
            await _client.PostAsync(
                $"/browser/{RepoId}/{RootFolderId}",
                form);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateFolder_With_User_Role_Returns_Forbidden()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("User");

        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/browser/{RepoId}/{RootFolderId}")
            {
                Content = new MultipartFormDataContent
                {
                    {
                        new StringContent("createFolder"),
                        "cmisaction"
                    },
                    {
                        new StringContent("Should Not Be Created"),
                        "name"
                    }
                }
            };

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await _client.SendAsync(request);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateType_With_User_Role_Returns_Forbidden()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("User");

        var unique =
            Guid.NewGuid().ToString("N");

        var typeJson =
            $$"""
            {
              "id": "custom:test{{unique}}",
              "displayName": "Forbidden Test Type",
              "description": "Must not be created",
              "parentTypeId": "cmis:document",
              "propertyDefinitions": []
            }
            """;

        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/browser")
            {
                Content = new MultipartFormDataContent
                {
                    {
                        new StringContent("createType"),
                        "cmisaction"
                    },
                    {
                        new StringContent(typeJson),
                        "type"
                    }
                }
            };

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await _client.SendAsync(request);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------
    // Query endpoint
    // ---------------------------------------------------------

    [Fact]
    public async Task Query_Without_Statement_Returns_BadRequest_With_CmisErrorEnvelope()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("User");

        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/browser")
            {
                Content = new FormUrlEncodedContent(
                    new[]
                    {
                        new KeyValuePair<string, string>(
                            "cmisaction",
                            "query")
                    })
            };

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await _client.SendAsync(request);

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("invalidArgument");
    }

    // ---------------------------------------------------------
    // Object property envelopes
    // ---------------------------------------------------------

    [Fact]
    public async Task Browser_Object_Returns_Cmis_Property_Envelope()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("User");

        // Do not depend on an old hard-coded seed such as "doc-101".
        // Create the object required by this test in the current InMemory DB.
        var document =
            await CreateIntegrationDocumentAsync();

        var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/browser/{RepoId}/{document.Id}");

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await _client.SendAsync(request);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("\"properties\"");
        content.Should().Contain("cmis:name");
        content.Should().Contain("cmis:lastModificationDate");
        content.Should().Contain("cmis:contentStreamLength");
        content.Should().Contain(document.Name);
    }

    [Fact]
    public async Task Browser_Children_Returns_Property_Envelopes()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("User");

        // The integration DB can legitimately start with an empty root folder.
        // Seed one document so the test can actually inspect an object envelope.
        var document =
            await CreateIntegrationDocumentAsync();

        var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/browser/{RepoId}/{RootFolderId}?cmisselector=children");

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await _client.SendAsync(request);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK);

        var content =
            await response.Content.ReadAsStringAsync();

        content.Should().Contain("\"objects\"");
        content.Should().Contain("\"properties\"");
        content.Should().Contain("cmis:name");
        content.Should().Contain(document.Name);
    }

    // ---------------------------------------------------------
    // Dynamic type management - create / update / delete
    // ---------------------------------------------------------

    [Fact]
    public async Task CreateType_As_Admin_Creates_Runtime_Type()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("Admin");

        var suffix =
            Guid.NewGuid().ToString("N");

        var typeId =
            $"custom:contract{suffix}";

        var propertyId =
            $"custom:contractNumber{suffix}";

        var typeJson =
            $$"""
            {
              "id": "{{typeId}}",
              "displayName": "Runtime Contract",
              "description": "Created by integration test",
              "parentTypeId": "cmis:document",
              "propertyDefinitions": [
                {
                  "propertyId": "{{propertyId}}",
                  "localName": "contractNumber",
                  "propertyType": "string",
                  "cardinality": "single",
                  "updatability": "readwrite",
                  "required": true
                }
              ]
            }
            """;

        var response =
            await SendTypeActionAsync(
                token,
                "createType",
                typeJson: typeJson);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created);

        var body =
            await response.Content.ReadAsStringAsync();

        body.Should().Contain(typeId);
        body.Should().Contain(propertyId);
        body.Should().Contain("cmis:document");

        // Read it back through the normal CMIS typeDefinition selector.
        var readBack =
            await _client.GetAsync(
                $"/browser?cmisselector=typeDefinition&typeId={Uri.EscapeDataString(typeId)}");

        readBack.StatusCode.Should().Be(
            HttpStatusCode.OK);

        var readBody =
            await readBack.Content.ReadAsStringAsync();

        readBody.Should().Contain(typeId);
        readBody.Should().Contain(propertyId);

        // Cleanup so the shared integration DB remains tidy.
        var delete =
            await SendTypeActionAsync(
                token,
                "deleteType",
                typeId: typeId);

        delete.StatusCode.Should().Be(
            HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateType_As_Admin_Updates_Runtime_Type()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("Admin");

        var suffix =
            Guid.NewGuid().ToString("N");

        var typeId =
            $"custom:updateTest{suffix}";

        var originalPropertyId =
            $"custom:reference{suffix}";

        var createJson =
            $$"""
            {
              "id": "{{typeId}}",
              "displayName": "Before Update",
              "description": "Original",
              "parentTypeId": "cmis:document",
              "propertyDefinitions": [
                {
                  "propertyId": "{{originalPropertyId}}",
                  "localName": "reference",
                  "propertyType": "string",
                  "cardinality": "single",
                  "updatability": "readwrite",
                  "required": false
                }
              ]
            }
            """;

        var create =
            await SendTypeActionAsync(
                token,
                "createType",
                typeJson: createJson);

        create.StatusCode.Should().Be(
            HttpStatusCode.Created);

        var newPropertyId =
            $"custom:status{suffix}";

        var updateJson =
            $$"""
            {
              "displayName": "After Update",
              "description": "Updated",
              "parentTypeId": "cmis:document",
              "propertyDefinitions": [
                {
                  "propertyId": "{{originalPropertyId}}",
                  "localName": "reference",
                  "propertyType": "string",
                  "cardinality": "single",
                  "updatability": "readwrite",
                  "required": false
                },
                {
                  "propertyId": "{{newPropertyId}}",
                  "localName": "status",
                  "propertyType": "string",
                  "cardinality": "single",
                  "updatability": "readwrite",
                  "required": false
                }
              ]
            }
            """;

        var update =
            await SendTypeActionAsync(
                token,
                "updateType",
                typeId,
                updateJson);

        update.StatusCode.Should().Be(
            HttpStatusCode.OK);

        var body =
            await update.Content.ReadAsStringAsync();

        body.Should().Contain("After Update");
        body.Should().Contain(newPropertyId);

        var delete =
            await SendTypeActionAsync(
                token,
                "deleteType",
                typeId: typeId);

        delete.StatusCode.Should().Be(
            HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteType_As_Admin_Removes_Unused_Runtime_Type()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("Admin");

        var suffix =
            Guid.NewGuid().ToString("N");

        var typeId =
            $"custom:deleteTest{suffix}";

        var createJson =
            $$"""
            {
              "id": "{{typeId}}",
              "displayName": "Delete Me",
              "description": "Temporary type",
              "parentTypeId": "cmis:document",
              "propertyDefinitions": []
            }
            """;

        var create =
            await SendTypeActionAsync(
                token,
                "createType",
                typeJson: createJson);

        create.StatusCode.Should().Be(
            HttpStatusCode.Created);

        var delete =
            await SendTypeActionAsync(
                token,
                "deleteType",
                typeId: typeId);

        delete.StatusCode.Should().Be(
            HttpStatusCode.NoContent);

        var readBack =
            await _client.GetAsync(
                $"/browser?cmisselector=typeDefinition&typeId={Uri.EscapeDataString(typeId)}");

        readBack.StatusCode.Should().Be(
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteType_Rejects_Base_Cmis_Type()
    {
        var token =
            await RegisterLoginAndAssignRoleAsync("Admin");

        var response =
            await SendTypeActionAsync(
                token,
                "deleteType",
                typeId: "cmis:document");

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest);

        var body =
            await response.Content.ReadAsStringAsync();

        body.Should().Contain("constraint");
        body.Should().Contain("cannot be deleted");
    }

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------

    private async Task<CmisObject> CreateIntegrationDocumentAsync()
    {
        using var scope =
            _factory.Services.CreateScope();

        var context =
            scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

        // Integration InMemory DB does not necessarily contain
        // the repository root object, so make the test self-contained.
        var rootExists =
            await context.Objects.AnyAsync(
                o => o.Id == RootFolderId);

        if (!rootExists)
        {
            context.Objects.Add(
                new CmisObject
                {
                    Id = RootFolderId,
                    Name = RootFolderId,
                    TypeId = "cmis:folder",
                    ParentId = null,
                    Path = "/",
                    CreatedBy = "integration-test",
                    CreationDate = DateTime.UtcNow,
                    LastModificationDate = DateTime.UtcNow
                });

            await context.SaveChangesAsync();
        }

        var service =
            scope.ServiceProvider
                .GetRequiredService<ICmisService>();

        var fileName =
            $"integration-{Guid.NewGuid():N}.txt";

        return await service.CreateDocumentAsync(
            RootFolderId,
            fileName,
            "text/plain",
            System.Text.Encoding.UTF8.GetBytes(
                "integration test"));
    }
    private async Task<HttpResponseMessage> SendTypeActionAsync(
        string token,
        string action,
        string? typeId = null,
        string? typeJson = null)
    {
        var form =
            new MultipartFormDataContent
            {
                {
                    new StringContent(action),
                    "cmisaction"
                }
            };

        if (!string.IsNullOrWhiteSpace(typeId))
        {
            form.Add(
                new StringContent(typeId),
                "typeId");
        }

        if (!string.IsNullOrWhiteSpace(typeJson))
        {
            form.Add(
                new StringContent(typeJson),
                "type");
        }

        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/browser")
            {
                Content = form
            };

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);

        return await _client.SendAsync(request);
    }

    // Registers + logs in a user, then assigns the requested role directly
    // through UserManager. There is intentionally no public self-escalation
    // endpoint in the application.
    private async Task<string> RegisterLoginAndAssignRoleAsync(
        string role)
    {
        var email =
            $"test_{Guid.NewGuid():N}@example.com";

        var password =
            "StrongP@ssw0rd!";

        var registerResponse =
            await _client.PostAsJsonAsync(
                "/auth/register",
                new
                {
                    email,
                    password
                });

        registerResponse.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Created);

        using (var scope =
               _factory.Services.CreateScope())
        {
            var userManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        UserManager<IdentityUser>>();

            var roleManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        RoleManager<IdentityRole>>();

            if (!await roleManager.RoleExistsAsync(role))
            {
                var createRole =
                    await roleManager.CreateAsync(
                        new IdentityRole(role));

                createRole.Succeeded.Should().BeTrue();
            }

            var user =
                await userManager.FindByEmailAsync(email);

            user.Should().NotBeNull();

            var roleResult =
                await userManager.AddToRoleAsync(
                    user!,
                    role);

            roleResult.Succeeded.Should().BeTrue();
        }

        var loginResponse =
            await _client.PostAsJsonAsync(
                "/auth/login",
                new
                {
                    email,
                    password
                });

        loginResponse.StatusCode.Should().Be(
            HttpStatusCode.OK);

        var body =
            await loginResponse.Content
                .ReadFromJsonAsync<LoginResponse>();

        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();

        return body.AccessToken;
    }

    private record LoginResponse(
        string AccessToken,
        string TokenType,
        int ExpiresIn,
        string RefreshToken);
}