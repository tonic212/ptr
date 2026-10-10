using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PokerCompanion.App.Services;

public static class BundledDataStore
{
    private static readonly Lazy<string> Location = new(Extract);
    public static string Root => Location.Value;
    private static string Extract()
    {
        const string prefix = "PokerCompanionAssets/";
        Assembly assembly = typeof(BundledDataStore).Assembly;
        string[] names = assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        if (names.Length == 0) return AppContext.BaseDirectory;
        var files = new List<(string Path, byte[] Bytes)>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string name in names)
        {
            using var input = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Bundled chart asset missing.");
            using var output = new MemoryStream(); input.CopyTo(output);
            byte[] data = output.ToArray(); hash.AppendData(Encoding.UTF8.GetBytes(name)); hash.AppendData(data);
            files.Add((name[prefix.Length..].Replace('\\', '/'), data));
        }
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokerCompanion", "assets", Convert.ToHexString(hash.GetHashAndReset())[..16]);
        foreach (var file in files)
        {
            string path = Path.GetFullPath(Path.Combine(root, file.Path));
            if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid bundled asset path.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(file.Bytes))) continue;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, file.Bytes); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return root;
    }
}
