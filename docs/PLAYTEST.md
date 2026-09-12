# UADCarrier PoC v0 — Playtest Kit

Goal: prove carriers build, sail, and strike end-to-end using only working pieces,
then tune from playtest feedback. No new code or data in this round.

## What works right now (verified live)

- 6 CV hulls (3 `type(cv)` + 3 `type(ca)` aliases) with towers/funnels/engines.
- Standard torpedo tubes placeable as air wings (`torpedo(1)` requirement met).
- `cv` ship type registered (`canBuild=True`), AI builds via personalities.
- Hull unlocks via `hull_strength` techs (1919/1929/1940 eras).
- Stable: no constructor crashes, no error spam (after air-wing part rollback).

## Build test (designer)

1. New 1940 campaign (or custom battle designer).
2. Designer → **Heavy Cruiser** section → find **Experimental / Fleet / Heavy Fleet
   Carrier** hulls (the `_ca` aliases; the `type(cv)` originals are identical stats).
3. Place: front tower, funnel(s), engines, then torpedo tubes on deck mounts.
   Use **vanilla torpedo tubes as air wings** (interim). Distinct Mk 1/2/3 Air
   Wing parts (`torpedo_x6/x7/x8`) are BLOCKED: although listed with correct
   art, clicking one makes the game load a Heavy Cruiser hull over your
   design. Suspected hardcoded `torpedo_x0..x5` table (new indices read out
   of bounds as hull objects). Experiment preserved in
   `csv/partModels_override.csv` + git history; needs Harmony-side table
   extension, not CSV. DO NOT re-add without that fix.
4. Save as shared design named `CV-PoC` (so battle tests reuse it).

**Pass criteria:** hull selectable, all parts place, design saves without errors.
Check MelonLoader log for `partcategories_name` / `PartMats` errors — if the
`_xN` naming hypothesis holds, there should be none.

## Battle test (custom battle)

1. Custom battle, 1940+, player side: 1× `CV-PoC` (+1 escort DD for screening).
2. Enemy: 1–2 CA/BB (surface gunships, no carriers yet — isolates strike behavior).
3. Observe:
   - Does the carrier hold standoff range (via `optimalEnemyDistance=1`) or charge guns?
   - Do torpedo strikes launch at range? Hit rate? Damage vs. expectations?
   - Does the AI escort screen, or wander?
   - Any errors in MelonLoader log during battle?

## Tuning knobs (change → retest, one at a time)

| Knob (file) | Effect | PoC starting value | Direction to try |
|---|---|---|---|
| `torpedo_detection_base` (params.csv) | Strike detectability | 682.5 | Lower → stealthier strikes |
| `torpedo_inaccuracy` (params.csv) | Strike spread | 0.0025 | Lower → tighter air strikes |
| `torpedo_reload` (params.csv) | Deck cycle time | 110s | Raise → slower sortie rate |
| `torpedo_ammo` (params.csv) | Sorties before replenish | 2 | Raise → bigger air group |
| `taf_epfm_v5_ai_torpedo_nerf` (params_override) | AI strike accuracy | 0.90 | Lower → weaker AI strikes |
| `optimalEnemyDistance` (shipTypes cv row) | Standoff range ratio | 1 (max) | Already max; lower only if carriers refuse to close when they should |
| `ai_distance_mod` (params.csv) | AI engage distance | 1.025 | Raise → AI holds longer range |
| `buildRatio(cv;X)` (aiPersonalities) | AI carrier construction | 0.2–1.2 by nation | Raise → more AI carriers |

## PoC v0 result — VALIDATED (full skirmish round)
- Carrier hull + weapons + vanilla-tube torpedo strikes all functional.
- Damage model, upgrades work. No crashes, no hull-eating (post x6/x7/x8 revert).
- Platform proven; remaining work is feel-tuning + Harmony-side features below.

## v1.2 Harmony round — AIR WINGS VALIDATED
- Mk 1/2/3 Air Wing tubes list, show art (via TAF cache seeding), place on
  deck mounts, and fire in battle (behave as regular torpedoes = correct interim).
- Root causes fixed: missing torpedoTubes rows, TechTorpedoGrade default-5
  remap to 1/2/3, and — the big one — our own LoadModel prefix rewriting
  data.model, which manufactured the GetModelNameScale Any(null). Lesson:
  keep "(custom)" on custom parts; never rewrite shared PartData model fields.
- Patch-safety rules learned: blittable values + untouched refs only; no
  struct __result, no ref Il2Cpp params, no TypeByName (use SafeFindType).

## v1.1 Harmony round — TEST PROCEDURE (superseded by v1.2 above)
Deployed: air-wing parts back (x6/x7/x8 + tube-stats rows, a fix missing last
time) + 16 individually-guarded Harmony patches + cv.enabled=true.

1. **Air wings**: designer torpedo section → place Mk 1/2/3 Air Wing tubes.
   - PASS: places on deck mounts, no hull swap, no error spam.
   - If hull swap recurs, the guard logs `BLOCKED hull swap` and your design
     is safe — report the surrounding log lines.
2. **Designer CV button**: enter Constructor → look for CV button in the type
   picker. v1 wires it via the game's own Ui.OnClick; clicking logs diagnostics
   (may not select cv yet — that needs one probe round). Report: visible? click
   log lines in CarrierMod.log?
3. **Custom battle CV row**: skirmish setup → look for CV button/row.
   Report: present? selectable? battle launches?
4. **After any test**: paste `MelonLoader/Logs/CarrierMod.log` highlights —
   especially `patch OK/FAIL` lines at startup, `CalcCategory`, `ChoosePart`,
   `discovered vanilla ship-type count`, and any `BLOCKED` lines.

## Feedback to collect (report back)

1. Build: any part unplaceable? Which, on which hull?
2. Battle: standoff held? Strikes launched? Hit/damage feel?
3. AI: escorts screen? Enemy reacts to strikes?
4. Errors: any new log spam (paste lines)?
5. Feel: too strong / too weak / just right, and at what range?

## Expansion path (after PoC validates)

- Distinct air-wing visuals/labels (revisit custom torpedo parts with correct registries).
- Designer CV picker button + custom-battle CV row (UI injection, in progress).
- Dedicated `aviation` tech line (needs Harmony registry patch).
- Strike range extension toward Long Lance distances (22–40 km) via tube stats.
- Campaign AI carrier operations (task forces, invasions with air cover).
