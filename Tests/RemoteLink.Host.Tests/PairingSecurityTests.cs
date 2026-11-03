using System.Security.Cryptography;
using RemoteLink.Core.Models.Pairing;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Tests
{
    public sealed class PairingSecurityTests
    {
        [Fact]
        public void BothDevicesDeriveSameVerificationCode()
        {
            PairingSecurityService security = new();
            
            using PairingKeyExchange desktop = security.CreateKeyExchange();
            using PairingKeyExchange laptop = security.CreateKeyExchange();
            
            byte[] desktopPublicKey = desktop.ExportPublicKey();
            byte[] laptopPublicKey = laptop.ExportPublicKey();
            
            byte[] desktopSecret = desktop.DeriveSharedSecret(laptopPublicKey);
            byte[] laptopSecret = laptop.DeriveSharedSecret(desktopPublicKey);
            
            Assert.True(CryptographicOperations.FixedTimeEquals(desktopSecret, laptopSecret));
            
            using ECDsa desktopIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using ECDsa laptopIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            
            byte[] transcript = security.CreateTranscript(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), desktopIdentity.ExportSubjectPublicKeyInfo(), laptopIdentity.ExportSubjectPublicKeyInfo(), desktopPublicKey, laptopPublicKey);
            
            byte[] transcriptHash = security.CreateTranscriptHash(transcript);
            
            byte[] desktopVerificationKey = security.DeriveVerificationKey(desktopSecret, transcriptHash);
            byte[] laptopVerificationKey = security.DeriveVerificationKey(laptopSecret, transcriptHash);
            
            string desktopCode = security.CreateVerificationCode(desktopVerificationKey, transcriptHash);
            string laptopCode = security.CreateVerificationCode(laptopVerificationKey, transcriptHash);
            
            Assert.Equal(desktopCode, laptopCode);
            Assert.Matches(@"^\d{6}$", desktopCode);
            
            CryptographicOperations.ZeroMemory(desktopSecret);
            CryptographicOperations.ZeroMemory(laptopSecret);
            CryptographicOperations.ZeroMemory(desktopVerificationKey);
            CryptographicOperations.ZeroMemory(laptopVerificationKey);
        }

        [Fact]
        public void DifferentSessionsProduceDifferentSharedSecrets()
        {
            PairingSecurityService security = new();
            
            using PairingKeyExchange first = security.CreateKeyExchange();
            using PairingKeyExchange second = security.CreateKeyExchange();
            using PairingKeyExchange third = security.CreateKeyExchange();
            
            byte[] firstSecret = first.DeriveSharedSecret(second.ExportPublicKey());
            byte[] secondSecret = first.DeriveSharedSecret(third.ExportPublicKey());
            
            Assert.False(CryptographicOperations.FixedTimeEquals(firstSecret, secondSecret));
            
            CryptographicOperations.ZeroMemory(firstSecret);
            CryptographicOperations.ZeroMemory(secondSecret);
        }

        [Fact]
        public void ValidSignedPairingMessageIsAccepted()
        {
            PairingSecurityService security = new();
            
            using ECDsa identity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            
            using PairingKeyExchange exchange = security.CreateKeyExchange();

            Guid requestId = Guid.NewGuid();
            Guid deviceId = Guid.NewGuid();
            
            byte[] identityPublicKey = identity.ExportSubjectPublicKeyInfo();

            byte[] ephemeralPublicKey = exchange.ExportPublicKey();

            byte[] transcriptHash = SHA256.HashData("RemoteLink test transcript"u8.ToArray());
            
            SignedPairingMessage message = security.CreateSignedMessage(requestId, deviceId, identityPublicKey, ephemeralPublicKey, transcriptHash, data => identity.SignData(data, HashAlgorithmName.SHA256));
            
            bool verified = security.VerifySignedMessage(message, identityPublicKey, ephemeralPublicKey, transcriptHash, requestId, deviceId);
            
            Assert.True(verified);
        }

        [Fact]
        public void ModifiedTranscriptIsRejected()
        {
            PairingSecurityService security = new();
            
            using ECDsa identity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            
            using PairingKeyExchange exchange = security.CreateKeyExchange();
            
            Guid requestId = Guid.NewGuid();
            Guid deviceId = Guid.NewGuid();
            
            byte[] identityPublicKey = identity.ExportSubjectPublicKeyInfo();

            byte[] ephemeralPublicKey = exchange.ExportPublicKey();

            byte[] originalHash = SHA256.HashData("Original transcript"u8.ToArray());

            byte[] modifiedHash = SHA256.HashData("Modified transcript"u8.ToArray());
            
            SignedPairingMessage message = security.CreateSignedMessage(requestId, deviceId, identityPublicKey, ephemeralPublicKey, originalHash, data => identity.SignData(data, HashAlgorithmName.SHA256));
            
            bool verified = security.VerifySignedMessage(message, identityPublicKey, ephemeralPublicKey, modifiedHash, requestId, deviceId);
            
            Assert.False(verified);
        }

        [Fact]
        public void DifferentIdentityKeyIsRejected()
        {
            PairingSecurityService security = new();
            
            using ECDsa originalIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            using ECDsa differentIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            using PairingKeyExchange exchange = security.CreateKeyExchange();

            Guid requestId = Guid.NewGuid();
            Guid deviceId = Guid.NewGuid();

            byte[] originalPublicKey = originalIdentity.ExportSubjectPublicKeyInfo();

            byte[] differentPublicKey = differentIdentity.ExportSubjectPublicKeyInfo();

            byte[] ephemeralPublicKey = exchange.ExportPublicKey();

            byte[] transcriptHash = SHA256.HashData("Test transcript"u8.ToArray());
            
            SignedPairingMessage message = security.CreateSignedMessage(requestId, deviceId, originalPublicKey, ephemeralPublicKey, transcriptHash, data => originalIdentity.SignData(data, HashAlgorithmName.SHA256));
            
            bool verified = security.VerifySignedMessage(message, differentPublicKey, ephemeralPublicKey, transcriptHash, requestId, deviceId);
            
            Assert.False(verified);
        }
    }
}