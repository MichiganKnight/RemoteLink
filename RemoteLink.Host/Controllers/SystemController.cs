using Microsoft.AspNetCore.Mvc;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/system")]
    public sealed class SystemController : ControllerBase
    {
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(new
            {
                Application = "RemoteLink",
                Status = "Online",
                MachineName = Environment.MachineName,
                Version = "0.1.0",
                TimestampUtc = DateTime.UtcNow
            });
        }
    }
}