# UADCarrier — Mechanics Design Base

## v2 ROADMAP (approved direction): planes as miniature ships

Supersedes the statistical abstraction, which is FROZEN as the fallback
baseline (v1: user-validated tuning in "Air-wing weight model" below).

Core concept: a strike "plane" is a tiny Ship entity with high speed, tiny HP,
one torpedo tube. Spawns from carrier at battle start, AI approaches to
torpedo launch range, fires, returns to carrier, rearms, repeats. Fully
targetable by defensive fire = real AA layer. Art/animation attach later;
janky visuals are accepted (framework first).

USER DESIGN DECISIONS (binding):
1. Targeting filter: MAIN guns may not target planes. SECONDARY-class weapons
   may — including DD/TB primaries, which are secondary-class. Filter is by
   gun size class, not by mount slot.
2. Hit model: plane hit chance LOW at range, scaling up on approach curve.
   Tiny HP: one bullet hit shreds. Tuning pass required.
3. Lifecycle: spawn at battle start from carrier -> strike -> return ->
   rearm alongside carrier using the SAME torpedo_reload timer (~110s ≈ 2min
   game time; real-life deck cycle is longer but this fits game pacing) ->
   relaunch. Custom manager code; no engine support.
4. Collision/ram: planes EXCLUDED from collision and ram mechanics.
5. ONE universal plane design per mark for ALL factions (no per-nation
   variants). Minimize bloat, minimize bugs.
6. Plane hull reuses an existing small-boat mesh (TB-class) scaled down;
   torpedo mounts inherited from that mesh so the strike weapon works
   data-only.

STAGED PLAN:
- Stage 1 (prototype): STATUS **PAUSED at 90%** — see "v2 experiment log" below.

## v2 experiment log (planes as ships — where it stands)
PROVEN WORKING:
- `plane` shipType + `plane_strike_1` hull load cleanly (CSV); game generates
  real designs via Ship.CreateRandom (names: "PLANE Emden"); designs convert
  to real battle ships via Ship.Create(design, player, isTempForBattle=true)
  (isDesign=False, ships MOVE at plane speeds).
- Carrier detection: post-deployment scan (cv_ hull, >1000m from origin).
- Ammo model: torpedo_ammo=99 in game-root params.csv (TAF override ignored
  for vanilla params); wing deck-cycle via tube reload ladders 2.0/2.4/2.8.
- Wing-only stats: caliber size 2.5/3.0/3.5 on tube rows (range/speed/damage
  per-part, no national leak).

BLOCKERS (exact):
1. **parts=0**: ships created outside battle-init have NO instantiated parts
   (hullAndParts empty) -> no visuals (children=1), no weapons, no AI.
   Part instantiation happens ONLY in battle deployment loading.
2. **deployment stall**: spawning during setup puts plane designs into the
   deployment loader, which stalls battle start on them (root cause unknown —
   candidate: plane_strike_1 hull fields: sections 0-2 OK for TBs, but
   minMainTurrets/Barrels=0 or speedLimiter=45 may break section/visual gen).
3. AttachShips NREs on division attach (may resolve once parts exist).
4. Init-race: spawning mid-init froze battle start (fixed by post-deploy
   spawn, but that hits blocker 1).

NEXT STEPS (when resumed):
a. Deployment-readiness: bisect plane_strike_1 hull fields vs tb_standard
   (try minMain 2/2, speedLimiter 28, var(bow_a) tag) until setup-spawn
   deploys without stall -> then full integration is FREE (visuals, parts,
   AI, division).
b. If (a) stalls on plane hull itself: clone a TB hull row wholesale and
   re-skin (name/placement), keep type(plane).
c. Last resort: reverse part-instantiation from deployment loader (Ghidra).

## v2 STAGE 1: COMPLETE — PLANE VISIBLE IN BATTLE (validated)
The winning pipeline (all steps user-verified in battle):
1. `plane` shipType (TB-clone stats, buildRatio=0) + `plane_strike_1` hull
   (TB-clone of jap_tb_hull, scale 0.45, TB tags, all countries) via CSV.
2. Post-deployment trigger: watcher scans until a DEPLOYED cv_ carrier is
   found (>1000m from origin — setup ships are ghosts at origin).
