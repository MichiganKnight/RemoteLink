using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Authentication;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/local/peer-test")]
    public sealed class LocalPeerTestController(TrustedPeerClientService peerClient) : ControllerBase
    {
        [HttpPost("authenticate")]
        public async Task<IActionResult> Authenticate([FromBody] PeerTestRequest request, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !System.Net.IPAddress.IsLoopback(remoteIp))
            {
                return NotFound();
            }

            if (request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.Address))
            {
                return BadRequest("Device ID and address are required.");
            }

            try
            {
                PeerAuthenticationResponse session = await peerClient.AuthenticateAsync(request.DeviceId, request.Address, cancellationToken);

                return Ok(new
                {
                    authenticated = true,
                    remoteDeviceId = session.ServerDeviceId,
                    sessionId = session.SessionId,
                    expiresUtc = session.ExpiresUtc
                });
            }
            catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or HttpRequestException or TaskCanceledException)
            {
                return BadRequest(new
                {
                    authenticated = false,
                    error = exception.Message
                });
            }
        }
    }
    
    public sealed class PeerTestRequest
    {
        public Guid DeviceId { get; set; }

        public string Address { get; set; } = string.Empty;
    }
}