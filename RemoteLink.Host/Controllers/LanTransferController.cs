using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Transfers.Requests;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/lan/transfers")]
    public sealed class LanTransferController(PeerAuthenticationService authentication, TransferService transfers) : ControllerBase
    {
        [HttpPost("offers")]
        public async Task<IActionResult> ReceiveOffer([FromBody] IncomingTransferRequest request, CancellationToken cancellationToken)
        {
            string? authorization = Request.Headers.Authorization.ToString();

            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            string token = authorization["Bearer ".Length..].Trim();

            if (!authentication.TryValidateSession(token, out Guid authenticatedDeviceId))
            {
                return Unauthorized();
            }

            try
            {
                return Ok(await transfers.ReceiveOfferAsync(authenticatedDeviceId, request, cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
            catch (OverflowException)
            {
                return BadRequest("Transfer size exceeds supported limits.");
            }
        }
    }
}