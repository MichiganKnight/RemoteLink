using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models;
using RemoteLink.Core.Models.Pairing;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/local/pairing")]
    public class LocalPairingController(PairingClientService client, PairingSessionService sessions, ILogger<LocalPairingController> logger) : ControllerBase
    {
        [HttpPost("start")]
        public async Task<IActionResult> Start([FromBody] StartPairingRequest request, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteAddress || !IPAddress.IsLoopback(remoteAddress))
            {
                return NotFound();
            }

            try
            {
                PairingProposalResponse response = await client.SendProposalAsync(request, cancellationToken);

                return Accepted(response);
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (HttpRequestException exception)
            {
                return StatusCode(502, $"Remote pairing request failed: {exception.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(504, "The remote Host did not respond in time.");
            }
        }

        [HttpGet("incoming")]
        public IActionResult GetIncomingRequests()
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteAddress || !IPAddress.IsLoopback(remoteAddress))
            {
                return NotFound();
            }

            IReadOnlyList<PairingRequest> requests = sessions.GetRequests();

            return Ok(requests);
        }
        
        [HttpPost("verify")]
        public async Task<IActionResult> Verify([FromBody] VerifyPairingRequest request, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !IPAddress.IsLoopback(remoteIp))
            {
                return NotFound();
            }

            if (request.RequestId == Guid.Empty || request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.Address) || string.IsNullOrWhiteSpace(request.ExpectedTlsThumbprint))
            {
                return BadRequest("Invalid pairing verification request.");
            }

            try
            {
                PairingHandshakeVerification result = await client.VerifyHandshakeAsync(request.RequestId, request.DeviceId, request.Address, request.ExpectedTlsThumbprint, cancellationToken);

                return Ok(result);
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "Pairing handshake HTTP request failed for device {DeviceId}", request.DeviceId);

                return StatusCode(502, new
                {
                    Error = "Remote pairing handshake request failed.",
                    Message = exception.Message,
                    InnerException = exception.InnerException?.Message,
                    StatusCode = exception.StatusCode.HasValue ? (int)exception.StatusCode.Value : (int?)null
                });
            }
            catch (CryptographicException exception)
            {
                return StatusCode(422, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, "Pairing verification timed out.");
            }
        }
    }
}