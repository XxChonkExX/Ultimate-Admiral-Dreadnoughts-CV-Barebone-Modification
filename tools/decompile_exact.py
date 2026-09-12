# -*- coding: utf-8 -*-
# Decompile exact VA targets (dump.cs-verified), write to file.
targets = {
    "LoadModel_Part": 0x1807F53D0,
    "LoadUnloadBattle_bool": 0x1807F16F0,
    "AddPart": 0x181F8D290,
    "StatEffectPrivate": 0x181F98980,
    "RefreshHullStats": 0x181F87220,
    "LoadUnloadBattle_store": 0x181F81580,
    "CreateSectionsForShip": 0x181F34130,
    "CreateVisualForSections": 0x181F308E0,
}

args = getScriptArgs()
outPath = args[0] if args else "targets3.c"

from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
di = DecompInterface()
di.openProgram(currentProgram)
tm = ConsoleTaskMonitor()

preferred = currentProgram.getImageBase().getOffset()
addrSpace = currentProgram.getAddressFactory().getDefaultAddressSpace()
out = []
for name, va in targets.items():
    rva = va - 0x180000000
    try:
        addr2 = addrSpace.getAddress(preferred + rva)
    except:
        addr2 = None
    if addr2 is None:
        out.append("// %s : ADDRESS FAILED" % name)
        continue
    fn = getFunctionAt(addr2)
    if fn is None:
        fn = createFunction(addr2, name)
    if fn is None:
        out.append("// %s @ %s : FUNCTION CREATION FAILED" % (name, addr2))
        continue
    res = di.decompileFunction(fn, 240, tm)
    if res.decompileCompleted():
        out.append("// ===== %s @ %s =====" % (name, addr2))
        out.append(res.getDecompiledFunction().getC())
    else:
        out.append("// %s @ %s : DECOMPILE FAILED" % (name, addr2))

f = open(outPath, "w")
f.write("\n".join(out))
f.close()
print("decompile_exact: wrote " + outPath)