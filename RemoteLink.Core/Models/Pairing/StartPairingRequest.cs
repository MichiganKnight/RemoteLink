namespace RemoteLink.Core.Models.Pairing
{
    public sealed class StartPairingRequest
    {
        public Guid DeviceId { get; set; }
        
        public string Address { get; set; } = string.Empty;
        
        public string ExpectedTlsThumbprint { get; set; } = string.Empty;
    }
}