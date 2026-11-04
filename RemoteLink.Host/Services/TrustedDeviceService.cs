using System.Security.Cryptography;
using System.Text.Json;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class TrustedDeviceService
    {
        private readonly object _sync = new();

        private readonly string _filePath;

        private readonly Dictionary<Guid, TrustedDevice> _devices;

        public TrustedDeviceService()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLink", "Security");

            Directory.CreateDirectory(directory);

            _filePath = Path.Combine(directory, "trusted-devices.json");

            if (File.Exists(_filePath))
            {
                List<TrustedDevice>? saved = JsonSerializer.Deserialize<List<TrustedDevice>>(File.ReadAllText(_filePath));

                _devices = (saved ?? []).ToDictionary(device => device.DeviceId);
            }
            else
            {
                _devices = new Dictionary<Guid, TrustedDevice>();
            }
        }

        public IReadOnlyList<TrustedDevice> GetDevices()
        {
            lock (_sync)
            {
                return _devices.Values.Select(Copy)
                    .OrderBy(device => device.DeviceName)
                    .ToList();
            }
        }

        public TrustedDevice? GetDevice(Guid deviceId)
        {
            lock (_sync)
            {
                return _devices.TryGetValue(deviceId, out TrustedDevice? device) ? Copy(device) : null;
            }
        }

        public bool IsTrusted(Guid deviceId, string publicKey)
        {
            lock (_sync)
            {
                if (!_devices.TryGetValue(deviceId, out TrustedDevice? device))
                {
                    return false;
                }

                try
                {
                    return device.IsTrusted && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(device.PublicKey), Convert.FromBase64String(publicKey));
                }
                catch (FormatException)
                {
                    return false;
                }
            }
        }

        public void SaveVerifiedDevice(TrustedDevice device)
        {
            if (device.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(device.DeviceName) || string.IsNullOrWhiteSpace(device.PublicKey))
            {
                throw new ArgumentException("A complete verified device identity is required.");
            }

            byte[] publicKey;

            try
            {
                publicKey = Convert.FromBase64String(device.PublicKey);

                using ECDsa verifier = ECDsa.Create();

                verifier.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);

                if (bytesRead != publicKey.Length || verifier.KeySize != 256)
                {
                    throw new CryptographicException("Invalid device identity key.");
                }
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("Invalid public key encoding.", exception);
            }

            string fingerprint = Convert.ToHexString(SHA256.HashData(publicKey));

            if (!string.Equals(fingerprint, device.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException("Device fingerprint does not match its public key.");
            }

            lock (_sync)
            {
                if (_devices.TryGetValue(device.DeviceId, out TrustedDevice? existing) && !string.Equals(existing.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CryptographicException("A different identity is already trusted for this device ID.");
                }

                Dictionary<Guid, TrustedDevice> updated = new(_devices)
                {
                    [device.DeviceId] = Copy(device)
                };

                string temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

                try
                {
                    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(updated.Values.ToList(), new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

                    File.Move(temporaryPath, _filePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }

                _devices.Clear();

                foreach (KeyValuePair<Guid, TrustedDevice> entry in updated)
                {
                    _devices.Add(entry.Key, entry.Value);
                }
            }
        }

        private static TrustedDevice Copy(TrustedDevice device)
        {
            return new TrustedDevice
            {
                DeviceId = device.DeviceId,
                DeviceName = device.DeviceName,
                PublicKey = device.PublicKey,
                Fingerprint = device.Fingerprint,
                PairedUtc = device.PairedUtc,
                LastConnectedUtc = device.LastConnectedUtc,
                IsTrusted = device.IsTrusted
            };
        }
    }
}