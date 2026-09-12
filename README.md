# Ultimate Admiral Dreadnoughts — CV Barebone Modification

Operational aircraft carriers for **Ultimate Admiral: Dreadnoughts (v1.7.0.0)**. This mod
adds a fully working carrier ship type with torpedo-carrying air wings that launch as
squadron waves, fly historical-permark speeds, lead their targets, get shot down by
enemy secondary batteries, and return to rearm — in **both skirmish and campaign**.
The AI can build and fight carriers against you with the exact same systems.

Everything is data-driven where possible and code-driven where the game demanded it.
The goal was a *barebone but complete* carrier loop that the community can tune,
extend, and art-up.

---

## Highlights

- **3 carrier hulls** (Langley / Yorktown / Essex-CA platforms) across the tech tree, plus a
  registered `cv` ship type available to the player *and* the AI in skirmish & campaign
- **Air wing torpedo tubes** — Mk1/Mk2/Mk3 (`torpedo_x6/x7/x8`) deck mounts with per-mark
  range, damage, and reload ladders; 1 plane per tube
- **Visual strike planes** — each wing tube spawns a real, visible miniature ship (one per
  mark, any nation) that hunts, drops with intercept lead, returns, and rearms on deck
- **Squadron waves** — planes launch by mark in deck-launch cadence (one every few seconds),
  share a squadron target, and stay bunched because each wave flies a single speed
- **Historical speeds** — Mk1 90 / Mk2 110 / Mk3 130 map-units/s, all configurable
- **Intercept lead** — drops solve time-of-flight against target velocity; no more stern misses
- **Secondaries-as-AA** — enemy ships' secondary/casemate batteries roll real probability
  against your planes (range curve × final-approach vulnerability). No secondaries in your
  design = no AA cover. Planes have 2 HP; kills are permanent for the battle
- **Multi-carrier** — every deployed CV on *either* side runs its own air wing
- **Campaign support** — the mod auto-saves its plane design as a shared design and reloads
  it in campaign battles (where the game cannot generate designs)
- **Enemy wing-tube gate** — carriers can't "fire" their wing tubes as guns; planes are the
  only delivery system
- **Config file** — every important number is a knob in `CarrierModConfig.csv`

---

## Requirements

