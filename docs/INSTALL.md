# UADCarrier — Install

Standalone carrier mechanics mod. Does not touch UAD:Rework or any other mod.
Installs as data-file overlays onto a TAF-based game (UAD 1.7.0.0).

## Prerequisites
- Ultimate Admiral: Dreadnoughts 1.7.0.0 with MelonLoader + TAF (TweaksAndFixes).
- Back up `Mods/Default_Files/` before merging (copy the folder aside).

## Merge steps
0. **Registry patch (required)** — copy `Mods/CarrierMod.dll` to `<UAD>/Mods/`.
   The game ignores new ship types from CSV, so this tiny MelonLoader mod
   registers the `cv` type additively at startup (no game methods patched, no
   existing entries touched; safe alongside TAF/EPFM/DIP). Source in
   `source/CarrierMod/`; rebuild with `dotnet build -c Release`.
1. **Air wings** — append `csv/torpedoTubes_carrier.csv` rows to
   `Mods/Default_Files/UAD_Files/torpedoTubes.csv`.
2. **AI** — apply `csv/aiPersonalities_carrier.txt` fragments to
   `Mods/Default_Files/UAD_Files/aiPersonalities.csv` (see file for exact edits).
3. **Ship type** — append `csv/shipTypes_carrier.csv` row to
   `Mods/Default_Files/UAD_Files/shipTypes.csv`.
4. **Hulls** — append `csv/parts_carrier.csv` rows to
   `Mods/Default_Files/UAD_Files/parts.csv`.
5. **Hull unlocks** — add `cv_1_langley` to `hull_strength_12`'s `unlock(...)`,
   `cv_2_yorktown` to `hull_strength_14`'s, and `cv_3_essex` to
   `hull_strength_end`'s, in `Mods/Default_Files/UAD_Files/technologies.csv`.
   (See `docs/HULL_UNLOCKS.md` for why: the game ignores new tech lines from CSV.)
6. Launch. Carriers unlock via the normal hull techs (1919/1929/1940); AI nations
   research and build them per the build ratios. Or run `source/merge_carrier.ps1`
   to apply steps 1–5 automatically (back up `Default_Files/` first).

## Tuning
- Strike range: `params.csv` torpedo-range params (Long Lance precedent 22–40 km).
- Deck cycle: `torpReload_*` columns in the appended tube rows.
- Strike accuracy: `torpAcc_*` columns; weather no-fly via `accuracies.csv` groups.
- See `docs/DESIGN.md` for the mechanics model and `research/` for calibration data.

## Uninstall
Restore the backed-up `Mods/Default_Files/` folder.
