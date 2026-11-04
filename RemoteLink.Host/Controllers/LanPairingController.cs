using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models;
using RemoteLink.Core.Models.Pairing;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/lan/pairing")]
    public sealed class LanPairingController(PairingSessionService sessions, PairingSecurityService security, DeviceIdentityService localIdentity, PairingCryptoSessionService cryptoSessions, DeviceSecurityService deviceSecurity, PairingApprovalService approvals, PairingDecisionSecurityService decisionSecurity) : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, DateTime> RecentRequests = new();

        [HttpPost("propose")]
        [RequestSizeLimit(4096)]
        public IActionResult Propose([FromBody] PairingProposal proposal)
        {
            if (HttpContext.Connection.LocalPort != 5019)
            {
                return NotFound();
            }

            if (proposal.RequestId == Guid.Empty || proposal.DeviceId == Guid.Empty || proposal.DeviceId == localIdentity.DeviceId)
            {
                return BadRequest("Invalid pairing request identifiers.");
            }

            if (string.IsNullOrWhiteSpace(proposal.DeviceName) || proposal.DeviceName.Length > 100 || string.IsNullOrWhiteSpace(proposal.IdentityPublicKey) || string.IsNullOrWhiteSpace(proposal.EphemeralPublicKey) || proposal.IdentityPublicKey.Length > 1024 || proposal.EphemeralPublicKey.Length > 1024)
            {
                return BadRequest("Invalid pairing request fields.");
            }

            if (!security.IsValidIdentityPublicKey(proposal.IdentityPublicKey) || !security.IsValidEphemeralPublicKey(proposal.EphemeralPublicKey))
            {
                return BadRequest("Invalid pairing public keys.");
            }

            string remoteAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            DateTime now = DateTime.UtcNow;

            if (RecentRequests.TryGetValue(remoteAddress, out DateTime previousRequest) && now - previousRequest < TimeSpan.FromSeconds(5))
            {
                return StatusCode(429, "Pairing requests are temporarily rate limited.");
            }

            RecentRequests[remoteAddress] = now;

            byte[] remoteEphemeralPublicKey = Convert.FromBase64String(proposal.EphemeralPublicKey);

            PairingKeyExchange exchange = security.CreateKeyExchange();

            bool sessionCreated;

            try
            {
                sessionCreated = cryptoSessions.TryCreateSession(proposal.RequestId, proposal.DeviceId, PairingCryptoRole.Responder, exchange, remoteEphemeralPublicKey, out _);
            }
            catch
            {
                exchange.Dispose();
                
                throw;
            }

            if (!sessionCreated)
            {
                exchange.Dispose();

                return Conflict("A pairing session with this ID already exists, or the session limit has been reached.");
            }

            try
            {
                byte[] identityPublicKey = Convert.FromBase64String(proposal.IdentityPublicKey);

                string fingerprint = Convert.ToHexString(SHA256.HashData(identityPublicKey));

                PairingRequest request = sessions.CreateRequest(proposal.RequestId, proposal.DeviceId, proposal.DeviceName, proposal.IdentityPublicKey, fingerprint);

                return Accepted(new PairingProposalResponse
                {
                    RequestId = request.RequestId,
                    AcceptedForReview = true,
                    Message = "Pairing request is pending local review."
                });
            }
            catch (InvalidOperationException)
            {
                cryptoSessions.RemoveSession(proposal.RequestId);

                return Conflict("A pairing request with this ID already exists.");
            }
            catch
            {
                cryptoSessions.RemoveSession(proposal.RequestId);
                
                throw;
            }
        }

        [HttpPost("handshake/{requestId:guid}")]
        public IActionResult Handshake(Guid requestId)
        {
            if (HttpContext.Connection.LocalPort != 5019)
            {
                return NotFound();
            }
            
            PairingRequest? request = sessions.GetRequest(requestId);

            if (request is null || request.Status != PairingStatus.Pending)
            {
                return NotFound("No pending pairing request exists.");
            }
            
            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(requestId);
            
            if (session is null || session.Role != PairingCryptoRole.Responder || session.RemoteDeviceId != request.DeviceId)
            {
                return Conflict("The responder session is unavailable.");
            }

            DeviceSecurityIdentity localPublicIdentity = deviceSecurity.GetPublicIdentity();

            byte[] initiatorIdentityKey = Convert.FromBase64String(request.PublicKey);
            
            byte[] responderIdentityKey = Convert.FromBase64String(localPublicIdentity.PublicKey);
            
            bool transcriptCreated = cryptoSessions.TryCreateTranscript(requestId, request.DeviceId, localIdentity.DeviceId, initiatorIdentityKey, responderIdentityKey, security, out byte[] transcriptHash);
            
            if (!transcriptCreated)
            {
                return Conflict("Unable to construct pairing transcript.");
            }
            
            SignedPairingMessage signedMessage = security.CreateSignedMessage(requestId, localIdentity.DeviceId,responderIdentityKey, session.LocalEphemeralPublicKey, transcriptHash, deviceSecurity);

            return Ok(new PairingHandshakeResponse
            {
                RequestId = requestId,
                ResponderDeviceId = localIdentity.DeviceId,
                ResponderDeviceName = localIdentity.DeviceName,
                SignedMessage = signedMessage
            });
        }

        [HttpPost("confirm-initiator/{requestId:guid}")]
        [RequestSizeLimit(4096)]
        public IActionResult ConfirmInitiator(Guid requestId, [FromBody] PairingInitiatorConfirmation confirmation)
        {
            if (HttpContext.Connection.LocalPort != 5019)
            {
                return NotFound();
            }
            
            PairingRequest? request = sessions.GetRequest(requestId);

            if (request is null || request.Status != PairingStatus.Pending || request.ExpiresUtc <= DateTime.UtcNow)
            {
                return NotFound("No pending pairing request exists.");
            }

            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(requestId);

            if (session is null || session.Role != PairingCryptoRole.Responder || session.RemoteDeviceId != request.DeviceId || session.RemoteEphemeralPublicKey is null)
            {
                return Conflict("The responder pairing session is unavailable.");
            }

            SignedPairingMessage? signed = confirmation?.SignedMessage;

            if (signed is null || signed.RequestId != requestId || signed.DeviceId != request.DeviceId)
            {
                return BadRequest("Invalid initiator confirmation.");
            }

            try
            {
                byte[] initiatorIdentityKey = Convert.FromBase64String(request.PublicKey);

                byte[] responderIdentityKey = Convert.FromBase64String(deviceSecurity.GetPublicIdentity().PublicKey);

                if (!cryptoSessions.TryCreateTranscript(requestId, request.DeviceId, localIdentity.DeviceId, initiatorIdentityKey, responderIdentityKey, security, out byte[] expectedTranscriptHash))
                {
                    return Conflict("Unable to reconstruct the pairing transcript.");
                }

                bool signatureVerified = security.VerifySignedMessage(signed, initiatorIdentityKey, session.RemoteEphemeralPublicKey, expectedTranscriptHash, requestId, request.DeviceId);

                if (!signatureVerified)
                {
                    sessions.TryUpdateStatus(requestId, PairingStatus.Pending, PairingStatus.Failed);

                    cryptoSessions.RemoveSession(requestId);

                    return UnprocessableEntity("The initiator signature or transcript is invalid.");
                }

                if (!sessions.TryUpdateStatus(requestId, PairingStatus.Pending, PairingStatus.AwaitingConfirmation))
                {
                    return Conflict("The pairing request is no longer pending.");
                }
                
                if (!approvals.TryCreate(requestId, request.DeviceId, request.ExpiresUtc))
                {
                    sessions.TryUpdateStatus(requestId, PairingStatus.AwaitingConfirmation, PairingStatus.Failed);

                    cryptoSessions.RemoveSession(requestId);

                    return Conflict("Unable to initialize pairing approval state.");
                }

                return Ok(new
                {
                    requestId,
                    initiatorAuthenticated = true,
                    awaitingConfirmation = true
                });
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
            {
                sessions.TryUpdateStatus(requestId, PairingStatus.Pending, PairingStatus.Failed);
                
                cryptoSessions.RemoveSession(requestId);
                
                return UnprocessableEntity("The initiator confirmation could not be verified.");
            }
        }
        
        [HttpPost("decision/{requestId:guid}")]
        [RequestSizeLimit(4096)]
        public IActionResult ReceiveDecision(Guid requestId, [FromBody] SignedPairingDecision decision)
        {
            if (HttpContext.Connection.LocalPort != 5019)
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
                return Conflict("The authenticated pairing session is unavailable.");
            }

            try
            {
                byte[] initiatorIdentityKey = Convert.FromBase64String(request.PublicKey);

                byte[] responderIdentityKey = Convert.FromBase64String(deviceSecurity.GetPublicIdentity().PublicKey);

                if (!cryptoSessions.TryCreateTranscript(requestId, request.DeviceId, localIdentity.DeviceId, initiatorIdentityKey, responderIdentityKey, security, out byte[] transcriptHash))
                {
                    return Conflict("Unable to reconstruct the pairing transcript.");
                }

                bool valid = decisionSecurity.VerifyDecision(decision, requestId, request.DeviceId, transcriptHash, initiatorIdentityKey);

                if (!valid)
                {
                    return UnprocessableEntity("The pairing decision signature is invalid.");
                }

                PairingApprovalSnapshot? existing = approvals.GetState(requestId);

                if (existing is null)
                {
                    return Conflict("Pairing approval state is unavailable.");
                }
                
                if (existing.RemoteDecision != PairingDecisionState.Pending)
                {
                    bool sameDecision = decision.Approved ? existing.RemoteDecision == PairingDecisionState.Approved : existing.RemoteDecision == PairingDecisionState.Rejected;

                    if (!sameDecision)
                    {
                        return Conflict("The remote pairing decision cannot be changed.");
                    }

                    return Ok(existing);
                }

                if (existing.Rejected)
                {
                    return Conflict("The pairing request has already been rejected.");
                }

                PairingApprovalSnapshot? updated = approvals.SetRemoteApproval(requestId, request.DeviceId, decision.Approved);

                if (updated is null)
                {
                    return Conflict("Unable to record the remote pairing decision.");
                }

                if (!decision.Approved)
                {
                    sessions.TryUpdateStatus(requestId, PairingStatus.AwaitingConfirmation, PairingStatus.Rejected);

                    cryptoSessions.RemoveSession(requestId);
                }

                return Ok(updated);
            }
            catch (Exception exception)when (exception is FormatException or CryptographicException or ArgumentException)
            {
                return UnprocessableEntity("The pairing decision could not be verified.");
            }
        }
        
        [HttpGet("decision/{requestId:guid}")]
        public IActionResult GetResponderDecision(Guid requestId)
        {
            if (HttpContext.Connection.LocalPort != 5019)
            {
                return NotFound();
            }

            PairingRequest? request = sessions.GetRequest(requestId);

            if (request is null || request.Status != PairingStatus.AwaitingConfirmation || request.ExpiresUtc <= DateTime.UtcNow)
            {
                return NotFound("No active authenticated pairing request exists.");
            }

            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(requestId);

            if (session is null || session.Role != PairingCryptoRole.Responder || session.RemoteDeviceId != request.DeviceId || session.ExpiresUtc <= DateTime.UtcNow)
            {
                return Conflict("The responder pairing session is unavailable.");
            }

            PairingApprovalSnapshot? approval = approvals.GetState(requestId);

            if (approval is null)
            {
                return Conflict("Pairing approval state is unavailable.");
            }

            if (approval.LocalDecision == PairingDecisionState.Pending)
            {
                return Ok(new
                {
                    requestId,
                    awaitingLocalDecision = true,
                    signedDecision = (SignedPairingDecision?)null
                });
            }

            try
            {
                byte[] initiatorIdentityKey = Convert.FromBase64String(request.PublicKey);

                byte[] responderIdentityKey = Convert.FromBase64String(deviceSecurity.GetPublicIdentity().PublicKey);

                if (!cryptoSessions.TryCreateTranscript(requestId, request.DeviceId, localIdentity.DeviceId, initiatorIdentityKey, responderIdentityKey, security, out byte[] transcriptHash))
                {
                    return Conflict("Unable to reconstruct the pairing transcript.");
                }

                bool approved = approval.LocalDecision == PairingDecisionState.Approved;

                SignedPairingDecision signedDecision = decisionSecurity.CreateDecision(requestId, localIdentity.DeviceId, approved, transcriptHash, deviceSecurity);

                return Ok(new
                {
                    requestId,
                    awaitingLocalDecision = false,
                    signedDecision
                });
            }
            catch (Exception exception)when (exception is FormatException or CryptographicException or ArgumentException)
            {
                return UnprocessableEntity("Unable to sign the responder pairing decision.");
            }
        }
    }
}