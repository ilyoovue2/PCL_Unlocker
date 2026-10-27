using System.Security.Cryptography;
using dnlib.DotNet;
using PCLUnlocker.Patches;

namespace PCLUnlocker;

internal static class Verifier
{
    public static void VerifyPatched(string originalPath, string patchedPath)
    {
        using var original = ModuleDefMD.Load(originalPath, new ModuleCreationOptions { TryToLoadPdbFromDisk = false });
        using var patched = ModuleDefMD.Load(patchedPath, new ModuleCreationOptions { TryToLoadPdbFromDisk = false });

        if (patched.Assembly is null)
            throw new PatchException("补丁结果不是程序集。");
        if (!string.Equals(patched.Assembly.Name.String, TargetInfo.ExpectedAssemblyName, StringComparison.Ordinal) ||
            patched.Assembly.Version != TargetInfo.ExpectedVersion)
            throw new PatchException("补丁改变了程序集名称或版本。");

        foreach (var patch in PatchEngine.AllPatches)
        {
            if (patch.Detect(patched) != PatchState.Applied)
                throw new PatchException($"补丁未生效或无法识别：{patch.Id} {patch.Title}");
        }

        var originalResources = ResourceHashes(original);
        var patchedResources = ResourceHashes(patched);
        if (originalResources.Count != patchedResources.Count)
            throw new PatchException("补丁改变了资源数量。");
        foreach (var (name, hash) in originalResources)
        {
            if (!patchedResources.TryGetValue(name, out var newHash) ||
                !string.Equals(hash, newHash, StringComparison.Ordinal))
                throw new PatchException($"补丁改变了资源内容：{name}");
        }

        if (patched.EntryPoint is null || original.EntryPoint is null)
            throw new PatchException("补丁改变了入口点。");
        if (!string.Equals(patched.EntryPoint.FullName, original.EntryPoint.FullName, StringComparison.Ordinal))
            throw new PatchException("补丁改变了入口点方法。");
    }

    public static IReadOnlyList<(string Id, string Title, PatchState State)> ReportStates(ModuleDefMD module) =>
        PatchEngine.AllPatches.Select(p => (p.Id, p.Title, p.Detect(module))).ToArray();

    public static bool IsFullyPatched(ModuleDefMD module) =>
        PatchEngine.AllPatches.All(p => p.Detect(module) == PatchState.Applied);

    private static Dictionary<string, string> ResourceHashes(ModuleDefMD module)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in module.Resources)
        {
            var name = resource.Name.String;
            if (resource is EmbeddedResource embedded)
            {
                var reader = embedded.CreateReader();
                var bytes = reader.ReadBytes((int)reader.Length);
                result[name] = Convert.ToHexString(SHA256.HashData(bytes));
            }
            else
            {
                result[name] = $"non-embedded:{resource.ResourceType}";
            }
        }
        return result;
    }
}
