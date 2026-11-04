using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Chat.Responses;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/local/chat-delivery")]
    public sealed class LocalChatDeliveryController(ChatDeliveryService delivery) : ControllerBase
    {
        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] LocalChatDeliveryRequest request, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
            {
                return NotFound();
            }

            if (request.DeviceId == Guid.Empty || request.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.Address))
            {
                return BadRequest("Device ID, message ID, and address are required.");
            }

            try
            {
                ChatDeliveryAcknowledgment acknowledgment = await delivery.DeliverAsync(request.DeviceId, request.Address, request.MessageId, cancellationToken);

                return Ok(acknowledgment);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException or CryptographicException or HttpRequestException or TaskCanceledException)
            {
                return BadRequest(exception.Message);
            }
        }
    }
    
    public sealed class LocalChatDeliveryRequest
    {
        public Guid DeviceId { get; set; }

        public Guid MessageId { get; set; }

        public string Address { get; set; } = string.Empty;
    }
}