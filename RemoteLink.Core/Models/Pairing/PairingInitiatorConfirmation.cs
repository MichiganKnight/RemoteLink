namespace RemoteLink.Core.Models.Pairing
{
    public sealed class PairingInitiatorConfirmation
    {
        public SignedPairingMessage SignedMessage { get; set; } = new();
    }
}