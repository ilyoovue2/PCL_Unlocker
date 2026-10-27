using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLUnlocker.Patches;

internal static class IlHelpers
{
    public static TypeDef GetType(ModuleDefMD module, string fullName)
    {
        var type = module.GetTypes().FirstOrDefault(t => t.FullName == fullName);
        if (type is null)
            throw new PatchException($"找不到类型：{fullName}");
        return type;
    }

    public static MethodDef GetMethod(TypeDef type, string name, int parameterCount)
    {
        var method = type.Methods.FirstOrDefault(m =>
            m.Name == name &&
            m.MethodSig is not null &&
            m.MethodSig.Params.Count == parameterCount);
        if (method is null)
            throw new PatchException($"找不到方法：{type.FullName}::{name}({parameterCount} 个参数)");
        if (!method.HasBody)
            throw new PatchException($"方法没有 IL 方法体：{type.FullName}::{name}");
        return method;
    }

    public static MethodDef GetMethod(ModuleDefMD module, string typeFullName, string name, int parameterCount) =>
        GetMethod(GetType(module, typeFullName), name, parameterCount);

    public static FieldDef GetField(TypeDef type, string name)
    {
        var field = type.Fields.FirstOrDefault(f => f.Name == name);
        if (field is null)
            throw new PatchException($"找不到字段：{type.FullName}::{name}");
        return field;
    }

    public static FieldDef GetField(ModuleDefMD module, string typeFullName, string name) =>
        GetField(GetType(module, typeFullName), name);

    public static Instruction FindInstruction(MethodDef method, Func<Instruction, bool> predicate)
    {
        var instruction = method.Body.Instructions.FirstOrDefault(predicate);
        if (instruction is null)
            throw new PatchException($"在 {method.DeclaringType?.FullName}::{method.Name} 中找不到预期指令。");
        return instruction;
    }

    public static Instruction? TryFindInstruction(MethodDef method, Func<Instruction, bool> predicate) =>
        method.Body.Instructions.FirstOrDefault(predicate);

    public static bool IsCall(Instruction instruction, string name, string? declaringTypeContains = null)
    {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
            return false;
        return instruction.Operand is IMethod method &&
               method.Name == name &&
               (declaringTypeContains is null || method.DeclaringType?.FullName.Contains(declaringTypeContains, StringComparison.Ordinal) == true);
    }

    public static bool IsNewObj(Instruction instruction, string name, string? declaringTypeContains = null)
    {
        if (instruction.OpCode != OpCodes.Newobj)
            return false;
        return instruction.Operand is IMethod method &&
               method.Name == name &&
               (declaringTypeContains is null || method.DeclaringType?.FullName.Contains(declaringTypeContains, StringComparison.Ordinal) == true);
    }

    public static bool IsLdstr(Instruction instruction, string value) =>
        instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string s && s == value;

