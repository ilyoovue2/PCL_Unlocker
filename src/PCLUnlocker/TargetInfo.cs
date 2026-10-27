using System.Security.Cryptography;

namespace PCLUnlocker;

internal sealed class TargetInfo
{
    public const string ExpectedAssemblyName = "Plain Craft Launcher 2";
    public static readonly Version ExpectedVersion = new(2, 99, 0, 1);
    public const long ExpectedSize = 6_013_952L;
    public const string ExpectedSha256 = "0DB34E19D9EEB6D496D20FABE786EC16BBD36BA6B0CCDC8D29CB7E909C114397";

    public string Path { get; }
    public long Size { get; }
    public string Sha256 { get; }

    private TargetInfo(string path, long size, string sha256)
    {
        Path = path;
        Size = size;
        Sha256 = sha256;
    }

    public static TargetInfo Load(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new PatchException($"找不到目标文件：{full}");

        var size = new FileInfo(full).Length;
        var hash = ComputeSha256(full);
        return new TargetInfo(full, size, hash);
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public bool MatchesKnownBuild => Size == ExpectedSize &&
                                     string.Equals(Sha256, ExpectedSha256, StringComparison.OrdinalIgnoreCase);

    public string Describe() =>
        $"{System.IO.Path.GetFileName(Path)}，{Size:N0} 字节，SHA256 {Sha256}";
}

internal sealed class PatchException : Exception
{
    public PatchException(string message) : base(message) { }
    public PatchException(string message, Exception inner) : base(message, inner) { }
}
