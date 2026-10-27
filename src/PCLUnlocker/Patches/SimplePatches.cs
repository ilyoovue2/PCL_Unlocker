using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLUnlocker.Patches;

/// <summary>P01：恢复下载页的“原版游戏”和“社区资源”。</summary>
internal sealed class P01DownloadLeftCtor : IPatch
{
    public string Id => "P01";
    public string Title => "恢复下载页“原版游戏”入口";

    public PatchState Detect(ModuleDefMD module)
    {
        var ctor = IlHelpers.GetMethod(module, "PCL.PageDownloadLeft", ".ctor", 0);
        var init = IlHelpers.FindCallInMethod(ctor, "InitializeComponent");
        return IlHelpers.Next(ctor, init)?.OpCode == OpCodes.Ret ? PatchState.Applied : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var ctor = IlHelpers.GetMethod(module, "PCL.PageDownloadLeft", ".ctor", 0);
        var init = IlHelpers.FindCallInMethod(ctor, "InitializeComponent");
        if (IlHelpers.Next(ctor, init)?.OpCode == OpCodes.Ret)
            return;
        ctor.Body.Instructions.Insert(ctor.Body.Instructions.IndexOf(init) + 1, Instruction.Create(OpCodes.Ret));
    }
}

/// <summary>P02：下载安装页使用真实的 DlClientListLoader，而不是抛异常的占位任务。</summary>
internal sealed class P02DownloadInstallLoader : IPatch
{
    public string Id => "P02";
    public string Title => "恢复原版游戏版本列表加载";

    public PatchState Detect(ModuleDefMD module)
    {
        var loaderInit = IlHelpers.GetMethod(module, "PCL.PageDownloadInstall", "LoaderInit", 0);
        var field = IlHelpers.GetField(module, "PCL.ModDownload", "DlClientListLoader");
        var usesField = IlHelpers.TryFindInstruction(loaderInit,
            i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldDef f && f.Name == "DlClientListLoader") is not null;
        var hasStub = IlHelpers.TryFindInstruction(loaderInit, i => IlHelpers.IsLdstr(i, "关闭下载")) is not null;
        return usesField && !hasStub ? PatchState.Applied : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var loaderInit = IlHelpers.GetMethod(module, "PCL.PageDownloadInstall", "LoaderInit", 0);
        var body = loaderInit.Body;
        var start = IlHelpers.FindInstruction(loaderInit, i => IlHelpers.IsLdstr(i, "关闭下载"));
        var end = IlHelpers.FindInstruction(loaderInit, i =>
            i.OpCode == OpCodes.Newobj &&
            i.Operand is IMethod m &&
            m.Name == ".ctor" &&
            m.DeclaringType?.Name.String.Contains("LoaderTask", StringComparison.Ordinal) == true);
        var index = body.Instructions.IndexOf(start);
        IlHelpers.RemoveRange(loaderInit, start, end);
        body.Instructions.Insert(index,
            Instruction.Create(OpCodes.Ldsfld, IlHelpers.GetField(module, "PCL.ModDownload", "DlClientListLoader")));
    }
}

/// <summary>P03：无版本时启动按钮显示“下载游戏”。</summary>
internal sealed class P03LaunchDownloadButton : IPatch
{
    public string Id => "P03";
    public string Title => "恢复启动页“下载游戏”按钮";

    private static Instruction[] GetTargets(ModuleDefMD module)
    {
        var method = IlHelpers.GetMethod(module, "PCL.PageLaunchLeft", "RefreshButtonsUI", 0);
        var switchInstruction = IlHelpers.FindInstruction(method, i => i.OpCode == OpCodes.Switch);
        if (switchInstruction.Operand is not Instruction[] targets || targets.Length < 3)
            throw new PatchException("PageLaunchLeft.RefreshButtonsUI 的 switch 目标数量异常。");
        return targets;
    }

    public PatchState Detect(ModuleDefMD module)
    {
        var targets = GetTargets(module);
        return ReferenceEquals(targets[1], targets[2]) ? PatchState.Applied : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var targets = GetTargets(module);
        targets[1] = targets[2];
    }
}

/// <summary>P04：实例 Mod 禁用页的下载按钮可见。</summary>
internal sealed class P04InstanceDownloadButton : IPatch
{
    public string Id => "P04";
    public string Title => "恢复实例页下载按钮";

    private static MethodDef GetMethod(ModuleDefMD module) =>
        IlHelpers.GetMethod(module, "PCL.PageInstanceModDisabled", "BtnDownload_Loaded", 0);

