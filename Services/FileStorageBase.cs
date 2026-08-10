namespace altinnendata_api.Services
{
    /// <summary>
    /// Shared local-filesystem storage: path resolution, reads, and deletes under a root directory.
    /// </summary>
    public abstract class FileStorageBase
    {
        protected readonly string Root;
        protected readonly long MaxBytes;

        protected FileStorageBase(string root, long maxBytes)
        {
            Root = Path.GetFullPath(root);
            MaxBytes = maxBytes;
            Directory.CreateDirectory(Root);
        }

        public Stream OpenRead(string storedPath) =>
            new FileStream(ResolvePath(storedPath), FileMode.Open, FileAccess.Read, FileShare.Read);

        public void Delete(string storedPath)
        {
            var fullPath = ResolvePath(storedPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        public bool Exists(string storedPath) => File.Exists(ResolvePath(storedPath));

        protected async Task WriteAsync(string storedName, IFormFile file, CancellationToken ct)
        {
            await using var stream = new FileStream(ResolvePath(storedName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(stream, ct);
        }

        protected async Task WriteBytesAsync(string storedName, byte[] data, CancellationToken ct)
        {
            await using var stream = new FileStream(ResolvePath(storedName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(data, ct);
        }

        protected string ResolvePath(string storedPath)
        {
            var name = Path.GetFileName(storedPath);
            var fullPath = Path.GetFullPath(Path.Combine(Root, name));
            if (!fullPath.StartsWith(Root, StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid stored path.");
            return fullPath;
        }
    }
}
