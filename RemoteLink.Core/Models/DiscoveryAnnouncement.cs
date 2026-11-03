namespace RemoteLink.Core.Models
{
    public sealed class DiscoveryAnnouncement
    {
        public string Protocol { get; set; } = "RemoteLink.Discovery";
        
        public int ProtocolVersion { get; set; } = 1;
        
        public Guid DeviceId { get; set; }
        
        public string DeviceName { get; set; } = string.Empty;

        public int DiscoveryPort { get; set; } = 5018;
    }
}