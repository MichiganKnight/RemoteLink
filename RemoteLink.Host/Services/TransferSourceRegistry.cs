using System.Collections.Concurrent;

namespace RemoteLink.Host.Services
{
    public sealed class TransferSourceRegistry
    {
        private readonly ConcurrentDictionary<Guid, string> _sources = new();

        public void Register(Guid itemId, string sourcePath)
        {
            string fullPath = Path.GetFullPath(sourcePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Transfer source file does not exist.", fullPath);
            }

            _sources[itemId] = fullPath;
        }

        public string Resolve(Guid itemId)
        {
            if (!_sources.TryGetValue(itemId, out string? path))
            {
                throw new FileNotFoundException("Transfer source file is not registered.");
            }

            return path;
        }

        public void Remove(Guid itemId)
        {
            _sources.TryRemove(itemId, out _);
        }
    }
}