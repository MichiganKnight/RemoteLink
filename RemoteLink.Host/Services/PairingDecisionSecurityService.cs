using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class PairingDecisionSecurityService
    {
        private static readonly byte[] ProtocolLabel = Encoding.UTF8.GetBytes("RemoteLink-Pairing-Decision-v1");

        public SignedPairingDecision CreateDecision(Guid requestId, Guid deviceId, bool approved, byte[] transcriptHash, DeviceSecurityService deviceSecurity)
        {
            ValidateArguments(requestId, deviceId, transcriptHash);

            byte[] message = CreateSignatureInput(requestId, deviceId, approved, transcriptHash);

            byte[] signature = deviceSecurity.SignData(message);

            return new SignedPairingDecision
            {
                RequestId = requestId,
                DeviceId = deviceId,
                Approved = approved,
                TranscriptHash = Convert.ToBase64String(transcriptHash),
                Signature = Convert.ToBase64String(signature)
            };
        }

        public bool VerifyDecision(SignedPairingDecision decision, Guid expectedRequestId, Guid expectedDeviceId, byte[] expectedTranscriptHash, byte[] expectedIdentityPublicKey)
        {
            if (decision is null || decision.RequestId != expectedRequestId || decision.DeviceId != expectedDeviceId || expectedTranscriptHash.Length != 32)
            {
                return false;
            }

            try
            {
                byte[] receivedHash = Convert.FromBase64String(decision.TranscriptHash);

                byte[] signature = Convert.FromBase64String(decision.Signature);

                if (!CryptographicOperations.FixedTimeEquals(receivedHash, expectedTranscriptHash))
                {
                    return false;
                }

                byte[] message = CreateSignatureInput(expectedRequestId, expectedDeviceId, decision.Approved, expectedTranscriptHash);

                using ECDsa verifier = ECDsa.Create();

                verifier.ImportSubjectPublicKeyInfo(expectedIdentityPublicKey, out int bytesRead);

                if (bytesRead != expectedIdentityPublicKey.Length)
                {
                    return false;
                }

                return verifier.VerifyData(message, signature, HashAlgorithmName.SHA256);
            }
            catch (Exception exception)when (exception is FormatException or CryptographicException or ArgumentException)
            {
                return false;
            }
        }

        private static byte[] CreateSignatureInput(Guid requestId, Guid deviceId, bool approved, byte[] transcriptHash)
        {
            using MemoryStream stream = new();

            WriteField(stream, ProtocolLabel);
            WriteField(stream, requestId.ToByteArray());
            WriteField(stream, deviceId.ToByteArray());
            WriteField(stream, [approved ? (byte)1 : (byte)0]);
            WriteField(stream, transcriptHash);

            return stream.ToArray();
        }

        private static void WriteField(Stream stream, byte[] value)
        {
            Span<byte> length = stackalloc byte[4];

            BinaryPrimitives.WriteInt32BigEndian(length, value.Length);

            stream.Write(length);
            stream.Write(value);
        }

        private static void ValidateArguments(Guid requestId, Guid deviceId, byte[] transcriptHash)
        {
            if (requestId == Guid.Empty || deviceId == Guid.Empty)
            {
                throw new ArgumentException("Valid pairing and device IDs are required.");
            }

            if (transcriptHash is null || transcriptHash.Length != 32)
            {
                throw new ArgumentException("A valid SHA-256 transcript hash is required.");
            }
        }
    }
}