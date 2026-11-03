namespace RemoteLink.Core.Models.Pairing
{
    public sealed class SignedPairingMessage
    {
        public Guid RequestId { get; set; }
        
        public Guid DeviceId { get; set; }
        
        public string PublicKey { get; set; } = string.Empty;
        
        public string EphemeralPublicKey { get; set; } = string.Empty;

        public string TranscriptHash { get; set; } = string.Empty;
        
        public string Signature { get; set; } = string.Empty;
    }
}