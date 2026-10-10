using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PokerCompanion.App.Services.Recognition;

public sealed class OllamaRuntimeInstaller
{
    public const string Version = "v0.40.2";
    private const string Digest = "e29ad1d5063dd4b54b2492d9b00adab2cff9621bfa654b6c92aa8d6f1fdfe7fc";
    private const long DownloadSize = 1468085195;
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokerCompanion", "runtime", Version);
    public async Task<string> EnsureAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        string executable = Path.Combine(_root, "ollama.exe");
        if (File.Exists(executable) && File.Exists(Path.Combine(_root, ".complete"))) return executable;
        Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
        string archive = _root + "." + Guid.NewGuid().ToString("N") + ".zip";
        string staging = archive + ".extract";
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_root)!);
            if (drive.AvailableFreeSpace < 12L * 1024 * 1024 * 1024)
                throw new IOException("First-time vision setup needs at least 12 GB of free disk space.");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await http.GetAsync("https://github.com/ollama/ollama/releases/download/" + Version + "/ollama-windows-amd64.zip", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            long received = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = File.Create(archive))
            {
                var buffer = new byte[1024 * 1024];
                int count;
                DateTime lastProgress = DateTime.MinValue;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    received += count;
                    if (received > DownloadSize) throw new InvalidDataException("Runtime download exceeded expected size.");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    if ((DateTime.UtcNow - lastProgress).TotalSeconds >= 1)
                    { progress($"Downloading local vision runtime: {received * 100.0 / DownloadSize:F0}% (first setup only)."); lastProgress = DateTime.UtcNow; }
                }
            }
            if (received != DownloadSize || !Convert.ToHexString(hash.GetHashAndReset()).Equals(Digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Runtime download failed its SHA-256 integrity check. Retry setup.");
            progress("Extracting local vision runtime.");
            await Task.Run(() =>
            {
                Directory.CreateDirectory(staging);
                using var zip = ZipFile.OpenRead(archive);
                long extracted = 0;
                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    extracted += entry.Length;
                    if (extracted > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("Runtime archive is too large.");
                    string destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!destination.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid runtime archive path.");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!); entry.ExtractToFile(destination);
                }
                if (!File.Exists(Path.Combine(staging, "ollama.exe"))) throw new InvalidDataException("Runtime executable is missing.");
                File.WriteAllText(Path.Combine(staging, ".complete"), Digest);
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
                Directory.Move(staging, _root);
            }, cancellationToken);
            return executable;
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}
