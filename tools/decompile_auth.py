# -*- coding: utf-8 -*-
# Decompile script.json-verified addresses (authoritative).
targets = {
    "Ship$$LoadUnloadBattle": 0x181F6C9B0,
    "Ship$$AddPart": 0x181F147B0,
    "Ship$$StatEffectPrivate": 0x181F98680,
    "Ship$$RefreshHullStats": 0x181F87430,
    "Ship$$CreateSectionsForShip": 0x181F33120,
    "Ship$$CreateVisualForSections": 0x181F34130,
    "Ship$$Init": 0x181F649B0,
}

args = getScriptArgs()
outPath = args[0] if args else "targets4.c"

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
    fn = getFunctionAt(addr2)
    if fn is None:
        fn = createFunction(addr2, name)
    if fn is None:
        out.append("// %s @ %s : FUNCTION CREATION FAILED" % (name, addr2))
        continue
    res = di.decompileFunction(fn, 300, tm)
    if res.decompileCompleted():
        out.append("// ===== %s @ %s =====" % (name, addr2))
        out.append(res.getDecompiledFunction().getC())
    else:
        out.append("// %s @ %s : DECOMPILE FAILED" % (name, addr2))

f = open(outPath, "w")
f.write("\n".join(out))
f.close()
print("decompile_auth: wrote " + outPath)
