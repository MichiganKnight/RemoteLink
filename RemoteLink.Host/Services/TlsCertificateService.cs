using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteLink.Host.Services
{
    public sealed class TlsCertificateService : IDisposable
    {
        private readonly X509Certificate2 _certificate;
        
        public TlsCertificateService()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLink", "Security");
            
            Directory.CreateDirectory(directory);
            
            string certificatePath = Path.Combine(directory, "tls-certificate.pfx");

            if (File.Exists(certificatePath))
            {
                _certificate = LoadCertificate(certificatePath);
                
                Console.WriteLine("[TLS] Existing certificate loaded.");
            }
            else
            {
                _certificate = CreateCertificate(certificatePath);
                
                Console.WriteLine("[TLS] New certificate generated and saved.");
            }
            
            Console.WriteLine($"[TLS] Certificate Thumbprint: {_certificate.Thumbprint}");

            if (!_certificate.HasPrivateKey)
            {
                throw new CryptographicException("The TLS certificate has no private key.");
            }
        }
        
        public X509Certificate2 GetCertificate() => _certificate;

        private static X509Certificate2 CreateCertificate(string certificatePath)
        {
            using RSA rsa = RSA.Create(3072);
            
            CertificateRequest request = new("CN=RemoteLink", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1")
            }, true));
            
            SubjectAlternativeNameBuilder san = new();
            
            san.AddDnsName("localhost");
            san.AddIpAddress(IPAddress.Loopback);
            san.AddIpAddress(IPAddress.IPv6Loopback);
            
            request.CertificateExtensions.Add(san.Build());
            
            using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
            
            byte[] pfx = generated.Export(X509ContentType.Pkcs12, string.Empty);

            byte[] encryptedPfx;

            try
            {
                encryptedPfx = ProtectedData.Protect(pfx, null, DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
            }

            using FileStream stream = new(certificatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            
            stream.Write(encryptedPfx);
            stream.Flush(flushToDisk: true);
            
            return LoadCertificate(certificatePath);
        }

        private static X509Certificate2 LoadCertificate(string certificatePath)
        {
            byte[] encryptedPfx = File.ReadAllBytes(certificatePath);
            
            byte[] pfx = ProtectedData.Unprotect(encryptedPfx, null, DataProtectionScope.CurrentUser);

            try
            {
                return X509CertificateLoader.LoadPkcs12(pfx, string.Empty, X509KeyStorageFlags.UserKeySet);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
            }
        }

        public void Dispose()
        {
            _certificate.Dispose();
        }
    }
}