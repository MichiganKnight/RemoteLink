using System.Security.Cryptography;
using RemoteLink.Host.Services;

namespace RemoteLink.Tests
{
    public sealed class PairingHandshakeTests
    {
        [Fact]
        public void InitiatorAndResponderCreateMatchingTranscriptHashes()
        {
            PairingSecurityService security = new();

            using PairingCryptoSessionService initiatorSessions = new();
            using PairingCryptoSessionService responderSessions = new();

            Guid requestId = Guid.NewGuid();
            Guid initiatorId = Guid.NewGuid();
            Guid responderId = Guid.NewGuid();

            using ECDsa initiatorIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            using ECDsa responderIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            byte[] initiatorIdentityKey = initiatorIdentity.ExportSubjectPublicKeyInfo();

            byte[] responderIdentityKey = responderIdentity.ExportSubjectPublicKeyInfo();

            PairingKeyExchange initiatorExchange = security.CreateKeyExchange();

            PairingKeyExchange responderExchange = security.CreateKeyExchange();

            byte[] initiatorEphemeralKey = initiatorExchange.ExportPublicKey();

            byte[] responderEphemeralKey = responderExchange.ExportPublicKey();

            Assert.True(initiatorSessions.TryCreateSession(requestId, responderId, PairingCryptoRole.Initiator, initiatorExchange, responderEphemeralKey, out _));

            Assert.True(responderSessions.TryCreateSession(requestId, initiatorId, PairingCryptoRole.Responder, responderExchange, initiatorEphemeralKey, out _));

            Assert.True(initiatorSessions.TryCreateTranscript(requestId, initiatorId, responderId, initiatorIdentityKey, responderIdentityKey, security, out byte[] initiatorHash));

            Assert.True(responderSessions.TryCreateTranscript(requestId, initiatorId, responderId, initiatorIdentityKey, responderIdentityKey, security, out byte[] responderHash));

            Assert.Equal(initiatorHash, responderHash);
        }
    }
}