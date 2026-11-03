namespace RemoteLink.Core.Models.Pairing
{
    public sealed class PairingProposalResponse
    {
        public Guid RequestId { get; set; }
        
        public bool AcceptedForReview { get; set; }
        
        public string Message { get; set; } = string.Empty;
    }
}