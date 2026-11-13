using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElectronicsStoreApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request)
    {
        bool created = await _authService.RegisterAsync(request);

        if (!created)
        {
            return Conflict(new
            {
                message = "Пользователь с таким именем уже существует."
            });
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "Регистрация выполнена."
        });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthTokenResponse>> Login(
        [FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);

        if (result is null)
        {
            return Unauthorized(new
            {
                message = "Неверное имя пользователя или пароль."
            });
        }

        return Ok(result);
    }

    // Получение профиля текущего пользователя.
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileResponse>> GetCurrentProfile()
    {
        string? userIdClaim =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(userIdClaim, out int userId) ||
            userId <= 0)
        {
            return Unauthorized(new
            {
                message = "Не удалось определить пользователя."
            });
        }

        var profile = await _authService.GetProfileAsync(userId);

        if (profile is null)
        {
            return Unauthorized(new
            {
                message = "Пользователь не найден."
            });
        }

        return Ok(profile);
    }

    // Изменение пароля текущего пользователя.
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request)
    {
        string? userIdClaim =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(userIdClaim, out int userId) ||
            userId <= 0)
        {
            return Unauthorized(new
            {
                message = "Не удалось определить пользователя."
            });
        }

        var result = await _authService.ChangePasswordAsync(
            userId,
            request);

        switch (result)
        {
            case ChangePasswordStatus.Success:
                return Ok(new
                {
                    message = "Пароль успешно изменён."
                });

            case ChangePasswordStatus.UserNotFound:
                return NotFound(new
                {
                    message = "Пользователь не найден."
                });

            case ChangePasswordStatus.InvalidCurrentPassword:
                return BadRequest(new
                {
                    message = "Текущий пароль указан неверно."
                });

            case ChangePasswordStatus.NewPasswordSameAsCurrent:
                return BadRequest(new
                {
                    message = "Новый пароль должен отличаться от текущего."
                });

            default:
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}