    public static bool IsLdcI4(Instruction instruction, int value)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldc_I4_M1: return value == -1;
            case Code.Ldc_I4_0: return value == 0;
            case Code.Ldc_I4_1: return value == 1;
            case Code.Ldc_I4_2: return value == 2;
            case Code.Ldc_I4_3: return value == 3;
            case Code.Ldc_I4_4: return value == 4;
            case Code.Ldc_I4_5: return value == 5;
            case Code.Ldc_I4_6: return value == 6;
            case Code.Ldc_I4_7: return value == 7;
            case Code.Ldc_I4_8: return value == 8;
            case Code.Ldc_I4_S: return instruction.Operand is sbyte b && b == value;
            case Code.Ldc_I4: return instruction.Operand is int i && i == value;
            default: return false;
        }
    }

    public static bool IsLdloc(Instruction instruction, int localIndex) =>
        instruction.OpCode.Code switch
        {
            Code.Ldloc_0 => localIndex == 0,
            Code.Ldloc_1 => localIndex == 1,
            Code.Ldloc_2 => localIndex == 2,
            Code.Ldloc_3 => localIndex == 3,
            Code.Ldloc_S => instruction.Operand is Local l && l.Index == localIndex,
            Code.Ldloc => instruction.Operand is Local l2 && l2.Index == localIndex,
            _ => false
        };

    public static bool IsStloc(Instruction instruction, int localIndex) =>
        instruction.OpCode.Code switch
        {
            Code.Stloc_0 => localIndex == 0,
            Code.Stloc_1 => localIndex == 1,
            Code.Stloc_2 => localIndex == 2,
            Code.Stloc_3 => localIndex == 3,
            Code.Stloc_S => instruction.Operand is Local l && l.Index == localIndex,
            Code.Stloc => instruction.Operand is Local l2 && l2.Index == localIndex,
            _ => false
        };

    public static int IndexOf(MethodDef method, Instruction instruction) =>
        method.Body.Instructions.IndexOf(instruction);

    public static Instruction? Next(MethodDef method, Instruction instruction)
    {
        var index = method.Body.Instructions.IndexOf(instruction);
        return index >= 0 && index + 1 < method.Body.Instructions.Count
            ? method.Body.Instructions[index + 1]
            : null;
    }

    public static Instruction? Previous(MethodDef method, Instruction instruction)
    {
        var index = method.Body.Instructions.IndexOf(instruction);
        return index > 0 ? method.Body.Instructions[index - 1] : null;
    }

    public static void RemoveRange(MethodDef method, Instruction start, Instruction end)
    {
        var instructions = method.Body.Instructions;
        var from = instructions.IndexOf(start);
        var to = instructions.IndexOf(end);
        if (from < 0 || to < 0 || to < from)
            throw new PatchException($"非法删除范围：{method.DeclaringType?.FullName}::{method.Name}");
        for (var i = to; i >= from; i--)
            instructions.RemoveAt(i);
    }

    public static int GetLdcI4Value(Instruction instruction)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldc_I4_M1: return -1;
            case Code.Ldc_I4_0: return 0;
            case Code.Ldc_I4_1: return 1;
            case Code.Ldc_I4_2: return 2;
            case Code.Ldc_I4_3: return 3;
            case Code.Ldc_I4_4: return 4;
            case Code.Ldc_I4_5: return 5;
            case Code.Ldc_I4_6: return 6;
            case Code.Ldc_I4_7: return 7;
            case Code.Ldc_I4_8: return 8;
            case Code.Ldc_I4_S: return instruction.Operand is sbyte b ? b : throw new PatchException("非法的 ldc.i4.s 操作数。");
            case Code.Ldc_I4: return instruction.Operand is int i ? i : throw new PatchException("非法的 ldc.i4 操作数。");
            default: throw new PatchException("预期 ldc.i4 指令。");
        }
    }

    public static void ReplaceLdcI4(Instruction instruction, int value)
    {
        _ = GetLdcI4Value(instruction);
        switch (value)
        {
            case -1: instruction.OpCode = OpCodes.Ldc_I4_M1; instruction.Operand = null; break;
            case 0: instruction.OpCode = OpCodes.Ldc_I4_0; instruction.Operand = null; break;
            case 1: instruction.OpCode = OpCodes.Ldc_I4_1; instruction.Operand = null; break;
            case 2: instruction.OpCode = OpCodes.Ldc_I4_2; instruction.Operand = null; break;
            case 3: instruction.OpCode = OpCodes.Ldc_I4_3; instruction.Operand = null; break;
            case 4: instruction.OpCode = OpCodes.Ldc_I4_4; instruction.Operand = null; break;
            case 5: instruction.OpCode = OpCodes.Ldc_I4_5; instruction.Operand = null; break;
            case 6: instruction.OpCode = OpCodes.Ldc_I4_6; instruction.Operand = null; break;
            case 7: instruction.OpCode = OpCodes.Ldc_I4_7; instruction.Operand = null; break;
            case 8: instruction.OpCode = OpCodes.Ldc_I4_8; instruction.Operand = null; break;
            default:
                if (value is >= -128 and <= 127)
                {
                    instruction.OpCode = OpCodes.Ldc_I4_S;
                    instruction.Operand = (sbyte)value;
                }
                else
                {
                    instruction.OpCode = OpCodes.Ldc_I4;
                    instruction.Operand = value;
                }
                break;
        }
    }

    public static IEnumerable<(MethodDef Method, Instruction Instruction)> AllInstructions(ModuleDefMD module)
    {
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                foreach (var instruction in method.Body.Instructions)
                    yield return (method, instruction);
            }
        }
    }

    public static Instruction FindCallInMethod(MethodDef method, string name, string? declaringTypeContains = null) =>
        FindInstruction(method, i => IsCall(i, name, declaringTypeContains));

    public static IMethod GetCalledMethod(ModuleDefMD module, string name, string declaringTypeContains)
    {
        foreach (var (_, instruction) in AllInstructions(module))
        {
            if (instruction.OpCode is var op &&
                (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj) &&
                instruction.Operand is IMethod method &&
                method.Name == name &&
                method.DeclaringType?.FullName.Contains(declaringTypeContains, StringComparison.Ordinal) == true)
                return method;
        }
        throw new PatchException($"找不到外部方法引用：{declaringTypeContains}::{name}");
    }

    public static IMethod GetNewObjMethod(ModuleDefMD module, string name, string declaringTypeContains, int parameterCount)
    {
        foreach (var (_, instruction) in AllInstructions(module))
        {
            if (instruction.OpCode == OpCodes.Newobj &&
                instruction.Operand is IMethod method &&
                method.Name == name &&
                method.DeclaringType?.FullName.Contains(declaringTypeContains, StringComparison.Ordinal) == true &&
                method.MethodSig?.Params.Count == parameterCount)
                return method;
        }
        throw new PatchException($"找不到构造方法引用：{declaringTypeContains}::{name}/{parameterCount}");
    }

    public static int LocalIndex(Instruction instruction)
    {
        return instruction.OpCode.Code switch
        {
            Code.Ldloc_0 or Code.Stloc_0 => 0,
            Code.Ldloc_1 or Code.Stloc_1 => 1,
            Code.Ldloc_2 or Code.Stloc_2 => 2,
            Code.Ldloc_3 or Code.Stloc_3 => 3,
            Code.Ldloc_S or Code.Stloc_S => (instruction.Operand as Local)?.Index ?? -1,
            Code.Ldloc or Code.Stloc => (instruction.Operand as Local)?.Index ?? -1,
            _ => -1
        };
    }
}
