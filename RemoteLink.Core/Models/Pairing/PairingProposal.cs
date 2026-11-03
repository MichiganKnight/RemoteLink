namespace RemoteLink.Core.Models.Pairing
{
    public sealed class PairingProposal
    {
        public Guid RequestId { get; set; }
        
        public Guid DeviceId { get; set; }
        
        public string DeviceName { get; set; } = string.Empty;
        
        public string IdentityPublicKey { get; set; } = string.Empty;
        
        public string EphemeralPublicKey { get; set; } = string.Empty;
    }
}