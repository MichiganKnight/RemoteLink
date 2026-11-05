namespace RemoteLink.Core.Models.Transfers.Requests
{
    public sealed class TransferDecisionNotification
    {
        public Guid TransferId { get; set; }

        public Guid SenderDeviceId { get; set; }

        public Guid RecipientDeviceId { get; set; }

        public bool Accepted { get; set; }
    }
}