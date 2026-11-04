namespace RemoteLink.Core.Models.Pairing
{
    public sealed class LocalPairingDecisionRequest
    {
        public Guid RequestId { get; set; }

        public bool Approved { get; set; }
    }
}