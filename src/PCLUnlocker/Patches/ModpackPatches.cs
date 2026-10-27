using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLUnlocker.Patches;

/// <summary>P10：恢复无参 ModpackInstall() 的文件选择与后台安装入口。</summary>
internal sealed class P10ModpackInstallEntry : IPatch
{
    public string Id => "P10";
    public string Title => "恢复“导入整合包”入口";

    private static MethodDef GetMethod(ModuleDefMD module) =>
        IlHelpers.GetMethod(module, "PCL.ModModpack", "ModpackInstall", 0);

    public PatchState Detect(ModuleDefMD module)
    {
        var method = GetMethod(module);
        var hasSelect = IlHelpers.TryFindInstruction(method, i => IlHelpers.IsCall(i, "SelectFile")) is not null;
        var hasThread = IlHelpers.TryFindInstruction(method, i => IlHelpers.IsCall(i, "RunInThread")) is not null;
        return hasSelect && hasThread ? PatchState.Applied : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var method = GetMethod(module);
        var body = method.Body;
        var refs = ModuleRefs.Create(module);

        var closureType = IlHelpers.GetType(module, "PCL.ModModpack/_Closure$__0-0");
        var closureCtor = IlHelpers.GetMethod(closureType, ".ctor", 0);
        var lambda = IlHelpers.GetMethod(closureType, "_Lambda$__0", 0);
        var fileField = IlHelpers.GetField(closureType, "$VB$Local_File");

        body.ExceptionHandlers.Clear();
        body.Instructions.Clear();
        body.Variables.Clear();
        body.InitLocals = true;
        body.MaxStack = 11;

        var closureLocal = new Local(closureType.ToTypeSig());
        var fileLocal = new Local(module.CorLibTypes.String);
        body.Variables.Add(closureLocal);
        body.Variables.Add(fileLocal);

        var ret = Instruction.Create(OpCodes.Ret);
        var instructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Newobj, closureCtor),
            Instruction.Create(OpCodes.Stloc, closureLocal),
            Instruction.Create(OpCodes.Ldstr, "选择整合包压缩文件"),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Ldstr, "整合包文件(*.rar;*.zip;*.mrpack)|*.rar;*.zip;*.mrpack"),
            Instruction.Create(OpCodes.Call, refs.OneOfImplicit),
            Instruction.Create(OpCodes.Newobj, refs.NullableCtor),
            Instruction.Create(OpCodes.Call, refs.SelectFile),
            Instruction.Create(OpCodes.Call, refs.FirstOrDefault),
            Instruction.Create(OpCodes.Stloc, fileLocal),
            Instruction.Create(OpCodes.Ldloc, fileLocal),
            Instruction.Create(OpCodes.Call, refs.IsNullOrEmpty),
            Instruction.Create(OpCodes.Brtrue, ret),
            Instruction.Create(OpCodes.Ldloc, closureLocal),
            Instruction.Create(OpCodes.Ldloc, fileLocal),
            Instruction.Create(OpCodes.Stfld, fileField),
            Instruction.Create(OpCodes.Ldloc, closureLocal),
            Instruction.Create(OpCodes.Ldftn, lambda),
            Instruction.Create(OpCodes.Newobj, refs.ActionCtor),
            Instruction.Create(OpCodes.Call, refs.RunInThread),
            ret
        };

        foreach (var instruction in instructions)
            body.Instructions.Add(instruction);
    }
}

/// <summary>P11：恢复 ModpackInstall(File, InstanceName, Logo) 的整合包类型分发。</summary>
internal sealed class P11ModpackInstallDispatch : IPatch
{
    public string Id => "P11";
    public string Title => "恢复整合包安装分发";

    private static MethodDef GetMethod(ModuleDefMD module) =>
        IlHelpers.GetMethod(module, "PCL.ModModpack", "ModpackInstall", 3);

    private static Instruction FindGuard(MethodDef method) =>
        IlHelpers.FindInstruction(method, i =>
            i.OpCode == OpCodes.Ldstr &&
            i.Operand is string s &&
            s.Contains("不再提供 MC 下载功能", StringComparison.Ordinal));

