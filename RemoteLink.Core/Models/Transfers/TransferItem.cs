namespace RemoteLink.Core.Models.Transfers
{
    public sealed class TransferItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TransferId { get; set; }

        public string RelativePath { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public long TransferredBytes { get; set; }

        public string? Sha256 { get; set; }

        public bool IsDirectory { get; set; }

        public TransferJob Transfer { get; set; } = null!;
    }
}