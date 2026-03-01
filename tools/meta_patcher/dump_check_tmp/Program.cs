using System;
using System.Linq;
using System.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

class X
{
    static void Main(string[] a)
    {
        var m = ModuleDefMD.Load(a[0]);
        var meta = m.Types.FirstOrDefault(t => t.FullName == "LuaInterface.MetaFunctions");
        if (meta != null)
        {
            var gm = meta.Methods.FirstOrDefault(mm => mm.Name == "getMember");
            if (gm?.Body != null)
            {
                int i = 0;
                foreach (var ins in gm.Body.Instructions)
                {
                    if ((ins.OpCode == OpCodes.Ldc_I4_S || ins.OpCode == OpCodes.Ldc_I4) && ins.GetLdcI4Value() == 48)
                        Console.WriteLine($"flag48 at {i}");
                    if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string st && st.Contains("unknown member name"))
                        Console.WriteLine($"unknown_member_ldstr_at={i} text={st}");
                    i++;
                }
                Console.WriteLine("getMember_window_260_320:");
                for (int w = 260; w <= 320 && w < gm.Body.Instructions.Count; w++)
                {
                    Console.WriteLine($"  {w:D3}: {gm.Body.Instructions[w]}");
                }
            }
        }

        var translator = m.Types.FirstOrDefault(t => t.FullName == "LuaInterface.ObjectTranslator");
        var importType = translator?.Methods.FirstOrDefault(mm => mm.Name == "importType");
        bool hasImportTypeFileLoggerBranch = importType?.Body?.Instructions.Any(ins =>
            ins.OpCode == OpCodes.Ldstr &&
            ins.Operand is string s &&
            s == "JyGame.FileLogger") == true;
        Console.WriteLine($"importType_has_FileLogger_branch={hasImportTypeFileLoggerBranch}");
        if (importType != null && importType.Body != null)
        {
            Console.WriteLine($"importType_method={importType.FullName}");
            int i2 = 0;
            foreach (var ins in importType.Body.Instructions.Take(80))
            {
                Console.WriteLine($"  {i2:D3}: {ins}");
                i2++;
            }
        }
        if (translator != null)
        {
            Console.WriteLine("ObjectTranslator_fields:");
            foreach (var f in translator.Fields)
            {
                Console.WriteLine($"  {f.FieldType.FullName} {f.Name} static={f.IsStatic}");
            }

            var fromState = translator.Methods.FirstOrDefault(mm => mm.Name == "FromState");
            if (fromState != null && fromState.Body != null)
            {
                Console.WriteLine($"FromState_method={fromState.FullName}");
                int i4 = 0;
                foreach (var ins in fromState.Body.Instructions.Take(120))
                {
                    Console.WriteLine($"  {i4:D3}: {ins}");
                    i4++;
                }
            }

            var findType = translator.Methods.FirstOrDefault(mm => mm.Name == "FindType" && mm.Parameters.Count == 2);
            if (findType != null && findType.Body != null)
            {
                Console.WriteLine($"FindType_method={findType.FullName}");
                int i3 = 0;
                foreach (var ins in findType.Body.Instructions.Take(120))
                {
                    Console.WriteLine($"  {i3:D3}: {ins}");
                    i3++;
                }
            }

            var throwError = translator.Methods.FirstOrDefault(mm => mm.Name == "throwError");
            if (throwError != null && throwError.Body != null)
            {
                Console.WriteLine($"throwError_method={throwError.FullName}");
                int i5 = 0;
                foreach (var ins in throwError.Body.Instructions.Take(120))
                {
                    Console.WriteLine($"  {i5:D3}: {ins}");
                    i5++;
                }
            }
        }

