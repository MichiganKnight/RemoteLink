using Microsoft.AspNetCore.Mvc;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/system")]
    public sealed class SystemController : ControllerBase
    {
        [HttpGet("status")]
        public ActionResult<SystemStatusResponse> GetStatus()
        {
            return Ok(new SystemStatusResponse
            {
                Application = "RemoteLink",
                Status = "Online",
                MachineName = Environment.MachineName,
                Version = "0.1.0",
                TimestampUtc = DateTime.UtcNow
            });
        }
    }

    public sealed class SystemStatusResponse
    {
        public string Application { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string MachineName { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public DateTime TimestampUtc { get; set; }
    }
}