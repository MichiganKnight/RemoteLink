namespace RemoteLink.Core.Models.Pairing
{
    public sealed class PairingHandshakeResponse
    {
        public Guid RequestId { get; set; }
        public Guid ResponderDeviceId { get; set; }
        
        public string ResponderDeviceName { get; set; } = string.Empty;

        public SignedPairingMessage SignedMessage { get; set; } = new();
    }
}