using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/devices")]
    public sealed class DevicesController(LanDiscoveryService discovery, DeviceIdentityService identity) : ControllerBase
    {
        [HttpGet]
        public ActionResult<IReadOnlyList<DiscoveredDevice>> GetDevices()
        {
            return Ok(discovery.GetDevices());
        }

        [HttpGet("local")]
        public IActionResult GetLocalDevice()
        {
            return Ok(new
            {
                Id = identity.DeviceId,
                Name = identity.DeviceName,
                DiscoveryPort = 5018
            });
        }
    }
}