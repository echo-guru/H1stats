using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpGet("usernames")]
    public async Task<IActionResult> ListUsernames(CancellationToken ct)
    {
        var names = await _auth.ListEnabledUsernamesAsync(ct);
        return Ok(names);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        if (result is null)
            return Unauthorized(new { message = "Invalid username or password" });
        return Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _auth.LogoutAsync(User?.Identity?.Name ?? "", ct);
        return Ok();
    }
}
