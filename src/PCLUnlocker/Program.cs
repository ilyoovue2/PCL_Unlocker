using System.Diagnostics;
using dnlib.DotNet;
using PCLUnlocker.Patches;

namespace PCLUnlocker;

internal static class Program
{
    private const string BackupSuffix = ".pclunlocker.bak";

    public static int Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.Help)
            {
                CliOptions.PrintHelp();
                return 0;
            }

            var target = TargetLocator.Find(options.Target);
            Console.WriteLine($"目标：{target}");
            if (options.SelfTest)
                return SelfTest.Run(target, options.Force);

            if (options.Restore)
                return Restore(target);

            using var module = PatchEngine.LoadAndValidate(target);

            if (options.VerifyOnly)
            {
                var states = Verifier.ReportStates(module);
                foreach (var (id, title, state) in states)
                    Console.WriteLine($"  [{id}] {title}：{(state == PatchState.Applied ? "已应用" : "未应用")}");
                var all = states.All(s => s.State == PatchState.Applied);
                Console.WriteLine(all ? "状态：已完整修补。" : "状态：未完整修补。");
                return all ? 0 : 1;
            }

            if (IsProcessRunning(target))
                throw new PatchException("检测到 PCL 正在运行，请先关闭程序再修补。");

            if (!options.Yes && !Confirm(target, options))
                return 3;

            var output = options.Output is null ? target : Path.GetFullPath(options.Output);
            var result = PatchEngine.ApplyToFile(target, output, options.Force);
            PrintResult(result, options.ReportPath);
            return 0;
        }
        catch (UnsupportedTargetException ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 2;
        }
        catch (PatchException ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"未预期的错误：{ex}");
            return 1;
        }
    }

    private static int Restore(string target)
    {
        var backup = target + BackupSuffix;
        if (!File.Exists(backup))
            throw new PatchException($"找不到备份文件：{backup}");
        var backupSha = TargetInfo.ComputeSha256(backup);
        if (!string.Equals(backupSha, TargetInfo.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new PatchException($"备份文件不是已知的原始 2.99.0.1 文件，拒绝恢复。\nSHA256：{backupSha}");
        if (IsProcessRunning(target))
            throw new PatchException("检测到 PCL 正在运行，请先关闭程序再恢复。");
        File.Copy(backup, target, overwrite: true);
        Console.WriteLine($"已恢复：{target}");
        Console.WriteLine($"SHA256：{TargetInfo.ComputeSha256(target)}");
        return 0;
    }

    private static bool Confirm(string target, CliOptions options)
    {
        var mode = options.Output is null ? "原地修补" : $"输出到 {Path.GetFullPath(options.Output)}";
        Console.WriteLine($"即将{mode}。");
        if (options.Output is null)
            Console.WriteLine($"备份将写入：{target}{BackupSuffix}");
        Console.Write("继续？[Y/n] ");
        var answer = Console.ReadLine();
        return string.IsNullOrWhiteSpace(answer) ||
               answer.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintResult(PatchRunResult result, string? reportPath)
    {
        if (result.AlreadyPatched)
            Console.WriteLine(string.Equals(result.InputPath, result.OutputPath, StringComparison.OrdinalIgnoreCase)
                ? "目标已经是完整修补状态，未写文件。"
                : "目标已经是完整修补状态，已写出副本。");
        else
            Console.WriteLine("补丁应用完成：");
        foreach (var patch in result.Patches)
            Console.WriteLine($"  [{patch.Id}] {patch.Title}");
        Console.WriteLine($"输出：{result.OutputPath}");
        Console.WriteLine($"原 SHA256：{result.InputSha256}");
        Console.WriteLine($"新 SHA256：{result.OutputSha256}");

        var lines = new List<string>
        {
            $"PCLUnlocker 报告 {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"输入：{result.InputPath}",
            $"输入 SHA256：{result.InputSha256}",
            $"输出：{result.OutputPath}",
            $"输出 SHA256：{result.OutputSha256}",
            $"已修补：{result.AlreadyPatched}",
            string.Empty,
            "补丁列表："
        };
        lines.AddRange(result.Patches.Select(p => $"  [{p.Id}] {p.Title}"));

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var full = Path.GetFullPath(reportPath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllLines(full, lines);
            Console.WriteLine($"报告：{full}");
        }
    }

    private static bool IsProcessRunning(string target)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(target);
            return Process.GetProcessesByName(name).Any(p =>
            {
                try { return !p.HasExited; } catch { return false; }
            });
        }
        catch
        {
            return false;
        }
    }
}

internal static class TargetLocator
{
    public static string Find(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);
            if (!File.Exists(full))
                throw new PatchException($"目标文件不存在：{full}");
            return full;
        }

        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        for (var depth = 0; depth < 5 && directory is not null; depth++, directory = directory.Parent)
        {
            var direct = Path.Combine(directory.FullName, "Plain Craft Launcher 2.exe");
            if (File.Exists(direct))
                return direct;
            var nested = Path.Combine(directory.FullName, "PCL", "Plain Craft Launcher 2.exe");
            if (File.Exists(nested))
                return nested;
        }

        throw new PatchException("未找到 Plain Craft Launcher 2.exe，请使用 --target 指定路径。");
    }
}
