namespace RemoteLink.Core.Models.Pairing
{
    public enum PairingStatus
    {
        Pending = 0,
        AwaitingConfirmation = 1,
        Paired = 2,
        Rejected = 3,
        Expired = 4,
        Failed = 5
    }
}