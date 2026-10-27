using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLUnlocker.Patches;

/// <summary>
/// 从模块已有调用点捕获外部方法/构造函数引用，避免手写跨程序集签名。
/// </summary>
internal sealed class ModuleRefs
{
    public required IMethod SelectFile { get; init; }
    public required IMethod OneOfImplicit { get; init; }
    public required IMethod NullableCtor { get; init; }
    public required IMethod FirstOrDefault { get; init; }
    public required IMethod IsNullOrEmpty { get; init; }
    public required IMethod ActionCtor { get; init; }
    public required IMethod RunInThread { get; init; }
    public required IMethod AddToUIElementCollection { get; init; }
    public required IMethod GetChildren { get; init; }

    public static ModuleRefs Create(ModuleDefMD module)
    {
        var all = IlHelpers.AllInstructions(module).ToList();

        var selectEntry = all.FirstOrDefault(i =>
            i.Instruction.OpCode == OpCodes.Call &&
            i.Instruction.Operand is IMethod m && m.Name == "SelectFile");
        if (selectEntry.Instruction is null)
            throw new PatchException("找不到 Dialogs.SelectFile 调用点。");
        var selectFileInstruction = selectEntry.Instruction;
        var selectFile = (IMethod)selectFileInstruction.Operand;

        var nullableCtorInstruction = IlHelpers.Previous(selectEntry.Method, selectFileInstruction)
            ?? throw new PatchException("SelectFile 调用前缺少 Nullable 构造。");
        if (nullableCtorInstruction.OpCode != OpCodes.Newobj || nullableCtorInstruction.Operand is not IMethod nullableCtor)
            throw new PatchException("SelectFile 调用前缺少 Nullable<OneOf> 构造。");

        var oneOfImplicitInstruction = IlHelpers.Previous(selectEntry.Method, nullableCtorInstruction)
            ?? throw new PatchException("Nullable 构造前缺少 OneOf 隐式转换。");
        if (oneOfImplicitInstruction.OpCode != OpCodes.Call || oneOfImplicitInstruction.Operand is not IMethod oneOfImplicit)
            throw new PatchException("Nullable 构造前缺少 OneOf 隐式转换。");

        var firstOrDefault = FindStringFirstOrDefault(module);
        var isNullOrEmpty = IlHelpers.GetCalledMethod(module, "IsNullOrEmpty", "System.String");
        var actionCtor = IlHelpers.GetNewObjMethod(module, ".ctor", "System.Action", 2);
        var runInThread = IlHelpers.GetCalledMethod(module, "RunInThread", "PCL.ModBase");
        var add = IlHelpers.GetCalledMethod(module, "Add", "UIElementCollection");
        var getChildren = IlHelpers.GetCalledMethod(module, "get_Children", "Panel");

        return new ModuleRefs
        {
            SelectFile = selectFile,
            OneOfImplicit = oneOfImplicit,
            NullableCtor = nullableCtor,
            FirstOrDefault = firstOrDefault,
            IsNullOrEmpty = isNullOrEmpty,
            ActionCtor = actionCtor,
            RunInThread = runInThread,
            AddToUIElementCollection = add,
            GetChildren = getChildren
        };
    }

    private static IMethod FindStringFirstOrDefault(ModuleDefMD module)
    {
        foreach (var (_, instruction) in IlHelpers.AllInstructions(module))
        {
            if (instruction.OpCode != OpCodes.Call || instruction.Operand is not IMethod method)
                continue;
            if (method.Name != "FirstOrDefault" || method.MethodSig?.Params.Count != 1)
                continue;
            if (method.DeclaringType?.FullName != "System.Linq.Enumerable")
                continue;
            if (method is MethodSpec spec &&
                spec.GenericInstMethodSig?.GenericArguments.Count == 1 &&
                spec.GenericInstMethodSig.GenericArguments[0].FullName == "System.String")
                return method;
        }
        throw new PatchException("找不到 Enumerable.FirstOrDefault<string>(IEnumerable<string>) 调用点。");
    }
}
