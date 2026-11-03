namespace RemoteLink.Core.Models
{
    public sealed class DeviceSecurityIdentity
    {
        public Guid DeviceId { get; set; }

        public string DeviceName { get; set; } = string.Empty;

        public string Algorithm { get; set; } = "ECDSA-P256";

        public string PublicKey { get; set; } = string.Empty;

        public string Fingerprint { get; set; } = string.Empty;
    }
}