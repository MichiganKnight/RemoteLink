namespace RemoteLink.Host.Services
{
    public sealed record TransferFileAcknowledgment(Guid TransferId, Guid ItemId, long ReceivedBytes, string Sha256, bool Completed);
}