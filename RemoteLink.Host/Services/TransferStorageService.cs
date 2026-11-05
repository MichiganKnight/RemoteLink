
using System.Security.Cryptography;

namespace RemoteLink.Host.Services
{
    public sealed class TransferStorageService
    {
        private readonly string _rootDirectory;
        private readonly string _incomingDirectory;
        private readonly string _temporaryDirectory;

        public TransferStorageService()
        {
            _rootDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLink", "Transfers");

            _incomingDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "RemoteLink");

            _temporaryDirectory = Path.Combine(_rootDirectory, "Temporary");

            Directory.CreateDirectory(_rootDirectory);
            Directory.CreateDirectory(_incomingDirectory);
            Directory.CreateDirectory(_temporaryDirectory);
        }

        public string IncomingDirectory => _incomingDirectory;

        public string GetTemporaryFilePath(Guid transferId, Guid itemId)
        {
            string directory = Path.Combine(_temporaryDirectory, transferId.ToString("N"));

            Directory.CreateDirectory(directory);

            return Path.Combine(directory, itemId.ToString("N") + ".part");
        }

        public string GetCompletedFilePath(string fileName)
        {
            string safeName = Path.GetFileName(fileName);

            if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..")
            {
                throw new ArgumentException("Invalid file name.");
            }

            string destination = Path.Combine(_incomingDirectory, safeName);

            if (!File.Exists(destination) && !Directory.Exists(destination))
            {
                return destination;
            }

            string name = Path.GetFileNameWithoutExtension(safeName);
            string extension = Path.GetExtension(safeName);

            for (int index = 1; index < 10000; index++)
            {
                destination = Path.Combine(_incomingDirectory, $"{name} ({index}){extension}");

                if (!File.Exists(destination) && !Directory.Exists(destination))
                {
                    return destination;
                }
            }

            throw new IOException("Unable to generate a unique destination file name.");
        }

        public static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken = default)
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024, useAsync: true);

            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);

            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}