using System.Net;
using System.Net.Http.Json;
using CMIS_IyaSoft.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CMIS_IyaSoft.Tests.IntegrationTests;

// Real, confirmed IDs from DbInitializer's seed data: repositoryId "mini-cmis-repo",
// root folder id "root-folder". These tests spin up the real app in-memory via
// WebApplicationFactory, with the DB swapped for EF Core InMemory.
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
    }

    [Fact]
    public async Task Browser_Types_Returns_CmisFolder_And_CmisDocument_Types()
    {
        var response = await _client.GetAsync("/browser?cmisselector=types");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("cmis:folder");
        content.Should().Contain("cmis:document");
    }

    [Fact]
    public async Task Browser_TypeDefinition_Returns_PropertyDefinitions()
    {
        var response = await _client.GetAsync("/browser?cmisselector=typeDefinition&typeId=cmis:folder");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("propertyDefinitions");
        content.Should().Contain("cmis:name");
    }

    [Fact]
    public async Task Browser_TypeDefinition_Returns_NotFound_For_Unknown_Type()
    {
        var response = await _client.GetAsync("/browser?cmisselector=typeDefinition&typeId=cmis:doesNotExist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("objectNotFound");
    }

    [Fact]
    public async Task Browser_Children_Of_Root_Requires_Auth()
    {
        var response = await _client.GetAsync($"/browser/{RepoId}/{RootFolderId}?cmisselector=children");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateFolder_Without_Bearer_Token_Returns_Unauthorized()
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("createFolder"), "cmisaction" },
            { new StringContent("Test Folder"), "name" }
        };

        var response = await _client.PostAsync($"/browser/{RepoId}/{RootFolderId}", form);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateFolder_With_User_Role_Returns_Forbidden()
    {
        // "User" role is read-only per the spec's role model - createFolder needs Admin/Manager
        var token = await RegisterLoginAndAssignRoleAsync("User");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/browser/{RepoId}/{RootFolderId}")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent("createFolder"), "cmisaction" },
                { new StringContent("Should Not Be Created"), "name" }
            }
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Query_Without_Statement_Returns_BadRequest_With_CmisErrorEnvelope()
    {
        // Needs a real role (Admin/Manager/User all pass the [Authorize(Roles=...)] gate
        // on the query endpoint) so the request reaches the actual validation logic
        // instead of being rejected earlier by the role check.
        var token = await RegisterLoginAndAssignRoleAsync("User");

        var request = new HttpRequestMessage(HttpMethod.Post, "/browser")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("cmisaction", "query")
            })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("invalidArgument");
    }

    // Registers + logs in a user, then assigns a role directly via UserManager
    // (bypassing HTTP - there's intentionally no public "assign yourself a role"
    // endpoint, so this mirrors how an admin would grant access out-of-band).
    private async Task<string> RegisterLoginAndAssignRoleAsync(string role)
    {
        var email = $"test_{Guid.NewGuid():N}@example.com";
        var password = "StrongP@ssw0rd!";

        await _client.PostAsJsonAsync("/auth/register", new { email, password });

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await userManager.FindByEmailAsync(email);
            await userManager.AddToRoleAsync(user!, role);
        }

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private record LoginResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken);
}
