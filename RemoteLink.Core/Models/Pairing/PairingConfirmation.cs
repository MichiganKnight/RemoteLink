namespace RemoteLink.Core.Models.Pairing
{
    public sealed class PairingConfirmation
    {
        public Guid RequestId { get; set; }
        
        public bool Approved { get; set; }
    }
}