    public PatchState Detect(ModuleDefMD module)
    {
        var method = GetMethod(module);
        return IlHelpers.IsLdcI4(method.Body.Instructions[0], 0) ? PatchState.Applied : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var method = GetMethod(module);
        var first = method.Body.Instructions[0];
        if (!IlHelpers.IsLdcI4(first, 2))
            throw new PatchException("BtnDownload_Loaded 的首条指令不是 ldc.i4.2。");
        IlHelpers.ReplaceLdcI4(first, 0);
    }
}

/// <summary>P05：空版本页的下载按钮可见。</summary>
internal sealed class P05EmptyDownloadButton : IPatch
{
    public string Id => "P05";
    public string Title => "恢复空版本页下载按钮";

    private static Instruction GetVisibilityInstruction(ModuleDefMD module)
    {
        var method = IlHelpers.GetMethod(module, "PCL.PageSelectRight", "McInstanceListUI", 1);
        var getButtons = method.Body.Instructions
            .Where(i => IlHelpers.IsCall(i, "get_BtnEmptyDownload"))
            .ToList();
        if (getButtons.Count == 0)
            throw new PatchException("找不到 get_BtnEmptyDownload。");
        // 最后一个才是“无可用版本”的正常空状态分支；前一个是隐藏版本模式分支。
        var getButton = getButtons[^1];
        var setVisibility = IlHelpers.FindInstruction(method, i =>
            method.Body.Instructions.IndexOf(i) > method.Body.Instructions.IndexOf(getButton) &&
            IlHelpers.IsCall(i, "set_Visibility"));
        return IlHelpers.Previous(method, setVisibility) ?? throw new PatchException("BtnEmptyDownload 的 Visibility 参数缺失。");
    }

    public PatchState Detect(ModuleDefMD module)
    {
        var visibility = GetVisibilityInstruction(module);
        return IlHelpers.IsLdcI4(visibility, 0) ? PatchState.Applied : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var visibility = GetVisibilityInstruction(module);
        if (!IlHelpers.IsLdcI4(visibility, 2))
            throw new PatchException("BtnEmptyDownload 的 Visibility 不是 Collapsed(2)。");
        IlHelpers.ReplaceLdcI4(visibility, 0);
    }
}

/// <summary>P06：版本选择页恢复“导入整合包”条目。</summary>
internal sealed class P06ImportModpackItem : IPatch
{
    public string Id => "P06";
    public string Title => "恢复版本选择页“导入整合包”";

    private static MethodDef GetMethod(ModuleDefMD module) =>
        IlHelpers.GetMethod(module, "PCL.PageSelectLeft", "McFolderListUI", 0);

    private static Instruction? FindInsertTarget(MethodDef method)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (!IlHelpers.IsCall(instruction, "add_Click"))
                continue;
            var cursor = IlHelpers.Previous(method, instruction);
            for (var i = 0; i < 20 && cursor is not null; i++, cursor = IlHelpers.Previous(method, cursor))
            {
                if (IlHelpers.IsLdloc(cursor, 1))
                    return cursor;
            }
        }
        return null;
    }

    public PatchState Detect(ModuleDefMD module)
    {
        var method = GetMethod(module);
        foreach (var instruction in method.Body.Instructions)
        {
            if (IlHelpers.IsCall(instruction, "Add", "UIElementCollection") &&
                IlHelpers.Previous(method, instruction) is { } prev &&
                IlHelpers.IsLdloc(prev, 1))
                return PatchState.Applied;
        }
        return PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var method = GetMethod(module);
        var target = FindInsertTarget(method)
            ?? throw new PatchException("找不到 ItemInstall 的 AddHandler 位置。");
        var pageType = IlHelpers.GetType(module, "PCL.PageSelectLeft");
        var getPanList = IlHelpers.GetMethod(pageType, "get_PanList", 0);
        var getChildren = IlHelpers.FindCallInMethod(method, "get_Children", "Panel");
        var add = IlHelpers.FindCallInMethod(method, "Add", "UIElementCollection");

        var inserted = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldsfld, IlHelpers.GetField(module, "PCL.ModMain", "FrmSelectLeft")),
            Instruction.Create(OpCodes.Callvirt, getPanList),
            Instruction.Create(OpCodes.Callvirt, (IMethod)getChildren.Operand!),
            Instruction.Create(OpCodes.Ldloc_1),
            Instruction.Create(OpCodes.Callvirt, (IMethod)add.Operand!),
            Instruction.Create(OpCodes.Pop)
        };
        var index = method.Body.Instructions.IndexOf(target);
        foreach (var instruction in inserted)
            method.Body.Instructions.Insert(index++, instruction);
    }
}

