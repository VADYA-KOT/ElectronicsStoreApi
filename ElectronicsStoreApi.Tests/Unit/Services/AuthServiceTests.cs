using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace ElectronicsStoreApi.Tests.Unit.Services;

public class AuthServiceTests
{
    private const string CurrentPassword = "OldPassword123!";
    private const string NewPassword = "NewPassword456!";

    // ============================================
    // TEST INFRASTRUCTURE
    // ============================================

    private static StoreDbContext CreateContext(
        SaveChangesInterceptor? interceptor = null)
    {
        var optionsBuilder =
            new DbContextOptionsBuilder<StoreDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString());

        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return new StoreDbContext(optionsBuilder.Options);
    }

    private static IConfiguration CreateConfiguration(
        bool includeIssuer = true,
        bool includeAudience = true,
        bool includeKey = true)
    {
        var settings = new Dictionary<string, string?>();

        if (includeIssuer)
        {
            settings["Jwt:Issuer"] = "ElectronicsStoreApi.Tests";
        }

        if (includeAudience)
        {
            settings["Jwt:Audience"] =
                "ElectronicsStoreApi.Tests.Client";
        }

        if (includeKey)
        {
            settings["Jwt:Key"] =
                "dGVzdC1vbmx5LWtleS1mb3ItaW50ZWdyYXRpb24tdGVzdHMtMzJieXRlcw==";
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static AuthService CreateService(
        StoreDbContext context,
        IPasswordHasher<AppUser>? passwordHasher = null,
        IConfiguration? configuration = null)
    {
        return new AuthService(
            context,
            passwordHasher ?? new PasswordHasher<AppUser>(),
            configuration ?? CreateConfiguration());
    }

    private static async Task<AppUser> CreateUserAsync(
        StoreDbContext context,
        IPasswordHasher<AppUser> passwordHasher,
        string username = "testuser",
        string password = CurrentPassword,
        string role = "User")
    {
        var user = new AppUser
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            PasswordHash = string.Empty,
            Role = role,
            CreatedAtUtc = DateTime.UtcNow
        };

        user.PasswordHash =
            passwordHasher.HashPassword(user, password);

        context.AppUsers.Add(user);

        await context.SaveChangesAsync();

        return user;
    }

    // ============================================
    // REGISTER
    // ============================================

    [Fact]
    public async Task RegisterAsync_ValidRequest_CreatesUser()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();
        var service = CreateService(context, passwordHasher);

        var request = new RegisterRequest
        {
            Username = "  newuser  ",
            Password = CurrentPassword
        };

        var result = await service.RegisterAsync(request);

        Assert.True(result);

        var user = await context.AppUsers.SingleAsync();

        Assert.Equal("newuser", user.Username);
        Assert.Equal("NEWUSER", user.NormalizedUsername);
        Assert.Equal("User", user.Role);

        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                CurrentPassword));
    }

    [Fact]
    public async Task RegisterAsync_UserAlreadyExists_ReturnsFalse()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        await CreateUserAsync(
            context,
            passwordHasher,
            username: "existing");

        var service = CreateService(context, passwordHasher);

        var request = new RegisterRequest
        {
            Username = " EXISTING ",
            Password = CurrentPassword
        };

        var result = await service.RegisterAsync(request);

        Assert.False(result);
        Assert.Single(await context.AppUsers.ToListAsync());
    }

    [Fact]
    public async Task RegisterAsync_PostgresUniqueViolation_ReturnsFalse()
    {
        await using var context =
            CreateContext(new UniqueViolationInterceptor());

        var service = CreateService(context);

        var request = new RegisterRequest
        {
            Username = "newuser",
            Password = CurrentPassword
        };

        var result = await service.RegisterAsync(request);

        Assert.False(result);
        Assert.Empty(await context.AppUsers.ToListAsync());
    }

    // ============================================
    // LOGIN
    // ============================================

    [Fact]
    public async Task LoginAsync_UserDoesNotExist_ReturnsNull()
    {
        await using var context = CreateContext();

        var service = CreateService(context);

        var request = new LoginRequest
        {
            Username = "unknown",
            Password = CurrentPassword
        };

        var result = await service.LoginAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsNull()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        await CreateUserAsync(context, passwordHasher);

        var service = CreateService(context, passwordHasher);

        var request = new LoginRequest
        {
            Username = "testuser",
            Password = "WrongPassword123!"
        };

        var result = await service.LoginAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsJwtToken()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        var user = await CreateUserAsync(
            context,
            passwordHasher,
            username: "admin",
            role: "Admin");

        var service = CreateService(context, passwordHasher);

        var request = new LoginRequest
        {
            Username = " ADMIN ",
            Password = CurrentPassword
        };

        var result = await service.LoginAsync(request);

        Assert.NotNull(result);

        Assert.Equal("Bearer", result.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        Assert.Equal(user.Id.ToString(), token.Subject);

        Assert.Equal(
            user.Username,
            token.Claims.Single(
                claim => claim.Type == JwtRegisteredClaimNames.UniqueName)
                .Value);

        Assert.Equal(
            "Admin",
            token.Claims.Single(
                claim => claim.Type == ClaimTypes.Role).Value);

        Assert.Equal(
            "ElectronicsStoreApi.Tests",
            token.Issuer);

        Assert.Contains(
            "ElectronicsStoreApi.Tests.Client",
            token.Audiences);
    }

    [Fact]
    public async Task LoginAsync_RehashNeeded_UpdatesPasswordHash()
    {
        await using var context = CreateContext();

        var realHasher = new PasswordHasher<AppUser>();

        var user = await CreateUserAsync(
            context,
            realHasher);

        string oldPasswordHash = user.PasswordHash;

        var passwordHasher = new RehashNeededPasswordHasher();

        var service = CreateService(context, passwordHasher);

        var request = new LoginRequest
        {
            Username = user.Username,
            Password = CurrentPassword
        };

        var result = await service.LoginAsync(request);

        Assert.NotNull(result);
        Assert.NotEqual(oldPasswordHash, user.PasswordHash);

        Assert.Equal(
            PasswordVerificationResult.Success,
            realHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                CurrentPassword));
    }

    [Fact]
    public async Task LoginAsync_MissingJwtIssuer_ThrowsInvalidOperationException()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        await CreateUserAsync(context, passwordHasher);

        var service = CreateService(
            context,
            passwordHasher,
            CreateConfiguration(includeIssuer: false));

        var request = new LoginRequest
        {
            Username = "testuser",
            Password = CurrentPassword
        };

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LoginAsync(request));

        Assert.Equal("Jwt:Issuer не настроен.", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_MissingJwtAudience_ThrowsInvalidOperationException()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        await CreateUserAsync(context, passwordHasher);

        var service = CreateService(
            context,
            passwordHasher,
            CreateConfiguration(includeAudience: false));

        var request = new LoginRequest
        {
            Username = "testuser",
            Password = CurrentPassword
        };

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LoginAsync(request));

        Assert.Equal("Jwt:Audience не настроен.", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_MissingJwtKey_ThrowsInvalidOperationException()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        await CreateUserAsync(context, passwordHasher);

        var service = CreateService(
            context,
            passwordHasher,
            CreateConfiguration(includeKey: false));

        var request = new LoginRequest
        {
            Username = "testuser",
            Password = CurrentPassword
        };

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LoginAsync(request));

        Assert.Equal("Jwt:Key не настроен.", exception.Message);
    }

    // ============================================
    // GET PROFILE
    // ============================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetProfileAsync_InvalidUserId_ReturnsNull(
        int userId)
    {
        await using var context = CreateContext();

        var service = CreateService(context);

        var result = await service.GetProfileAsync(userId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetProfileAsync_UserDoesNotExist_ReturnsNull()
    {
        await using var context = CreateContext();

        var service = CreateService(context);

        var result = await service.GetProfileAsync(123);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetProfileAsync_UserExists_ReturnsProfile()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        var user = await CreateUserAsync(
            context,
            passwordHasher,
            username: "profileuser",
            role: "Admin");

        var service = CreateService(context);

        var result = await service.GetProfileAsync(user.Id);

        Assert.NotNull(result);

        Assert.Equal(user.Id, result.Id);
        Assert.Equal("profileuser", result.Username);
        Assert.Equal("Admin", result.Role);
        Assert.Equal(user.CreatedAtUtc, result.CreatedAtUtc);
    }

    // ============================================
    // CHANGE PASSWORD
    // ============================================

    [Fact]
    public async Task ChangePasswordAsync_UserDoesNotExist_ReturnsUserNotFound()
    {
        await using var context = CreateContext();

        var service = CreateService(context);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = CurrentPassword,
            NewPassword = NewPassword
        };

        var result = await service.ChangePasswordAsync(123, request);

        Assert.Equal(
            ChangePasswordStatus.UserNotFound,
            result);
    }

    [Fact]
    public async Task ChangePasswordAsync_InvalidCurrentPassword_ReturnsInvalidCurrentPassword()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        var user = await CreateUserAsync(context, passwordHasher);

        string oldPasswordHash = user.PasswordHash;

        var service = CreateService(context, passwordHasher);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "WrongPassword123!",
            NewPassword = NewPassword
        };

        var result = await service.ChangePasswordAsync(
            user.Id,
            request);

        Assert.Equal(
            ChangePasswordStatus.InvalidCurrentPassword,
            result);

        Assert.Equal(oldPasswordHash, user.PasswordHash);
    }

    [Fact]
    public async Task ChangePasswordAsync_NewPasswordSameAsCurrent_ReturnsNewPasswordSameAsCurrent()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        var user = await CreateUserAsync(context, passwordHasher);

        string oldPasswordHash = user.PasswordHash;

        var service = CreateService(context, passwordHasher);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = CurrentPassword,
            NewPassword = CurrentPassword
        };

        var result = await service.ChangePasswordAsync(
            user.Id,
            request);

        Assert.Equal(
            ChangePasswordStatus.NewPasswordSameAsCurrent,
            result);

        Assert.Equal(oldPasswordHash, user.PasswordHash);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidRequest_ChangesPassword()
    {
        await using var context = CreateContext();

        var passwordHasher = new PasswordHasher<AppUser>();

        var user = await CreateUserAsync(context, passwordHasher);

        var service = CreateService(context, passwordHasher);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = CurrentPassword,
            NewPassword = NewPassword
        };

        var result = await service.ChangePasswordAsync(
            user.Id,
            request);

        Assert.Equal(
            ChangePasswordStatus.Success,
            result);

        var savedUser = await context.AppUsers
            .AsNoTracking()
            .SingleAsync(u => u.Id == user.Id);

        Assert.Equal(
            PasswordVerificationResult.Failed,
            passwordHasher.VerifyHashedPassword(
                savedUser,
                savedUser.PasswordHash,
                CurrentPassword));

        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(
                savedUser,
                savedUser.PasswordHash,
                NewPassword));
    }

    // ============================================
    // TEST HELPERS
    // ============================================

    private sealed class RehashNeededPasswordHasher
        : IPasswordHasher<AppUser>
    {
        private readonly PasswordHasher<AppUser> _inner = new();

        public string HashPassword(
            AppUser user,
            string password)
        {
            return _inner.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(
            AppUser user,
            string hashedPassword,
            string providedPassword)
        {
            return PasswordVerificationResult.SuccessRehashNeeded;
        }
    }

    private sealed class UniqueViolationInterceptor
        : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException(
                "Simulated unique constraint violation.",
                new PostgresException(
                    "Duplicate key",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.UniqueViolation));
        }
    }
}