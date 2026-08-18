using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace CMIS_IyaSoft.Tests.IntegrationTests;

public class AuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_With_Valid_Credentials_Returns_Success()
    {
        var email = $"test_{Guid.NewGuid():N}@example.com";
        var payload = new { email, password = "StrongP@ssw0rd!" };

        var response = await _client.PostAsJsonAsync("/auth/register", payload);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Login_With_Valid_Credentials_Returns_BearerToken()
    {
        var email = $"test_{Guid.NewGuid():N}@example.com";
        var password = "StrongP@ssw0rd!";
        await _client.PostAsJsonAsync("/auth/register", new { email, password });

        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_With_Invalid_Credentials_Returns_Unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "doesnotexist@example.com",
            password = "WrongPassword123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_With_Weak_Password_Returns_BadRequest()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new
        {
            email = $"weak_{Guid.NewGuid():N}@example.com",
            password = "123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_Without_Token_Returns_Unauthorized()
    {
        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_With_Valid_Token_Returns_Email_And_Empty_Roles_For_New_User()
    {
        var email = $"test_{Guid.NewGuid():N}@example.com";
        var password = "StrongP@ssw0rd!";
        await _client.PostAsJsonAsync("/auth/register", new { email, password });
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(email);
        // A freshly self-registered user has no roles assigned - see note in
        // HealthAndBrowserEndpointsTests about the implications of this.
        content.Should().Contain("\"roles\":[]");
    }

    // Matches the shape returned by ASP.NET Core Identity's MapIdentityApi login endpoint
    private record LoginResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken);
}