        var fileLogger = m.Types.FirstOrDefault(t => t.FullName == "JyGame.FileLogger");
        Console.WriteLine($"FileLogger_type_found={fileLogger != null}");
        if (fileLogger != null)
        {
            Console.WriteLine("FileLogger_fields:");
            foreach (var f in fileLogger.Fields)
            {
                Console.WriteLine($"  field {f.Name} static={f.IsStatic} public={f.IsPublic} famOrAssem={f.IsFamilyOrAssembly} fam={f.IsFamily} assembly={f.IsAssembly} private={f.IsPrivate}");
            }

            Console.WriteLine("FileLogger_properties:");
            foreach (var p in fileLogger.Properties)
            {
                Console.WriteLine($"  prop {p.Name}");
            }
        }

        var proxyType = m.Types.FirstOrDefault(t => t.FullName == "LuaInterface.ProxyType");
        Console.WriteLine($"ProxyType_found={proxyType != null}");
        if (proxyType != null)
        {
            foreach (var pm in proxyType.Methods.Where(mm => mm.Name == "GetMember" || mm.Name == "get_UnderlyingSystemType"))
            {
                Console.WriteLine($"ProxyType_method={pm.FullName}");
                if (pm.Body != null)
                {
                    int idx = 0;
                    foreach (var ins in pm.Body.Instructions.Take(40))
                    {
                        Console.WriteLine($"  {idx:D3}: {ins}");
                        idx++;
                    }
                }
            }
        }

        Console.WriteLine("Refs:");
        foreach (var r in m.GetAssemblyRefs())
            Console.WriteLine(r.FullName);

        if (a.Length > 1 && Directory.Exists(a[1]))
        {
            Console.WriteLine("ScanManagedForJyGame.FileLogger:");
            foreach (var f in Directory.GetFiles(a[1], "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var mm = ModuleDefMD.Load(f);
                    var tt = mm.Types.FirstOrDefault(t => t.FullName == "JyGame.FileLogger");
                    if (tt != null)
                    {
                        var hasInstance = tt.Fields.Any(ff => ff.Name == "instance");
                        Console.WriteLine($"  {Path.GetFileName(f)} hasType=true hasField_instance={hasInstance}");
                    }
                    mm.Dispose();
                }
                catch
                {
                }
            }

            Console.WriteLine("ScanManagedForLuaTypes:");
            foreach (var f in Directory.GetFiles(a[1], "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var mm = ModuleDefMD.Load(f);
                    bool hasLuaFunc = mm.Types.Any(t => t.FullName == "LuaInterface.LuaFunction");
                    bool hasMeta = mm.Types.Any(t => t.FullName == "LuaInterface.MetaFunctions");
                    bool hasTranslator = mm.Types.Any(t => t.FullName == "LuaInterface.ObjectTranslator");
                    bool hasScriptMgr = mm.Types.Any(t => t.FullName == "LuaScriptMgr");
                    if (hasLuaFunc || hasMeta || hasTranslator || hasScriptMgr)
                    {
                        Console.WriteLine($"  {Path.GetFileName(f)} LuaFunction={hasLuaFunc} MetaFunctions={hasMeta} ObjectTranslator={hasTranslator} LuaScriptMgr={hasScriptMgr}");
                    }
                    mm.Dispose();
                }
                catch
                {
                }
            }
        }

        Console.WriteLine("AllMethodsWith_unknown_member_name:");
        foreach (var tt in m.GetTypes())
        {
            foreach (var mm in tt.Methods)
            {
                if (mm.Body == null) continue;
                if (mm.Body.Instructions.Any(ins =>
                    ins.OpCode == OpCodes.Ldstr &&
                    ins.Operand is string s &&
                    s == "unknown member name "))
                {
                    Console.WriteLine($"  {mm.FullName}");
                }
            }
        }

        var cxs = m.Types.FirstOrDefault(t => t.FullName == "JyGame.CustomXmlSerializer");
        if (cxs != null)
        {
            var gm2 = cxs.Methods.FirstOrDefault(mm => mm.Name == "GetMembers");
            if (gm2 != null && gm2.Body != null)
            {
                Console.WriteLine($"CustomXmlSerializer.GetMembers={gm2.FullName}");
                int j = 0;
                foreach (var ins in gm2.Body.Instructions.Take(220))
                {
                    Console.WriteLine($"  {j:D3}: {ins}");
                    j++;
                }
            }
        }

