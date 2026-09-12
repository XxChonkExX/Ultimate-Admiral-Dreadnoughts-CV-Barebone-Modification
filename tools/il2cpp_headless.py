# -*- coding: utf-8 -*-
# Headless variant of Il2CppDumper's ghidra.py
# Usage: postScript il2cpp_headless.py <script.json> <stringliteral.json>
import json

processFields = [
    "ScriptMethod",
    "ScriptString",
    "ScriptMetadata",
    "ScriptMetadataMethod",
    "Addresses",
]

functionManager = currentProgram.getFunctionManager()
baseAddress = currentProgram.getImageBase()
USER_DEFINED = ghidra.program.model.symbol.SourceType.USER_DEFINED

def get_addr(addr):
    return baseAddress.add(addr)

args = getScriptArgs()
f = None
import java.io
if len(args) >= 1:
    f = java.io.File(args[0])
data = json.loads(open(f.absolutePath, 'rb').read().decode('utf-8'))

if "ScriptMethod" in data and "ScriptMethod" in processFields:
    scriptMethods = data["ScriptMethod"]
    monitor.initialize(len(scriptMethods))
    monitor.setMessage("Methods")
    for scriptMethod in scriptMethods:
        addr = get_addr(scriptMethod["Address"])
        name = scriptMethod["Name"].encode("utf-8")
        name = name.replace(' ', '-')
        try:
            createLabel(addr, name, True, USER_DEFINED)
            func = getFunctionAt(addr)
            if func is None:
                createFunction(addr, None)
        except:
            pass
        monitor.incrementProgress(1)

print("il2cpp_headless: done applying %d methods" % len(scriptMethods))
