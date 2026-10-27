using dnlib.DotNet;
using dnlib.DotNet.Writer;
using PCLUnlocker.Patches;

namespace PCLUnlocker;

internal sealed class UnsupportedTargetException : Exception
{
    public UnsupportedTargetException(string message) : base(message) { }
}

internal sealed class PatchRunResult
{
    public required IReadOnlyList<IPatch> Patches { get; init; }
    public required IReadOnlyList<PatchState> States { get; init; }
    public required bool AlreadyPatched { get; init; }
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required string InputSha256 { get; init; }
    public required string OutputSha256 { get; init; }
}

internal static class PatchEngine
{
    public static IReadOnlyList<IPatch> AllPatches { get; } = new IPatch[]
    {
        new P01DownloadLeftCtor(),
        new P02DownloadInstallLoader(),
        new P03LaunchDownloadButton(),
        new P04InstanceDownloadButton(),
        new P05EmptyDownloadButton(),
        new P06ImportModpackItem(),
        new P07ResourceInstallButton(),
        new P08ModifyMinecraftCard(),
        new P09UpdateDialogDownload(),
        new P10ModpackInstallEntry(),
        new P11ModpackInstallDispatch(),
        new P12ResourceDetailInstall()
    };

    public static ModuleDefMD LoadAndValidate(string path)
    {
        var info = TargetInfo.Load(path);
        ModuleDefMD module;
        try
        {
            // 从字节加载，避免 dnlib 对原文件建立内存映射而阻止备份/替换。
            var bytes = File.ReadAllBytes(info.Path);
            module = ModuleDefMD.Load(bytes, new ModuleCreationOptions { TryToLoadPdbFromDisk = false });
        }
        catch (Exception ex)
        {
            throw new UnsupportedTargetException($"无法把目标作为 .NET 程序集读取：{ex.Message}");
        }

        var assembly = module.Assembly;
        if (assembly is null)
        {
            module.Dispose();
            throw new UnsupportedTargetException("目标不是程序集。");
        }

        var name = assembly.Name.String;
        var version = assembly.Version;
        if (!string.Equals(name, TargetInfo.ExpectedAssemblyName, StringComparison.Ordinal) ||
            version != TargetInfo.ExpectedVersion)
        {
            module.Dispose();
            throw new UnsupportedTargetException(
                $"不支持的 PCL 版本：{name} {version}。本工具仅支持 {TargetInfo.ExpectedAssemblyName} {TargetInfo.ExpectedVersion}。");
        }

        return module;
    }

    public static PatchRunResult ApplyToFile(string inputPath, string outputPath, bool force)
    {
        var info = TargetInfo.Load(inputPath);
        using var module = LoadAndValidate(inputPath);
        var states = AllPatches.Select(p => p.Detect(module)).ToArray();
        var appliedCount = states.Count(s => s == PatchState.Applied);
        var alreadyPatched = appliedCount == AllPatches.Count;

        if (appliedCount > 0 && !alreadyPatched)
        {
            var pending = AllPatches.Where((_, i) => states[i] == PatchState.NotApplied).Select(p => p.Id);
            throw new PatchException(
                $"检测到部分补丁已应用，状态不明确。未应用项：{string.Join(", ", pending)}。请先恢复原始文件。");
        }

        if (!alreadyPatched && !info.MatchesKnownBuild && !force)
        {
            throw new UnsupportedTargetException(
                $"文件哈希与已知的 2.99.0.1 正式版不一致。\n" +
                $"实际 SHA256：{info.Sha256}\n" +
                $"预期 SHA256：{TargetInfo.ExpectedSha256}\n" +
                "如果确认版本正确，可使用 --force 继续（补丁模式仍可能不匹配）。");
        }

        if (!alreadyPatched)
        {
            for (var i = 0; i < AllPatches.Count; i++)
            {
                var patch = AllPatches[i];
                try
                {
                    patch.Apply(module);
                }
                catch (Exception ex) when (ex is not PatchException)
                {
                    throw new PatchException($"[{patch.Id}] {patch.Title} 应用失败：{ex.Message}", ex);
                }
                catch (PatchException ex)
                {
                    throw new PatchException($"[{patch.Id}] {patch.Title} 应用失败：{ex.Message}", ex);
                }
            }
        }

        var finalStates = AllPatches.Select(p => p.Detect(module)).ToArray();
        if (finalStates.Any(s => s != PatchState.Applied))
        {
            var failed = AllPatches.Where((_, i) => finalStates[i] != PatchState.Applied).Select(p => p.Id);
            throw new PatchException($"补丁应用后校验失败：{string.Join(", ", failed)}");
        }

        var fullOutput = Path.GetFullPath(outputPath);
        if (alreadyPatched && string.Equals(fullOutput, info.Path, StringComparison.OrdinalIgnoreCase))
        {
            return new PatchRunResult
            {
                Patches = AllPatches,
                States = finalStates,
                AlreadyPatched = true,
                InputPath = info.Path,
                OutputPath = fullOutput,
                InputSha256 = info.Sha256,
                OutputSha256 = info.Sha256
            };
        }

        var directory = Path.GetDirectoryName(fullOutput)
            ?? throw new PatchException("无法确定输出目录。");
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".pclunlocker-{Guid.NewGuid():N}.tmp");

        try
        {
            var writerOptions = new ModuleWriterOptions(module)
            {
                Logger = DummyLogger.NoThrowInstance,
                WritePdb = false
            };
            writerOptions.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
            module.Write(temp, writerOptions);

            // 重新载入并做完整校验，确保 dnlib 写出的文件仍然有效。
            Verifier.VerifyPatched(info.Path, temp);
            var outputSha = TargetInfo.ComputeSha256(temp);

            if (string.Equals(fullOutput, info.Path, StringComparison.OrdinalIgnoreCase))
            {
                var backup = info.Path + ".pclunlocker.bak";
                if (!File.Exists(backup))
                {
                    File.Copy(info.Path, backup, overwrite: false);
                }
                else
                {
                    var backupSha = TargetInfo.ComputeSha256(backup);
                    if (!string.Equals(backupSha, info.Sha256, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(backupSha, TargetInfo.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new PatchException(
                            $"备份文件已存在但内容不是当前原始文件：{backup}\n" +
                            "请先移走该备份，或使用 --output 输出到新文件。");
                    }
                }

                try
                {
                    File.Replace(temp, info.Path, null);
                }
                catch (Exception)
                {
                    File.Copy(temp, info.Path, overwrite: true);
                    File.Delete(temp);
                }
            }
            else
            {
                File.Move(temp, fullOutput, overwrite: true);
            }

            return new PatchRunResult
            {
                Patches = AllPatches,
                States = finalStates,
                AlreadyPatched = alreadyPatched,
                InputPath = info.Path,
                OutputPath = fullOutput,
                InputSha256 = info.Sha256,
                OutputSha256 = outputSha
            };
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* ignore */ }
            }
        }
    }
}
