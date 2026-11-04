namespace RemoteLink.Core.Models.Transfers.Requests
{
    public sealed class IncomingTransferRequest
    {
        public Guid TransferId { get; set; }

        public Guid SenderDeviceId { get; set; }

        public Guid RecipientDeviceId { get; set; }

        public string Name { get; set; } = string.Empty;

        public long TotalBytes { get; set; }

        public List<IncomingTransferItem> Items { get; set; } = new();
    }

    public sealed class IncomingTransferItem
    {
        public Guid ItemId { get; set; }

        public string RelativePath { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public bool IsDirectory { get; set; }
    }
}