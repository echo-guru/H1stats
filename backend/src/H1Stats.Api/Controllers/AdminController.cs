using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IDatabaseConnectionService _db;
    private readonly IUserAdminService _users;

    public AdminController(IDatabaseConnectionService db, IUserAdminService users)
    {
        _db = db;
        _users = users;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken ct) =>
        Ok(await _users.ListUsersAsync(ct));

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var (user, error) = await _users.CreateUserAsync(request, ct);
        if (error is not null)
            return BadRequest(new { message = error });
        return Ok(user);
    }

    [HttpPut("users/{username}")]
    public async Task<IActionResult> UpdateUser(
        string username, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        var (user, error) = await _users.UpdateUserAsync(username, request, ct);
        if (error == "User not found")
            return NotFound(new { message = error });
        if (error is not null)
            return BadRequest(new { message = error });
        return Ok(user);
    }

    [HttpGet("database")]
    public IActionResult GetDatabaseConfig() => Ok(_db.GetSettings());

    [HttpPut("database")]
    public async Task<IActionResult> UpdateDatabaseConfig([FromBody] DatabaseSettings settings, CancellationToken ct)
    {
        await _db.UpdateSettingsAsync(settings, ct);
        return Ok(_db.GetSettings());
    }

    [HttpPost("database/test")]
    public async Task<IActionResult> TestDatabase([FromBody] DatabaseSettings? settings, CancellationToken ct)
    {
        var result = await _db.TestConnectionAsync(settings, ct);
        return Ok(result);
    }
}
