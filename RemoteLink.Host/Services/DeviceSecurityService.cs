using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RemoteLink.Core.Models;

namespace RemoteLink.Host.Services
{
    public sealed class DeviceSecurityService : IDisposable
    {
        private readonly ECDsa _privateKey;
        private readonly DeviceIdentityService _deviceIdentity;

        public DeviceSecurityService(DeviceIdentityService deviceIdentity)
        {
            _deviceIdentity = deviceIdentity;
            
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLink", "Security");
            
            Directory.CreateDirectory(directory);
            
            string keyPath = Path.Combine(directory, "device-key.bin");

            if (File.Exists(keyPath))
            {
                byte[] encryptedKey = File.ReadAllBytes(keyPath);

                byte[] privateKey;

                try
                {
                    privateKey = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);
                }
                catch (CryptographicException exception)
                {
                    throw new InvalidOperationException($"RemoteLink could not decrypt its existing device private key at: {keyPath}", exception);
                }

                ECDsa loadedKey = ECDsa.Create();

                try
                {
                    loadedKey.ImportPkcs8PrivateKey(privateKey, out _);

                    if (loadedKey.KeySize != 256 || loadedKey.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                    {
                        throw new CryptographicException("Unsupported stored device key.");
                    }

                    _privateKey = loadedKey;
                }
                catch
                {
                    loadedKey.Dispose();

                    throw;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(privateKey);
                }
                
                Console.WriteLine("[Security] Existing device identity loaded.");
            }
            else
            {
                ECDsa newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

                try
                {
                    byte[] privateKey = newKey.ExportPkcs8PrivateKey();

                    byte[] encryptedKey;

                    try
                    {
                        encryptedKey = ProtectedData.Protect(privateKey, null, DataProtectionScope.CurrentUser);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(privateKey);
                    }

                    using (FileStream stream = new(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(encryptedKey);
                        stream.Flush(flushToDisk: true);
                    }

                    if (!File.Exists(keyPath))
                    {
                        throw new IOException($"Device security key was not saved: {keyPath}");
                    }
                    
                    Console.WriteLine($"[Security] Encrypted key saved successfully: {keyPath}");

                    _privateKey = newKey;

                    Console.WriteLine("[Security] New device identity generated and saved.");
                }
                catch
                {
                    newKey.Dispose();
                    
                    throw;
                }
            }
        }

        public DeviceSecurityIdentity GetPublicIdentity()
        {
            byte[] publicKey = _privateKey.ExportSubjectPublicKeyInfo();

            byte[] fingerprint = SHA256.HashData(publicKey);

            return new DeviceSecurityIdentity
            {
                DeviceId = _deviceIdentity.DeviceId,
                DeviceName = _deviceIdentity.DeviceName,
                Algorithm = "ECDSA-P256",
                PublicKey = Convert.ToBase64String(publicKey),
                Fingerprint = Convert.ToHexString(fingerprint)
            };
        }
        
        public byte[] SignData(byte[] data)
        {
            return _privateKey.SignData(data, HashAlgorithmName.SHA256);
        }

        public void Dispose()
        {
            _privateKey.Dispose();
        }
        
        public X509Certificate2 CreateServerCertificate()
        {
            CertificateRequest request = new($"CN={_deviceIdentity.DeviceId}", _privateKey, HashAlgorithmName.SHA256);
            
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1")
            }, true));

            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        }
    }
}