        DumpMethodIL(m, "LuaInterface.LuaFunction", "call", 200);
        DumpMethodIL(m, "LuaInterface.LuaFunction", "Call", 160);
        DumpMethodIL(m, "LuaInterface.LuaFunction", "PopValuesSafe", 200);
        DumpMethodIL(m, "LuaInterface.LuaFunction", "EnsureTranslatorForCall", 120);
        DumpMethodIL(m, "LuaInterface.LuaFunction", "push", 120);
        DumpMethodIL(m, "LuaInterface.LuaFunction", "BeginPCall", 120);
        DumpMethodIL(m, "LuaInterface.LuaBase", "PushArgs", 160);
        DumpMethodIL(m, "LuaScriptMgr", "PushTraceBack", 160);
        DumpMethodIL(m, "LuaScriptMgr", "GetLuaFunction", 220);
        DumpMethodIL(m, "LuaScriptMgr", "PushVarObject", 260);
        DumpMethodIL(m, "LuaScriptMgr", "GetTranslator", 220);
        DumpMethodsReferencingField(m, "LuaScriptMgr", "traceback");
        DumpMethodIL(m, "LuaInterface.LuaState", "GetFunction", 220);
        DumpTypeSummary(m, "LuaInterface.LuaFunction");
        DumpTypeSummary(m, "LuaInterface.LuaBase");
        DumpTypeSummary(m, "LuaInterface.ObjectTranslator");
        DumpMethodIL(m, "JyGame.LuaManager", "Call", 220);
        DumpMethodIL(m, "JyGame.TriggerLogic", "InitluaExtensionConditions", 220);
        DumpMethodIL(m, "JyGame.LuaManager", "init_luaConfig", 220);
    }

    static void DumpTypeSummary(ModuleDefMD m, string typeName)
    {
        var t = m.Types.FirstOrDefault(tt => tt.FullName == typeName);
        if (t == null) return;
        Console.WriteLine($"TYPE {t.FullName}");
        foreach (var f in t.Fields)
            Console.WriteLine($"  FIELD {f.FieldType.FullName} {f.Name} static={f.IsStatic}");
        foreach (var mm in t.Methods)
            Console.WriteLine($"  METHOD {mm.FullName}");
    }

    static void DumpMethodsReferencingField(ModuleDefMD m, string typeName, string fieldName)
    {
        var t = m.Types.FirstOrDefault(tt => tt.FullName == typeName);
        if (t == null) return;
        Console.WriteLine($"FIELDREF {typeName}::{fieldName}");
        foreach (var tt in m.GetTypes())
        {
            foreach (var mm in tt.Methods)
            {
                if (mm.Body == null) continue;
                bool hit = mm.Body.Instructions.Any(ins =>
                    (ins.OpCode == OpCodes.Ldfld || ins.OpCode == OpCodes.Ldsfld || ins.OpCode == OpCodes.Stfld || ins.OpCode == OpCodes.Stsfld) &&
                    ins.Operand is IField f &&
                    f.Name == fieldName &&
                    f.DeclaringType.FullName == typeName);
                if (hit)
                    Console.WriteLine($"  {mm.FullName}");
            }
        }
    }

    static void DumpMethodIL(ModuleDefMD m, string typeName, string methodName, int maxIns)
    {
        var t = m.Types.FirstOrDefault(tt => tt.FullName == typeName);
        if (t == null) return;
        foreach (var mm in t.Methods.Where(x => x.Name == methodName))
        {
            if (mm.Body == null) continue;
            Console.WriteLine($"DUMP {mm.FullName}");
            int k = 0;
            foreach (var ins in mm.Body.Instructions.Take(maxIns))
            {
                Console.WriteLine($"  {k:D3}: {ins}");
                k++;
            }
        }
    }
}
