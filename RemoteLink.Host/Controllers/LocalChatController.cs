using System.Net;
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Chat.Requests;
using RemoteLink.Core.Models.Chat.Responses;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/local/chat")]
    public sealed class LocalChatController(ChatService chat) : ControllerBase
    {
        private bool IsLocalRequest()
        {
            return HttpContext.Connection.LocalPort == 5017 && HttpContext.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
        }

        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations(CancellationToken cancellationToken)
        {
            if (!IsLocalRequest())
            {
                return NotFound();
            }
            
            return Ok(await chat.GetConversationsAsync(cancellationToken));
        }

        [HttpPost("conversations/{deviceId:guid}")]
        public async Task<IActionResult> OpenConversation(Guid deviceId, CancellationToken cancellationToken)
        {
            if (!IsLocalRequest())
            {
                return NotFound();
            }

            try
            {
                return Ok(await chat.OpenConversationAsync(deviceId, cancellationToken));
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(403, exception.Message);
            }
        }

        [HttpGet("conversations/{deviceId:guid}/messages")]
        public async Task<IActionResult> GetMessages(Guid deviceId, [FromQuery] DateTime? beforeUtc, [FromQuery] Guid? beforeId, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
        {
            if (!IsLocalRequest())
            {
                return NotFound();
            }

            try
            {
                return Ok(await chat.GetMessagesAsync(deviceId, beforeUtc, beforeId, limit, cancellationToken));
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(403, exception.Message);
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
        }

        [HttpPost("conversations/{deviceId:guid}/messages")]
        public async Task<IActionResult> SendMessage(Guid deviceId, [FromBody] SendChatMessageRequest request, CancellationToken cancellationToken)
        {
            if (!IsLocalRequest())
            {
                return NotFound();
            }

            try
            {
                ChatMessageResponse message = await chat.QueueMessageAsync(deviceId, request.Content, cancellationToken);
                
                return Accepted(message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(403, exception.Message);
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingMessages(CancellationToken cancellationToken)
        {
            if (!IsLocalRequest())
            {
                return NotFound();
            }
            
            return Ok(await chat.GetPendingMessagesAsync(cancellationToken));
        }
        
        [HttpPost("conversations/{deviceId:guid}/read")]
        public async Task<IActionResult> MarkConversationRead(Guid deviceId, CancellationToken cancellationToken)
        {
            try
            {
                int updated = await chat.MarkConversationReadAsync(deviceId, cancellationToken);

                return Ok(new
                {
                    deviceId,
                    markedRead = updated
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
    }
}