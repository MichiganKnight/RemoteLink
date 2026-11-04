namespace RemoteLink.Core.Models.Transfers
{
    public enum TransferStatus
    {
        Pending = 0,
        AwaitingApproval = 1,
        Accepted = 2,
        Transferring = 3,
        Paused = 4,
        Completed = 5,
        Failed = 6,
        Rejected = 7,
        Cancelled = 8
    }
}