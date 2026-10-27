using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;

internal static class JitVerify
{
    private static readonly Dictionary<string, byte[]> Embedded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("用法：JitVerify.exe <patched PCL exe>");
                return 2;
            }

            Console.WriteLine("加载：" + Path.GetFullPath(args[0]));
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            var bytes = File.ReadAllBytes(Path.GetFullPath(args[0]));
            var assembly = Assembly.Load(bytes);
            Console.WriteLine("主程序集已加载");
            LoadEmbeddedAssemblies(assembly);
            Console.WriteLine("内嵌程序集已加载");

            var targets = new[]
            {
                new object[] { "PCL.PageDownloadLeft", ".ctor", 0 },
                new object[] { "PCL.PageDownloadInstall", "LoaderInit", 0 },
                new object[] { "PCL.PageLaunchLeft", "RefreshButtonsUI", 0 },
                new object[] { "PCL.PageInstanceModDisabled", "BtnDownload_Loaded", 0 },
                new object[] { "PCL.PageSelectRight", "McInstanceListUI", 1 },
                new object[] { "PCL.PageSelectLeft", "McFolderListUI", 0 },
                new object[] { "PCL.PageResource", "set_PageType", 1 },
                new object[] { "PCL.PageDownloadModify", "LoadInstanceFinished", 1 },
                new object[] { "PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", 1 },
                new object[] { "PCL.PageDownloadResourceDetail", "PCLUnlocker_InstallClick", 2 },
                new object[] { "PCL.ModMinecraft", "McDownloadClientUpdateHint", 2 },
                new object[] { "PCL.ModModpack", "ModpackInstall", 0 },
                new object[] { "PCL.ModModpack", "ModpackInstall", 3 }
            };
            var failed = 0;
            foreach (var target in targets)
            {
                if (!TryPrepare(assembly, (string)target[0], (string)target[1], (int)target[2]))
                    failed++;
            }

            Console.WriteLine(failed == 0 ? "JIT 验证通过。" : "JIT 验证失败项：" + failed);
            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("JitVerify 异常：" + ex.GetType().FullName);
            Console.Error.WriteLine(ex.Message);
            if (ex.InnerException != null)
                Console.Error.WriteLine("Inner: " + ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message);
            return 3;
        }
    }

    private static bool TryPrepare(Assembly assembly, string typeName, string methodName, int parameterCount)
    {
        try
        {
            var type = assembly.GetType(typeName, true);
            MethodBase method = null;
            if (methodName == ".ctor" && parameterCount == 0)
            {
                method = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            }
            else
            {
                foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (candidate.Name == methodName && candidate.GetParameters().Length == parameterCount)
                    {
                        method = candidate;
                        break;
                    }
                }
            }
            if (method == null)
            {
                Console.WriteLine("FAIL  找不到方法：" + typeName + "::" + methodName + "/" + parameterCount);
                return false;
            }

            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            Console.WriteLine("OK    " + typeName + "::" + methodName + "/" + parameterCount);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL  " + typeName + "::" + methodName + "/" + parameterCount + " -> " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        var simpleName = new AssemblyName(args.Name).Name;
        byte[] bytes;
        if (simpleName != null && Embedded.TryGetValue(simpleName, out bytes))
            return Assembly.Load(bytes);
        return null;
    }

    private static void LoadEmbeddedAssemblies(Assembly assembly)
    {
        var stream = assembly.GetManifestResourceStream("PCL.Resources.resources");
        if (stream == null)
            throw new InvalidOperationException("找不到 PCL.Resources.resources");
        using (var reader = new ResourceReader(stream))
        {
            var enumerator = reader.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var key = enumerator.Key as string;
                var value = enumerator.Value as byte[];
                if (key != null && value != null)
                    Embedded[key] = value;
            }
        }
        Console.WriteLine("内嵌程序集：" + string.Join(", ", Embedded.Keys));
    }
}
