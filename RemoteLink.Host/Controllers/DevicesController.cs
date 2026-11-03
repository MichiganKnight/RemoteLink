using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/devices")]
    public sealed class DevicesController(LanDiscoveryService _discovery, DeviceIdentityService _identity) : ControllerBase
    {
        [HttpGet]
        public ActionResult<IReadOnlyList<DiscoveredDevice>> GetDevices()
        {
            return Ok(_discovery.GetDevices());
        }

        [HttpGet("local")]
        public IActionResult GetLocalDevice()
        {
            return Ok(new
            {
                Id = _identity.DeviceId,
                Name = _identity.DeviceName,
                DiscoveryPort = 5018
            });
        }
    }
}