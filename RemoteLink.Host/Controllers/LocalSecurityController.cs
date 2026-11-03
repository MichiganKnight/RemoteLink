using System.Net;
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/local/security")]
    public sealed class LocalSecurityController(TlsCertificateService tls) : ControllerBase
    {
        [HttpGet("tls-thumbprint")]
        public IActionResult GetTlsThumbprint()
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteAddress || !IPAddress.IsLoopback(remoteAddress))
            {
                return NotFound();
            }

            return Ok(new
            {
                thumbprint = tls.GetThumbprint()
            });
        }
    }
}