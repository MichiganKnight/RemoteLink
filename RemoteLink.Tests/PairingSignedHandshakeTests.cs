using System.Security.Cryptography;
using RemoteLink.Core.Models.Pairing;
using RemoteLink.Host.Services;

namespace RemoteLink.Tests
{
    public sealed class PairingSignedHandshakeTests
    {
        [Fact]
        public void SignedResponderHandshakeVerifiesSuccessfully()
        {
            PairingSecurityService security = new();

            Guid requestId = Guid.NewGuid();
            Guid initiatorId = Guid.NewGuid();
            Guid responderId = Guid.NewGuid();

            using ECDsa initiatorIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            using ECDsa responderIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            using PairingKeyExchange initiatorExchange = security.CreateKeyExchange();

            using PairingKeyExchange responderExchange = security.CreateKeyExchange();

            byte[] initiatorIdentityKey = initiatorIdentity.ExportSubjectPublicKeyInfo();

            byte[] responderIdentityKey = responderIdentity.ExportSubjectPublicKeyInfo();

            byte[] initiatorEphemeral = initiatorExchange.ExportPublicKey();

            byte[] responderEphemeral = responderExchange.ExportPublicKey();

            byte[] transcript = security.CreateTranscript(requestId, initiatorId, responderId, initiatorIdentityKey, responderIdentityKey, initiatorEphemeral, responderEphemeral);

            byte[] transcriptHash = security.CreateTranscriptHash(transcript);

            SignedPairingMessage signed = security.CreateSignedMessage(requestId, responderId, responderIdentityKey, responderEphemeral, transcriptHash, data => responderIdentity.SignData(data, HashAlgorithmName.SHA256));

            Assert.True(security.VerifySignedMessage(signed, responderIdentityKey, responderEphemeral, transcriptHash, requestId, responderId));

            byte[] modifiedHash = (byte[])transcriptHash.Clone();
            modifiedHash[0] ^= 0x01;

            Assert.False(security.VerifySignedMessage(signed, responderIdentityKey, responderEphemeral, modifiedHash, requestId, responderId));
        }
    }
}