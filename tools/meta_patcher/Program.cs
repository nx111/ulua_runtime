using System;
using System.Linq;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--dump")
        {
            return DumpMethods(args[1]);
        }

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: meta_patcher <input-dll> <output-dll>");
            Console.WriteLine("   or: meta_patcher --dump <input-dll>");
            return 1;
        }

        var input = args[0];
        var output = args[1];

        var modCtx = ModuleDef.CreateModuleContext();
        var module = ModuleDefMD.Load(input, modCtx);

        var metaType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.MetaFunctions");
        if (metaType == null)
        {
            Console.WriteLine("Type LuaInterface.MetaFunctions not found");
            return 2;
        }

        bool patchedClassMethod = false;
        bool patchedMemberCache = false;
        bool patchedGetMember = false;
        bool patchedGetMemberFlags = PatchGetMemberVisibilityFlags(metaType);
        bool patchedUnknownInstance = PatchSuppressUnknownInstanceError(metaType, module);
        bool patchedCustomXmlSerializer = PatchCustomXmlSerializerGetMembers(module);
        bool patchedThrowErrorUnknownInstance = PatchThrowErrorUnknownInstance(module);
        bool patchedLuaFunctionEnsureTranslator = PatchLuaFunctionEnsureTranslator(module);
        bool patchedLuaFunctionPushSafe = PatchLuaFunctionPushSafe(module);
        bool patchedLuaFunctionPopValuesSafe = PatchLuaFunctionPopValuesSafe(module);
        bool patchedLuaFunctionCallGuard = PatchLuaFunctionCallGuard(module);
        bool patchedLuaFunctionCallArgsNull = PatchLuaFunctionCallArgsNullGuard(module);
        bool patchedLuaFunctionCallTrace = PatchLuaFunctionCallCatchTrace(module);
        bool patchedObjectTranslatorFromStateSafe = PatchObjectTranslatorFromStateSafe(module);
        bool patchedPushVarObjectTranslatorGuard = PatchLuaScriptMgrPushVarObjectTranslatorGuard(module);
        bool patchedPushTraceBackSafe = PatchLuaScriptMgrPushTraceBackSafe(module);
        bool patchedDisableLuaFunctionCache = PatchLuaScriptMgrDisableGetLuaFunctionCache(module);
        bool patchedThrowException = false;
        bool patchedProxyTypeGetMember = false;
        bool patchedImportType = PatchImportTypeForFileLogger(module);

        if (!patchedGetMemberFlags && !patchedUnknownInstance && !patchedCustomXmlSerializer && !patchedThrowErrorUnknownInstance && !patchedLuaFunctionEnsureTranslator && !patchedLuaFunctionPushSafe && !patchedLuaFunctionPopValuesSafe && !patchedLuaFunctionCallGuard && !patchedLuaFunctionCallArgsNull && !patchedLuaFunctionCallTrace && !patchedObjectTranslatorFromStateSafe && !patchedPushVarObjectTranslatorGuard && !patchedPushTraceBackSafe && !patchedDisableLuaFunctionCache && !patchedImportType)
        {
            Console.WriteLine("No patch points matched; nothing changed.");
            return 3;
        }

        module.Write(output);
        Console.WriteLine($"Patched: getClassMethod={patchedClassMethod}, checkMemberCache={patchedMemberCache}, getMember={patchedGetMember}, getMemberFlags={patchedGetMemberFlags}, unknownInstance={patchedUnknownInstance}, customXml={patchedCustomXmlSerializer}, throwErrorUnknownInstance={patchedThrowErrorUnknownInstance}, luaFuncEnsureTranslator={patchedLuaFunctionEnsureTranslator}, luaFuncPushSafe={patchedLuaFunctionPushSafe}, luaFuncPopValuesSafe={patchedLuaFunctionPopValuesSafe}, luaFuncCallGuard={patchedLuaFunctionCallGuard}, luaFuncCallArgsNull={patchedLuaFunctionCallArgsNull}, luaFuncCallTrace={patchedLuaFunctionCallTrace}, fromStateSafe={patchedObjectTranslatorFromStateSafe}, pushVarTranslatorGuard={patchedPushVarObjectTranslatorGuard}, pushTraceBackSafe={patchedPushTraceBackSafe}, disableLuaFuncCache={patchedDisableLuaFunctionCache}, throwException={patchedThrowException}, proxyGetMember={patchedProxyTypeGetMember}, importType={patchedImportType}");
        return 0;
    }

    static int DumpMethods(string input)
    {
        var modCtx = ModuleDef.CreateModuleContext();
        var module = ModuleDefMD.Load(input, modCtx);
        var metaType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.MetaFunctions");
        if (metaType == null)
        {
            Console.WriteLine("Type LuaInterface.MetaFunctions not found");
            return 2;
        }

        foreach (var name in new[] { "getClassMethod", "checkMemberCache", "getMember" })
        {
            var m = metaType.Methods.FirstOrDefault(x => x.Name == name);
            if (m == null || m.Body == null)
            {
                Console.WriteLine($"[{name}] not found or no body");
                continue;
            }

            Console.WriteLine($"=== {name} ({m.FullName}) ===");
            int i = 0;
            foreach (var ins in m.Body.Instructions.Take(120))
            {
                Console.WriteLine($"{i:D3}: {ins}");
                i++;
            }
        }

        return 0;
    }

    static bool PatchThrowExceptionFromError(ModuleDef module)
    {
        var luaStateType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaState");
        if (luaStateType == null)
            return false;

        var method = luaStateType.Methods.FirstOrDefault(m => m.Name == "ThrowExceptionFromError" && !m.IsStatic);
        if (method == null || method.Body == null)
            return false;

        // Idempotent: if already setting top to 0 in this method, skip.
        if (method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4_0))
        {
            // keep it conservative: if we can see lua_settop call with ldc.i4.0 nearby, treat as patched.
            var ins = method.Body.Instructions;
            for (int i = 0; i < ins.Count - 1; i++)
            {
                if (ins[i].OpCode == OpCodes.Ldc_I4_0 &&
                    ins[i + 1].OpCode == OpCodes.Call &&
                    ins[i + 1].Operand is IMethod m && m.Name == "lua_settop")
                    return false;
            }
        }

        var instrs = method.Body.Instructions;
        for (int i = 0; i < instrs.Count - 1; i++)
        {
            if (instrs[i].OpCode == OpCodes.Ldarg_1 &&
                instrs[i + 1].OpCode == OpCodes.Call &&
                instrs[i + 1].Operand is IMethod im && im.Name == "lua_settop")
            {
                instrs[i].OpCode = OpCodes.Ldc_I4_0;
                instrs[i].Operand = null;
                method.Body.SimplifyBranches();
                method.Body.OptimizeBranches();
                return true;
            }
        }

        return false;
    }

    static bool PatchGetClassMethod(ModuleDef module, TypeDef metaType)
    {
        var method = metaType.Methods.FirstOrDefault(m => m.Name == "getClassMethod" && m.IsStatic);
        if (method == null || method.Body == null)
            return false;

        var luaDllType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaDLL");
        var pushNil = luaDllType?.Methods.FirstOrDefault(m => m.Name == "lua_pushnil" && m.IsStatic && m.Parameters.Count == 1);
        if (pushNil == null)
            return false;

        // Find: call lua_tostring ... stloc.*
        var instrs = method.Body.Instructions;
        int callIdx = -1;
        int stlocIdx = -1;
        Local targetLocal = null!;

        for (int i = 0; i < instrs.Count - 1; i++)
        {
            if (instrs[i].OpCode == OpCodes.Call && instrs[i].Operand is IMethod im && im.Name == "lua_tostring")
            {
                var next = instrs[i + 1];
                if (next.IsStloc())
                {
                    callIdx = i;
                    stlocIdx = i + 1;
                    targetLocal = next.GetLocal(method.Body.Variables);
                    break;
                }
            }
        }

        if (stlocIdx < 0 || targetLocal == null)
            return false;

        // Skip if already patched: look for ldloc targetLocal + brtrue + pushnil nearby
        for (int i = stlocIdx + 1; i < Math.Min(stlocIdx + 12, instrs.Count); i++)
        {
            if (instrs[i].OpCode == OpCodes.Call && instrs[i].Operand is IMethod m && m.Name == "lua_pushnil")
                return false;
        }

        var continueInstr = Instruction.Create(OpCodes.Nop);
        var newInstrs = new[]
        {
            Instruction.Create(OpCodes.Ldloc, targetLocal),
            Instruction.Create(OpCodes.Brtrue_S, continueInstr),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, pushNil),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Ret),
            continueInstr,
        };

        int insertAt = stlocIdx + 1;
        for (int j = 0; j < newInstrs.Length; j++)
        {
            instrs.Insert(insertAt + j, newInstrs[j]);
        }

        method.Body.SimplifyBranches();
        method.Body.OptimizeBranches();
        return true;
    }

    static bool PatchCheckMemberCache(TypeDef metaType)
    {
        var method = metaType.Methods.FirstOrDefault(m => m.Name == "checkMemberCache" && !m.IsStatic);
        if (method == null || method.Body == null)
            return false;

        // Signature should be: (Hashtable memberCache, IReflect objType, string memberName)
        if (method.Parameters.Count < 4)
            return false;

        var instrs = method.Body.Instructions;

        // Skip if already guarded for both objType and memberName.
        bool hasObjTypeGuard = false;
        bool hasMemberGuard = false;
        for (int i = 0; i < Math.Min(20, instrs.Count - 3); i++)
        {
            if (instrs[i].OpCode == OpCodes.Ldarg_2 && instrs[i + 1].OpCode == OpCodes.Brtrue_S &&
                instrs[i + 2].OpCode == OpCodes.Ldnull && instrs[i + 3].OpCode == OpCodes.Ret)
            {
                hasObjTypeGuard = true;
            }
            if (instrs[i].OpCode == OpCodes.Ldarg_3 && instrs[i + 1].OpCode == OpCodes.Brtrue_S &&
                instrs[i + 2].OpCode == OpCodes.Ldnull && instrs[i + 3].OpCode == OpCodes.Ret)
            {
                hasMemberGuard = true;
            }
        }
        if (hasObjTypeGuard && hasMemberGuard)
            return false;

        int insertAt = 0;
        if (!hasObjTypeGuard)
        {
            var contObj = Instruction.Create(OpCodes.Nop);
            var objGuard = new[]
            {
                Instruction.Create(OpCodes.Ldarg_2),
                Instruction.Create(OpCodes.Brtrue_S, contObj),
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ret),
                contObj,
            };
            for (int i = 0; i < objGuard.Length; i++)
                instrs.Insert(insertAt + i, objGuard[i]);
            insertAt += objGuard.Length;
        }

        if (!hasMemberGuard)
        {
            var contName = Instruction.Create(OpCodes.Nop);
            var memberGuard = new[]
            {
                Instruction.Create(OpCodes.Ldarg_3),
                Instruction.Create(OpCodes.Brtrue_S, contName),
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ret),
                contName,
            };
            for (int i = 0; i < memberGuard.Length; i++)
                instrs.Insert(insertAt + i, memberGuard[i]);
        }

        method.Body.SimplifyBranches();
        method.Body.OptimizeBranches();
        return !hasObjTypeGuard || !hasMemberGuard;
    }

    static bool PatchGetMember(TypeDef metaType, ModuleDef module)
    {
        var method = metaType.Methods.FirstOrDefault(m => m.Name == "getMember" && !m.IsStatic);
        if (method == null || method.Body == null || method.Parameters.Count < 5)
            return false;

        var instrs = method.Body.Instructions;
        bool modified = false;

        IMethod getUnderlyingSystemType = metaType.Methods
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call)
            .Select(i => i.Operand as IMethod)
            .FirstOrDefault(im => im != null && im.Name == "get_UnderlyingSystemType" && im.DeclaringType.FullName == "System.Reflection.IReflect");

        if (getUnderlyingSystemType == null)
            return false;

        var getMemberCallIndexes = new List<int>();
        for (int i = 0; i < instrs.Count; i++)
        {
            if ((instrs[i].OpCode == OpCodes.Callvirt || instrs[i].OpCode == OpCodes.Call) &&
                instrs[i].Operand is IMethod called &&
                called.Name == "GetMember" &&
                called.DeclaringType.FullName == "System.Reflection.IReflect" &&
                called.MethodSig != null &&
                called.MethodSig.Params.Count == 2)
            {
                getMemberCallIndexes.Add(i);
            }
        }

        for (int k = getMemberCallIndexes.Count - 1; k >= 0; k--)
        {
            int callIdx = getMemberCallIndexes[k];
            int loadObjTypeIdx = -1;

            for (int j = callIdx - 1; j >= Math.Max(0, callIdx - 20); j--)
            {
                if (instrs[j].OpCode == OpCodes.Ldarg_2)
                {
                    loadObjTypeIdx = j;
                    break;
                }
            }

            if (loadObjTypeIdx < 0)
                continue;

            bool alreadyForcedToUnderlying =
                loadObjTypeIdx + 1 < instrs.Count &&
                instrs[loadObjTypeIdx + 1].OpCode == OpCodes.Callvirt &&
                instrs[loadObjTypeIdx + 1].Operand is IMethod getter &&
                getter.Name == "get_UnderlyingSystemType" &&
                getter.DeclaringType.FullName == "System.Reflection.IReflect";

            if (alreadyForcedToUnderlying)
                continue;

            // Convert objType -> objType.UnderlyingSystemType before GetMember lookup.
            instrs.Insert(loadObjTypeIdx + 1, Instruction.Create(OpCodes.Callvirt, getUnderlyingSystemType));
            modified = true;
        }

        Instruction unknownPath = null!;
        for (int i = 0; i < instrs.Count - 1; i++)
        {
            if ((instrs[i].OpCode == OpCodes.Ldloc_1 ||
                 (instrs[i].IsLdloc() && instrs[i].GetLocal(method.Body.Variables)?.Index == 1)) &&
                (instrs[i + 1].OpCode == OpCodes.Brfalse || instrs[i + 1].OpCode == OpCodes.Brfalse_S) &&
                instrs[i + 1].Operand is Instruction target)
            {
                unknownPath = target;
                break;
            }
        }

        if (unknownPath != null)
        {
            bool hasObjGuard = false;
            bool hasNameGuard = false;
            for (int i = 0; i < Math.Min(12, instrs.Count - 2); i++)
            {
                if (instrs[i].OpCode == OpCodes.Ldarg_2 &&
                    (instrs[i + 1].OpCode == OpCodes.Brtrue || instrs[i + 1].OpCode == OpCodes.Brtrue_S) &&
                    (instrs[i + 2].OpCode == OpCodes.Br || instrs[i + 2].OpCode == OpCodes.Br_S))
                {
                    hasObjGuard = true;
                }

                if (instrs[i].OpCode == OpCodes.Ldarg_S &&
                    instrs[i].Operand is Parameter p &&
                    p.Index == method.Parameters[4].Index &&
                    (instrs[i + 1].OpCode == OpCodes.Brtrue || instrs[i + 1].OpCode == OpCodes.Brtrue_S) &&
                    (instrs[i + 2].OpCode == OpCodes.Br || instrs[i + 2].OpCode == OpCodes.Br_S))
                {
                    hasNameGuard = true;
                }
            }

            int insertAt = 0;
            if (!hasObjGuard)
            {
                var contObj = Instruction.Create(OpCodes.Nop);
                var guardObj = new[]
                {
                    Instruction.Create(OpCodes.Ldarg_2),
                    Instruction.Create(OpCodes.Brtrue_S, contObj),
                    Instruction.Create(OpCodes.Br, unknownPath),
                    contObj,
                };
                for (int i = 0; i < guardObj.Length; i++)
                    instrs.Insert(insertAt + i, guardObj[i]);
                insertAt += guardObj.Length;
                modified = true;
            }

            if (!hasNameGuard)
            {
                var contName = Instruction.Create(OpCodes.Nop);
                var guardName = new[]
                {
                    Instruction.Create(OpCodes.Ldarg_S, method.Parameters[4]),
                    Instruction.Create(OpCodes.Brtrue_S, contName),
                    Instruction.Create(OpCodes.Br, unknownPath),
                    contName,
                };
                for (int i = 0; i < guardName.Length; i++)
                    instrs.Insert(insertAt + i, guardName[i]);
                modified = true;
            }
        }

        if (!modified)
            return false;

        method.Body.SimplifyBranches();
        method.Body.OptimizeBranches();
        return true;
    }

    static bool PatchGetMemberVisibilityFlags(TypeDef metaType)
    {
        var method = metaType.Methods.FirstOrDefault(m => m.Name == "getMember" && !m.IsStatic);
        if (method == null || method.Body == null)
            return false;

        var instrs = method.Body.Instructions;
        bool changed = false;

        for (int i = 0; i < instrs.Count; i++)
        {
            if ((instrs[i].OpCode == OpCodes.Callvirt || instrs[i].OpCode == OpCodes.Call) &&
                instrs[i].Operand is IMethod im &&
                im.Name == "GetMember" &&
                im.DeclaringType.FullName == "System.Reflection.IReflect" &&
                im.MethodSig != null &&
                im.MethodSig.Params.Count == 2)
            {
                // In the 6 instructions before GetMember, replace "ldc.i4.s 16" (Public)
                // with "ldc.i4.s 48" (Public|NonPublic).
                for (int j = Math.Max(0, i - 6); j < i; j++)
                {
                    if ((instrs[j].OpCode == OpCodes.Ldc_I4_S || instrs[j].OpCode == OpCodes.Ldc_I4) &&
                        instrs[j].GetLdcI4Value() == 16)
                    {
                        instrs[j].OpCode = OpCodes.Ldc_I4_S;
                        instrs[j].Operand = (sbyte)48;
                        changed = true;
                    }
                }
            }
        }

        if (!changed)
            return false;

        method.Body.SimplifyBranches();
        method.Body.OptimizeBranches();
        return true;
    }

    static bool PatchSuppressUnknownInstanceError(TypeDef metaType, ModuleDef module)
    {
        var method = metaType.Methods.FirstOrDefault(m => m.Name == "getMember" && !m.IsStatic);
        if (method == null || method.Body == null)
            return false;

        var instrs = method.Body.Instructions;

        int unknownStrIdx = -1;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Ldstr &&
                instrs[i].Operand is string s &&
                s == "unknown member name ")
            {
                unknownStrIdx = i;
                break;
            }
        }

        if (unknownStrIdx < 3)
            return false;

        int blockStart = unknownStrIdx - 3; // ldarg.0 before throwError branch

        int pushNilLdargIdx = -1;
        for (int i = unknownStrIdx; i < instrs.Count - 1; i++)
        {
            if (instrs[i].IsLdarg() &&
                instrs[i].GetParameter(method.Parameters)?.Index == 1 &&
                instrs[i + 1].OpCode == OpCodes.Call &&
                instrs[i + 1].Operand is IMethod im &&
                im.Name == "lua_pushnil")
            {
                pushNilLdargIdx = i;
                break;
            }
        }

        if (pushNilLdargIdx < 0)
            return false;
        var pushNilTarget = instrs[pushNilLdargIdx];

        // Idempotent: if this branch already skips unknown-member throw, do nothing.
        if ((instrs[blockStart].OpCode == OpCodes.Br || instrs[blockStart].OpCode == OpCodes.Br_S) &&
            ReferenceEquals(instrs[blockStart].Operand, pushNilTarget))
        {
            return false;
        }

        instrs.Insert(blockStart, Instruction.Create(OpCodes.Br_S, pushNilTarget));

        method.Body.SimplifyBranches();
        method.Body.OptimizeBranches();
        return true;
    }

    static bool PatchCustomXmlSerializerGetMembers(ModuleDef module)
    {
        var cxsType = module.Types.FirstOrDefault(t => t.FullName == "JyGame.CustomXmlSerializer");
        if (cxsType == null)
            return false;

        var getMembers = cxsType.Methods.FirstOrDefault(m =>
            m.Name == "GetMembers" &&
            m.Parameters.Count >= 1 &&
            m.Parameters[m.Parameters.Count - 1].Type.FullName == "System.Type");
        if (getMembers == null || getMembers.Body == null)
            return false;

        var isDefinedCalls = getMembers.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .Where(im => im.DeclaringType.FullName == "System.Reflection.MemberInfo" && im.Name == "IsDefined")
            .ToList();
        if (isDefinedCalls.Count == 0)
            return false;

        var memberInfoIsDefinedRef = isDefinedCalls.First();
        if (memberInfoIsDefinedRef == null)
            return false;

        var safeMethod = cxsType.Methods.FirstOrDefault(m =>
            m.Name == "SafeIsDefined" &&
            m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[0].Type.FullName == "System.Reflection.MemberInfo" &&
            m.Parameters[1].Type.FullName == "System.Type" &&
            m.Parameters[2].Type.FullName == "System.Boolean");

        if (safeMethod == null)
        {
            var memberInfoType = module.CorLibTypes.GetTypeRef("System.Reflection", "MemberInfo");
            var typeType = module.CorLibTypes.GetTypeRef("System", "Type");

            var sig = MethodSig.CreateStatic(
                module.CorLibTypes.Boolean,
                new ClassSig(memberInfoType),
                new ClassSig(typeType),
                module.CorLibTypes.Boolean);

            safeMethod = new MethodDefUser(
                "SafeIsDefined",
                sig,
                MethodImplAttributes.IL | MethodImplAttributes.Managed,
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig);

            var body = new CilBody { InitLocals = true };
            safeMethod.Body = body;
            var retLocal = new Local(module.CorLibTypes.Boolean);
            body.Variables.Add(retLocal);

            var i0 = Instruction.Create(OpCodes.Ldarg_0);
            var i1 = Instruction.Create(OpCodes.Brtrue_S, (Instruction)null!);
            var i2 = Instruction.Create(OpCodes.Ldc_I4_0);
            var i3 = Instruction.Create(OpCodes.Stloc_0);
            var i4 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

            var i5 = Instruction.Create(OpCodes.Ldarg_0);
            var i6 = Instruction.Create(OpCodes.Ldarg_1);
            var i7 = Instruction.Create(OpCodes.Ldarg_2);
            var i8 = Instruction.Create(OpCodes.Callvirt, memberInfoIsDefinedRef);
            var i9 = Instruction.Create(OpCodes.Stloc_0);
            var i10 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

            var i11 = Instruction.Create(OpCodes.Pop);
            var i12 = Instruction.Create(OpCodes.Ldc_I4_0);
            var i13 = Instruction.Create(OpCodes.Stloc_0);
            var i14 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

            var i15 = Instruction.Create(OpCodes.Ldloc_0);
            var i16 = Instruction.Create(OpCodes.Ret);

            i1.Operand = i5;
            i4.Operand = i15;
            i10.Operand = i15;
            i14.Operand = i15;

            body.Instructions.Add(i0);
            body.Instructions.Add(i1);
            body.Instructions.Add(i2);
            body.Instructions.Add(i3);
            body.Instructions.Add(i4);
            body.Instructions.Add(i5);
            body.Instructions.Add(i6);
            body.Instructions.Add(i7);
            body.Instructions.Add(i8);
            body.Instructions.Add(i9);
            body.Instructions.Add(i10);
            body.Instructions.Add(i11);
            body.Instructions.Add(i12);
            body.Instructions.Add(i13);
            body.Instructions.Add(i14);
            body.Instructions.Add(i15);
            body.Instructions.Add(i16);

            var exType = module.CorLibTypes.GetTypeRef("System", "Exception");
            body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = i0,
                TryEnd = i11,
                HandlerStart = i11,
                HandlerEnd = i15,
                CatchType = exType
            });

            body.SimplifyBranches();
            body.OptimizeBranches();

            cxsType.Methods.Add(safeMethod);
        }

        bool changed = false;
        foreach (var ins in getMembers.Body.Instructions)
        {
            if (ins.OpCode == OpCodes.Callvirt &&
                ins.Operand is IMethod im &&
                im.DeclaringType.FullName == "System.Reflection.MemberInfo" &&
                im.Name == "IsDefined")
            {
                ins.OpCode = OpCodes.Call;
                ins.Operand = safeMethod;
                changed = true;
            }
        }

        if (!changed)
            return false;

        getMembers.Body.SimplifyBranches();
        getMembers.Body.OptimizeBranches();
        return true;
    }

    static bool PatchThrowErrorUnknownInstance(ModuleDef module)
    {
        var translator = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.ObjectTranslator");
        if (translator == null)
            return false;

        var throwError = translator.Methods.FirstOrDefault(m =>
            m.Name == "throwError" &&
            !m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[1].Type.FullName == "System.IntPtr" &&
            m.Parameters[2].Type.FullName == "System.String");
        if (throwError == null || throwError.Body == null)
            return false;

        var instrs = throwError.Body.Instructions;

        // Idempotent: already patched if we can see the target message compare.
        if (instrs.Any(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s && s == "unknown member name instance"))
            return false;

        var stringOpEquality = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im => im.FullName == "System.Boolean System.String::op_Equality(System.String,System.String)");
        if (stringOpEquality == null)
            return false;

        if (instrs.Count < 2)
            return false;

        var continueInstr = instrs[0];
        var guard = new[]
        {
            Instruction.Create(OpCodes.Ldarg_2),
            Instruction.Create(OpCodes.Ldstr, "unknown member name instance"),
            Instruction.Create(OpCodes.Call, stringOpEquality),
            Instruction.Create(OpCodes.Brfalse_S, continueInstr),
            Instruction.Create(OpCodes.Ret),
        };

        for (int i = 0; i < guard.Length; i++)
            instrs.Insert(i, guard[i]);

        throwError.Body.SimplifyBranches();
        throwError.Body.OptimizeBranches();
        return true;
    }

    static bool PatchLuaFunctionCallGuard(ModuleDef module)
    {
        var luaFunctionType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaFunction");
        if (luaFunctionType == null)
            return false;

        var callMethod = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "Call" &&
            !m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[1].Type.FullName == "System.Object[]");
        if (callMethod == null || callMethod.Body == null)
            return false;

        // Idempotent: already guarded by exception handler.
        if (callMethod.Body.ExceptionHandlers.Count > 0)
            return false;

        var innerCall = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "call" &&
            !m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[1].Type.FullName == "System.Object[]" &&
            m.Parameters[2].Type.FullName == "System.Type[]");
        if (innerCall == null)
            return false;

        var logErrorRef = module.Types
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand!)
            .FirstOrDefault(im =>
                im.DeclaringType.FullName == "UnityEngine.Debug" &&
                im.Name == "LogError" &&
                im.MethodSig != null &&
                im.MethodSig.Params.Count == 1 &&
                im.MethodSig.Params[0].FullName == "System.Object");
        if (logErrorRef == null)
            return false;

        callMethod.Body.Instructions.Clear();
        callMethod.Body.ExceptionHandlers.Clear();
        callMethod.Body.Variables.Clear();
        callMethod.Body.InitLocals = true;

        var retSig = callMethod.Parameters[1].Type;
        if (retSig == null)
            return false;
        var resultLocal = new Local(retSig);
        var exceptionLocal = new Local(module.CorLibTypes.Object);
        callMethod.Body.Variables.Add(resultLocal);
        callMethod.Body.Variables.Add(exceptionLocal);

        var p0 = Instruction.Create(OpCodes.Ldarg_1);
        var p1 = Instruction.Create(OpCodes.Brtrue_S, (Instruction)null!);
        var p2 = Instruction.Create(OpCodes.Ldc_I4_0);
        var p3 = Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Object.TypeDefOrRef);
        var p4 = Instruction.Create(OpCodes.Starg_S, callMethod.Parameters[1]);
        var p5 = Instruction.Create(OpCodes.Nop);
        p1.Operand = p5;

        var i0 = Instruction.Create(OpCodes.Ldarg_0);
        var i1 = Instruction.Create(OpCodes.Ldarg_1);
        var i2 = Instruction.Create(OpCodes.Ldnull);
        var i3 = Instruction.Create(OpCodes.Call, innerCall);
        var i4 = Instruction.Create(OpCodes.Stloc_0);
        var i5 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

        var h0 = Instruction.Create(OpCodes.Stloc_1);
        var h1 = Instruction.Create(OpCodes.Ldloc_1);
        var h2 = Instruction.Create(OpCodes.Call, logErrorRef);
        var h3 = Instruction.Create(OpCodes.Ldnull);
        var h4 = Instruction.Create(OpCodes.Stloc_0);
        var h5 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

        var ret0 = Instruction.Create(OpCodes.Ldloc_0);
        var ret1 = Instruction.Create(OpCodes.Ret);

        i5.Operand = ret0;
        h5.Operand = ret0;

        callMethod.Body.Instructions.Add(p0);
        callMethod.Body.Instructions.Add(p1);
        callMethod.Body.Instructions.Add(p2);
        callMethod.Body.Instructions.Add(p3);
        callMethod.Body.Instructions.Add(p4);
        callMethod.Body.Instructions.Add(p5);
        callMethod.Body.Instructions.Add(i0);
        callMethod.Body.Instructions.Add(i1);
        callMethod.Body.Instructions.Add(i2);
        callMethod.Body.Instructions.Add(i3);
        callMethod.Body.Instructions.Add(i4);
        callMethod.Body.Instructions.Add(i5);
        callMethod.Body.Instructions.Add(h0);
        callMethod.Body.Instructions.Add(h1);
        callMethod.Body.Instructions.Add(h2);
        callMethod.Body.Instructions.Add(h3);
        callMethod.Body.Instructions.Add(h4);
        callMethod.Body.Instructions.Add(h5);
        callMethod.Body.Instructions.Add(ret0);
        callMethod.Body.Instructions.Add(ret1);

        var exType = module.CorLibTypes.GetTypeRef("System", "Exception");
        callMethod.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = i0,
            TryEnd = h0,
            HandlerStart = h0,
            HandlerEnd = ret0,
            CatchType = exType
        });

        callMethod.Body.SimplifyBranches();
        callMethod.Body.OptimizeBranches();
        return true;
    }

    static bool PatchLuaFunctionCallArgsNullGuard(ModuleDef module)
    {
        var luaFunctionType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaFunction");
        if (luaFunctionType == null)
            return false;

        var callMethod = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "call" &&
            !m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[1].Type.FullName == "System.Object[]" &&
            m.Parameters[2].Type.FullName == "System.Type[]");
        if (callMethod == null || callMethod.Body == null)
            return false;

        // Idempotent: already has args-null rewrite at method start.
        if (callMethod.Body.Instructions.Take(12).Any(i =>
            (i.OpCode == OpCodes.Starg || i.OpCode == OpCodes.Starg_S) &&
            i.GetParameter(callMethod.Parameters)?.Index == 1))
        {
            return false;
        }

        var instrs = callMethod.Body.Instructions;
        var cont = Instruction.Create(OpCodes.Nop);
        instrs.Insert(0, Instruction.Create(OpCodes.Ldarg_1));
        instrs.Insert(1, Instruction.Create(OpCodes.Brtrue_S, cont));
        instrs.Insert(2, Instruction.Create(OpCodes.Ldc_I4_0));
        instrs.Insert(3, Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Object.TypeDefOrRef));
        instrs.Insert(4, Instruction.Create(OpCodes.Starg_S, callMethod.Parameters[1]));
        instrs.Insert(5, cont);

        callMethod.Body.SimplifyBranches();
        callMethod.Body.OptimizeBranches();
        return true;
    }

    static bool PatchLuaFunctionCallCatchTrace(ModuleDef module)
    {
        var luaFunctionType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaFunction");
        var luaBaseType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaBase");
        if (luaFunctionType == null || luaBaseType == null)
            return false;

        var callMethod = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "Call" &&
            !m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[1].Type.FullName == "System.Object[]");
        if (callMethod == null || callMethod.Body == null)
            return false;

        // Requires catch-based guard to exist.
        if (callMethod.Body.ExceptionHandlers.Count == 0)
            return false;

        var instrs = callMethod.Body.Instructions;
        var envGetStackTrace = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand!)
            .FirstOrDefault(im =>
                im.FullName == "System.String System.Environment::get_StackTrace()");
        var logErrorRef = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand!)
            .FirstOrDefault(im =>
                im.DeclaringType.FullName == "UnityEngine.Debug" &&
                im.Name == "LogError" &&
                im.MethodSig != null &&
                im.MethodSig.Params.Count == 1 &&
                im.MethodSig.Params[0].FullName == "System.Object");
        var nameField = luaBaseType.Fields.FirstOrDefault(f =>
            f.Name == "name" &&
            f.FieldType.FullName == "System.String");
        if (envGetStackTrace == null || logErrorRef == null || nameField == null)
            return false;

        // Idempotent: already logs Environment.StackTrace in catch path.
        if (instrs.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                            i.Operand is IMethod im &&
                            im.FullName == "System.String System.Environment::get_StackTrace()"))
            return false;

        // Insert trace logs right after the first Debug.LogError(ex) call in catch block.
        for (int i = 0; i < instrs.Count - 1; i++)
        {
            if ((instrs[i].OpCode == OpCodes.Call || instrs[i].OpCode == OpCodes.Callvirt) &&
                instrs[i].Operand is IMethod im &&
                im == logErrorRef)
            {
                var inject = new[]
                {
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldfld, nameField),
                    Instruction.Create(OpCodes.Call, logErrorRef),
                    Instruction.Create(OpCodes.Call, envGetStackTrace),
                    Instruction.Create(OpCodes.Call, logErrorRef),
                };
                for (int j = 0; j < inject.Length; j++)
                    instrs.Insert(i + 1 + j, inject[j]);

                callMethod.Body.SimplifyBranches();
                callMethod.Body.OptimizeBranches();
                return true;
            }
        }

        return false;
    }

    static bool PatchObjectTranslatorFromStateSafe(ModuleDef module)
    {
        var translatorType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.ObjectTranslator");
        var luaDllType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaDLL");
        if (translatorType == null || luaDllType == null)
            return false;

        var fromState = translatorType.Methods.FirstOrDefault(m =>
            m.Name == "FromState" &&
            m.IsStatic &&
            m.Parameters.Count == 1 &&
            m.Parameters[0].Type.FullName == "System.IntPtr" &&
            m.ReturnType.FullName == "LuaInterface.ObjectTranslator");
        var listField = translatorType.Fields.FirstOrDefault(f =>
            f.Name == "list" &&
            f.IsStatic &&
            f.FieldType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>"));
        if (fromState == null || fromState.Body == null || listField == null)
            return false;

        // Idempotent: patched version uses list.Count bounds checks.
        if (fromState.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Callvirt &&
            i.Operand is IMethod im &&
            im.Name == "get_Count" &&
            im.DeclaringType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>")))
        {
            return false;
        }

        var listGetItem = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand!)
            .FirstOrDefault(im =>
                im.Name == "get_Item" &&
                im.DeclaringType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>"));
        var listGetCount = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand!)
            .FirstOrDefault(im =>
                im.Name == "get_Count" &&
                im.DeclaringType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>"));
        ITypeDefOrRef? listDeclType = listGetItem?.DeclaringType;
        if (listDeclType == null)
        {
            if (listField.FieldType is GenericInstSig gis)
                listDeclType = gis.GenericType.TypeDefOrRef;
            else
                listDeclType = listField.FieldType.ToTypeDefOrRef();
        }
        if (listGetItem == null && listDeclType != null)
        {
            listGetItem = new MemberRefUser(
                module,
                "get_Item",
                MethodSig.CreateInstance(translatorType.ToTypeSig(), module.CorLibTypes.Int32),
                listDeclType);
        }
        if (listGetCount == null && listDeclType != null)
        {
            listGetCount = new MemberRefUser(
                module,
                "get_Count",
                MethodSig.CreateInstance(module.CorLibTypes.Int32),
                listDeclType);
        }

        var luaGetGlobal = luaDllType.Methods.FirstOrDefault(m =>
            m.Name == "lua_getglobal" &&
            m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[0].Type.FullName == "System.IntPtr" &&
            m.Parameters[1].Type.FullName == "System.String");
        var luaToNumber = luaDllType.Methods.FirstOrDefault(m =>
            m.Name == "lua_tonumber" &&
            m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[0].Type.FullName == "System.IntPtr" &&
            m.Parameters[1].Type.FullName == "System.Int32");
        var luaPop = luaDllType.Methods.FirstOrDefault(m =>
            m.Name == "lua_pop" &&
            m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[0].Type.FullName == "System.IntPtr" &&
            m.Parameters[1].Type.FullName == "System.Int32");
        if (listGetItem == null || listGetCount == null || luaGetGlobal == null || luaToNumber == null || luaPop == null)
            return false;

        fromState.Body.Instructions.Clear();
        fromState.Body.ExceptionHandlers.Clear();
        fromState.Body.Variables.Clear();
        fromState.Body.InitLocals = true;
        fromState.Body.Variables.Add(new Local(module.CorLibTypes.Int32));

        var il = fromState.Body.Instructions;
        var hasList = Instruction.Create(OpCodes.Nop);
        var fallback = Instruction.Create(OpCodes.Nop);
        var retNull = Instruction.Create(OpCodes.Nop);

        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldstr, "_translator"));
        il.Add(Instruction.Create(OpCodes.Call, luaGetGlobal));
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldc_I4_M1));
        il.Add(Instruction.Create(OpCodes.Call, luaToNumber));
        il.Add(Instruction.Create(OpCodes.Conv_I4));
        il.Add(Instruction.Create(OpCodes.Stloc_0));
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Add(Instruction.Create(OpCodes.Call, luaPop));

        il.Add(Instruction.Create(OpCodes.Ldsfld, listField));
        il.Add(Instruction.Create(OpCodes.Brtrue_S, hasList));
        il.Add(Instruction.Create(OpCodes.Ldnull));
        il.Add(Instruction.Create(OpCodes.Ret));

        il.Add(hasList);
        il.Add(Instruction.Create(OpCodes.Ldloc_0));
        il.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Add(Instruction.Create(OpCodes.Blt_S, fallback));
        il.Add(Instruction.Create(OpCodes.Ldloc_0));
        il.Add(Instruction.Create(OpCodes.Ldsfld, listField));
        il.Add(Instruction.Create(OpCodes.Callvirt, listGetCount));
        il.Add(Instruction.Create(OpCodes.Bge_S, fallback));
        il.Add(Instruction.Create(OpCodes.Ldsfld, listField));
        il.Add(Instruction.Create(OpCodes.Ldloc_0));
        il.Add(Instruction.Create(OpCodes.Callvirt, listGetItem));
        il.Add(Instruction.Create(OpCodes.Ret));

        il.Add(fallback);
        il.Add(Instruction.Create(OpCodes.Ldsfld, listField));
        il.Add(Instruction.Create(OpCodes.Callvirt, listGetCount));
        il.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Add(Instruction.Create(OpCodes.Ble_S, retNull));
        il.Add(Instruction.Create(OpCodes.Ldsfld, listField));
        il.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Add(Instruction.Create(OpCodes.Callvirt, listGetItem));
        il.Add(Instruction.Create(OpCodes.Ret));

        il.Add(retNull);
        il.Add(Instruction.Create(OpCodes.Ldnull));
        il.Add(Instruction.Create(OpCodes.Ret));

        fromState.Body.SimplifyBranches();
        fromState.Body.OptimizeBranches();
        return true;
    }

    static bool PatchLuaScriptMgrPushVarObjectTranslatorGuard(ModuleDef module)
    {
        var scriptMgr = module.Types.FirstOrDefault(t => t.FullName == "LuaScriptMgr");
        var luaDllType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaDLL");
        if (scriptMgr == null || luaDllType == null)
            return false;

        var pushVarObject = scriptMgr.Methods.FirstOrDefault(m =>
            m.Name == "PushVarObject" &&
            m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[0].Type.FullName == "System.IntPtr" &&
            m.Parameters[1].Type.FullName == "System.Object");
        if (pushVarObject == null || pushVarObject.Body == null)
            return false;

        var getTranslator = scriptMgr.Methods.FirstOrDefault(m =>
            m.Name == "GetTranslator" &&
            m.IsStatic &&
            m.Parameters.Count == 1 &&
            m.Parameters[0].Type.FullName == "System.IntPtr");
        if (getTranslator == null)
            return false;

        var luaPushNil = luaDllType.Methods.FirstOrDefault(m =>
            m.Name == "lua_pushnil" &&
            m.IsStatic &&
            m.Parameters.Count == 1 &&
            m.Parameters[0].Type.FullName == "System.IntPtr");
        if (luaPushNil == null)
            return false;

        var instrs = pushVarObject.Body.Instructions;
        bool changed = false;

        for (int i = 0; i < instrs.Count - 6; i++)
        {
            if (instrs[i].OpCode != OpCodes.Call || !Equals(instrs[i].Operand, getTranslator))
                continue;

            // Pattern:
            // call GetTranslator
            // ldarg.0
            // ldarg.1
            // castclass LuaInterface.LuaCSFunction
            // callvirt ObjectTranslator::pushFunction
            // br ...
            if (!instrs[i + 1].IsLdarg() ||
                instrs[i + 1].GetParameter(pushVarObject.Parameters)?.Index != 0)
                continue;
            if (!instrs[i + 2].IsLdarg() ||
                instrs[i + 2].GetParameter(pushVarObject.Parameters)?.Index != 1)
                continue;
            if (instrs[i + 3].OpCode != OpCodes.Castclass ||
                instrs[i + 3].Operand is not ITypeDefOrRef castType ||
                castType.FullName != "LuaInterface.LuaCSFunction")
                continue;
            if (instrs[i + 4].OpCode != OpCodes.Callvirt ||
                instrs[i + 4].Operand is not IMethod pushFunc ||
                pushFunc.Name != "pushFunction" ||
                pushFunc.DeclaringType.FullName != "LuaInterface.ObjectTranslator")
                continue;
            if ((instrs[i + 5].OpCode != OpCodes.Br && instrs[i + 5].OpCode != OpCodes.Br_S) ||
                instrs[i + 5].Operand is not Instruction branchTarget)
                continue;

            // Idempotent: already guarded if there is a dup+brtrue sequence immediately after GetTranslator.
            if (i + 2 < instrs.Count &&
                instrs[i + 1].OpCode == OpCodes.Dup &&
                instrs[i + 2].OpCode == OpCodes.Brtrue_S)
            {
                continue;
            }

            var hasTranslator = instrs[i + 1];
            var guard = new[]
            {
                Instruction.Create(OpCodes.Dup),
                Instruction.Create(OpCodes.Brtrue_S, hasTranslator),
                Instruction.Create(OpCodes.Pop),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Call, luaPushNil),
                Instruction.Create(OpCodes.Br_S, branchTarget),
            };

            for (int g = 0; g < guard.Length; g++)
                instrs.Insert(i + 1 + g, guard[g]);

            changed = true;
            break;
        }

        if (!changed)
            return false;

        pushVarObject.Body.SimplifyBranches();
        pushVarObject.Body.OptimizeBranches();
        return true;
    }

    static bool PatchLuaScriptMgrPushTraceBackSafe(ModuleDef module)
    {
        var scriptMgr = module.Types.FirstOrDefault(t => t.FullName == "LuaScriptMgr");
        var luaDllType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaDLL");
        if (scriptMgr == null || luaDllType == null)
            return false;

        var method = scriptMgr.Methods.FirstOrDefault(m =>
            m.Name == "PushTraceBack" &&
            m.IsStatic &&
            m.Parameters.Count == 1 &&
            m.Parameters[0].Type.FullName == "System.IntPtr");
        if (method == null || method.Body == null)
            return false;

        var luaGetGlobal = luaDllType.Methods.FirstOrDefault(m =>
            m.Name == "lua_getglobal" &&
            m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[0].Type.FullName == "System.IntPtr" &&
            m.Parameters[1].Type.FullName == "System.String");
        if (luaGetGlobal == null)
            return false;

        // Idempotent: already rewritten to direct global lookup.
        if (method.Body.Instructions.Count <= 6 &&
            method.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is IMethod im &&
                im.Name == "lua_getglobal") &&
            !method.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Ldsfld || i.OpCode == OpCodes.Ldfld) &&
                i.Operand is IField f &&
                f.Name == "traceback"))
        {
            return false;
        }

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "traceback"));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, luaGetGlobal));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        method.Body.SimplifyBranches();
        method.Body.OptimizeBranches();
        return true;
    }

    static bool PatchLuaScriptMgrDisableGetLuaFunctionCache(ModuleDef module)
    {
        var scriptMgr = module.Types.FirstOrDefault(t => t.FullName == "LuaScriptMgr");
        if (scriptMgr == null)
            return false;

        var method = scriptMgr.Methods.FirstOrDefault(m =>
            m.Name == "GetLuaFunction" &&
            m.Parameters.Any(p => p.Type.FullName == "System.String"));
        if (method == null || method.Body == null)
            return false;

        var instrs = method.Body.Instructions;
        for (int i = 0; i < instrs.Count - 1; i++)
        {
            if ((instrs[i].OpCode != OpCodes.Call && instrs[i].OpCode != OpCodes.Callvirt) ||
                instrs[i].Operand is not IMethod called ||
                called.Name != "TryGetValue")
            {
                continue;
            }

            int branchIdx = -1;
            for (int j = i + 1; j <= Math.Min(i + 4, instrs.Count - 1); j++)
            {
                var op = instrs[j].OpCode;
                if ((op == OpCodes.Brtrue || op == OpCodes.Brtrue_S || op == OpCodes.Brfalse || op == OpCodes.Brfalse_S) &&
                    instrs[j].Operand is Instruction)
                {
                    branchIdx = j;
                    break;
                }
            }

            if (branchIdx < 0)
                continue;

            // Idempotent for the "force miss" form: pop TryGetValue result and push false.
            if (instrs[branchIdx].OpCode == OpCodes.Brfalse || instrs[branchIdx].OpCode == OpCodes.Brfalse_S)
            {
                if (branchIdx >= 2 &&
                    instrs[branchIdx - 2].OpCode == OpCodes.Pop &&
                    instrs[branchIdx - 1].OpCode == OpCodes.Ldc_I4_0)
                {
                    return false;
                }
            }

            // Common shape: branch on cache-hit. Flip it to branch on cache-miss.
            if (instrs[branchIdx].OpCode == OpCodes.Brtrue)
            {
                instrs[branchIdx].OpCode = OpCodes.Brfalse;
                method.Body.SimplifyBranches();
                method.Body.OptimizeBranches();
                return true;
            }
            if (instrs[branchIdx].OpCode == OpCodes.Brtrue_S)
            {
                instrs[branchIdx].OpCode = OpCodes.Brfalse_S;
                method.Body.SimplifyBranches();
                method.Body.OptimizeBranches();
                return true;
            }

            // If original method already uses brfalse, force the miss path explicitly.
            if (instrs[branchIdx].OpCode == OpCodes.Brfalse || instrs[branchIdx].OpCode == OpCodes.Brfalse_S)
            {
                instrs.Insert(branchIdx, Instruction.Create(OpCodes.Pop));
                instrs.Insert(branchIdx + 1, Instruction.Create(OpCodes.Ldc_I4_0));
                method.Body.SimplifyBranches();
                method.Body.OptimizeBranches();
                return true;
            }
        }

        return false;
    }

    static bool PatchLuaFunctionEnsureTranslator(ModuleDef module)
    {
        var luaFunctionType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaFunction");
        var luaBaseType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaBase");
        var translatorType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.ObjectTranslator");
        if (luaFunctionType == null || luaBaseType == null || translatorType == null)
            return false;

        var callMethod = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "call" &&
            !m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[1].Type.FullName == "System.Object[]" &&
            m.Parameters[2].Type.FullName == "System.Type[]");
        if (callMethod == null || callMethod.Body == null)
            return false;

        var translatorField = luaBaseType.Fields.FirstOrDefault(f => f.Name == "translator" && f.FieldType.FullName == "LuaInterface.ObjectTranslator");
        var lField = luaFunctionType.Fields.FirstOrDefault(f => f.Name == "L" && f.FieldType.FullName == "System.IntPtr");
        var fromState = translatorType.Methods.FirstOrDefault(m =>
            m.Name == "FromState" &&
            m.IsStatic &&
            m.Parameters.Count == 1 &&
            m.Parameters[0].Type.FullName == "System.IntPtr");
        var listField = translatorType.Fields.FirstOrDefault(f =>
            f.Name == "list" &&
            f.IsStatic &&
            f.FieldType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>"));
        var listGetItem = fromState?.Body?.Instructions
            .Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im =>
                im.Name == "get_Item" &&
                im.DeclaringType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>"));
        if (translatorField == null || lField == null || fromState == null)
            return false;

        var ensureMethod = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "EnsureTranslatorForCall" &&
            !m.IsStatic &&
            m.Parameters.Count == 1 &&
            m.ReturnType.FullName == "LuaInterface.ObjectTranslator");

        bool createdEnsure = false;
        if (ensureMethod == null)
        {
            ensureMethod = new MethodDefUser(
                "EnsureTranslatorForCall",
                MethodSig.CreateInstance(translatorType.ToTypeSig()),
                MethodImplAttributes.IL | MethodImplAttributes.Managed,
                MethodAttributes.Private | MethodAttributes.HideBySig);

            var body = new CilBody { InitLocals = false };
            ensureMethod.Body = body;

            var lRet = Instruction.Create(OpCodes.Ldarg_0);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, translatorField));
            body.Instructions.Add(Instruction.Create(OpCodes.Brtrue_S, lRet));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, lField));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, fromState));
            body.Instructions.Add(Instruction.Create(OpCodes.Stfld, translatorField));

            if (listField != null && listGetItem != null)
            {
                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, translatorField));
                body.Instructions.Add(Instruction.Create(OpCodes.Brtrue_S, lRet));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, listField));
                body.Instructions.Add(Instruction.Create(OpCodes.Brfalse_S, lRet));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, listField));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
                body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, listGetItem));
                body.Instructions.Add(Instruction.Create(OpCodes.Stfld, translatorField));
            }

            body.Instructions.Add(lRet);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, translatorField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));

            body.SimplifyBranches();
            body.OptimizeBranches();
            luaFunctionType.Methods.Add(ensureMethod);
            createdEnsure = true;
        }

        bool changedCall = false;
        foreach (var ins in callMethod.Body.Instructions)
        {
            if (ins.OpCode == OpCodes.Ldfld &&
                ins.Operand is IField f &&
                f.Name == "translator" &&
                f.DeclaringType.FullName == "LuaInterface.LuaBase")
            {
                ins.OpCode = OpCodes.Call;
                ins.Operand = ensureMethod;
                changedCall = true;
            }
        }

        if (changedCall)
        {
            callMethod.Body.SimplifyBranches();
            callMethod.Body.OptimizeBranches();
        }

        return createdEnsure || changedCall;
    }

    static bool PatchLuaFunctionPushSafe(ModuleDef module)
    {
        var luaFunctionType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaFunction");
        var luaBaseType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaBase");
        var luaStateType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaState");
        var luaDllType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaDLL");
        if (luaFunctionType == null || luaBaseType == null || luaStateType == null || luaDllType == null)
            return false;

        var pushWithState = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "push" &&
            !m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[1].Type.FullName == "System.IntPtr");
        var pushNoArg = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "push" &&
            !m.IsStatic &&
            m.Parameters.Count == 1);
        if (pushWithState == null || pushNoArg == null)
            return false;

        // Idempotent: method already patched if fallback calls lua_getglobal/lua_pushnil are present.
        bool alreadyPatched = false;
        if (pushWithState.Body != null)
        {
            alreadyPatched = pushWithState.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is IMethod im &&
                (im.Name == "lua_getglobal" || im.Name == "lua_pushnil"));
        }
        if (alreadyPatched)
            return false;

        var referenceField = luaBaseType.Fields.FirstOrDefault(f => f.Name == "_Reference" && f.FieldType.FullName == "System.Int32");
        var interpreterField = luaBaseType.Fields.FirstOrDefault(f => f.Name == "_Interpreter" && f.FieldType.FullName == "LuaInterface.LuaState");
        var nameField = luaBaseType.Fields.FirstOrDefault(f => f.Name == "name" && f.FieldType.FullName == "System.String");
        var lField = luaFunctionType.Fields.FirstOrDefault(f => f.Name == "L" && f.FieldType.FullName == "System.IntPtr");
        var functionField = luaFunctionType.Fields.FirstOrDefault(f => f.Name == "function" && f.FieldType.FullName == "LuaInterface.LuaCSFunction");
        if (referenceField == null || interpreterField == null || nameField == null || lField == null || functionField == null)
            return false;

        var pushCsFunction = luaStateType.Methods.FirstOrDefault(m =>
            m.Name == "pushCSFunction" &&
            !m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[1].Type.FullName == "LuaInterface.LuaCSFunction");
        var luaGetRef = luaDllType.Methods.FirstOrDefault(m =>
            m.Name == "lua_getref" &&
            m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[0].Type.FullName == "System.IntPtr" &&
            m.Parameters[1].Type.FullName == "System.Int32");
        var luaGetGlobal = luaDllType.Methods.FirstOrDefault(m =>
            m.Name == "lua_getglobal" &&
            m.IsStatic &&
            m.Parameters.Count == 2 &&
            m.Parameters[0].Type.FullName == "System.IntPtr" &&
            m.Parameters[1].Type.FullName == "System.String");
        var luaPushNil = luaDllType.Methods.FirstOrDefault(m =>
            m.Name == "lua_pushnil" &&
            m.IsStatic &&
            m.Parameters.Count == 1 &&
            m.Parameters[0].Type.FullName == "System.IntPtr");
        var stringIsNullOrEmpty = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im => im.FullName == "System.Boolean System.String::IsNullOrEmpty(System.String)");
        if (pushCsFunction == null || luaGetRef == null || luaGetGlobal == null || luaPushNil == null || stringIsNullOrEmpty == null)
            return false;

        RewritePushMethod(pushWithState, referenceField, interpreterField, nameField, lField, functionField, pushCsFunction, luaGetRef, luaGetGlobal, luaPushNil, stringIsNullOrEmpty, useStateArg: true);
        RewritePushMethod(pushNoArg, referenceField, interpreterField, nameField, lField, functionField, pushCsFunction, luaGetRef, luaGetGlobal, luaPushNil, stringIsNullOrEmpty, useStateArg: false);
        return true;
    }

    static bool PatchLuaFunctionPopValuesSafe(ModuleDef module)
    {
        var luaFunctionType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaFunction");
        var translatorType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.ObjectTranslator");
        if (luaFunctionType == null || translatorType == null)
            return false;

        var callMethod = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "call" &&
            !m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[1].Type.FullName == "System.Object[]" &&
            m.Parameters[2].Type.FullName == "System.Type[]");
        if (callMethod == null || callMethod.Body == null)
            return false;

        var ensureMethod = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "EnsureTranslatorForCall" &&
            !m.IsStatic &&
            m.Parameters.Count == 1 &&
            m.ReturnType.FullName == "LuaInterface.ObjectTranslator");
        if (ensureMethod == null)
            return false;

        var lField = luaFunctionType.Fields.FirstOrDefault(f => f.Name == "L" && f.FieldType.FullName == "System.IntPtr");
        var popValuesNoTypes = translatorType.Methods.FirstOrDefault(m =>
            m.Name == "popValues" &&
            !m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[1].Type.FullName == "System.IntPtr" &&
            m.Parameters[2].Type.FullName == "System.Int32");
        var popValuesWithTypes = translatorType.Methods.FirstOrDefault(m =>
            m.Name == "popValues" &&
            !m.IsStatic &&
            m.Parameters.Count == 4 &&
            m.Parameters[1].Type.FullName == "System.IntPtr" &&
            m.Parameters[2].Type.FullName == "System.Int32" &&
            m.Parameters[3].Type.FullName == "System.Type[]");
        if (lField == null || popValuesNoTypes == null || popValuesWithTypes == null)
            return false;

        var popSafeMethod = luaFunctionType.Methods.FirstOrDefault(m =>
            m.Name == "PopValuesSafe" &&
            !m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[1].Type.FullName == "System.Int32" &&
            m.Parameters[2].Type.FullName == "System.Type[]" &&
            m.ReturnType.FullName == "System.Object[]");

        bool createdPopSafe = false;
        if (popSafeMethod == null)
        {
            var popSafeRetSig = callMethod.ReturnType ?? module.CorLibTypes.Object.ToSZArraySig();
            var intSig = module.CorLibTypes.Int32 ?? callMethod.Body.Variables.FirstOrDefault(v => v.Type.FullName == "System.Int32")?.Type;
            var returnTypesSig = callMethod.Parameters.Count > 2
                ? callMethod.Parameters[2].Type
                : module.CorLibTypes.Object.ToSZArraySig();
            if (popSafeRetSig == null || intSig == null || returnTypesSig == null)
                return false;
            popSafeMethod = new MethodDefUser(
                "PopValuesSafe",
                MethodSig.CreateInstance(popSafeRetSig, intSig, returnTypesSig),
                MethodImplAttributes.IL | MethodImplAttributes.Managed,
                MethodAttributes.Private | MethodAttributes.HideBySig);
            luaFunctionType.Methods.Add(popSafeMethod);
            createdPopSafe = true;
        }

        if (popSafeMethod.Body == null)
            popSafeMethod.Body = new CilBody();
        popSafeMethod.Body.Instructions.Clear();
        popSafeMethod.Body.ExceptionHandlers.Clear();
        popSafeMethod.Body.Variables.Clear();
        popSafeMethod.Body.InitLocals = true;

        var trLocal = new Local(translatorType.ToTypeSig());
        var popRetLocal = new Local(popSafeMethod.ReturnType);
        popSafeMethod.Body.Variables.Add(trLocal);
        popSafeMethod.Body.Variables.Add(popRetLocal);

        var il = popSafeMethod.Body.Instructions;

        var e0 = Instruction.Create(OpCodes.Ldarg_0);
        var e1 = Instruction.Create(OpCodes.Call, ensureMethod);
        var e2 = Instruction.Create(OpCodes.Stloc_0);
        var e3 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

        var ec0 = Instruction.Create(OpCodes.Pop);
        var ec1 = Instruction.Create(OpCodes.Ldnull);
        var ec2 = Instruction.Create(OpCodes.Stloc_0);
        var ec3 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

        var afterEnsure = Instruction.Create(OpCodes.Ldloc_0);
        var hasTranslator = Instruction.Create(OpCodes.Nop);
        var callTyped = Instruction.Create(OpCodes.Nop);
        var popTryStart = Instruction.Create(OpCodes.Nop);
        var retPoint = Instruction.Create(OpCodes.Ldloc_1);
        var retIns = Instruction.Create(OpCodes.Ret);

        var p0 = Instruction.Create(OpCodes.Ldarg_2);
        var p1 = Instruction.Create(OpCodes.Brtrue_S, callTyped);
        var p2 = Instruction.Create(OpCodes.Ldloc_0);
        var p3 = Instruction.Create(OpCodes.Ldarg_0);
        var p4 = Instruction.Create(OpCodes.Ldfld, lField);
        var p5 = Instruction.Create(OpCodes.Ldarg_1);
        var p6 = Instruction.Create(OpCodes.Callvirt, popValuesNoTypes);
        var p7 = Instruction.Create(OpCodes.Stloc_1);
        var p8 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

        var pt0 = callTyped;
        var pt1 = Instruction.Create(OpCodes.Ldloc_0);
        var pt2 = Instruction.Create(OpCodes.Ldarg_0);
        var pt3 = Instruction.Create(OpCodes.Ldfld, lField);
        var pt4 = Instruction.Create(OpCodes.Ldarg_1);
        var pt5 = Instruction.Create(OpCodes.Ldarg_2);
        var pt6 = Instruction.Create(OpCodes.Callvirt, popValuesWithTypes);
        var pt7 = Instruction.Create(OpCodes.Stloc_1);
        var pt8 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

        var pc0 = Instruction.Create(OpCodes.Pop);
        var pc1 = Instruction.Create(OpCodes.Ldc_I4_0);
        var pc2 = Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Object.TypeDefOrRef);
        var pc3 = Instruction.Create(OpCodes.Stloc_1);
        var pc4 = Instruction.Create(OpCodes.Leave_S, (Instruction)null!);

        e3.Operand = afterEnsure;
        ec3.Operand = afterEnsure;
        p8.Operand = retPoint;
        pt8.Operand = retPoint;
        pc4.Operand = retPoint;

        il.Add(e0);
        il.Add(e1);
        il.Add(e2);
        il.Add(e3);
        il.Add(ec0);
        il.Add(ec1);
        il.Add(ec2);
        il.Add(ec3);

        il.Add(afterEnsure);
        il.Add(Instruction.Create(OpCodes.Brtrue_S, hasTranslator));
        il.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Object.TypeDefOrRef));
        il.Add(Instruction.Create(OpCodes.Stloc_1));
        il.Add(Instruction.Create(OpCodes.Br_S, retPoint));

        il.Add(hasTranslator);
        il.Add(popTryStart);
        il.Add(p0);
        il.Add(p1);
        il.Add(p2);
        il.Add(p3);
        il.Add(p4);
        il.Add(p5);
        il.Add(p6);
        il.Add(p7);
        il.Add(p8);
        il.Add(pt0);
        il.Add(pt1);
        il.Add(pt2);
        il.Add(pt3);
        il.Add(pt4);
        il.Add(pt5);
        il.Add(pt6);
        il.Add(pt7);
        il.Add(pt8);
        il.Add(pc0);
        il.Add(pc1);
        il.Add(pc2);
        il.Add(pc3);
        il.Add(pc4);
        il.Add(retPoint);
        il.Add(retIns);

        var exType = module.CorLibTypes.GetTypeRef("System", "Exception");
        popSafeMethod.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = e0,
            TryEnd = ec0,
            HandlerStart = ec0,
            HandlerEnd = afterEnsure,
            CatchType = exType
        });
        popSafeMethod.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = popTryStart,
            TryEnd = pc0,
            HandlerStart = pc0,
            HandlerEnd = retPoint,
            CatchType = exType
        });

        popSafeMethod.Body.SimplifyBranches();
        popSafeMethod.Body.OptimizeBranches();
        bool rewrotePopSafe = true;

        // Idempotent on caller side.
        if (callMethod.Body.Instructions.Any(i =>
            (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
            i.Operand is IMethod im &&
            im.DeclaringType.FullName == "LuaInterface.LuaFunction" &&
            im.Name == "PopValuesSafe"))
        {
            return createdPopSafe || rewrotePopSafe;
        }

        var instrs = callMethod.Body.Instructions;
        int startIdx = -1;
        int firstPopIdx = -1;
        int secondPopIdx = -1;
        int endIdx = -1;
        Local oldTopLocal = null!;
        Local retLocal = null!;

        // find oldTop local from lua_gettop storage
        for (int i = 0; i < instrs.Count - 1; i++)
        {
            if (instrs[i].OpCode == OpCodes.Call &&
                instrs[i].Operand is IMethod im &&
                im.Name == "lua_gettop" &&
                instrs[i + 1].IsStloc())
            {
                oldTopLocal = instrs[i + 1].GetLocal(callMethod.Body.Variables);
                break;
            }
        }

        for (int i = 0; i < instrs.Count; i++)
        {
            if ((instrs[i].OpCode == OpCodes.Callvirt || instrs[i].OpCode == OpCodes.Call) &&
                instrs[i].Operand is IMethod im &&
                im.DeclaringType.FullName == "LuaInterface.ObjectTranslator" &&
                im.Name == "popValues")
            {
                if (firstPopIdx < 0) firstPopIdx = i;
                else { secondPopIdx = i; break; }
            }
        }

        if (firstPopIdx > 0)
        {
            for (int i = firstPopIdx; i >= 1; i--)
            {
                if (instrs[i].OpCode == OpCodes.Ldarg_2 &&
                    (instrs[i + 1].OpCode == OpCodes.Brtrue || instrs[i + 1].OpCode == OpCodes.Brtrue_S))
                {
                    startIdx = i;
                    break;
                }
            }
        }

        if (secondPopIdx > 0)
        {
            for (int i = secondPopIdx + 1; i < instrs.Count; i++)
            {
                if (instrs[i].IsStloc())
                {
                    endIdx = i;
                    retLocal = instrs[i].GetLocal(callMethod.Body.Variables);
                    break;
                }
            }
        }

        if (startIdx < 0 || endIdx < startIdx || oldTopLocal == null || retLocal == null)
            return createdPopSafe || rewrotePopSafe;

        if (endIdx + 1 >= instrs.Count)
            return createdPopSafe || rewrotePopSafe;

        var afterBlock = instrs[endIdx + 1];
        int slotCount = endIdx - startIdx + 1;
        if (slotCount < 6)
            return createdPopSafe || rewrotePopSafe;

        instrs[startIdx + 0].OpCode = OpCodes.Ldarg_0;
        instrs[startIdx + 0].Operand = null;

        instrs[startIdx + 1].OpCode = OpCodes.Ldloc;
        instrs[startIdx + 1].Operand = oldTopLocal;

        instrs[startIdx + 2].OpCode = OpCodes.Ldarg_2;
        instrs[startIdx + 2].Operand = null;

        instrs[startIdx + 3].OpCode = OpCodes.Call;
        instrs[startIdx + 3].Operand = popSafeMethod;

        instrs[startIdx + 4].OpCode = OpCodes.Stloc;
        instrs[startIdx + 4].Operand = retLocal;

        instrs[startIdx + 5].OpCode = OpCodes.Br_S;
        instrs[startIdx + 5].Operand = afterBlock;

        for (int i = startIdx + 6; i <= endIdx; i++)
        {
            instrs[i].OpCode = OpCodes.Nop;
            instrs[i].Operand = null;
        }

        callMethod.Body.SimplifyBranches();
        callMethod.Body.OptimizeBranches();
        return true;
    }

    static void RewritePushMethod(
        MethodDef method,
        FieldDef referenceField,
        FieldDef interpreterField,
        FieldDef nameField,
        FieldDef lField,
        FieldDef functionField,
        MethodDef pushCsFunction,
        MethodDef luaGetRef,
        MethodDef luaGetGlobal,
        MethodDef luaPushNil,
        IMethod stringIsNullOrEmpty,
        bool useStateArg)
    {
        if (method.Body == null)
            return;

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;

        var il = method.Body.Instructions;

        var labelPushCs = Instruction.Create(OpCodes.Nop);
        var labelNameCheck = Instruction.Create(OpCodes.Nop);
        var labelPushNil = Instruction.Create(OpCodes.Nop);
        var labelRet = Instruction.Create(OpCodes.Ret);

        // if (_Reference != 0) { lua_getref(L, _Reference); return; }
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, referenceField));
        il.Add(Instruction.Create(OpCodes.Brfalse_S, labelPushCs));
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, lField));
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, referenceField));
        il.Add(Instruction.Create(OpCodes.Call, luaGetRef));
        il.Add(Instruction.Create(OpCodes.Ret));

        // if (_Interpreter != null && function != null) { _Interpreter.pushCSFunction(function); return; }
        il.Add(labelPushCs);
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, interpreterField));
        il.Add(Instruction.Create(OpCodes.Brfalse_S, labelNameCheck));
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, functionField));
        il.Add(Instruction.Create(OpCodes.Brfalse_S, labelNameCheck));
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, interpreterField));
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, functionField));
        il.Add(Instruction.Create(OpCodes.Callvirt, pushCsFunction));
        il.Add(Instruction.Create(OpCodes.Ret));

        // if (!string.IsNullOrEmpty(name)) { lua_getglobal(state, name); return; }
        il.Add(labelNameCheck);
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, nameField));
        il.Add(Instruction.Create(OpCodes.Call, stringIsNullOrEmpty));
        il.Add(Instruction.Create(OpCodes.Brtrue_S, labelPushNil));
        if (useStateArg)
            il.Add(Instruction.Create(OpCodes.Ldarg_1));
        else
        {
            il.Add(Instruction.Create(OpCodes.Ldarg_0));
            il.Add(Instruction.Create(OpCodes.Ldfld, lField));
        }
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, nameField));
        il.Add(Instruction.Create(OpCodes.Call, luaGetGlobal));
        il.Add(Instruction.Create(OpCodes.Ret));

        // lua_pushnil(state)
        il.Add(labelPushNil);
        if (useStateArg)
            il.Add(Instruction.Create(OpCodes.Ldarg_1));
        else
        {
            il.Add(Instruction.Create(OpCodes.Ldarg_0));
            il.Add(Instruction.Create(OpCodes.Ldfld, lField));
        }
        il.Add(Instruction.Create(OpCodes.Call, luaPushNil));
        il.Add(labelRet);

        method.Body.SimplifyBranches();
        method.Body.OptimizeBranches();
    }

    static bool PatchProxyTypeGetMember(ModuleDef module)
    {
        var proxyType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.ProxyType");
        if (proxyType == null)
            return false;

        var getMember = proxyType.Methods.FirstOrDefault(m =>
            m.Name == "GetMember" &&
            !m.IsStatic &&
            m.Parameters.Count == 3 &&
            m.Parameters[1].Type.FullName == "System.String" &&
            m.Parameters[2].Type.FullName == "System.Reflection.BindingFlags");

        if (getMember == null || getMember.Body == null)
            return false;

        // Idempotent: method already contains String::Trim fallback.
        if (getMember.Body.Instructions.Any(i =>
            (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
            i.Operand is IMethod im &&
            im.DeclaringType.FullName == "System.String" &&
            im.Name == "Trim"))
        {
            return false;
        }

        var typeGetMember = getMember.Body.Instructions
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im =>
                im.DeclaringType.FullName == "System.Type" &&
                im.Name == "GetMember" &&
                im.MethodSig != null &&
                im.MethodSig.Params.Count == 2 &&
                im.MethodSig.Params[0].FullName == "System.String");
        var stringIsNullOrEmpty = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im => im.FullName == "System.Boolean System.String::IsNullOrEmpty(System.String)");
        var stringTrim = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im => im.FullName == "System.String System.String::Trim()");
        var stringOpInequality = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im => im.FullName == "System.Boolean System.String::op_Inequality(System.String,System.String)");

        var proxyField = proxyType.Fields.FirstOrDefault(f => f.Name == "proxy" && f.FieldType.FullName == "System.Type");
        if (typeGetMember == null || proxyField == null)
            return false;

        getMember.Body.Variables.Clear();
        getMember.Body.Instructions.Clear();
        getMember.Body.ExceptionHandlers.Clear();
        getMember.Body.InitLocals = true;

        var membersLocal = new Local(typeGetMember?.MethodSig?.RetType ?? module.CorLibTypes.Object.ToSZArraySig());
        var trimmedNameLocal = new Local(module.CorLibTypes.String);
        getMember.Body.Variables.Add(membersLocal);   // V_0
        getMember.Body.Variables.Add(trimmedNameLocal); // V_1

        // broadFallbackFlags = Public | NonPublic | Instance | Static | FlattenHierarchy | IgnoreCase
        const int broadFallbackFlags = 16 | 32 | 4 | 8 | 64 | 1; // 125

        var il = getMember.Body.Instructions;
        var labelRet = Instruction.Create(OpCodes.Ldloc_0);
        var labelTryBroadOriginalName = Instruction.Create(OpCodes.Nop);
        var labelBroadDoneCheck = Instruction.Create(OpCodes.Nop);

        // members = proxy.GetMember(name, flags)
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, proxyField));
        il.Add(Instruction.Create(OpCodes.Ldarg_1));
        il.Add(Instruction.Create(OpCodes.Ldarg_2));
        il.Add(Instruction.Create(OpCodes.Callvirt, typeGetMember));
        il.Add(Instruction.Create(OpCodes.Stloc_0));

        // if (members.Length != 0) return members;
        il.Add(Instruction.Create(OpCodes.Ldloc_0));
        il.Add(Instruction.Create(OpCodes.Ldlen));
        il.Add(Instruction.Create(OpCodes.Conv_I4));
        il.Add(Instruction.Create(OpCodes.Brtrue_S, labelRet));

        // if (string.IsNullOrEmpty(name)) return members;
        il.Add(Instruction.Create(OpCodes.Ldarg_1));
        il.Add(Instruction.Create(OpCodes.Call, stringIsNullOrEmpty));
        il.Add(Instruction.Create(OpCodes.Brtrue_S, labelRet));

        // trimmed = name.Trim()
        il.Add(Instruction.Create(OpCodes.Ldarg_1));
        il.Add(Instruction.Create(OpCodes.Callvirt, stringTrim));
        il.Add(Instruction.Create(OpCodes.Stloc_1));

        // if (trimmed != name) members = proxy.GetMember(trimmed, flags)
        il.Add(Instruction.Create(OpCodes.Ldloc_1));
        il.Add(Instruction.Create(OpCodes.Ldarg_1));
        il.Add(Instruction.Create(OpCodes.Call, stringOpInequality));
        il.Add(Instruction.Create(OpCodes.Brfalse_S, labelTryBroadOriginalName));

        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, proxyField));
        il.Add(Instruction.Create(OpCodes.Ldloc_1));
        il.Add(Instruction.Create(OpCodes.Ldarg_2));
        il.Add(Instruction.Create(OpCodes.Callvirt, typeGetMember));
        il.Add(Instruction.Create(OpCodes.Stloc_0));

        // if (members.Length != 0) return members;
        il.Add(Instruction.Create(OpCodes.Ldloc_0));
        il.Add(Instruction.Create(OpCodes.Ldlen));
        il.Add(Instruction.Create(OpCodes.Conv_I4));
        il.Add(Instruction.Create(OpCodes.Brtrue_S, labelRet));

        // broad flags fallback with trimmed name
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, proxyField));
        il.Add(Instruction.Create(OpCodes.Ldloc_1));
        il.Add(Instruction.Create(OpCodes.Ldarg_2));
        il.Add(Instruction.Create(OpCodes.Ldc_I4, broadFallbackFlags));
        il.Add(Instruction.Create(OpCodes.Or));
        il.Add(Instruction.Create(OpCodes.Callvirt, typeGetMember));
        il.Add(Instruction.Create(OpCodes.Stloc_0));
        il.Add(Instruction.Create(OpCodes.Br_S, labelBroadDoneCheck));

        // broad flags fallback with original name
        il.Add(labelTryBroadOriginalName);
        il.Add(Instruction.Create(OpCodes.Ldarg_0));
        il.Add(Instruction.Create(OpCodes.Ldfld, proxyField));
        il.Add(Instruction.Create(OpCodes.Ldarg_1));
        il.Add(Instruction.Create(OpCodes.Ldarg_2));
        il.Add(Instruction.Create(OpCodes.Ldc_I4, broadFallbackFlags));
        il.Add(Instruction.Create(OpCodes.Or));
        il.Add(Instruction.Create(OpCodes.Callvirt, typeGetMember));
        il.Add(Instruction.Create(OpCodes.Stloc_0));

        il.Add(labelBroadDoneCheck);
        il.Add(labelRet);
        il.Add(Instruction.Create(OpCodes.Ret));

        getMember.Body.SimplifyBranches();
        getMember.Body.OptimizeBranches();
        return true;
    }

    static bool PatchImportTypeForFileLogger(ModuleDef module)
    {
        var translator = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.ObjectTranslator");
        if (translator == null)
            return false;

        var method = translator.Methods.FirstOrDefault(m => m.Name == "importType" && m.IsStatic && m.Parameters.Count == 1);
        if (method == null || method.Body == null)
            return false;

        var fileLogger = module.Types.FirstOrDefault(t => t.FullName == "JyGame.FileLogger");
        if (fileLogger == null)
            return false;

        var instrs = method.Body.Instructions;
        bool hasFileLoggerToken = instrs.Any(i =>
            i.OpCode == OpCodes.Ldtoken &&
            i.Operand is ITypeDefOrRef tr &&
            tr.FullName == "JyGame.FileLogger");
        bool hasListCountGuard = instrs.Any(i =>
            i.OpCode == OpCodes.Callvirt &&
            i.Operand is IMethod im &&
            im.Name == "get_Count" &&
            im.DeclaringType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>"));
        // Idempotent only when both FileLogger fast-path and list count guard exist.
        if (hasFileLoggerToken && hasListCountGuard)
            return false;

        var fromState = translator.Methods.FirstOrDefault(m => m.Name == "FromState" && m.IsStatic && m.Parameters.Count == 1);
        var findType = translator.Methods.FirstOrDefault(m => m.Name == "FindType" && !m.IsStatic && m.Parameters.Count == 2);
        var pushType = translator.Methods.FirstOrDefault(m => m.Name == "pushType" && !m.IsStatic && m.Parameters.Count == 3);
        var listField = translator.Fields.FirstOrDefault(f => f.Name == "list" && f.IsStatic);
        var listGetItem = fromState?.Body?.Instructions
            .Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im => im.Name == "get_Item" && im.DeclaringType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>"));
        var listGetCount = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand!)
            .FirstOrDefault(im => im.Name == "get_Count" && im.DeclaringType.FullName.Contains("System.Collections.Generic.List`1<LuaInterface.ObjectTranslator>"));
        if (listGetCount == null && listGetItem != null)
        {
            listGetCount = new MemberRefUser(
                module,
                "get_Count",
                MethodSig.CreateInstance(module.CorLibTypes.Int32),
                listGetItem.DeclaringType);
        }
        if (fromState == null || findType == null || pushType == null || listField == null || listGetItem == null || listGetCount == null)
            return false;

        var luaDllType = module.Types.FirstOrDefault(t => t.FullName == "LuaInterface.LuaDLL");
        var luaToString = luaDllType?.Methods.FirstOrDefault(m => m.Name == "lua_tostring" && m.IsStatic && m.Parameters.Count == 2);
        var luaPushNil = luaDllType?.Methods.FirstOrDefault(m => m.Name == "lua_pushnil" && m.IsStatic && m.Parameters.Count == 1);
        if (luaToString == null || luaPushNil == null)
            return false;

        // Reuse existing mscorlib method refs from the target assembly.
        // Do not call module.Import(typeof(...)) here, otherwise dnlib may bind
        // to System.Private.CoreLib on .NET SDK host and break Unity/Mono runtime.
        IMethod typeGetTypeFromHandle = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im => im.FullName == "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)");

        IMethod stringOpEquality = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.Body != null)
            .SelectMany(m => m.Body!.Instructions)
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault(im => im.FullName == "System.Boolean System.String::op_Equality(System.String,System.String)");

        if (typeGetTypeFromHandle == null || stringOpEquality == null)
            return false;
        method.Body.Variables.Clear();
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.InitLocals = true;

        method.Body.Variables.Add(new Local(translator.ToTypeSig()));                 // V_0 translator
        method.Body.Variables.Add(new Local(module.CorLibTypes.String));              // V_1 name
        method.Body.Variables.Add(new Local(module.CorLibTypes.GetTypeRef("System", "Type").ToTypeSig())); // V_2 type

        var labelHasTranslator = Instruction.Create(OpCodes.Nop);
        var labelHasName = Instruction.Create(OpCodes.Nop);
        var labelNormalLookup = Instruction.Create(OpCodes.Nop);
        var labelTranslatorReady = Instruction.Create(OpCodes.Nop);
        var labelAfterLookup = Instruction.Create(OpCodes.Nop);
        var labelPushNil = Instruction.Create(OpCodes.Nop);
        var labelRet = Instruction.Create(OpCodes.Nop);

        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Call, fromState));
        instrs.Add(Instruction.Create(OpCodes.Stloc_0));

        // translator fallback: if FromState failed, try first translator in static list.
        instrs.Add(Instruction.Create(OpCodes.Ldloc_0));
        instrs.Add(Instruction.Create(OpCodes.Brtrue_S, labelHasTranslator));
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, listField));
        instrs.Add(Instruction.Create(OpCodes.Brfalse_S, labelHasTranslator));
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, listField));
        instrs.Add(Instruction.Create(OpCodes.Callvirt, listGetCount));
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        instrs.Add(Instruction.Create(OpCodes.Ble_S, labelHasTranslator));
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, listField));
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        instrs.Add(Instruction.Create(OpCodes.Callvirt, listGetItem));
        instrs.Add(Instruction.Create(OpCodes.Stloc_0));
        instrs.Add(labelHasTranslator);

        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        instrs.Add(Instruction.Create(OpCodes.Call, luaToString));
        instrs.Add(Instruction.Create(OpCodes.Stloc_1));

        // Null type-name from Lua => push nil instead of throwing.
        instrs.Add(Instruction.Create(OpCodes.Ldloc_1));
        instrs.Add(Instruction.Create(OpCodes.Brtrue_S, labelHasName));
        instrs.Add(Instruction.Create(OpCodes.Br_S, labelPushNil));
        instrs.Add(labelHasName);

        // if (name == "JyGame.FileLogger") type = typeof(JyGame.FileLogger); else type = translator.FindType(name)
        instrs.Add(Instruction.Create(OpCodes.Ldloc_1));
        instrs.Add(Instruction.Create(OpCodes.Ldstr, "JyGame.FileLogger"));
        instrs.Add(Instruction.Create(OpCodes.Call, stringOpEquality));
        instrs.Add(Instruction.Create(OpCodes.Brfalse_S, labelNormalLookup));
        instrs.Add(Instruction.Create(OpCodes.Ldtoken, fileLogger));
        instrs.Add(Instruction.Create(OpCodes.Call, typeGetTypeFromHandle));
        instrs.Add(Instruction.Create(OpCodes.Stloc_2));
        instrs.Add(Instruction.Create(OpCodes.Br_S, labelAfterLookup));

        instrs.Add(labelNormalLookup);
        instrs.Add(Instruction.Create(OpCodes.Ldloc_0));
        instrs.Add(Instruction.Create(OpCodes.Brtrue_S, labelTranslatorReady));
        instrs.Add(Instruction.Create(OpCodes.Br_S, labelPushNil));
        instrs.Add(labelTranslatorReady);
        instrs.Add(Instruction.Create(OpCodes.Ldloc_0));
        instrs.Add(Instruction.Create(OpCodes.Ldloc_1));
        instrs.Add(Instruction.Create(OpCodes.Callvirt, findType));
        instrs.Add(Instruction.Create(OpCodes.Stloc_2));

        instrs.Add(labelAfterLookup);
        instrs.Add(Instruction.Create(OpCodes.Ldloc_2));
        instrs.Add(Instruction.Create(OpCodes.Brfalse_S, labelPushNil));
        instrs.Add(Instruction.Create(OpCodes.Ldloc_0));
        instrs.Add(Instruction.Create(OpCodes.Brfalse_S, labelPushNil));
        instrs.Add(Instruction.Create(OpCodes.Ldloc_0));
        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Ldloc_2));
        instrs.Add(Instruction.Create(OpCodes.Callvirt, pushType));
        instrs.Add(Instruction.Create(OpCodes.Br_S, labelRet));

        instrs.Add(labelPushNil);
        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Call, luaPushNil));

        instrs.Add(labelRet);
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        instrs.Add(Instruction.Create(OpCodes.Ret));

        method.Body.SimplifyBranches();
        method.Body.OptimizeBranches();
        return true;
    }
}

