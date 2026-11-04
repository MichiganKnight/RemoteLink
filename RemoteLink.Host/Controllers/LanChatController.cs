
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Chat.Requests;
using RemoteLink.Core.Models.Chat.Responses;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/lan/chat")]
    public sealed class LanChatController(ChatService chat, PeerAuthenticationService authentication) : ControllerBase
    {
        [HttpPost("messages")]
        [RequestSizeLimit(65536)]
        public async Task<IActionResult> ReceiveMessage([FromBody] ReceiveChatMessageRequest request, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5019)
            {
                return NotFound();
            }

            string? authorization = Request.Headers.Authorization.ToString();

            const string bearerPrefix = "Bearer ";

            if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            string token = authorization[bearerPrefix.Length..].Trim();

            if (!authentication.TryValidateSession(token, out Guid authenticatedDeviceId))
            {
                return Unauthorized();
            }

            if (request.SenderDeviceId != authenticatedDeviceId)
            {
                return StatusCode(403, "Sender identity mismatch.");
            }

            try
            {
                ChatDeliveryAcknowledgment acknowledgment = await chat.ReceiveMessageAsync(authenticatedDeviceId, request, cancellationToken);

                return Ok(acknowledgment);
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(403, "Device is no longer trusted.");
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
        }
    }
}