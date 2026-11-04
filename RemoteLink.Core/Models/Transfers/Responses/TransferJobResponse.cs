namespace RemoteLink.Core.Models.Transfers.Responses
{
    public sealed class TransferJobResponse
    {
        public Guid Id { get; set; }

        public Guid RemoteDeviceId { get; set; }

        public string Name { get; set; } = string.Empty;

        public TransferDirection Direction { get; set; }

        public TransferStatus Status { get; set; }

        public long TotalBytes { get; set; }

        public long TransferredBytes { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime? StartedUtc { get; set; }

        public DateTime? CompletedUtc { get; set; }

        public string? ErrorMessage { get; set; }

        public List<TransferItemResponse> Items { get; set; } = new();
    }

    public sealed class TransferItemResponse
    {
        public Guid Id { get; set; }

        public string RelativePath { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public long TransferredBytes { get; set; }

        public bool IsDirectory { get; set; }
    }
}