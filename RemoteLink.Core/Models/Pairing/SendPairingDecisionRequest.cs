namespace RemoteLink.Core.Models.Pairing
{
    public sealed class SendPairingDecisionRequest
    {
        public Guid RequestId { get; set; }

        public Guid RemoteDeviceId { get; set; }

        public string RemoteAddress { get; set; } = string.Empty;

        public string ExpectedTlsThumbprint { get; set; } = string.Empty;

        public string ExpectedRemoteIdentityFingerprint { get; set; } = string.Empty;

        public bool Approved { get; set; }
    }
}