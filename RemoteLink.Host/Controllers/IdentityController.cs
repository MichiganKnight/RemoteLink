using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/identity")]
    public sealed class IdentityController(DeviceSecurityService security) : ControllerBase
    {
        [HttpGet("public")]
        public ActionResult<DeviceSecurityIdentity> GetPublicIdentity()
        {
            return Ok(security.GetPublicIdentity());
        }
    }
}