/// <summary>P07：资源页“安装整合包”搜索按钮按资源类型显示。</summary>
internal sealed class P07ResourceInstallButton : IPatch
{
    public string Id => "P07";
    public string Title => "恢复资源页“安装整合包”按钮";

    private static MethodDef GetMethod(ModuleDefMD module) =>
        IlHelpers.GetMethod(module, "PCL.PageResource", "set_PageType", 1);

    public PatchState Detect(ModuleDefMD module)
    {
        var method = GetMethod(module);
        return IlHelpers.TryFindInstruction(method, i => i.OpCode == OpCodes.Bne_Un) is not null
            ? PatchState.Applied
            : PatchState.NotApplied;
    }

    public void Apply(ModuleDefMD module)
    {
        var method = GetMethod(module);
        var body = method.Body;
        var getButton = IlHelpers.FindInstruction(method, i => IlHelpers.IsCall(i, "get_BtnSearchInstallModPack"));
        var setVisibility = IlHelpers.FindInstruction(method, i =>
            body.Instructions.IndexOf(i) > body.Instructions.IndexOf(getButton) &&
            IlHelpers.IsCall(i, "set_Visibility"));
        if (IlHelpers.Next(method, getButton) is not { } firstDead ||
            IlHelpers.Previous(method, setVisibility) is not { } lastDead)
            throw new PatchException("资源页按钮可见性代码结构异常。");
        IlHelpers.RemoveRange(method, firstDead, lastDead);

        var collapsed = Instruction.Create(OpCodes.Ldc_I4_2);
        var inserted = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldc_I4_2),
            Instruction.Create(OpCodes.Bne_Un, collapsed),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Br, setVisibility),
            collapsed
        };
        var index = body.Instructions.IndexOf(setVisibility);
        foreach (var instruction in inserted)
            body.Instructions.Insert(index++, instruction);
    }
}

/// <summary>P08：版本修改页显示 Minecraft 卡片。</summary>
internal sealed class P08ModifyMinecraftCard : IPatch
{
    public string Id => "P08";
    public string Title => "恢复版本修改页 Minecraft 卡片";

    private static Instruction GetVisibilityInstruction(ModuleDefMD module)
    {
        var method = IlHelpers.GetMethod(module, "PCL.PageDownloadModify", "LoadInstanceFinished", 1);
        var getCard = IlHelpers.FindInstruction(method, i => IlHelpers.IsCall(i, "get_CardMinecraft"));
        return IlHelpers.Next(method, getCard) ?? throw new PatchException("get_CardMinecraft 后缺少指令。");
    }

    public PatchState Detect(ModuleDefMD module) =>
        IlHelpers.IsLdcI4(GetVisibilityInstruction(module), 0) ? PatchState.Applied : PatchState.NotApplied;

    public void Apply(ModuleDefMD module)
    {
        var instruction = GetVisibilityInstruction(module);
        if (!IlHelpers.IsLdcI4(instruction, 2))
            throw new PatchException("CardMinecraft 的 Visibility 不是 Collapsed(2)。");
        IlHelpers.ReplaceLdcI4(instruction, 0);
    }
}

/// <summary>P09：MC 更新提示恢复“下载”按钮。</summary>
internal sealed class P09UpdateDialogDownload : IPatch
{
    public string Id => "P09";
    public string Title => "恢复更新提示“下载”按钮";

    private static Instruction GetButtonTextInstruction(ModuleDefMD module)
    {
        var method = IlHelpers.GetMethod(module, "PCL.ModMinecraft", "McDownloadClientUpdateHint", 2);
        var ok = IlHelpers.FindInstruction(method, i => IlHelpers.IsLdstr(i, "确定"));
        return IlHelpers.Next(method, ok) ?? throw new PatchException("“确定”按钮后缺少指令。");
    }

    public PatchState Detect(ModuleDefMD module) =>
        IlHelpers.IsLdstr(GetButtonTextInstruction(module), "下载") ? PatchState.Applied : PatchState.NotApplied;

    public void Apply(ModuleDefMD module)
    {
        var instruction = GetButtonTextInstruction(module);
        if (!IlHelpers.IsLdstr(instruction, ""))
            throw new PatchException("更新提示的第二个按钮文本不是空字符串。");
        instruction.Operand = "下载";
    }
}
