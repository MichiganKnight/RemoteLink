namespace RemoteLink.Core.Models.Transfers.Requests
{
    public sealed class CreateTransferOfferRequest
    {
        public Guid RecipientDeviceId { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<CreateTransferItemRequest> Items { get; set; } = new();
    }

    public sealed class CreateTransferItemRequest
    {
        public string RelativePath { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public bool IsDirectory { get; set; }
    }
}