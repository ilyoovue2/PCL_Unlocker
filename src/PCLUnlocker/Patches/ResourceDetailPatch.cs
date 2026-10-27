using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLUnlocker.Patches;

/// <summary>P12：恢复资源详情页中整合包版本的“安装”点击入口。</summary>
internal sealed class P12ResourceDetailInstall : IPatch
{
    private const string AdapterName = "PCLUnlocker_InstallClick";

    public string Id => "P12";
    public string Title => "恢复整合包版本安装入口";

    private static TypeDef GetDetailType(ModuleDefMD module) =>
        IlHelpers.GetType(module, "PCL.PageDownloadResourceDetail");

    private static MethodDef GetMethod(ModuleDefMD module) =>
        IlHelpers.GetMethod(GetDetailType(module), "InitializeStackResourceVersions", 1);

    public PatchState Detect(ModuleDefMD module)
    {
        var detailType = GetDetailType(module);
        var adapter = detailType.Methods.FirstOrDefault(m => m.Name == AdapterName);
        if (adapter is null)
            return PatchState.NotApplied;
        var method = GetMethod(module);
        var hasCall = method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldftn && ReferenceEquals(i.Operand, adapter));
        return hasCall ? PatchState.Applied : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var detailType = GetDetailType(module);
        var method = GetMethod(module);
        var body = method.Body;

        var adapter = detailType.Methods.FirstOrDefault(m => m.Name == AdapterName);
        if (adapter is null)
            adapter = CreateAdapter(module, detailType);

        var toListItem = IlHelpers.FindInstruction(method, i => IlHelpers.IsCall(i, "ToListItem"));
        var add = IlHelpers.FindInstruction(method, i =>
            body.Instructions.IndexOf(i) > body.Instructions.IndexOf(toListItem) &&
            IlHelpers.IsCall(i, "Add", "UIElementCollection"));
        var pop = IlHelpers.Next(method, add) ?? throw new PatchException("版本列表 Add 调用后缺少 pop。");
        var loopContinue = IlHelpers.Next(method, pop) ?? throw new PatchException("版本列表循环缺少下一指令。");

        Instruction? existingStart = null;
        var cursor = IlHelpers.Previous(method, toListItem);
        for (var i = 0; i < 20 && cursor is not null; i++, cursor = IlHelpers.Previous(method, cursor))
        {
            if (cursor.OpCode == OpCodes.Ldarg_1 &&
                IlHelpers.Next(method, cursor) is { } next &&
                IlHelpers.IsCall(next, "get_Children"))
            {
                existingStart = cursor;
                break;
            }
        }
        if (existingStart is null)
            throw new PatchException("找不到版本列表 ToListItem 调用的起始位置。");

        var getChildren = IlHelpers.Next(method, existingStart)!;
        var clickHandlerType = IlHelpers.GetType(module, "PCL.MyListItem/ClickEventHandler");
        var iconHandlerType = IlHelpers.GetType(module, "PCL.MyIconButton/ClickEventHandler");
        var ctorSig = MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object, module.CorLibTypes.IntPtr);
        var clickHandlerCtor = new MemberRefUser(module, ".ctor", ctorSig, clickHandlerType);
        var iconHandlerCtor = new MemberRefUser(module, ".ctor", ctorSig, iconHandlerType);

        var saveClick = IlHelpers.GetMethod(detailType, "Save_Click", 2);
        var installClick = IlHelpers.GetMethod(detailType, "Install_Click", 2);
        var targetTypeField = IlHelpers.GetField(detailType, "TargetResourceType");

        if (body.Variables.Count <= 7)
            throw new PatchException("版本列表方法的局部变量数量与预期不符。");
        var versionLocal = body.Variables[7];
        var hasDuplicatesLocal = body.Variables[1];

        var injected = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, targetTypeField),
            Instruction.Create(OpCodes.Ldc_I4_2),
            Instruction.Create(OpCodes.Bne_Un, existingStart),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Callvirt, (IMethod)getChildren.Operand!),
            Instruction.Create(OpCodes.Ldloc, versionLocal),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldftn, adapter),
            Instruction.Create(OpCodes.Newobj, clickHandlerCtor),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldftn, saveClick),
            Instruction.Create(OpCodes.Newobj, iconHandlerCtor),
            Instruction.Create(OpCodes.Ldloc, hasDuplicatesLocal),
            Instruction.Create(OpCodes.Callvirt, (IMethod)toListItem.Operand!),
            Instruction.Create(OpCodes.Callvirt, (IMethod)add.Operand!),
            Instruction.Create(OpCodes.Pop),
            Instruction.Create(OpCodes.Br, loopContinue)
        };

        var index = body.Instructions.IndexOf(existingStart);
        foreach (var instruction in injected)
            body.Instructions.Insert(index++, instruction);
    }

    private static MethodDef CreateAdapter(ModuleDefMD module, TypeDef detailType)
    {
        var clickHandlerType = IlHelpers.GetType(module, "PCL.MyListItem/ClickEventHandler");
        var invoke = clickHandlerType.Methods.FirstOrDefault(m => m.Name == "Invoke")
            ?? throw new PatchException("找不到 MyListItem.ClickEventHandler.Invoke。");
        var signature = MethodSig.CreateInstance(invoke.MethodSig.RetType, invoke.MethodSig.Params.ToArray());

        var adapter = new MethodDefUser(
            AdapterName,
            signature,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            MethodAttributes.Private | MethodAttributes.HideBySig);
        var adapterBody = new CilBody
        {
            MaxStack = 8,
            InitLocals = true
        };
        var installClick = IlHelpers.GetMethod(detailType, "Install_Click", 2);
        var myListItem = IlHelpers.GetType(module, "PCL.MyListItem");

        adapterBody.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        adapterBody.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        adapterBody.Instructions.Add(Instruction.Create(OpCodes.Castclass, myListItem));
        adapterBody.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        adapterBody.Instructions.Add(Instruction.Create(OpCodes.Call, installClick));
        adapterBody.Instructions.Add(Instruction.Create(OpCodes.Ret));
        adapter.Body = adapterBody;
        detailType.Methods.Add(adapter);
        return adapter;
    }
}
