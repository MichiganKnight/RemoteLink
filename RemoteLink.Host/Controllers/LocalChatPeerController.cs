using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/local/chat-peers")]
    public sealed class LocalChatPeerController(ChatPeerAddressResolver addresses, TrustedDeviceService trustedDevices) : ControllerBase
    {
        [HttpPost("{deviceId:guid}/address")]
        public IActionResult RegisterAddress(Guid deviceId, [FromBody] RegisterChatPeerAddressRequest request)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !IPAddress.IsLoopback(remoteIp))
            {
                return NotFound();
            }

            if (trustedDevices.GetDevice(deviceId) is not { IsTrusted: true })
            {
                return BadRequest("Device is not trusted.");
            }

            if (!IPAddress.TryParse(request.Address, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork || !IsPrivateLanAddress(address))
            {
                return BadRequest("A private IPv4 LAN address is required.");
            }

            addresses.Update(deviceId, address.ToString());

            return Ok(new
            {
                deviceId,
                address = address.ToString()
            });
        }

        private static bool IsPrivateLanAddress(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();

            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168);
        }
    }

    public sealed class RegisterChatPeerAddressRequest
    {
        public string Address { get; set; } = string.Empty;
    }
}