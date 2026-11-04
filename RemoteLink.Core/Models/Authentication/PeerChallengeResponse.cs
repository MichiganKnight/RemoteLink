namespace RemoteLink.Core.Models.Authentication
{
    public sealed class PeerChallengeResponse
    {
        public Guid ChallengeId { get; set; }

        public Guid ServerDeviceId { get; set; }

        public string Nonce { get; set; } = string.Empty;

        public DateTime ExpiresUtc { get; set; }

        public string ServerSignature { get; set; } = string.Empty;
        
        public string ServerCertificateHash { get; set; } = string.Empty;
    }
}