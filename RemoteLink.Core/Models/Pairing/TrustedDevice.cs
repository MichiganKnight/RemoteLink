namespace RemoteLink.Core.Models.Pairing
{
    public sealed class TrustedDevice
    {
        public Guid DeviceId { get; set; }
        
        public string DeviceName { get; set; } = string.Empty;
        
        public string PublicKey { get; set; } = string.Empty;
        
        public string Fingerprint { get; set; } = string.Empty;
        
        public DateTime PairedUtc { get; set; }
        
        public DateTime LastConnectedUtc { get; set; }
        
        public bool IsTrusted { get; set; } = true;
    }
}