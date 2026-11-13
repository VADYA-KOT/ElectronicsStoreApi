using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace ElectronicsStoreApi.Services;

public enum ChangePasswordStatus
{
    Success,
    UserNotFound,
    InvalidCurrentPassword,
    NewPasswordSameAsCurrent
}

public class AuthService
{
    private readonly StoreDbContext _context;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IConfiguration _configuration;

    public AuthService(
        StoreDbContext context,
        IPasswordHasher<AppUser> passwordHasher,
        IConfiguration configuration)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    // Регистрация пользователя.
    public async Task<bool> RegisterAsync(RegisterRequest request)
    {
        string username = request.Username.Trim();
        string normalizedUsername = username.ToUpperInvariant();

        bool alreadyExists = await _context.AppUsers
            .AnyAsync(u => u.NormalizedUsername == normalizedUsername);

        if (alreadyExists)
        {
            return false;
        }

        var user = new AppUser
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            Role = "User",
            CreatedAtUtc = DateTime.UtcNow
        };

        user.PasswordHash =
            _passwordHasher.HashPassword(user, request.Password);

        _context.AppUsers.Add(user);

        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg &&
                  pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    // Вход пользователя.
    public async Task<AuthTokenResponse?> LoginAsync(LoginRequest request)
    {
        string normalizedUsername =
            request.Username.Trim().ToUpperInvariant();

        var user = await _context.AppUsers
            .SingleOrDefaultAsync(u =>
                u.NormalizedUsername == normalizedUsername);

        if (user is null)
        {
            return null;
        }

        var verification = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash =
                _passwordHasher.HashPassword(user, request.Password);

            await _context.SaveChangesAsync();
        }

        return CreateToken(user);
    }

    // Получение профиля пользователя по ID.
    public async Task<UserProfileResponse?> GetProfileAsync(int userId)
    {
        if (userId <= 0)
        {
            return null;
        }

        return await _context.AppUsers
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserProfileResponse
            {
                Id = user.Id,
                Username = user.Username,
                Role = user.Role,
                CreatedAtUtc = user.CreatedAtUtc
            })
            .SingleOrDefaultAsync();
    }

    // Изменение пароля пользователя.
    public async Task<ChangePasswordStatus> ChangePasswordAsync(
        int userId,
        ChangePasswordRequest request)
    {
        var user = await _context.AppUsers
            .SingleOrDefaultAsync(user => user.Id == userId);

        if (user is null)
        {
            return ChangePasswordStatus.UserNotFound;
        }

        // Проверяем текущий пароль.
        var currentPasswordVerification =
            _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.CurrentPassword);

        if (currentPasswordVerification ==
            PasswordVerificationResult.Failed)
        {
            return ChangePasswordStatus.InvalidCurrentPassword;
        }

        // Не разрешаем установить прежний пароль повторно.
        var newPasswordVerification =
            _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.NewPassword);

        if (newPasswordVerification !=
            PasswordVerificationResult.Failed)
        {
            return ChangePasswordStatus.NewPasswordSameAsCurrent;
        }

        // Сохраняем новый хеш пароля.
        user.PasswordHash =
            _passwordHasher.HashPassword(user, request.NewPassword);

        await _context.SaveChangesAsync();

        return ChangePasswordStatus.Success;
    }

    // Создание JWT.
    private AuthTokenResponse CreateToken(AppUser user)
    {
        string issuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "Jwt:Issuer не настроен.");

        string audience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "Jwt:Audience не настроен.");

        string encodedKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "Jwt:Key не настроен.");

        var key = new SymmetricSecurityKey(
            Convert.FromBase64String(encodedKey));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        DateTime now = DateTime.UtcNow;
        DateTime expires = now.AddMinutes(30);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return new AuthTokenResponse
        {
            AccessToken = new JwtSecurityTokenHandler()
                .WriteToken(token),
            TokenType = "Bearer",
            ExpiresAtUtc = expires
        };
    }
}