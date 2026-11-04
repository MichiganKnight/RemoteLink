namespace RemoteLink.Core.Models.Pairing
{
    public sealed class PairingDecisionResponse
    {
        public Guid RequestId { get; set; }

        public bool AwaitingLocalDecision { get; set; }

        public SignedPairingDecision? SignedDecision { get; set; }
    }
}