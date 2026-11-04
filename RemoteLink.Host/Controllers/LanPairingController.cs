
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Pairing;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/lan/pairing")]
    public sealed class LanPairingController(PairingSessionService sessions, PairingSecurityService security, DeviceIdentityService localIdentity, PairingCryptoSessionService cryptoSessions) : ControllerBase
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
    }
}