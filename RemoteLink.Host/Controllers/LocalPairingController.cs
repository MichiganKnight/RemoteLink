using System.Net;
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Pairing;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/local/pairing")]
    public class LocalPairingController(PairingClientService client) : ControllerBase
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
    }
}