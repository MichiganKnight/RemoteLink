namespace RemoteLink.Core.Models.Pairing
{
    public sealed class PairingLocalVerification
    {
        public required Guid RequestId { get; set; }
        public required Guid RemoteDeviceId { get; set; }
        
        public required string RemoteDeviceName { get; set; } = string.Empty;
        public required string VerificationCode { get; set; } = string.Empty;
        
        public required DateTime ExpiresUtc { get; set; }
    }
}