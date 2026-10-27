namespace PCLUnlocker;

internal static class SelfTest
{
    public static int Run(string targetPath, bool force)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PCLUnlocker-self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // 如果目标已经被修补过，优先使用同目录的原始备份作为自测输入。
            var source = targetPath;
            var backup = targetPath + ".pclunlocker.bak";
            if (File.Exists(backup) && TargetInfo.Load(backup).MatchesKnownBuild)
                source = backup;
            else if (PatchEngine.LoadAndValidate(targetPath) is { } module)
            {
                using (module)
                {
                    if (Verifier.IsFullyPatched(module))
                        throw new PatchException("目标已经是修补状态，且找不到可用的原始备份；请用 --target 指定原始 exe。");
                }
            }

            var input = Path.Combine(tempDir, "Plain Craft Launcher 2.exe");
            var output = Path.Combine(tempDir, "Plain Craft Launcher 2.patched.exe");
            File.Copy(source, input, overwrite: true);

            Console.WriteLine($"自测输入：{input}（来自 {source}）");
            var result = PatchEngine.ApplyToFile(input, output, force);
            if (result.AlreadyPatched)
                throw new PatchException("自测失败：输入已经是修补状态。");
            Console.WriteLine($"已应用 {result.Patches.Count} 个补丁，输出：{output}");

            Verifier.VerifyPatched(input, output);
            var outputInfo = TargetInfo.Load(output);
            if (string.Equals(result.InputSha256, outputInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new PatchException("自测失败：输出文件哈希与输入相同，补丁可能未写入。");

            // 再次修补应当被识别为“已修补”，而不是重复应用。
            var second = PatchEngine.ApplyToFile(output, Path.Combine(tempDir, "second.exe"), force);
            if (!second.AlreadyPatched)
                throw new PatchException("自测失败：已修补文件未被识别为已修补。");

            Console.WriteLine($"自测通过：{PatchEngine.AllPatches.Count} 个补丁全部生效，资源与程序集信息一致。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"自测失败：{ex.Message}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
