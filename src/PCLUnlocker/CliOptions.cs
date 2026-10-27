namespace PCLUnlocker;

internal sealed class CliOptions
{
    public string? Target { get; set; }
    public string? Output { get; set; }
    public bool InPlace { get; set; }
    public bool Restore { get; set; }
    public bool VerifyOnly { get; set; }
    public bool SelfTest { get; set; }
    public bool Force { get; set; }
    public bool Yes { get; set; }
    public bool Help { get; set; }
    public string? ReportPath { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "-t":
                case "--target":
                    o.Target = RequireValue(args, ref i, a);
                    break;
                case "-o":
                case "--output":
                    o.Output = RequireValue(args, ref i, a);
                    break;
                case "--in-place":
                    o.InPlace = true;
                    break;
                case "--restore":
                    o.Restore = true;
                    break;
                case "--verify":
                    o.VerifyOnly = true;
                    break;
                case "--self-test":
                    o.SelfTest = true;
                    break;
                case "-f":
                case "--force":
                    o.Force = true;
                    break;
                case "-y":
                case "--yes":
                    o.Yes = true;
                    break;
                case "-r":
                case "--report":
                    o.ReportPath = RequireValue(args, ref i, a);
                    break;
                case "-h":
                case "--help":
                case "-?":
                    o.Help = true;
                    break;
                default:
                    if (a.StartsWith('-'))
                        throw new PatchException($"未知参数：{a}");
                    // 允许直接传入 exe 路径
                    if (o.Target is not null)
                        throw new PatchException($"多余的位置参数：{a}");
                    o.Target = a;
                    break;
            }
        }

        if (o.InPlace && o.Output is not null)
            throw new PatchException("--in-place 与 --output 不能同时使用。");
        if (o.Restore && o.Output is not null)
            throw new PatchException("--restore 不支持 --output。");
        if (o.Restore && o.SelfTest)
            throw new PatchException("--restore 与 --self-test 不能同时使用。");

        return o;
    }

    private static string RequireValue(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
            throw new PatchException($"{option} 缺少参数。");
        return args[++i];
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            PCLUnlocker —— 恢复 PCL 2.99.0.1 被隐藏的 Minecraft 下载功能

            用法：
              PCLUnlocker.exe [目标 exe] [选项]

            选项：
              -t, --target <path>   指定 Plain Craft Launcher 2.exe
              -o, --output <path>   输出修补后的副本（不修改原文件）
                  --in-place        原地修补（默认），首次自动备份为
                                    Plain Craft Launcher 2.exe.pclunlocker.bak
                  --restore         从 .pclunlocker.bak 恢复原文件
                  --verify          只校验版本、补丁状态与资源，不写文件
                  --self-test       在临时目录复制目标、修补并校验，不触碰真实文件
              -f, --force           哈希不匹配时仍尝试（版本必须为 2.99.0.1）
              -y, --yes             不询问确认
              -r, --report <path>   写出补丁报告
              -h, --help            显示本帮助

            退出码：0 成功；1 一般错误；2 版本/哈希不支持；3 用户取消。
            """);
    }
}
