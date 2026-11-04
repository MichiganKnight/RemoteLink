namespace RemoteLink.Core.Models
{
    public sealed class PairingHandshakeVerification
    {
        public Guid RequestId { get; set; }
        public Guid RemoteDeviceId { get; set; }

        public string RemoteDeviceName { get; set; } = string.Empty;
        public string RemoteIdentityFingerprint { get; set; } = string.Empty;

        public string VerificationCode { get; set; } = string.Empty;
        public bool SignatureVerified { get; set; }
    }
}