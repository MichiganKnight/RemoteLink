using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class PairingSecurityService
    {
        private static readonly byte[] ProtocolLabel = Encoding.UTF8.GetBytes("RemoteLink-Pairing-v1");

        public PairingKeyExchange CreateKeyExchange()
        {
            return new PairingKeyExchange();
        }

        public byte[] CreateTranscript(Guid requestId, Guid initiatorDeviceId, Guid responderDeviceId, byte[] initiatorIdentityKey, byte[] responderIdentityKey, byte[] initiatorEphemeralKey, byte[] responderEphemeralKey)
        {
            using MemoryStream stream = new();
            
            WriteField(stream, ProtocolLabel);
            WriteField(stream, requestId.ToByteArray());
            WriteField(stream, initiatorDeviceId.ToByteArray());
            WriteField(stream, responderDeviceId.ToByteArray());
            WriteField(stream, initiatorIdentityKey);
            WriteField(stream, responderIdentityKey);
            WriteField(stream, initiatorEphemeralKey);
            WriteField(stream, responderEphemeralKey);

            return stream.ToArray();
        }

        public byte[] CreateTranscriptHash(byte[] transcript)
        {
            return SHA256.HashData(transcript);
        }

        public SignedPairingMessage CreateSignedMessage(Guid requestId, Guid deviceId, byte[] identityPublicKey, byte[] ephemeralPublicKey, byte[] transcriptHash, DeviceSecurityService deviceSecurity)
        {
            return CreateSignedMessage(requestId, deviceId, identityPublicKey, ephemeralPublicKey, transcriptHash, deviceSecurity.SignData);
        }

        public SignedPairingMessage CreateSignedMessage(Guid requestId, Guid deviceId, byte[] identityPublicKey, byte[] ephemeralPublicKey, byte[] transcriptHash, Func<byte[], byte[]> signData)
        {
            if (requestId == Guid.Empty || deviceId == Guid.Empty)
            {
                throw new ArgumentException("Valid request and device IDs are required.");
            }
            
            if (transcriptHash.Length != 32)
            {
                throw new ArgumentException("Transcript hash must be 32 bytes.");
            }
            
            byte[] signatureInput = CreateSignatureInput(requestId, deviceId, transcriptHash);
            
            byte[] signature = signData(signatureInput);

            return new SignedPairingMessage
            {
                RequestId = requestId,
                DeviceId = deviceId,
                PublicKey = Convert.ToBase64String(identityPublicKey),
                EphemeralPublicKey = Convert.ToBase64String(ephemeralPublicKey),
                TranscriptHash = Convert.ToBase64String(transcriptHash),
                Signature = Convert.ToBase64String(signature)
            };
        }
        
        public byte[] DeriveVerificationKey(byte[] sharedSecret, byte[] transcriptHash)
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, salt: transcriptHash, info: ProtocolLabel);
        }

        public string CreateVerificationCode(byte[] verificationKey, byte[] transcriptHash)
        {
            byte[] message = new byte[ProtocolLabel.Length + transcriptHash.Length];
            
            Buffer.BlockCopy(ProtocolLabel, 0, message, 0, ProtocolLabel.Length);
            Buffer.BlockCopy(transcriptHash, 0, message, ProtocolLabel.Length, transcriptHash.Length);
            
            byte[] mac = HMACSHA256.HashData(verificationKey, message);

            uint value = BinaryPrimitives.ReadUInt32BigEndian(mac.AsSpan(0, 4));
            
            return (value % 1_000_000).ToString("D6");
        }

        public bool VerifySignature(byte[] publicKey, byte[] data, byte[] signature)
        {
            using ECDsa verifier = ECDsa.Create();
            
            verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
            
            return verifier.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }

        public bool VerifySignedMessage(SignedPairingMessage message, byte[] expectedIdentityPublicKey, byte[] expectedEphemeralPublicKey, byte[] expectedTranscriptHash, Guid expectedRequestId, Guid expectedDeviceId)
        {
            if (message.RequestId != expectedRequestId || message.DeviceId != expectedDeviceId || expectedTranscriptHash.Length != 32)
            {
                return false;
            }

            try
            {
                byte[] messageIdentityKey = Convert.FromBase64String(message.PublicKey);

                byte[] messageEphemeralKey = Convert.FromBase64String(message.EphemeralPublicKey);

                byte[] messageTranscriptHash = Convert.FromBase64String(message.TranscriptHash);

                byte[] signature = Convert.FromBase64String(message.Signature);

                if (!CryptographicOperations.FixedTimeEquals(messageIdentityKey, expectedIdentityPublicKey) ||
                    !CryptographicOperations.FixedTimeEquals(messageEphemeralKey, expectedEphemeralPublicKey) ||
                    !CryptographicOperations.FixedTimeEquals(messageTranscriptHash, expectedTranscriptHash))
                {
                    return false;
                }

                byte[] signatureInput = CreateSignatureInput(expectedRequestId, expectedDeviceId, expectedTranscriptHash);

                return VerifySignature(expectedIdentityPublicKey, signatureInput, signature);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        public bool IsValidIdentityPublicKey(string encodedKey)
        {
            try
            {
                byte[] keyBytes = Convert.FromBase64String(encodedKey);

                using ECDsa key = ECDsa.Create();

                key.ImportSubjectPublicKeyInfo(keyBytes, out int bytesRead);

                return bytesRead == keyBytes.Length && key.KeySize == 256 && key.ExportParameters(false).Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
            {
                return false;
            }
        }

        public bool IsValidEphemeralPublicKey(string encodedKey)
        {
            try
            {
                byte[] keyBytes = Convert.FromBase64String(encodedKey);
                
                using ECDiffieHellman key = ECDiffieHellman.Create();
                
                key.ImportSubjectPublicKeyInfo(keyBytes, out int bytesRead);
                
                return bytesRead == keyBytes.Length && key.KeySize == 256 && key.ExportParameters(false).Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
            {
                return false;
            }
        }

        private static void WriteField(Stream stream, byte[] value)
        {
            Span<byte> length = stackalloc byte[4];
            
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
            
            stream.Write(length);
            stream.Write(value);
        }

        private static byte[] CreateSignatureInput(Guid requestId, Guid deviceId, byte[] transcriptHash)
        {
            using MemoryStream stream = new();
            
            WriteField(stream, Encoding.UTF8.GetBytes("RemoteLink-Pairing-Signature-v1"));
            
            WriteField(stream, requestId.ToByteArray());
            WriteField(stream, deviceId.ToByteArray());
            WriteField(stream, transcriptHash);

            return stream.ToArray();
        }
    }
    
    public sealed class PairingKeyExchange : IDisposable
    {
        private readonly ECDiffieHellman _keyExchange = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        public byte[] ExportPublicKey()
        {
            return _keyExchange.PublicKey.ExportSubjectPublicKeyInfo();
        }

        public byte[] DeriveSharedSecret(byte[] remotePublicKey)
        {
            using ECDiffieHellman remote = ECDiffieHellman.Create();
            
            remote.ImportSubjectPublicKeyInfo(remotePublicKey, out _);

            return _keyExchange.DeriveRawSecretAgreement(remote.PublicKey);
        }

        public void Dispose()
        {
            _keyExchange.Dispose();
        }
    }
}