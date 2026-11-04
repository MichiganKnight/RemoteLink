namespace RemoteLink.Core.Models.Transfers
{
    public sealed class TransferJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid RemoteDeviceId { get; set; }

        public string Name { get; set; } = string.Empty;

        public TransferDirection Direction { get; set; }

        public TransferStatus Status { get; set; } = TransferStatus.Pending;

        public long TotalBytes { get; set; }

        public long TransferredBytes { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime? StartedUtc { get; set; }

        public DateTime? CompletedUtc { get; set; }

        public string? ErrorMessage { get; set; }

        public ICollection<TransferItem> Items { get; set; } = new List<TransferItem>();
    }
}