    public PatchState Detect(ModuleDefMD module)
    {
        var method = GetMethod(module);
        var hasInstall = IlHelpers.TryFindInstruction(method, i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is IMethod m &&
            m.Name == "InstallPackCurseForge") is not null;
        var hasGuard = IlHelpers.TryFindInstruction(method, i =>
            i.OpCode == OpCodes.Ldstr &&
            i.Operand is string s &&
            s.Contains("不再提供 MC 下载功能", StringComparison.Ordinal)) is not null;
        return hasInstall && !hasGuard ? PatchState.Applied : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var method = GetMethod(module);
        var body = method.Body;
        var guard = FindGuard(method);
        var resultLocal = body.Variables[0];
        var archiveLocal = body.Variables[1];
        var baseFolderLocal = body.Variables[2];
        var packTypeLocal = body.Variables[4];

        if (body.Variables.Count < 5 || packTypeLocal.Type.ElementType != ElementType.I4)
            throw new PatchException("ModpackInstall 的局部变量布局与预期不符。");

        var exceptionCtor = IlHelpers.Next(method, guard) ?? throw new PatchException("guard 后缺少指令。");
        var throwInstruction = IlHelpers.Next(method, exceptionCtor) ?? throw new PatchException("guard 后缺少 throw。");
        if (exceptionCtor.OpCode != OpCodes.Newobj || throwInstruction.OpCode != OpCodes.Throw)
            throw new PatchException("ModpackInstall 的 guard 结构与预期不符。");
        body.Instructions.Remove(exceptionCtor);
        body.Instructions.Remove(throwInstruction);

        // 复用 guard 指令作为分发入口，保持 catch handler 的 HandlerEnd 引用不变。
        guard.OpCode = OpCodes.Ldloc;
        guard.Operand = packTypeLocal;

        var end = Instruction.Create(OpCodes.Ldloc, resultLocal);
        body.Instructions.Add(end);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        // 原 finally 处理器一直延伸到方法末尾；把它的结束边界移到新增的返回指令之前，
        // 否则 leave 目标会落进 finally 处理器内部。
        foreach (var handler in body.ExceptionHandlers)
        {
            if (handler.HandlerType == ExceptionHandlerType.Finally && handler.HandlerEnd is null)
                handler.HandlerEnd = end;
        }

        var curseForge = IlHelpers.GetMethod(IlHelpers.GetType(module, "PCL.ModModpack"), "InstallPackCurseForge", 5);
        var hmcl = IlHelpers.GetMethod(IlHelpers.GetType(module, "PCL.ModModpack"), "InstallPackHMCL", 3);
        var mmc = IlHelpers.GetMethod(IlHelpers.GetType(module, "PCL.ModModpack"), "InstallPackMMC", 3);
        var mcbbs = IlHelpers.GetMethod(IlHelpers.GetType(module, "PCL.ModModpack"), "InstallPackMCBBS", 4);
        var modrinth = IlHelpers.GetMethod(IlHelpers.GetType(module, "PCL.ModModpack"), "InstallPackModrinth", 5);
        var launcherPack = IlHelpers.GetMethod(IlHelpers.GetType(module, "PCL.ModModpack"), "InstallPackLauncherPack", 3);
        var compress = IlHelpers.GetMethod(IlHelpers.GetType(module, "PCL.ModModpack"), "InstallPackCompress", 2);

        var defaultCase = BuildCase(compress, 2, archiveLocal, baseFolderLocal, resultLocal, end);
        var caseCurseForge = BuildCase(curseForge, 5, archiveLocal, baseFolderLocal, resultLocal, end);
        var caseHmcl = BuildCase(hmcl, 3, archiveLocal, baseFolderLocal, resultLocal, end);
        var caseMmc = BuildCase(mmc, 3, archiveLocal, baseFolderLocal, resultLocal, end);
        var caseMcbbs = BuildCase(mcbbs, 4, archiveLocal, baseFolderLocal, resultLocal, end);
        var caseModrinth = BuildCase(modrinth, 5, archiveLocal, baseFolderLocal, resultLocal, end);
        var caseLauncher = BuildCase(launcherPack, 3, archiveLocal, baseFolderLocal, resultLocal, end);

        var targets = new Instruction[10];
        targets[0] = caseCurseForge[0];
        targets[1] = caseHmcl[0];
        targets[2] = caseMmc[0];
        targets[3] = caseMcbbs[0];
        targets[4] = caseModrinth[0];
        for (var i = 5; i < 9; i++)
            targets[i] = defaultCase[0];
        targets[9] = caseLauncher[0];

        var switchInstruction = Instruction.Create(OpCodes.Switch, targets);
        var insertAt = body.Instructions.IndexOf(guard) + 1;
        var emitted = new List<Instruction>
        {
            switchInstruction,
            Instruction.Create(OpCodes.Br, defaultCase[0])
        };
        emitted.AddRange(caseCurseForge);
        emitted.AddRange(caseHmcl);
        emitted.AddRange(caseMmc);
        emitted.AddRange(caseMcbbs);
        emitted.AddRange(caseModrinth);
        emitted.AddRange(caseLauncher);
        emitted.AddRange(defaultCase);

        foreach (var instruction in emitted)
            body.Instructions.Insert(insertAt++, instruction);

        body.MaxStack = (ushort)Math.Max((int)body.MaxStack, 8);
    }

    private static List<Instruction> BuildCase(
        MethodDef installMethod,
        int parameterCount,
        Local archiveLocal,
        Local baseFolderLocal,
        Local resultLocal,
        Instruction end)
    {
        var instructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldloc, archiveLocal),
            Instruction.Create(OpCodes.Ldloc, baseFolderLocal)
        };
        if (parameterCount >= 4)
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        if (parameterCount >= 5)
            instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        instructions.Add(Instruction.Create(OpCodes.Call, installMethod));
        instructions.Add(Instruction.Create(OpCodes.Stloc, resultLocal));
        instructions.Add(Instruction.Create(OpCodes.Leave, end));
        return instructions;
    }
}
