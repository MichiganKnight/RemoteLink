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
    public class LocalPairingController(PairingClientService client, PairingSessionService sessions, LanDiscoveryService discovery, PairingCryptoSessionService cryptoSessions, PairingSecurityService pairingSecurity, DeviceIdentityService identity, DeviceSecurityService deviceSecurity, PairingApprovalService approvals, TrustedDeviceService trustedDevices) : ControllerBase
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

            if (request is null || request.Status != PairingStatus.AwaitingConfirmation || request.ExpiresUtc <= DateTime.UtcNow)
            {
                return NotFound("No authenticated pairing request is awaiting confirmation.");
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
        
        //[HttpPost("decision")]
        public async Task<IActionResult> SendDecision([FromBody] SendPairingDecisionRequest request, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !IPAddress.IsLoopback(remoteIp))
            {
                return NotFound();
            }

            try
            {
                PairingApprovalSnapshot result = await client.SendDecisionAsync(request, cancellationToken);

                return Ok(result);
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (CryptographicException exception)
            {
                return UnprocessableEntity(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
            catch (HttpRequestException exception)
            {
                return StatusCode(502, $"Unable to deliver pairing decision: {exception.Message}");
            }
            catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(504, "The remote Host did not respond in time.");
            }
        }
        
        [HttpPost("incoming/decision")]
        public IActionResult DecideIncoming([FromBody] LocalPairingDecisionRequest decision)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !IPAddress.IsLoopback(remoteIp))
            {
                return NotFound();
            }

            PairingRequest? request = sessions.GetRequest(decision.RequestId);

            if (request is null || request.Status != PairingStatus.AwaitingConfirmation || request.ExpiresUtc <= DateTime.UtcNow)
            {
                return NotFound("No authenticated incoming pairing request exists.");
            }

            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(decision.RequestId);

            if (session is null || session.Role != PairingCryptoRole.Responder || session.RemoteDeviceId != request.DeviceId || session.ExpiresUtc <= DateTime.UtcNow)
            {
                return Conflict("The responder pairing session is unavailable.");
            }

            PairingApprovalSnapshot? updated = approvals.SetLocalApproval(decision.RequestId, decision.Approved);

            if (updated is null)
            {
                return Conflict("The local pairing decision cannot be changed.");
            }

            return Ok(updated);
        }
        
        [HttpGet("outgoing/{requestId:guid}/decision")]
        public async Task<IActionResult> GetOutgoingDecision(Guid requestId, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !IPAddress.IsLoopback(remoteIp))
            {
                return NotFound();
            }

            try
            {
                PairingApprovalSnapshot result = await client.RefreshResponderDecisionAsync(requestId, cancellationToken);

                return Ok(result);
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
            catch (CryptographicException exception)
            {
                return UnprocessableEntity(exception.Message);
            }
            catch (HttpRequestException exception)
            {
                return StatusCode(502, $"Unable to retrieve responder decision: {exception.Message}");
            }
            catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(504, "The responder did not reply in time.");
            }
        }

        [HttpPost("outgoing/decision")]
        public async Task<IActionResult> DecideOutgoing([FromBody] LocalPairingDecisionRequest request, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
            {
                return NotFound();
            }

            try
            {
                return Ok(await client.SendStoredDecisionAsync(request, cancellationToken));
            }
            catch (InvalidOperationException e)
            {
                return Conflict(e.Message);
            }
            catch (Exception e) when (e is HttpRequestException or CryptographicException or TaskCanceledException)
            {
                return StatusCode(502, e.Message);
            }
        }

        [HttpPost("outgoing/{requestId:guid}/finalize")]
        public async Task<IActionResult> FinalizeOutgoing(Guid requestId, CancellationToken cancellationToken)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
            {
                return NotFound();
            }

            try
            {
                return Ok(await client.FinalizePairingAsync(requestId, cancellationToken));
            }
            catch (InvalidOperationException e)
            {
                return Conflict(e.Message);
            }
            catch (Exception e) when (e is HttpRequestException or CryptographicException or TaskCanceledException)
            {
                return StatusCode(502, e.Message);
            }
        }

        [HttpGet("trusted")]
        public IActionResult GetTrustedDevices()
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
            {
                return NotFound();
            }

            return Ok(trustedDevices.GetDevices());
        }

        [HttpGet("incoming/{requestId:guid}/decision")]
        public IActionResult GetIncomingDecision(Guid requestId)
        {
            if (HttpContext.Connection.LocalPort != 5017 || HttpContext.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
            {
                return NotFound();
            }

            return approvals.GetState(requestId) is { } state ? Ok(state) : NotFound();
        }
    }
}