using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/lifecycle")]
    public class LifecycleController(IHostApplicationLifetime lifetime, IConfiguration configuration) : ControllerBase
    {
        [HttpPost("shutdown")]
        public IActionResult Shutdown([FromHeader(Name = "X-RemoteLink-Shutdown-Token")] string? providedToken)
        {
            if (HttpContext.Connection.RemoteIpAddress is not { } remoteAddress || !IPAddress.IsLoopback(remoteAddress))
            {
                return Forbid();
            }
            
            string? expectedToken = configuration["REMOTELINK_SHUTDOWN_TOKEN"];

            if (string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(providedToken))
            {
                return Unauthorized();
            }

            byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
            byte[] providedBytes = Encoding.UTF8.GetBytes(providedToken);

            if (expectedBytes.Length != providedBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                return Unauthorized();
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(250);

                lifetime.StopApplication();
            });

            return Accepted(new
            {
                Message = "RemoteLink Host is shutting down..."
            });
        }
    }
}