# tools/

Reverse-engineering helpers used during development (all optional to play —
you only need these to re-derive game internals after a game update).

- `il2cpp_headless.py` — Ghidra headless script: applies Il2CppDumper's
  `script.json` symbols/addresses to a Ghidra program of `GameAssembly.dll`.
- `decompile_exact.py` / `decompile_auth.py` — Ghidra headless scripts that
  decompile specific addresses (edit the `targets` dict) to a C file.

Typical usage: dump IL2CPP with Il2CppDumper, import `GameAssembly.dll` into
a Ghidra project, run the headless analyzer with `-scriptPath <this dir>
-postScript il2cpp_headless.py <dump output dir>`, then use the decompile
scripts with the addresses from `script.json`. The two `run_*.bat` batches
in this folder were machine-local (excluded from the repo) — recreate them
from the batch contents in `docs/DESIGN.md` if needed.
