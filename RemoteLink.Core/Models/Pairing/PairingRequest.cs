namespace RemoteLink.Core.Models.Pairing
{
    public sealed class PairingRequest
    {
        public Guid RequestId { get; set; }
        
        public Guid DeviceId { get; set; }
        
        public string DeviceName { get; set; } = string.Empty;
        
        public string PublicKey { get; set; } = string.Empty;
        
        public string Fingerprint { get; set; } = string.Empty;
        
        public DateTime CreatedUtc { get; set; }
        
        public DateTime ExpiresUtc { get; set; }
        
        public PairingStatus Status { get; set; } = PairingStatus.Pending;
    }
}