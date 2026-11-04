using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Transfers.Requests;
using RemoteLink.Core.Models.Transfers.Responses;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/local/transfers")]
    public sealed class LocalTransferController(TransferService transfers, TransferOfferService offers) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetTransfers(CancellationToken cancellationToken)
        {
            return Ok(await transfers.GetTransfersAsync(cancellationToken));
        }

        [HttpPost("{transferId:guid}/decision")]
        public async Task<IActionResult> Decide(Guid transferId, [FromBody] TransferDecisionRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await transfers.DecideAsync(transferId, request.Accepted, cancellationToken));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
        }
        
        [HttpPost("offers")]
        public async Task<IActionResult> CreateOffer([FromBody] CreateTransferOfferRequest request, CancellationToken cancellationToken)
        {
            try
            {
                TransferJobResponse result = await offers.CreateAndSendOfferAsync(request, cancellationToken);

                return Accepted(result);
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
            catch (HttpRequestException exception)
            {
                return StatusCode(502, exception.Message);
            }
        }
    }
}