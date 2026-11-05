namespace RemoteLink.Core.Models.Transfers.Requests
{
    public sealed class CreateLocalFileTransferRequest
    {
        public Guid RecipientDeviceId { get; set; }

        public string SourcePath { get; set; } = string.Empty;
    }
}