| Component | Version |
|---|---|
| Ultimate Admiral: Dreadnoughts | **1.7.0.0** |
| [MelonLoader](https://melonwiki.xyz/) | 0.6.x (net6) |
| [TweaksAndFixes (TAF)](https://www.nexusmods.com/ultimateadmiraldreadnoughts) | 3.21.1 |
| .NET 6 (only if building from source) | SDK |

> The mod is developed and tested against **TAF 3.21.1** — it patches several TAF-shared
> game classes and relies on TAF's skirmish/AI design generation.

---

## Installation

1. Install **MelonLoader** for UAD (drop `MelonLoader/` into the game root, run once).
2. Install **TAF 3.21.1** into the game's `Mods/` folder per its own instructions.
3. Copy everything from **`GameFiles/Mods/`** in this repo into the game's
   `...\steamapps\common\Ultimate Admiral Dreadnoughts\Mods\` folder:

   | File | What it does |
   |---|---|
   | `CarrierMod.dll` | The mod (MelonLoader plugin) |
   | `parts_override.csv` | Carrier hulls, air-wing tubes, plane hull |
   | `shipTypes_override.csv` | `cv` + `plane` ship types |
   | `technologies_override.csv` | Hull/tech unlocks for the CV line |
   | `torpedoTubes_override.csv` | Wing tube stats + reload ladders |
   | `aiPersonalities_override.csv` | AI carrier build priorities |
   | `partModels_override.csv` | Model bindings (uses existing TB meshes) |
   | `genarmordata.csv` | Armor generation rules incl. `plane` entries (**replaces all TAF armor rules** — if you run another armor mod, merge carefully) |

4. Start the game. `MelonLoader/Logs/CarrierMod.log` should begin with
   `[CarrierMod] log start` followed by `patch OK:` lines.
5. First launch also creates `Mods/CarrierModConfig.csv` (see **Tuning** below).

**Campaign note:** campaign battles cannot generate new designs, so the mod mints its plane
design during any **skirmish** battle (it is saved automatically as the shared design
`CarrierMod Plane`). Run one skirmish with a carrier once per game install; after that,
campaign carriers launch planes automatically. The design is keyed per nation — a campaign
carrier of nation X reuses a design minted by nation X (a fallback also tries the opposing
battle nation).

---

## Gameplay quickstart

1. **Skirmish or campaign → ship designer → CV type.** Pick a carrier hull, fit
   **Air Wing Torpedo Tubes** (Mk1/2/3) from the air-wing category, plus whatever
   secondaries you want (they are your AA!) and a small main battery.
2. Deploy into battle. After the battle fully settles (~45 s), your carrier launches its
   air wing in mark-waves: `wave 0 (torpedo_x8): 2 planes launching...`
3. Planes hunt the nearest live enemy, drop one torpedo each inside ~1,250 m with lead
   computed against the target's movement, RTB, rearm on deck (~135 s), and go out again.
4. Enemy secondaries will engage your planes on the way in — hardest right at the drop.
   Kills are permanent: your deck strength shrinks through the battle.
5. If your carrier sinks, your airborne planes ditch.

---

## Tuning — `Mods/CarrierModConfig.csv`

Created automatically on first run; every line is `key,value`. Delete a line to fall back
to the default. **Changes require a game restart.**

| Key | Default | Meaning |
|---|---|---|
| `verbose` | `false` | High-frequency diagnostics (scene scans, tech remaps, launch traces) |
| `mk1_plane_speed` | `90` | Mk1 flight speed (map units/s) |
| `mk2_plane_speed` | `110` | Mk2 flight speed |
| `mk3_plane_speed` | `130` | Mk3 flight speed |
| `torpedo_drop_speed` | `90` | Effective in-water speed of a dropped torpedo |
| `plane_torpedo_boost` | `1.0` | Extra multiplier on plane-dropped torpedoes only |
| `torpedo_life` | `60` | Torpedo lifetime (s) |
| `release_distance` | `1250` | Drop range from target (m) — historic Swordfish range |
| `rearm_distance` | `150` | "On deck" distance from the carrier (m) |
| `rearm_time` | `135` | Deck cycle between sorties (s) |
| `aa_range` | `2500` | AA engagement envelope (m) |
| `aa_cycle` | `2.5` | Seconds between AA volleys |
| `aa_plane_hp` | `2` | Hits a plane survives |
| `aa_crit_chance` | `0.20` | Chance any hit is a one-shot kill |
| `aa_hit_mult` | `0.95` | Global hit-chance multiplier (planes are evasive) |
| `aa_max_rolls` | `6` | Max AA rolls per ship per volley |
| `wave_gap` | `30` | Seconds between squadron waves |
| `launch_gap` | `3` | Deck-launch cadence inside a wave (s) |
| `wave_time_limit` | `600` | Max time a carrier keeps planning new waves (s) |

The per-hit probability curve (not exposed, in `AaHitChance`) is: 0.3 % beyond 2,500 m →
0.5–3 % at 2,500–1,000 m → 3–8 % at 1,000–300 m → 10 % inside 300 m, ×3 during final
approach (outbound, armed, < 1,500 m).

**Reference balances:** a fully secondary-fitted modern battleship should shred most of a
raid but still eat fish; a bare gunboat gets away with murder against planes. `AA_HIT_MULT`
and `aa_crit_chance` are the two dials that most change the feel.

---

## How it works (for modders)

- **Registry**: `cv`/`plane` ship types + carrier hulls are added through the TAF-style
  override CSVs; a small startup patch re-asserts the type registration before the
  designer builds its buttons.
- **Spawning**: an eternal 5 s watcher (started at boot, so it works in skirmish,
  campaign, and missions) looks for *deployed* `cv_` hulls. Each gets a coroutine that
  waits out the battle-load window, then builds one design per plane via
  `Ship.CreateRandom` (skirmish) or the shared design (campaign), clones it with
  `Ship.Create`, transfers the design's original part (`AddPart` + `LoadModel` —
  `Instantiate` copies of IL2CPP parts NRE in `NeedRecalcCache`), and starts a per-plane
  state machine.
- **AI**: `PlaneAI` ticks at 4 Hz with kinematic transform stepping (planes are
  collider-less, division-less ships — the game physics is disabled on them: no ramming,
  no wave drag). Drops use the carrier's wing tube as the `Torpedo.Create` `from` part so
  per-tube damage/boost attribution works, and aiming solves an intercept against
  `Ship.velocityCurrent`.
- **AA**: a 2.5 s manager counts each enemy ship's `gun_sec` + `gun_casemate` mounts
  (via `Part.CalcCategory`, cached 10 s/ship) and rolls the curve per mount.
- **Patching discipline**: every Harmony patch is blittable-args-only, no struct
  `__result` round-trips, no `ref` IL2CPP parameters, no iterator/coroutine originals.
  These constraints come from hard crashes — keep them.

Full development log (every dead end and fix) is in [`docs/DESIGN.md`](docs/DESIGN.md).

---

## Known limitations

- Plane visuals reuse existing TB hull meshes at reduced scale (art hooks are in place;
  proper models are welcome).
- Planes don't appear in the battle report; ship damage from their torpedoes is credited
  to the carrier's wing tube.
- Targeting ignores the game's spotting/fog rules (planes effectively have perfect recon).
- One shared plane design per nation (minted from skirmish); per-nation custom planes are
  a future idea.
- Developed/tested against UAD **1.7.0.0** + **TAF 3.21.1** only.

---

## Troubleshooting

- Log: `MelonLoader/Logs/CarrierMod.log` (fresh each launch). Set `verbose,true` in the
  config for full diagnostics.
- **No planes spawn**: look for `DEPLOYED CARRIER FOUND` — if absent, the hull is not a
  `cv_` type or the battle hasn't deployed. If present followed by factory errors, check
  that `plane` shipType exists (`shipTypes_override.csv` installed?).
- **Campaign: `NO plane design available`** — run one skirmish battle with a carrier to
  mint the shared design (see Installation note).
- **`PlaneAI: no friendly carrier left; ditching.`** — working as intended.

---

## Attributions

- **Ultimate Admiral: Dreadnoughts** — game by **Game Labs**. All game data referenced or
  extended remains their property; this mod is a fan work, unaffiliated and non-commercial.
- **[TweaksAndFixes (TAF)](https://www.nexusmods.com/ultimateadmiraldreadnoughts)** — its
  data-driven override architecture, design generation, and shared-design systems this mod
  builds on; portions of the installed CSV override format follow its conventions.
- **[MelonLoader](https://github.com/LavaGang/MelonLoader)**,
  **[HarmonyX](https://github.com/BepInEx/HarmonyX)**,
  **[Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)** — the runtime that makes
  IL2CPP modding sane.
- **[Il2CppDumper](https://github.com/Perfare/Il2CppDumper)** and
  **[Ghidra](https://ghidra-sre.org/)** — reverse-engineering toolchain used to map game
  internals (`tools/` contains the headless scripts written for this project).
- Historical balance references: public summaries of interwar/warship torpedo and AA
  performance (Washington/Royal Navy markings eras), plus the community's collective
  playtesting sanity.

## License

MIT — see [LICENSE](LICENSE). Game assets and game data remain © Game Labs.
