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
}
