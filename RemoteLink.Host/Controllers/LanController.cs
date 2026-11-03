using Microsoft.AspNetCore.Mvc;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/lan")]
    public sealed class LanController(DeviceSecurityService security) : ControllerBase
    {
        [HttpGet("identity")]
        public IActionResult GetIdentity()
        {
            if (HttpContext.Connection.LocalPort != 5019)
            {
                return NotFound();
            }

            return Ok(security.GetPublicIdentity());
        }
    }
}