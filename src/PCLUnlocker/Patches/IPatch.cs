using dnlib.DotNet;

namespace PCLUnlocker.Patches;

internal enum PatchState
{
    NotApplied,
    Applied
}

internal interface IPatch
{
    string Id { get; }
    string Title { get; }
    PatchState Detect(ModuleDefMD module);
    void Apply(ModuleDefMD module);
}
