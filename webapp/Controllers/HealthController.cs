using Microsoft.AspNetCore.Mvc;

namespace ScreenConnect.WebApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok("OK");
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok("Pong");
    }
} 