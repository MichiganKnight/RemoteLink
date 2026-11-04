namespace RemoteLink.Core.Models.Pairing
{
    public sealed class SignedPairingDecision
    {
        public Guid RequestId { get; set; }

        public Guid DeviceId { get; set; }

        public bool Approved { get; set; }

        public string TranscriptHash { get; set; } = string.Empty;

        public string Signature { get; set; } = string.Empty;
    }
}