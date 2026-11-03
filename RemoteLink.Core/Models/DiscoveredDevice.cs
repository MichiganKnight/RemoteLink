namespace RemoteLink.Core.Models
{
    public sealed class DiscoveredDevice
    {
        public Guid Id { get; set; }
        
        public string Name { get; set; } = string.Empty;
        
        public string IpAddress { get; set; } = string.Empty;
        
        public int DiscoveryPort { get; set; }
        
        public DateTime LastSeenUtc { get; set; }
        
        public bool IsOnline { get; set; }
    }
}