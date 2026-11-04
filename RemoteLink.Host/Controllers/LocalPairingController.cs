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
    public class LocalPairingController(PairingClientService client, PairingSessionService sessions, LanDiscoveryService discovery, PairingCryptoSessionService cryptoSessions, PairingSecurityService pairingSecurity, DeviceIdentityService identity, DeviceSecurityService deviceSecurity) : ControllerBase
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
                return StatusCode(502, exception.Message);
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
        
        [HttpPost("start-discovered/{deviceId:guid}")]
        public async Task<IActionResult> StartDiscovered(Guid deviceId, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !IPAddress.IsLoopback(remoteIp))
            {
                return NotFound();
            }

            DiscoveredDevice? device = discovery.GetDevices().FirstOrDefault(item => item.Id == deviceId && item.IsOnline);

            if (device is null)
            {
                return NotFound("The selected device is no longer available.");
            }

            try
            {
                PairingHandshakeVerification verification = await client.StartDiscoveredPairingAsync(device.Id, device.IpAddress, cancellationToken);

                return Ok(verification);
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (CryptographicException exception)
            {
                return StatusCode(422, exception.Message);
            }
            catch (HttpRequestException exception)
            {
                return StatusCode(502, $"Unable to contact remote Host: {exception.Message}");
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
            catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(504, "The remote Host did not respond in time.");
            }
        }

        [HttpGet("incoming/{requestId:guid}/verification")]
        public IActionResult GetIncomingVerification(Guid requestId)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !IPAddress.IsLoopback(remoteIp))
            {
                return NotFound();
            }
            
            PairingRequest? request = sessions.GetRequest(requestId);

            if (request is null || request.Status != PairingStatus.Pending || request.ExpiresUtc <= DateTime.UtcNow)
            {
                return NotFound("No active pairing request exists.");
            }
            
            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(requestId);

            if (session is null || session.Role != PairingCryptoRole.Responder || session.RemoteDeviceId != request.DeviceId || session.ExpiresUtc <= DateTime.UtcNow)
            {
                return Conflict("The responder session is unavailable.");
            }

            try
            {
                byte[] initiatorIdentityKey = Convert.FromBase64String(request.PublicKey);

                DeviceSecurityIdentity localIdentity = deviceSecurity.GetPublicIdentity();

                byte[] responderIdentityKey = Convert.FromBase64String(localIdentity.PublicKey);

                string code = pairingSecurity.CalculateSessionVerificationCode(requestId, request.DeviceId, identity.DeviceId, initiatorIdentityKey, responderIdentityKey, cryptoSessions);

                return Ok(new PairingLocalVerification
                {
                    RequestId = requestId,
                    RemoteDeviceId = request.DeviceId,
                    RemoteDeviceName = request.DeviceName,
                    VerificationCode = code,
                    ExpiresUtc = session.ExpiresUtc < request.ExpiresUtc ? session.ExpiresUtc : request.ExpiresUtc
                });
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or InvalidOperationException)
            {
                return Conflict("The pairing verification code is unavailable.");
            }
        }
    }
}