namespace RemoteLink.Core.Models.Authentication
{
    public sealed class PeerAuthenticationRequest
    {
        public Guid ChallengeId { get; set; }
        
        public Guid DeviceId { get; set; }
        
        public string Signature { get; set; } = string.Empty;
    }
}