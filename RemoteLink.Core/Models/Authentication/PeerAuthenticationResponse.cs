namespace RemoteLink.Core.Models.Authentication
{
    public sealed class PeerAuthenticationResponse
    {
        public Guid SessionId { get; set; }
        
        public Guid ServerDeviceId { get; set; }
        
        public string AccessToken { get; set; } = string.Empty;
        
        public DateTime ExpiresUtc { get; set; }
    }
}