3. `Ship.CreateRandom(plane, carrier.player, ...)` → generates design
   (parts=1, hull=plane_strike_1) → onDone callback receives it.
4. `Ship.Create(design, player, isTempForBattle=true, false, false)` →
   battle clone. NOTE: clone drops design parts (parts=0 at creation —
   Create does NOT copy design parts).
5. **Wait 45s** — battle must fully settle; part-carrying planes during
   load freeze battle init.
6. Transfer: `AddPart(part)` per design part (registers logically) +
   `Part.LoadModel(clone, true)` (builds the visual) +
   `part.transform.SetParent(clone.transform, true)` (reparent — model
   otherwise renders at the invisible design shell's location).
RESULT: visible miniature TB-hull plane floating off the carrier's stern,
battle runs clean, wing strikes unaffected.

REMAINING NREs (inert in visual-only mode): AttachShips (division attach)
and LoadUnloadBattle→CreateVisualForSections — both needed only for
AI/battle integration (Stage 2/3).

STAGE 2 QUEUE: movement (division attach NRE root cause, or customMoveTo
fallback), strike/RTB cycle, spawn positioning (stern offset), scale.

INTERIM: v1.2 statistical abstraction is the shipped gameplay (air wings =
torpedo strikes, unlimited ammo, long deck cycle). Fallback frozen.

LESSONS (ammo/speed/damage isolation):
- torpedo_ammo/torpedo_reload are VANILLA params: edit game-root params.csv
  directly. Mods\params_override.csv is TAF-only — vanilla params there are
  IGNORED. torpedo_ammo=99 now live (wings effectively unlimited; deck-cycle
  via wing tube reload ladders x6=2.0/x7=2.4/x8=2.8 => 220/264/308s).
- torpedo_prop_wing techs REMOVED: techs are national and leaked speed/range
  to ALL torpedoes of the nation. Wing damage restored to FULL ship-torpedo
  strength (user-approved for unlimited-ammo regime).
- Wing speed/range isolation: Harmony postfix on Torpedo.Create(Part part,...)
  keyed on firing tube name — x6 speed x2.0, x7 x2.5, x8 x3.0 (same lifetime
  => longer range too). Per-tube, zero leak. Param-name fallbacks in code.
- Stage 2: RTB + rearm manager (torpedo_reload timer), despawn on death/
  battle end. Real sortie rhythm.
- Stage 3: targeting filters (mains ignore, secondaries engage) + hit-chance
  proximity curve. Defenses feel right.
- Stage 4: fighter/recon variants (gun mount, spot stats), strafing.

Open RE questions: Ship spawn API (design->Ship at runtime), AI move-order
API for RTB leg, design generator call for auto-valid designs, collision
exclusion flag, target-selection patch point for gun filters.

> PIVOT: the dedicated `cv` ship class (picker button, battle row) is retired —
> vanilla maps those by hardcoded int/enum and every injection path failed or
> crashed. Carriers ship as **type(ca) hulls with carrier stats + distinct air
> wings**: they list under Heavy Cruiser in designer and skirmish, unlock via
> existing `hull_strength` eras, and fight by standoff torpedo strike. The `cv`
> ShipType registration stays (harmless; AI ratios reference it) as a future
> hook if the hardcoded UI ever opens up. v1.2 validated: hulls + Mk 1/2/3 air
> wings place and strike in battle.

Goal: functional carrier warfare inside UAD's engine, built only from systems the
game already has. No new entity types, no flight model. Artists/animators can later
add flight-deck art, island models, and deck-park visuals on top of these mechanics.

## Core model: wings as tube-launched strikes
- Each air wing generation (`airwing_tb_mk1/2/3`) is a torpedo-tube row.
- **Hangar/elevator throughput = tube count × reload.** Two elevators ≈ two tubes
  with a long deck-cycle reload. Fitting more "tubes" = bigger air group.
- **Hangar capacity = ammo.** `torpedo_ammo` per tube = sorties before replenishment;
  replenish at port / sea per existing ammo rules.
- **Strike range/speed/damage** come from the tube + global torpedo params. Use the
  Type 93 Long Lance precedent (22–40 km) for late-war strike reach.
- **Accuracy** via `torpAcc_*` (worse early, better late) plus weather no-fly groups.

## Tech progression (branches off the battleship line)
- `aviation_deck` (needs `hull_construction;5`) → 1920s conversions, mk1 wings.
- `aviation_ops` (needs `aviation_deck;5`) → 1930s purpose-built CVs, mk2 wings.
- `aviation_strike` (needs `aviation_ops;5`) → 1940s fleet carriers, mk3 wings.

## Air-wing weight model (v1.4 tuning pass, rev2 "min/max caveat")
Stock hulls of the line were tuned battle-ready; shipyard ships were slower.
Design target: carriers noticeably slower/less nimble than same-size cruisers
but not sluggish; big decks hangar-heavy. Current: turn 38/32/26 (cruisers
72-79), stability 82/85/80, speedLimiter 6/7/8 (vanilla ca 3-4).
Wing mass rev2 (per-torp tonnes incl airframe/fuel/elevator share; shipyard
air groups were heavier than paper): Mk1 90t, Mk2 150t, Mk3 240t.
Strike kinematics: wing techs torpedo_prop_wing1/2/3 (1922/1936/1944) give
speed_mod 35/55/75% and range_mod 60/120/200% — "nearly unlimited on battle
map" for Mk3; taf_torpedo_max_launch_range_percent 0.9 -> 1.0 so launches use
full reach. Damage: torpedo_damage(-64/-68/-72) — aerial torps were 450-533mm
(18-21in warheads ~200-450kg TNT eq) vs ship torpedoes 610-660mm; wing hit
should hurt but not one-shot the anti-torpedo scheme of a capital ship.
Turn ladder final (playtest-tuned): 19/15/11 (cruisers 72-79; wide sweeping
radius). Speed mods final: 119/161/205% (2.2-3x base = aircraft feel).
USER-VALIDATED BASELINE v1: speed/damage/turn all playtest-approved.

## AI
- `buildRatio(cv;X)` per nation (Japan/US high, others staggered) + generic 0.5.
- `TechMod(aviation_*;N)` priorities so the AI actually researches the line.
- Battle behavior reuses existing levers: `ai_distance_mod`,
  `optimal_battle_range_multiplier`, division screening. Carriers hold standoff and
  launch strikes; escorts screen. If the AI charges carriers into gun range, that is
  the one place a small Harmony patch (AI range-keeping for the CV type) may be needed.

## Scope deliberately excluded (v1)
Fleet air defense/CAP, recon, dive-bomber vs torpedo-bomber profiles, altitude,
landing/takeoff visuals. These need art + possibly code; the strike mechanics here
are the foundation they build on.

## For artists
Needed art hooks (no code changes required to add them once mechanics land):
flight-deck part + island models (`partModels.csv`), deck-park/cv hangar visuals,
nation-specific deck markings. Mount points for elevator "tubes" go in `mounts.csv`.
See `reference/` for the canonical schemas of each file.

# v2 PLANES-AS-SHIPS — STAGE 2 EXPERIMENT LOG (live battle sorties)

State machine PlaneAI (Outbound -> drop -> Retreat -> Rearm -> repeat), 0.25s
tick, kinematic transform stepping (clone has no division; game ignores its
move orders). Validated in live skirmish, 3 sessions.

Tuning history (user playtest feedback):
- PLANE_SPEED 25 -> 62.5 (wave/buoyancy made 25 feel slower than a 35kt DD;
  MakeKinematic fixed the physics fight) -> 75 (+20% user request).
- RELEASE_DIST 2500 -> 1250 (historic Swordfish drop range, user request).
- REARM_DIST 150 / REARM_TIME 60: validated - plane halts within ~150m of
  moving carrier, 60s deck cycle, re-sorts.
- Kinematics: rigidbodies isKinematic + useGravity off (10 RBs), colliders
  disabled (16, incl design ghost) = collision exclusion, no wave drag.
- Design ghost: colliders off + parked 2000m below map (Ui.UpdateBattle
  hover raycast NRE hardening). Eviction still 0 containers (field unknown).

STRIKE MECHANICS (validated 00:41 session): plane design is hull-only
(parts=1, no tube part) so drops use the CARRIER's wing tube as the
Torpedo.Create rom part -> full x8 damage family + boost patch keys off
it; pass speed 30 (boost x3 -> ~90 effective, bi-plane era). STRIKE log:
	orpedo dropped from carrier wing tube at ~2500m. Torpedo VISIBLE on
drop (user-confirmed). Damage-on-hit attribution still unverified.

KNOWN ROUGH EDGES (next): hit/damage confirmation; secondary-guns-only
targeting + low hit chance scaling (enemy main guns must ignore planes);
multi-plane flights (one universal design per mark, all factions);
evasive movement; division attach (AttachShips NRE, deferred).

FREEZE FIX (01:00 session): battle init deadlocked when CreateRandom/clone
ran during the loading tail (2 of 4 sessions; nondeterministic). All spawn
work now happens in SpawnAfterSettle: carrier detect -> 45s settle ->
CreateRandom -> clone -> transfer -> PlaneAI. Nothing of ours touches the
scene until the battle is fully loaded.

VALIDATED (01:12 session): SpawnAfterSettle freeze fix holds - full cycle
clean: 45s settle -> design gen -> clone -> transfer -> PlaneAI, drop at
1240m (<= new 1250 release), RTB underway at session end. No init freeze,
no UI NREs. STAGE 2 STRIKE LOOP: functionally complete pending one open
question - damage-on-hit attribution (from=carrier wing tube) still not
visually confirmed by user.

STAGE 2.5 (01:33 build): SQUADRONS + wing-tube ordnance gate.
- 1 plane per wing tube: squad size = CountWingTubes(carrier) (x6/x7/x8
  mounts on the CV design). Designer tube mounts control swarm size.
- One CreateRandom design, N Ship.Create clones, line-astern berths.
- SquadronTransfer: per-plane part COPIES (Instantiate) via AddPart +
  LoadModel; originals stay on parked ghost. PlaneAI per plane with idx,
  0.4s stagger; plane i drops from tube[i % tubeCount] (paired).
- Wing-tube ordnance gate: Torpedo.Create PREFIX blocks game launches from
  x6/x7/x8 (tubes cycle empty; wing tube launch BLOCKED log). Plane drops
  pass via CarrierMod._planeDrop flag. Base speeds 45/36/30 (x6/x7/x8) ->
  ~90 effective after boost.
- ScanShips() shared cache (0.5s) for N-plane tick scans.
- DAMAGE VALIDATED (01:22 session): ~3000 dmg on transport from single
  plane drop via carrier wing tube rom attribution.

BUG (12:26 session): squadron invisible - all 6 part COPIES failed
(transferred 0/1, rigidbodies 0; bare catch ate the exception). Gate +
squadron flight + pairing + BLOCKED logs all WORK. Fix: transfer loop now
logs every failure path (Instantiate null / copy threw / missing Part +
component dump / AddPart/LoadModel missing / transfer step threw) and plane
0 falls back to the ORIGINAL part (proven single-plane path) so at least
one plane is always visible. Next log pinpoints why Instantiate+GetComponent
failed (suspects: interop GetComponent<Part> on fresh copy, HideFlags, or
AddPart/LoadModel throwing on copies).

VALIDATED (13:39 session): 10-tube squadron FULLY functional - 10/10 planes
created, staggered STRIKE0-9 drops at ~1240m (x8 x3 / x7 x2.5 / x6 x2 all
~90-98 effective), formation RTB, gate blocking AI tube launches. Only
visuals missing: every part transfer (copy AND plane-0 copy path) threw
TargetInvocationException - inner exception was swallowed (wrapper only
logged). FIX: TryAttachPart logs AddPart vs LoadModel inner exceptions
separately; plane 0 retries with ORIGINAL part if copy transfer fails.
Next log names the real error (suspect: LoadModel on Instantiate copies).

ROOT CAUSE + FIX (14:03 session): AddPart threw NRE at
Ship.NeedRecalcCache(Part) -> AddPart(Part). Unity Instantiate copies only
SERIALIZED fields on Il2Cpp components; the copy's non-serialized cached
state is null -> NeedRecalcCache NREs. Originals always worked (plane 0
fallback visible on screen). FIX: SEQUENTIAL DESIGN PIPELINE - one
CreateRandom per plane; each design donates its OWN original part to its
clone (proven path, N times). ~2s per design + 1.5s gap; 6-tube CV builds
out over ~20s post-settle. PlaneAI unchanged. WaitCreateFinishIl2Cpp +
SquadronTransfer + copy strategies deleted (dead paths).

TUNING (user): PLANE_SPEED 75 -> 150 (~90kt Swordfish relative speed);
REARM_TIME 60 -> 120 (2x deck cycle to offset 2x speed).

VALIDATED (14:44 session): sequential pipeline flawless - 6/6 planes built
one design per plane, 6/6 'transferred 1/1 parts (original)', staggered
STRIKE0-5 at ~1240m, formation RTB, 120s rearm cycles, clean teardown.
Speed 150 confirmed (target closing faster than 75). STILL INVISIBLE:
root cause found - removing the old 'plane moved near carrier' line (it
moved the GHOST) left ghosts parked at -2000m; the part transfers
underwater and rides below its plane. FIX: AttachOneAfterDelay now moves
the ghost to the clone position before AddPart/LoadModel (restores the
proven geometry from every session that rendered).

STAGE 2 COMPLETE (user-confirmed): all planes visible + working, reload and
firing excellent. PLANE-DROP SPEED BOOST (user request): plane torpedoes
trailed moving targets too long. Torpedo.Create postfix now applies extra
PLANE_TORP_BOOST 1.8x when CarrierMod._planeDrop is set (~90 -> ~160
effective). Ship torpedo speeds remain vanilla (boost only fires for
x6/x7/x8 wing-tube parts, and _planeDrop only during our drops).

TARGET LIVENESS (user request): target scan skips s.isSinking / s.isDead
(Ship properties from dump.cs 452744). Scan re-runs every tick, so strikes
re-divert to live vessels automatically the tick after a kill.

INTERCEPT LEAD (user request: drops always trailed movers): drop aim now
solves intercept - reads Ship.velocityCurrent (fallback Rigidbody.velocity),
iterates time-of-flight 3x at TORP_EFF=162 u/s, aims at predicted point.
Log: STRIKE{n} ... lead Xm (tof N.Ns). Historically sane: flat-drop attacks
led by target speed x run time.

BALANCE PASS (user: 'exceedingly deadly' with lead + 1.8x): PLANE_TORP_BOOST
1.8 -> 1 (drops back to ship-era ~90 u/s); TORP_EFF 162 -> 90 (lead solver
matches); REARM_TIME 120 -> 135 (+15s per user). Lead + 90 u/s is the new
baseline - historically plausible and still lethal.

PER-MARK PLANE SPEEDS (user, historical combat speeds): Mk1 90, Mk2 110,
Mk3 130 u/s. Plane mark = its paired wing tube (x6/x7/x8). PlaneSpeedForIdx
resolves speed once at AI start; Outbound/RTB at full speed, Rearm taxi at
half. Log: 'PlaneAI{n} speed=N (mark by paired tube)'.

STAGE 3: ANTI-AIR (secondaries-as-AA, user-approved design). AA manager
(2.5s cycles): enemy ships within 2500m roll per working secondary/casemate
mount (cap 6/ship/cycle). Hit curve/roll: >2500m 0.3% | 2500-1000 0.5-3% |
1000-300 3-8% | <300m 10%; x3 in final approach (Outbound + ammo + <1500m).
Plane HP 2; hit = damage (smoke -> 25% slower) or kill (20% crit one-shot).
Kill = fireball, tip-over dive 2.2s, splash (FireBaseScript.CreateExplosion),
hidden below map (not destroyed - teardown safety). Misses show flak puffs
(CreateExplosion radius 2.5). Category via Part.CalcCategory(ship, data)
-> PartCategoryData.name == 'gun_sec' | 'gun_casemate' (log-once dump
validates: 'AA category seen:'). Incentive preserved: no secondaries in
the design = no AA cover.

AA VALIDATED (20:08 session, 10-tube CV vs BB Otto): category mapping
confirmed (gun_sec + gun_casemate counted). 10/10 planes delivered their
torpedoes (all strikes landed, lead solver scaling correctly per mark:
~305m lead @10.6s tof Mk3, ~350m @11.7s Mk1), then 10/10 were lost to AA
- most killed in the RTB/outbound bands at 1300-2400m. Torpedo-8 attrition
profile: strike gets through, squadron pays fully. Curve knobs
(AaHitChance) available if losses need softening vs heavy AA fits.

POLISH ROUND (post-AA validation): AA_HIT_MULT 0.95 (planes 5% harder to
hit, user). VISUAL FIX: CreateExplosion rendered nothing (silent fail);
FireExplosion now logs fallback reason once AND always spawns unlit
primitive-sphere puffs (grey=flak, orange=hit, big orange=kill splash,
Object.Destroy(go, 0.8s) auto-cleanup). BUGFIX: PlaneAI re-acquire now
filters FRIENDLY carriers only (player.Pointer match) - previously planes
could rearm at an ENEMY cv_ once theirs sank; no friendly carrier left =
plane ditches via PlaneDeath (fireball + splash).

MULTI-CARRIER (priority #1): watcher no longer stops after the first CV.
Every deployed cv_ (>=1000m from origin) not yet in _spawnedCarrierPtrs
(ship.Pointer, per-battle) starts its own SpawnAfterSettle pipeline.
_nextPlaneIdx = global unique plane idx per battle (no cross-carrier
collisions on tube pairing / records); reset + registry clear on
new-battle detection. Carriers sunk during the 45s settle are skipped.
Test: CV vs CV mirror match should show BOTH sides launching squadrons.
NEXT (#2): campaign battles - PreInitCustomBattle may not fire there; if
no 'spawn experiment armed' lines appear in a campaign battle, add a
scene-based arming hook.

CAMPAIGN SUPPORT (#2): campaign battles load via BattleManager.PrepareBattle
(iterator, unpatchable) which never fires PreInitCustomBattle. Answer: the
spawn watcher is now ETERNAL - started once at mod init, polls every 5s,
serves custom + campaign + missions without caring which loader ran.
Design ghosts (isDesign) skipped so setup scenes can't trigger spawns.
Per-battle reset still via PreInitCustomBattle (custom); in campaign,
fresh Ship Pointers make re-detection automatic (stale records are
destroyed-object refs the AA manager skips).

CAMPAIGN DESIGN FACTORY FIX: CreateRandom NREs for campaign players on
first MoveNext (inlined designer state missing mid-battle - MoveNext
decompile: Ship.Create(NULL shell) -> GetHull -> ChangeHull ->
EnterConstructor -> GenerateRandomShip; TAF never generates in campaign
so no proof it works there). FIX = SHARED-DESIGN PERSISTENCE:
- Skirmish: first plane design auto-saved via IsSharedDesign + ToStore +
  Util.SerializeObjectByte + Storage.SaveSharedDesignShipByte(
  GetSavedDesignPath('CarrierMod Plane')) + GameData.LoadSharedDesigns().
- Campaign: on first CreateRandom throw, _campaignMode latches (no more
  factory attempts) and planes load via CampaignController.Instance
  .GetSharedDesign(player, planeType, year, checkTech=false) + UpdateOwner
  -> same clone/transfer pipeline. Nation fallback tries every distinct
  battle player. Cache: _sharedPlaneDesignCache per battle.
- Requires ONE skirmish battle with a carrier first (mints the file).

BUGFIX (21:09 multi-carrier skirmish): planes NRE'd instantly - my
multi-carrier watcher rewrite DROPPED the _planeTypeObj lookup; fresh
session = null shipType = GetHull(null) NRE inside CreateRandom state 0
(leaked 'XX <name>' shell confirms: Ship.Create succeeded, GetHull threw).
Fix: SpawnOnePlane fetches plane shipType per plane. Campaign latch
(_campaignMode) now resets on new-battle detection (skirmish re-arms
always retry the factory; campaign persists in shared-design mode since
PreInitCustomBattle never fires there). Multi-carrier detection itself
WORKED: Aoba 6 planes + Pueblo 14 planes queued independently.
NOTE: Pueblo = 14 wing tubes = 14-plane squadron; ~3.5s/plane build.

CV-vs-CV FIX (21:09 retest): planes sat in water - target scan excluded
cv_ hulls (legacy guard from when carriers were 'our' spawns); in a
CV-vs-CV duel every enemy was a carrier = zero targets. Carriers now
valid targets (player filter handles friendlies; plane_ still excluded).
AA scan: carriers also now fire AA (their real secondaries; wing tubes
aren't gun_sec so no self-interference). Spawn lag with 20 planes noted -
sequential design factories hitch frames; mitigation later if needed.

SQUADRON WAVES (user request: deck-launch delay + mark bunching): wing
tubes grouped by mark; each mark launches as its OWN wave - same-speed
planes never string out. Cadence: 3s between plane launches (spreads the
design-factory hitch AND reads as deck ops), 30s between waves. Wave
cohesion: per-wave SquadronState with a SHARED TARGET - first plane to
acquire sets it, the rest follow (re-picked when it dies). Tube pairing
per wave: plane's drop tube = its mark's tubes[slot % n]. Takeoff delay:
PlaneAI start = 2s + slot*2.5s. SpeedForMark(mark) replaces idx-based
lookup. Reset: _squadronStates/_nextWaveId cleared on new battle.

MATERIALIZATION-AWARE WAVES (22:20 CV-vs-CV session): AI carrier Borbone
streamed parts in over ~90s (1 of 6 tubes at settle, all 6 by +90s) ->
wave plan launched 1 plane only. Fix: wave LOOP recounts per launch
(launchedPerMark tracking), keeps going until all tubes covered, 15s
recheck when covered (parts may still stream), 10-min cap. SHARED-DESIGN
SAVE NRE (every attempt): moved save after clone exists + stepwise logs
(IsSharedDesign/design.ToStore/clone.ToStore fallback/serialize/path/
write/reload) to pinpoint the failing step next run. Battle narrative
from log: Donskoi 6 planes -> 4 strikes delivered, 3 shot down by
Borbone's AA-heavy fit (6 casemates + 4 secondaries); Borbone's single
plane cycled sorties; ditching fired when carriers died.

VISUALS PASS (user: AI auto-built a carrier and WON - full mod loop works
end to end; all planes visually accounted for). FLAK VISUALS REMOVED per
user: primitive puffs rendered at ship gun barrels, not planes, and made
a mess. FireExplosion/Puff/CreateExplosion path deleted. AA = pure
probability; kills show only the plane's tip-over dive + water contact.

RELEASE CLEANUP (pre-GitHub): removed dead code - Patch_ConstructorUI,
FindWingTube (singular), TryAttachToCarrierDivision, CountWingTubes,
IsSpawnedPlane(VesselEntity), fields _spawnArmed/_wasDisarmed/_placeTried/
_spawnedPlane/_probeAttached/_pickerSig, consts PLANE_SPEED/TORP_SPEED.
RANGE-PATCH SCOPE FIX: IsSpawnedPlaneShip now matches hull=='plane_strike_1'
(all planes consistently; was exact-name match vs first plane only).
LOG SPAM FIX: _aaCatLogged diagnostic never latched (fired per part per
scan) - removed; categories confirmed. VERBOSE gate (default false) on
5s scene scans, TechTorpedoGrade per-call, torp launch traces, gate
blocks, PreInit arm spam. Strikes/waves/AA hits+kills/errors always log.
OPTIMIZATION: CountSecondaries cached per-ship (10s TTL, cleared per
battle) - eliminates ~1200 CalcCategory calls per AA cycle in big raids.
Build: 0 warnings, 0 errors.

USER TUNE ROUND (19:00): plane HP 2 -> 3; release 1250 -> 900m;
torpedo_drop_speed 90 -> 135 (+50%); tube calibers 2.5/3.0/3.5 ->
2.8/3.35/3.9 for ~+25% damage (quadratic warhead assumption; speed
untouched - explicit base speeds + boost patch). Verify: hits ~3000 ->
~3750 on same target class.

REPLAY BUG: RESOLVED (21:27 session). Three consecutive battles incl.
replays, zero plane entries in any PrepareBattle spawn list, zero NREs.
Decisive evidence: purge caught 2 plane AMOUNT entries at boot
('amounts 1/13' -> removed) - the nested-dict + vanilla-layer fix was the
layer that mattered; TAF's prep regenerates designs from those counts, so
removing the type key starves the whole chain. Downstream layers (retire
everywhere, save-scrub, teardown, rebuild skip) stand as defense in depth.
Remaining cosmetic: AirWingTorpedoTubes localization errors in designer
(harmless, pre-existing since v1.2).
