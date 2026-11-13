using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Response;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class AuthApiIntegrationTests
{
    private const string OriginalPassword = "TestPassword_12345";
    private const string NewPassword = "NewPassword_67890";

    // ==================================================
    // GET /api/auth/me
    // ==================================================

    [Fact]
    public async Task GetCurrentProfile_AuthenticatedUser_ReturnsProfile()
    {
        using var factory = new TestApplicationFactory();

        var authenticated =
            await CreateAuthenticatedUserAsync(factory);

        using var client = authenticated.Client;

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile =
            await response.Content.ReadFromJsonAsync<UserProfileResponse>();

        Assert.NotNull(profile);
        Assert.True(profile.Id > 0);
        Assert.Equal(authenticated.Username, profile.Username);
        Assert.Equal("User", profile.Role);
        Assert.NotEqual(default, profile.CreatedAtUtc);
    }

    [Fact]
    public async Task GetCurrentProfile_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentProfile_DeletedUser_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory();

        var authenticated =
            await CreateAuthenticatedUserAsync(factory);

        using var client = authenticated.Client;

        await DeleteUserAsync(factory, authenticated.Username);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // ==================================================
    // POST /api/auth/change-password
    // ==================================================

    [Fact]
    public async Task ChangePassword_ValidRequest_ChangesPassword()
    {
        using var factory = new TestApplicationFactory();

        var authenticated =
            await CreateAuthenticatedUserAsync(factory);

        using var client = authenticated.Client;

        var changeResponse = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                currentPassword = OriginalPassword,
                newPassword = NewPassword
            });

        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        // Старый пароль после смены больше не должен работать.
        var oldPasswordResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = authenticated.Username,
                password = OriginalPassword
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            oldPasswordResponse.StatusCode);

        // Новый пароль должен позволять войти.
        var newPasswordResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = authenticated.Username,
                password = NewPassword
            });

        Assert.Equal(
            HttpStatusCode.OK,
            newPasswordResponse.StatusCode);

        var token =
            await newPasswordResponse.Content
                .ReadFromJsonAsync<AuthTokenResponse>();

        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
    }

    [Fact]
    public async Task ChangePassword_InvalidCurrentPassword_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        var authenticated =
            await CreateAuthenticatedUserAsync(factory);

        using var client = authenticated.Client;

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                currentPassword = "WrongPassword_12345",
                newPassword = NewPassword
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordSameAsCurrent_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        var authenticated =
            await CreateAuthenticatedUserAsync(factory);

        using var client = authenticated.Client;

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                currentPassword = OriginalPassword,
                newPassword = OriginalPassword
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_DeletedUser_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        var authenticated =
            await CreateAuthenticatedUserAsync(factory);

        using var client = authenticated.Client;

        await DeleteUserAsync(factory, authenticated.Username);

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                currentPassword = OriginalPassword,
                newPassword = NewPassword
            });

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordTooShort_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        var authenticated =
            await CreateAuthenticatedUserAsync(factory);

        using var client = authenticated.Client;

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                currentPassword = OriginalPassword,
                newPassword = "Short123"
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                currentPassword = OriginalPassword,
                newPassword = NewPassword
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // ==================================================
    // POST /api/auth/register
    // ==================================================

    [Fact]
    public async Task Register_DuplicateUsername_ReturnsConflict()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        string username = $"auth_{Guid.NewGuid():N}";

        var firstResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username,
                password = OriginalPassword
            });

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var secondResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username,
                password = OriginalPassword
            });

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);
    }

    // ==================================================
    // POST /api/auth/login
    // ==================================================

    [Fact]
    public async Task Login_InvalidPassword_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory();

        var authenticated =
            await CreateAuthenticatedUserAsync(factory);

        using var client = authenticated.Client;

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = authenticated.Username,
                password = "WrongPassword_12345"
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUsername_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = $"unknown_{Guid.NewGuid():N}",
                password = OriginalPassword
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // ==================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ==================================================

    private static async Task<(
        HttpClient Client,
        string Username)> CreateAuthenticatedUserAsync(
            TestApplicationFactory factory)
    {
        var client = factory.CreateClient();

        string username = $"auth_{Guid.NewGuid():N}";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username,
                password = OriginalPassword
            });

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username,
                password = OriginalPassword
            });

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var token =
            await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        return (client, username);
    }

    private static async Task DeleteUserAsync(
        TestApplicationFactory factory,
        string username)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        string normalizedUsername = username.ToUpperInvariant();

        var user = await context.AppUsers.SingleAsync(
            user => user.NormalizedUsername == normalizedUsername);

        context.AppUsers.Remove(user);

        await context.SaveChangesAsync();
    }
}