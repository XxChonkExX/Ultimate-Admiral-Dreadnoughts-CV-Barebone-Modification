using System;
using System.Collections;
using MelonLoader;
using HarmonyLib;
using Il2Cpp;

[assembly: MelonInfo(typeof(CarrierMod.CarrierMod), "UADCarrier Registry Patch", "1.0.0", "UADCarrier")]
[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]

namespace CarrierMod
{
    /// <summary>
    /// Registers data-registry entries the vanilla CSV loader ignores:
    /// v1 = the "cv" ShipType (hulls, tubes, AI ratios already load from CSV).
    /// Fully additive: no game methods patched, no existing entries modified.
    /// Safe to run alongside TAF / EPFM / DIP. Any failure is logged and swallowed.
    /// All diagnostics mirror to CarrierMod.log (MelonLoader/Logs) to avoid Unity spam.
    /// </summary>
    public class CarrierMod : MelonMod
    {
        private static string _logPath = null;
        private static readonly object _logLock = new object();
        // Release default: quiet. High-frequency diagnostics (5s scene scans,
        // per-call tech-remap + torpedo launch traces, gate blocks) only fire
        // when VERBOSE is true; state transitions/strikes/errors always log.
        private static void LogVerbose(string msg)
        {
            if (VERBOSE) Log(msg);
        }

        public static void Log(string msg)
        {
            try
            {
                MelonLogger.Msg(msg);
                lock (_logLock)
                {
                    if (_logPath == null)
                    {
                        string dir = null;
                        try { dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
                        // Mods folder -> parent is game root; Logs under MelonLoader/Logs.
                        try
                        {
                            string modsDir = System.IO.Path.GetDirectoryName(
                                System.Reflection.Assembly.GetExecutingAssembly().Location);
                            string gameRoot = System.IO.Directory.GetParent(modsDir).FullName;
                            dir = System.IO.Path.Combine(gameRoot, "MelonLoader", "Logs");
                        }
                        catch { }
                        if (dir == null) return;
                        _logPath = System.IO.Path.Combine(dir, "CarrierMod.log");
                    }
                    System.IO.File.AppendAllText(_logPath,
                        System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + System.Environment.NewLine);
                }
            }
            catch { }
        }
        // ===== CONFIG =====
        // Mods/CarrierModConfig.csv: 'key,value' lines ('#' comments). Missing
        // file -> defaults below AND a documented template is written. Unknown
        // keys are ignored (forward compatible). All values try/catch-guarded:
        // a broken config can only ever fall back to tuned defaults.
        private static bool VERBOSE = false;
        private static float MK1_SPEED = 90f, MK2_SPEED = 110f, MK3_SPEED = 130f;
        private static float PLANE_DROP_SPEED = 135f;   // effective, before any boost
        private static float PLANE_TORP_BOOST = 1f;    // extra multiplier on plane drops
        private static float TORP_LIFE = 60f;
        private static float RELEASE_DIST = 900f;
        private static float REARM_DIST = 150f;
        private static float REARM_TIME = 135f;
        private static float AA_RANGE = 2500f, AA_CYCLE = 2.5f, AA_CRIT = 0.20f, AA_HIT_MULT = 0.95f;
        private static int PLANE_HP = 3, AA_MAX_ROLLS = 6;
        private static float WAVE_GAP = 30f, LAUNCH_GAP = 3f, WAVE_TIME_LIMIT = 600f;

        private static float SpeedForMark(string mark)
        {
            if (mark == "torpedo_x6") return MK1_SPEED;
            if (mark == "torpedo_x7") return MK2_SPEED;
            if (mark == "torpedo_x8") return MK3_SPEED;
            return MK2_SPEED;
        }
        private static float FactorForTube(string tubeName)
        {
            if (tubeName == "torpedo_x6") return 2.0f;
            if (tubeName == "torpedo_x7") return 2.5f;
            return 3.0f;
        }

        private static void LoadConfig()
        {
            string dir = null;
            try { dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
            if (dir == null) return;
            string cfgPath = System.IO.Path.Combine(dir, "CarrierModConfig.csv");
            try
            {
                if (!System.IO.File.Exists(cfgPath))
                {
                    System.IO.File.WriteAllLines(cfgPath, new string[]
                    {
                        "# CarrierMod configuration ('key,value' lines; '#' = comment).",
                        "# Delete a line to fall back to the default shown here.",
                        "# File is read once at game start; changes need a game restart.",
                        "verbose,false",
                        "# Plane flight speeds (map units/s) per air-wing mark:",
                        "mk1_plane_speed,90",
                        "mk2_plane_speed,110",
                        "mk3_plane_speed,130",
                        "# Plane torpedo: EFFECTIVE drop speed (u/s) + extra multiplier.",
                        "# Effective = drop_speed x tube_factor x boost (defaults: 90).",
                        "torpedo_drop_speed,135",
                        "plane_torpedo_boost,1.0",
                        "torpedo_life,60",
                        "# Strike geometry: release range, RTB distance, deck cycle (s).",
                        "release_distance,900",
                        "rearm_distance,150",
                        "rearm_time,135",
                        "# Anti-air: envelope (m), volley cycle (s), plane HP, one-shot",
                        "# crit chance, global hit-chance multiplier, max rolls/ship.",
                        "aa_range,2500",
                        "aa_cycle,2.5",
                        "aa_plane_hp,3",
                        "aa_crit_chance,0.20",
                        "aa_hit_mult,0.95",
                        "aa_max_rolls,6",
                        "# Squadron waves: gap between waves (s), deck launch cadence (s),",
                        "# wave planning time cap (s).",
                        "wave_gap,30",
                        "launch_gap,3",
                        "wave_time_limit,600",
                        "# After a custom carrier battle, disable the results-screen",
                        "# replay button (its reload freezes on plane divisions).",
                        "disable_replay_button,true",
                    });
                }
            }
            catch { }
            try
            {
                if (!System.IO.File.Exists(cfgPath)) return;
                foreach (var raw in System.IO.File.ReadAllLines(cfgPath))
                {
                    try
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                        var sep = line.IndexOf(',');
                        if (sep < 0) sep = line.IndexOf('=');
                        if (sep < 0) continue;
                        var key = line.Substring(0, sep).Trim().ToLowerInvariant();
                        var val = line.Substring(sep + 1).Trim();
                        if (val.Length == 0) continue;
                        switch (key)
                        {
                            case "verbose": VERBOSE = val == "true" || val == "1" || val == "yes"; break;
                            case "mk1_plane_speed": MK1_SPEED = float.Parse(val); break;
                            case "mk2_plane_speed": MK2_SPEED = float.Parse(val); break;
                            case "mk3_plane_speed": MK3_SPEED = float.Parse(val); break;
                            case "torpedo_drop_speed": PLANE_DROP_SPEED = float.Parse(val); break;
                            case "plane_torpedo_boost": PLANE_TORP_BOOST = float.Parse(val); break;
                            case "torpedo_life": TORP_LIFE = float.Parse(val); break;
                            case "release_distance": RELEASE_DIST = float.Parse(val); break;
                            case "rearm_distance": REARM_DIST = float.Parse(val); break;
                            case "rearm_time": REARM_TIME = float.Parse(val); break;
                            case "aa_range": AA_RANGE = float.Parse(val); break;
                            case "aa_cycle": AA_CYCLE = float.Parse(val); break;
                            case "aa_plane_hp": PLANE_HP = int.Parse(val); break;
                            case "aa_crit_chance": AA_CRIT = float.Parse(val); break;
                            case "aa_hit_mult": AA_HIT_MULT = float.Parse(val); break;
                            case "aa_max_rolls": AA_MAX_ROLLS = int.Parse(val); break;
                            case "wave_gap": WAVE_GAP = float.Parse(val); break;
                            case "launch_gap": LAUNCH_GAP = float.Parse(val); break;
                            case "wave_time_limit": WAVE_TIME_LIMIT = float.Parse(val); break;
                            case "disable_replay_button": DISABLE_REPLAY_BUTTON = val == "true" || val == "1" || val == "yes"; break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            try
            {
                Log("[CarrierMod] config: mk " + MK1_SPEED + "/" + MK2_SPEED + "/" + MK3_SPEED +
                    " | drop " + PLANE_DROP_SPEED + "x" + PLANE_TORP_BOOST +
                    " | release " + RELEASE_DIST + " | rearm " + REARM_TIME + "s" +
                    " | AA range " + AA_RANGE + " hp " + PLANE_HP + " mult " + AA_HIT_MULT +
                    " | waves " + WAVE_GAP + "/" + LAUNCH_GAP + "s" +
                    (VERBOSE ? " | verbose" : ""));
            }
            catch { }
        }

        public override void OnLateInitializeMelon()
        {
            try
            {
                LoadConfig();
                // Fresh log per game run: multi-run appends made crash diagnosis
                // ambiguous. Truncate here (GameData wait = process start).
                try
                {
                    string modsDir = System.IO.Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string gameRoot = System.IO.Directory.GetParent(modsDir).FullName;
                    _logPath = System.IO.Path.Combine(gameRoot, "MelonLoader", "Logs", "CarrierMod.log");
                    System.IO.File.WriteAllText(_logPath,
                        System.DateTime.Now.ToString("HH:mm:ss.fff") + " [CarrierMod] log start v1.3-seed" + System.Environment.NewLine);
                }
                catch { }
                Log("[CarrierMod] waiting for GameData...");
                try
                {
                    var hm = new HarmonyLib.Harmony("UADCarrier.RegistryPatch");
                    // Feature gates (all round-trip validated; kept for quick
                    // bisection if a future game patch breaks something).
                    const bool UI_REFRESH = true;
                    const bool UI_INTMAP = true;
                    // Per-method patching: one missing method must not kill the rest.
                    PatchOne(hm, "PartData.PostProcess", typeof(PartData), "PostProcess", null, typeof(Patch_TorpedoPostProcess));
                    PatchOne(hm, "Ship.TechTorpedoGrade", typeof(Ship), "TechTorpedoGrade", null, typeof(Patch_TorpedoGrade));
                    PatchOne(hm, "Ship.Init", typeof(Ship), "Init", null, typeof(Patch_ShipInit));
                    PatchOne(hm, "Ship.Create", typeof(Ship), "Create", null, typeof(Patch_ShipCreate));
                    PatchOne(hm, "Part.CalcCategory", typeof(Part), "CalcCategory", null, typeof(Patch_TorpedoCategory));
                    PatchOne(hm, "Ui.ChoosePart", typeof(Ui), "ChoosePart", typeof(Patch_ChoosePartLog), null);
                    PatchOne(hm, "Ui.PreChoosePart", typeof(Ui), "PreChoosePart", typeof(Patch_PreChoosePartLog), null);
                    PatchOne(hm, "Part.LoadModel", typeof(Part), nameof(Part.LoadModel), typeof(Patch_PartLoadModel), null);
                    PatchOne(hm, "Ui.OnClick", typeof(Ui), "OnClick", typeof(Patch_UiOnClick), null);
                    if (UI_REFRESH)
                    PatchOne(hm, "Ui.RefreshConstructorInfo", typeof(Ui), "RefreshConstructorInfo", null, typeof(Patch_RefreshConstructorInfo));
                    if (UI_INTMAP)
                    {
                    PatchOne(hm, "Ui.GetShipTypeFromIntNumber", typeof(Ui), "GetShipTypeFromIntNumber", typeof(Patch_IntToType), null);
                    PatchOne(hm, "Ui.GetIntNumberFromShipType", typeof(Ui), "GetIntNumberFromShipType", typeof(Patch_TypeToInt), null);
                    PatchOne(hm, "Ui.IsShipTypeActiveForCurrentPlayer", typeof(Ui), "IsShipTypeActiveForCurrentPlayer", null, typeof(Patch_IsActive));
                    PatchOne(hm, "Ui.IsPlayerShipTypeActive", typeof(Ui), "IsPlayerShipTypeActive", null, typeof(Patch_IsActive));
                    PatchOne(hm, "Ui.IsEnemyShipTypeActive", typeof(Ui), "IsEnemyShipTypeActive", null, typeof(Patch_IsActive));
                    }
                    PatchOne(hm, "Ui.InitialCustomBattleShipTypeButtonAndShips", typeof(Ui), "InitialCustomBattleShipTypeButtonAndShips", null, typeof(Patch_SkirmishButtons));
                    PatchOne(hm, "Ui.SkirmishSetupInit", typeof(Ui), "SkirmishSetupInit", null, typeof(Patch_SkirmishInit));
                    PatchOne(hm, "BattleManager.PreInitCustomBattle", typeof(BattleManager), "PreInitCustomBattle", typeof(Patch_PreInitBattle), null);
                    // REPLAY path: UpdateLoadingCustomBattleFromSave/InitCustomBattleFromSave
                    // does NOT call PreInitCustomBattle — without this hook the
                    // TAF purge + battle reset never run on replays.
                    PatchOne(hm, "BattleManager.InitCustomBattleFromSave", typeof(BattleManager), "InitCustomBattleFromSave", typeof(Patch_PreInitBattle), null);
                    // REINIT GATE: TAF rebuilds SkPlayer designs as real ships
                    // here; purge first so planes never reach the builder.
                    try
                    {
                        var tafSetupT = SafeFindType("TweaksAndFixes", "TweaksAndFixes.UiM+SkirmishSetupMod");
                        var reinitM = tafSetupT != null ? HarmonyLib.AccessTools.Method(tafSetupT, "InitializePlayerMadeShips") : null;
                        var reinitPre = HarmonyLib.AccessTools.Method(typeof(Patch_TafReinit), "Run");
                        if (reinitM != null && reinitPre != null)
                        {
                            hm.Patch(reinitM, new HarmonyLib.HarmonyMethod(reinitPre));
                            Log("[CarrierMod] patch OK: TAF InitializePlayerMadeShips (reinit gate)");
                        }
                        else Log("[CarrierMod] patch SKIP: TAF InitializePlayerMadeShips not found");
                    }
                    catch (Exception ex) { Log("[CarrierMod] reinit gate failed: " + ex.Message); }
                    // REBUILD GATE: skip plane designs at the per-ship rebuild.
                    try
                    {
                        var rbM = HarmonyLib.AccessTools.Method(typeof(BattleManager), "RebuildShipInSkirmish");
                        var rbPre = HarmonyLib.AccessTools.Method(typeof(Patch_RebuildShip), "Run");
                        if (rbM != null && rbPre != null)
                        {
                            hm.Patch(rbM, new HarmonyLib.HarmonyMethod(rbPre));
                            Log("[CarrierMod] patch OK: BattleManager.RebuildShipInSkirmish (plane skip)");
                        }
                        else Log("[CarrierMod] patch SKIP: RebuildShipInSkirmish not found");
                    }
                    catch (Exception ex) { Log("[CarrierMod] rebuild gate failed: " + ex.Message); }
                    // TEARDOWN ERASE: erase our planes via the game's own API
                    // at every battle exit, before teardown sweeps run.
                    PatchOne(hm, "CampaignController.CleanupShips", typeof(CampaignController), "CleanupShips", typeof(Patch_TeardownErase), null);
                    // SAVE SCRUB: the custom-battle fleet save snapshots LIVE
                    // battle ships — planes must already be gone when it runs.
                    PatchOne(hm, "BattleManager.CustomBattleSavePlayerDesigns", typeof(BattleManager), "CustomBattleSavePlayerDesigns", typeof(Patch_SaveScrub), null);
                    // Wing-only strike boost: Torpedo.Create(Part from, ...) — the
                    // Part arg identifies the firing tube, so the boost is
                    // per-tube and cannot leak to ship torpedoes. Param name
                    // discovered from Harmony error output: "from".
                    bool torpPatched = false;
                    {
                        try
                        {
                            var orig = HarmonyLib.AccessTools.Method(typeof(Torpedo), "Create");
                            var post = HarmonyLib.AccessTools.Method(typeof(Patch_TorpedoCreate), "Run");
                            var gate = HarmonyLib.AccessTools.Method(typeof(Patch_TorpedoCreate), "Gate");
                            if (orig != null && post != null && gate != null)
                            {
                                hm.Patch(orig, new HarmonyLib.HarmonyMethod(gate), new HarmonyLib.HarmonyMethod(post));
                                Log("[CarrierMod] patch OK: Torpedo.Create (param=from, wing gate)");
                                torpPatched = true;
                            }
                        }
                        catch (Exception ex) { Log("[CarrierMod] Torpedo.Create param=from failed: " + ex.Message); }
                    }
                    if (!torpPatched) Log("[CarrierMod] patch FAIL: Torpedo.Create");
                    // Wing boost fallback: Ship.ModifySpeedTorpedo (blittable
                    // float + instance ref = patch-safe). Ship-level: carriers
                    // launch only wings, so a ship-level multiplier is wing-only
                    // in practice. Uses max mark present on the ship.
                    // REMOVED: ModifySpeedTorpedo is STATIC (cannot scope to carriers) and never fired; per-tube Torpedo.Create boost supersedes it.
                    // REVERTED (startup crash: IL2CPP detours on VesselEntity range methods unstable).
                    // Plane battle-load unblock: LoadUnloadBattle NREs inside
                    // StatEffectPrivate (plane's stat collections never seeded
                    // by battle init). Answer op-range queries for OUR plane
                    // only; every other ship keeps vanilla behavior.
                    // Ghidra: GetMax/MinRangeWhitStatEffectInKm return INT and
                    // are declared on VesselEntity (Ship inherits); patch at
                    // Ship level with int __result works (int-typed, name-
                    // scoped to our plane only).
                    PatchOne(hm, "Ship.GetMaxRangeWhitStatEffectInKm", typeof(Ship), "GetMaxRangeWhitStatEffectInKm", typeof(Patch_PlaneMaxRange), null);
                    PatchOne(hm, "Ship.GetMinRangeWhitStatEffectInKm", typeof(Ship), "GetMinRangeWhitStatEffectInKm", typeof(Patch_PlaneMinRange), null);
                    Log("[CarrierMod] harmony patches applied (see per-patch lines).");
                    Log("[CarrierMod] replay kill armed (disable_replay_button=" + DISABLE_REPLAY_BUTTON + ").");
                }
                catch (Exception hex)
                {
                    MelonLogger.Warning("[CarrierMod] harmony patch failed (non-fatal): " + hex.Message);
                }
                MelonCoroutines.Start(WaitAndRegister());
                // Eternal spawn watcher: covers ALL battle modes from boot
                // (custom + campaign + missions). See SpawnPlanesWhenReady.
                EnsureSpawnWatcher();
                // One-shot purge once TAF + GameData are live (boot leftovers).
                MelonCoroutines.Start(StartupPurgeWhenReady());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[CarrierMod] init failed (non-fatal): " + ex.Message);
            }
        }

        private IEnumerator WaitAndRegister()
        {
            // Wait until vanilla has populated shipTypes (game init, before MainMenu).
            int tries = 0;
            while (tries < 600) // ~10s at 60fps
            {
                try
                {
                    var gd = G.GameData;
                    if (gd != null && gd.shipTypes != null && gd.shipTypes.Count > 0)
                        break;
                }
                catch { /* GameData not ready yet */ }
                tries++;
                yield return null;
            }

            try
            {
                RegisterShipTypeCv();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[CarrierMod] registration failed (non-fatal): " + ex);
            }
            yield break;
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            try
            {
                // Inject BEFORE Awake-time wiring runs: if the picker wires buttons
                // by enumerating existing children, our ShipTypeCV clone gets wired
                // like a native button. This is the earliest hook MelonLoader offers.
                if (sceneName == "Constructor")
                {
                    Log("[CarrierMod] scene init (pre-Awake): attempting early clone.");
                    MelonCoroutines.Start(InjectCvButtonEarly());
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[CarrierMod] pre-Awake hook failed (non-fatal): " + ex.Message);
            }
        }

        private IEnumerator InjectCvButtonEarly()
        {
            // Run immediately (no frame wait): UI hierarchy exists at init time,
            // but Awake wiring hasn't run yet.
            yield return null; // single frame to let hierarchy settle
            try
            {
                var ui = G.ui;
                if (ui == null || ui.conShipTypeButtons == null)
                {
                    Log("[CarrierMod] early inject: picker unavailable yet");
                    yield break;
                }
                var cont = ui.conShipTypeButtons;
                UnityEngine.Transform btnParent = cont.transform;
                if (btnParent.childCount == 1)
                {
                    var only = btnParent.GetChild(0);
                    if (only.childCount > 0) btnParent = only;
                }
                // Skip if already injected.
                for (int ci = 0; ci < btnParent.childCount; ci++)
                {
                    if (btnParent.GetChild(ci).gameObject.name == "ShipTypeCV")
                    {
                        Log("[CarrierMod] early inject: ShipTypeCV already present");
                        yield break;
                    }
                }
                UnityEngine.GameObject template = null;
                for (int ci = 0; ci < btnParent.childCount; ci++)
                {
                    var ch = btnParent.GetChild(ci).gameObject;
                    if (ch.activeSelf) { template = ch; break; }
                }
                if (template == null)
                {
                    Log("[CarrierMod] early inject: no template found");
                    yield break;
                }
                var cvBtn = UnityEngine.Object.Instantiate(template, btnParent);
                cvBtn.name = "ShipTypeCV";
                cvBtn.SetActive(true);
                Log("[CarrierMod] early inject: ShipTypeCV cloned pre-Awake");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[CarrierMod] early inject failed: " + ex.Message);
            }
            yield break;
        }

        // Disarm when we leave battle contexts (main menu), so the next battle's
        // first arm resets the guard again.
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            try
            {
                // Disarm between battles: main-menu/loadout scenes clear the arm
                // flag so the next battle's first PreInit resets the spawn guard.
                if (sceneName == "MainMenu") { _wasArmed = false; }
                // Keep TAF's per-mode snapshot in sync (covers startup race).
                try { InjectIntoTafSnapshot(); } catch { }
                // Same for the vanilla skirmish dicts (idempotent).
                try { TryInjectVanillaSkirmish(); } catch { }
                // (Plane spawn watcher now starts from ArmSpawnExperiment —
                // scene-load start raced ahead of the arming and exited.)
                // On every scene load, look for custom-battle setup class rows
                // (e.g. "0 Battleship Classes") to map that UI's structure.
                try { ScanForSetupRows(); } catch { }
                // Re-assert cv on every Constructor enter: covers startup insertion
                // plus any later rebuild of shipTypes (e.g. TAF reload) that would
                // drop it. Logs the live key set for diagnostics.
                // PIVOT (CA platform): the dedicated-CV picker effort is retired.
                // Carriers ship as type(ca) hulls (visible under Heavy Cruiser);
                // cloning a ShipTypeCV button only added an unselectable
                // duplicate. Early inject stays off; registration kept (harmless,
                // future option) as is the torpedo/air-wing group.
                if (false && sceneName == "Constructor")
                    {
                    try { _guidErrLogged = false; } catch { }
                        try
                        {
                            _wiringHookActive = true;
                            _wiringHits = 0;
                            MelonCoroutines.Start(DisableWiringHookAfter(8f));
                        }
                        catch { }
                        try { DumpTorpedoState(); } catch { }
                // STAGE-1 EXPERIMENT REMOVED (crash on air-wing placement):
                // plane shipType + hull row + PrepareBattle patch + spawn
                // coroutine all bisected out. Reintroduce ONE at a time.
                    var gd = G.GameData;
                    if (gd != null && gd.shipTypes != null)
                    {
                        var keys = new System.Collections.Generic.List<string>();
                        foreach (var k in gd.shipTypes.Keys) keys.Add(k);
                        Log("[CarrierMod] Constructor enter; shipTypes: " + string.Join(",", keys.ToArray()));
                        if (!gd.shipTypes.ContainsKey("cv"))
                        {
                            Log("[CarrierMod] cv missing at Constructor enter, re-registering.");
                            RegisterShipTypeCv();
                        }
                        // Diagnostic: log canBuild per type to see if cv is filtered.
                        try
                        {
                            foreach (var kv in gd.shipTypes)
                            {
                                bool cb = false;
                                try { cb = kv.Value.canBuild; } catch { cb = false; }
                                Log("[CarrierMod] type " + kv.Key + " canBuild=" + cb);
                            }
                        }
                        catch (Exception ex2)
                        {
                            MelonLogger.Warning("[CarrierMod] canBuild dump failed: " + ex2.Message);
                        }
                        // Picker injection is handled by Patch_ConstructorUI (post-
                        // vanilla-wiring, correctly wired via Ui.OnClick). The old
                        // late coroutine clone is retired: it created duplicate
                        // ShipTypeCV buttons.
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[CarrierMod] scene hook failed (non-fatal): " + ex.Message);
            }
        }

        private IEnumerator InjectCvButton()
        {
            // Wait for the designer UI to finish building its picker.
            for (int w = 0; w < 30; w++) yield return null;
            try
            {
                var ui = G.ui;
                if (ui == null || ui.conShipTypeButtons == null)
                {
                    Log("[CarrierMod] inject: conShipTypeButtons unavailable");
                    yield break;
                }
                var cont = ui.conShipTypeButtons;
                // Descend into Layout container which holds the actual type buttons.
                UnityEngine.Transform btnParent = cont.transform;
                if (btnParent.childCount == 1)
                {
                    var only = btnParent.GetChild(0);
                    Log("[CarrierMod] inject: single child " + only.gameObject.name + ", descending");
                    if (only.childCount > 0) btnParent = only;
                }
                int n = btnParent.childCount;
                Log("[CarrierMod] inject: picker buttons=" + n);
                UnityEngine.GameObject template = null;
                for (int ci = 0; ci < n; ci++)
                {
                    var ch = btnParent.GetChild(ci).gameObject;
                    Log("[CarrierMod] inject: child " + ci + " name=" + ch.name + " active=" + ch.activeSelf);
                    if (template == null && ch.activeSelf) template = ch;
                    // Log component presence on first button using typed lookups.
                    if (ci == 0)
                    {
                        try
                        {
                            Log("[CarrierMod] inject: child0 Button=" + (ch.GetComponent<UnityEngine.UI.Button>() != null)
                                + " Text=" + (ch.GetComponent<UnityEngine.UI.Text>() != null)
                                + " Image=" + (ch.GetComponent<UnityEngine.UI.Image>() != null));
                            // True Il2Cpp runtime types (bypasses managed base-wrapper GetType).
                            try
                            {
                                var arr = ch.GetComponents<UnityEngine.Component>();
                                for (int ai = 0; ai < arr.Length; ai++)
                                {
                                    var comp = arr[ai];
                                    if (comp == null) continue;
                                    System.IntPtr ptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtr(comp);
                                    System.IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(ptr);
                                    System.IntPtr namePtr = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name(klass);
                                    string tname = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(namePtr);
                                    Log("[CarrierMod] inject: child0 true-type[" + ai + "]=" + tname);
                                }
                            }
                            catch (Exception ext)
                            {
                                MelonLogger.Warning("[CarrierMod] inject: true-type enum failed: " + ext.Message);
                            }
                            var btn = ch.GetComponent<UnityEngine.UI.Button>();
                            if (btn != null)
                            {
                                int pc = 0;
                                try { pc = btn.onClick.GetPersistentEventCount(); } catch { }
                                Log("[CarrierMod] inject: child0 onClick persistent=" + pc);
                                // Runtime (code-added) listeners via UnityEventBase internals.
                                try
                                {
                                    var evt = (object)btn.onClick;
                                    var t = evt.GetType();
                                    int runtime = -1;
                                    // UnityEventBase -> m_Calls (InvokableCallList) -> m_RuntimeCalls (List)
                                    var callsF = t.GetField("m_Calls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                    // Walk base types for UnityEventBase private fields.
                                    System.Type bt = t;
                                    object calls = null;
                                    while (bt != null && calls == null)
                                    {
                                        var ff = bt.GetField("m_Calls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        if (ff != null) calls = ff.GetValue(evt);
                                        bt = bt.BaseType;
                                    }
                                    if (calls != null)
                                    {
                                        var rtF = calls.GetType().GetField("m_RuntimeCalls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        if (rtF == null)
                                        {
                                            // Il2Cpp backing: try property or alternate name.
                                            foreach (var ff in calls.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                                                Log("[CarrierMod] inject: calls field " + ff.Name + " : " + ff.FieldType.Name);
                                        }
                                        else
                                        {
                                            var lst = rtF.GetValue(calls) as System.Collections.IList;
                                            runtime = lst != null ? lst.Count : -2;
                                        }
                                    }
                                    Log("[CarrierMod] inject: child0 onClick runtime=" + runtime);
                                }
                                catch (Exception exr)
                                {
                                    MelonLogger.Warning("[CarrierMod] inject: runtime listener inspect failed: " + exr.Message);
                                }
                            }
                        }
                        catch (Exception exc)
                        {
                            MelonLogger.Warning("[CarrierMod] inject: button inspect failed: " + exc.Message);
                        }
                    }
                }
                if (template == null)
                {
                    Log("[CarrierMod] inject: no active template button found");
                    yield break;
                }
                // Clone template for cv, following the ShipTypeXX naming pattern
                // in case selection resolves by button name.
                var cvBtn = UnityEngine.Object.Instantiate(template, btnParent);
                cvBtn.name = "ShipTypeCV";
                cvBtn.SetActive(true);
                Log("[CarrierMod] inject: cloned button as " + cvBtn.name);
                // Retarget any ShipType-typed fields to cv.
                try
                {
                    var gd = G.GameData;
                    object cvObj = null;
                    if (gd != null && gd.shipTypes != null && gd.shipTypes.ContainsKey("cv"))
                        cvObj = gd.shipTypes["cv"];
                    if (cvObj != null)
                    {
                        foreach (var c in cvBtn.GetComponents<UnityEngine.Component>())
                        {
                            if (c == null) continue;
                            var t = c.GetType();
                            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                            {
                                try
                                {
                                    if (f.FieldType.FullName != null && f.FieldType.FullName.EndsWith("ShipType"))
                                    {
                                        f.SetValue(c, cvObj);
                                        Log("[CarrierMod] inject: set field " + t.Name + "." + f.Name + " to cv");
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    else
                    {
                        Log("[CarrierMod] inject: cv object not found, button cloned but not retargeted");
                    }
                }
                catch (Exception exr)
                {
                    MelonLogger.Warning("[CarrierMod] inject retarget failed: " + exr.Message);
                }
                // Relabel via TMP_Text (runtime-resolved, no compile-time dependency).
                try
                {
                    bool relabeled = false;
                    System.Type tmpType = null;
                    try { tmpType = SafeFindType("Unity.TextMeshPro", "TMPro.TMP_Text"); } catch { }
                    if (tmpType != null)
                    {
                        var textProp = HarmonyLib.AccessTools.Property(tmpType, "text");
                        if (textProp != null && textProp.CanWrite)
                        {
                            foreach (var c in cvBtn.GetComponentsInChildren<UnityEngine.Component>(true))
                            {
                                if (c == null) continue;
                                try
                                {
                                    string full = null;
                                    try { full = c.GetIl2CppType().FullName; } catch { }
                                    if (full != null && full.Contains("TMP_Text"))
                                    {
                                        textProp.SetValue(c, "CV");
                                        Log("[CarrierMod] inject: relabeled TMP_Text to CV");
                                        relabeled = true;
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            Log("[CarrierMod] inject: TMP_Text.text property not found");
                        }
                    }
                    else
                    {
                        Log("[CarrierMod] inject: TMPro.TMP_Text type not found");
                    }
                    if (!relabeled)
                    {
                        foreach (var c in cvBtn.GetComponentsInChildren<UnityEngine.Component>(true))
                        {
                            if (c == null) continue;
                            try
                            {
                                var ut = c.TryCast<UnityEngine.UI.Text>();
                                if (ut != null)
                                {
                                    ut.text = "CV";
                                    Log("[CarrierMod] inject: relabeled UI.Text to CV");
                                    relabeled = true;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    if (!relabeled) Log("[CarrierMod] inject: no text component found to relabel");
                    // Log clone rect/visibility for render confirmation.
                    try
                    {
                        var rt = cvBtn.GetComponent<UnityEngine.RectTransform>();
                        if (rt != null)
                            Log("[CarrierMod] inject: clone rect pos=" + rt.anchoredPosition.ToString() + " size=" + rt.sizeDelta.ToString() + " scale=" + cvBtn.transform.localScale.ToString());
                    }
                    catch { }
                }
                catch (Exception exl)
                {
                    MelonLogger.Warning("[CarrierMod] inject relabel failed: " + exl.Message);
                }
                Log("[CarrierMod] inject: done");
                // Click probe: prove the clone is interactive regardless of copied listeners.
                try
                {
                    var probeBtn = cvBtn.GetComponent<UnityEngine.UI.Button>();
                    if (probeBtn != null)
                    {
                        System.Action managed = () =>
                        {
                            Log("[CarrierMod] PROBE: CV clone clicked!");
                        };
                        var il2cppAction = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(managed);
                        probeBtn.onClick.AddListener(il2cppAction);
                        Log("[CarrierMod] inject: click probe attached");
                    }
                }
                catch (Exception exp)
                {
                    MelonLogger.Warning("[CarrierMod] inject probe failed: " + exp.Message);
                }
                // Force layout rebuild so the new button is positioned, then log all positions.
                try
                {
                    var layoutParent = btnParent;
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(layoutParent as UnityEngine.RectTransform);
                    for (int pi = 0; pi < layoutParent.childCount; pi++)
                    {
                        var pch = layoutParent.GetChild(pi).gameObject;
                        var prt = pch.GetComponent<UnityEngine.RectTransform>();
                        string ps = prt != null ? prt.anchoredPosition.ToString() : "?";
                        Log("[CarrierMod] inject: layout child " + pi + " " + pch.name + " pos=" + ps + " active=" + pch.activeSelf);
                    }
                }
                catch (Exception exlay)
                {
                    MelonLogger.Warning("[CarrierMod] inject layout rebuild failed: " + exlay.Message);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[CarrierMod] inject failed: " + ex.Message);
            }
            yield break;
        }

        // Ground truth for the "Must place on floor" failure: are our torpedo
        // rows actually present in the live registries, and how do they parse?
        // NOTE: Il2Cpp properties can't be read via reflection (GetValue throws),
        // so this uses direct compile-time access like TAF does.
        private static void DumpTorpedoState()
        {
            try
            {
                var gd = G.GameData;
                if (gd == null) { Log("[CarrierMod] diag: GameData null"); return; }
                // Enumerate GameData members containing torp/model/part for the tube container.
                try
                {
                    var t = gd.GetIl2CppType();
                    foreach (var f in t.GetFields())
                    {
                        string fn = "";
                        try { fn = f.Name.ToLowerInvariant(); } catch { continue; }
                        if (fn.Contains("torp") || fn.Contains("tube"))
                            Log("[CarrierMod] diag: GameData field " + f.Name + " : " + f.FieldType.Name);
                    }
                }
                catch (Exception ex) { Log("[CarrierMod] diag: field enum failed: " + ex.Message); }
                foreach (var nm in new string[] { "torpedo_x1", "torpedo_x2", "torpedo_x6", "torpedo_x7", "torpedo_x8" })
                {
                    try
                    {
                        PartData pd = null;
                        bool has = false;
                        try { has = gd.parts.ContainsKey(nm); if (has) pd = gd.parts[nm]; } catch (Exception ex) { Log("[CarrierMod] diag: parts read failed: " + ex.Message); break; }
                        if (!has) { Log("[CarrierMod] diag: parts missing " + nm); continue; }
                        string mp = "?", st = "?", torp = "?", hull = "?";
                        try { mp = pd.mountPoints; } catch { }
                        try { st = pd.stats; } catch { }
                        try { torp = pd.isTorpedo.ToString(); } catch { }
                        try { hull = pd.isHull.ToString(); } catch { }
                        Log("[CarrierMod] diag: " + nm + " mounts=[" + mp + "] stats=" + st + " isTorpedo=" + torp + " isHull=" + hull);
                    }
                    catch { }
                }
                foreach (var mn in new string[] { "torpedo_x2_15", "torpedo_x6_15", "torpedo_x7_15", "torpedo_x8_15" })
                {
                    try
                    {
                        bool has = false;
                        try { has = gd.partModels.ContainsKey(mn); } catch (Exception ex) { Log("[CarrierMod] diag: partModels read failed: " + ex.Message); break; }
                        Log("[CarrierMod] diag: partModels " + mn + "=" + has);
                    }
                    catch { }
                }
                // Null-scan 2: the partModels dicts. If pm.models (or scales etc.)
                // failed to build for our rows, GetModelNameScale's Any() dies.
                foreach (var mn in new string[] { "torpedo_x2_15", "torpedo_x6_15" })
                {
                    try
                    {
                        PartModelData pmd = null;
                        try { pmd = gd.partModels[mn]; } catch { continue; }
                        if (pmd == null) { Log("[CarrierMod] diag: " + mn + " row NULL"); continue; }
                        string mo = "?", sc = "?", mx = "?", wm = "?", cl = "?";
                        try { mo = pmd.models == null ? "NULL" : "n=" + pmd.models.Count; } catch (Exception ex) { mo = "ERR:" + ex.GetType().Name; }
                        try { sc = pmd.scales == null ? "NULL" : "n=" + pmd.scales.Count; } catch (Exception ex) { sc = "ERR:" + ex.GetType().Name; }
                        try { mx = pmd.maxScales == null ? "NULL" : "n=" + pmd.maxScales.Count; } catch (Exception ex) { mx = "ERR:" + ex.GetType().Name; }
                        try { wm = pmd.weightModifiers == null ? "NULL" : "n=" + pmd.weightModifiers.Count; } catch (Exception ex) { wm = "ERR:" + ex.GetType().Name; }
                        try { cl = pmd.caliberLengthModifiers == null ? "NULL" : "n=" + pmd.caliberLengthModifiers.Count; } catch (Exception ex) { cl = "ERR:" + ex.GetType().Name; }
                        Log("[CarrierMod] diag: " + mn + " models=" + mo + " scales=" + sc + " maxScales=" + mx + " weightMod=" + wm + " calLenMod=" + cl);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log("[CarrierMod] diag failed: " + ex.Message); }
        }

        private static string _lastScanSig = "";
        private static bool _wiringHookActive = false;
        private static int _wiringHits = 0;
        private static bool _guidErrLogged = false;

        private static void PatchOne(HarmonyLib.Harmony hm, string desc, System.Type target,
            string methodName, System.Type patchPrefix, System.Type patchPostfix)
        {
            try
            {
                var orig = HarmonyLib.AccessTools.Method(target, methodName);
                if (orig == null) { Log("[CarrierMod] patch SKIP (method not found): " + desc); return; }
                HarmonyLib.HarmonyMethod pre = null, post = null;
                if (patchPrefix != null)
                {
                    var m = HarmonyLib.AccessTools.Method(patchPrefix, "Run");
                    if (m != null) pre = new HarmonyLib.HarmonyMethod(m);
                }
                if (patchPostfix != null)
                {
                    var m = HarmonyLib.AccessTools.Method(patchPostfix, "Run");
                    if (m != null) post = new HarmonyLib.HarmonyMethod(m);
                }
                if (pre == null && post == null) { Log("[CarrierMod] patch SKIP (no Run): " + desc); return; }
                hm.Patch(orig, pre, post);
                Log("[CarrierMod] patch OK: " + desc);
            }
            catch (Exception ex)
            {
                Log("[CarrierMod] patch FAIL: " + desc + " : " + ex.Message);
            }
        }

        // Vanilla skirmish type count (int-threaded UI). cvIndex = count.
        // Discovered from customBattleButtonList size — NEVER by probing
        // GetShipTypeFromIntNumber out of bounds (an OOB call can hard-crash
        // IL2CPP instead of throwing). -1 = unknown, patches stay inert.
        private static int _cvIndex = -1;
        private static bool IsOurTorpedo(string nm)
        {
            return nm == "torpedo_x6" || nm == "torpedo_x7" || nm == "torpedo_x8";
        }
        private static int CvIndex()
        {
            if (_cvIndex >= 0) return _cvIndex;
            try
            {
                var ui = G.ui;
                if (ui == null) return -1;
                object listObj = GetMember(ui, "customBattleButtonList");
                if (listObj == null) return -1;
                int count = 0;
                try { count = (int)listObj.GetType().GetProperty("Count").GetValue(listObj, null); } catch { return -1; }
                if (count <= 0) return -1;
                _cvIndex = count;
                Log("[CarrierMod] cvIndex=" + count + " from button list.");
            }
            catch { }
            return _cvIndex;
        }
        private static bool IsCvIndex(int idx) { int c = CvIndex(); return c >= 0 && idx == c; }
        private static object CvObject()
        {
            try
            {
                var gd = G.GameData;
                if (gd != null && gd.shipTypes != null && gd.shipTypes.ContainsKey("cv"))
                    return gd.shipTypes["cv"];
            }
            catch { }
            return null;
        }
        private static IEnumerator EmptyCoroutine() { yield break; }

        // Air-wing parts (torpedo_x6/7/8) reuse vanilla tube meshes/stats.
        // Before the native LoadModel runs, point their model at torpedo_x2.
        [HarmonyPatch(typeof(Part), nameof(Part.LoadModel))]
        // NOTE (prime suspect for GetModelNameScale Any(null)): this prefix used
        // to rewrite data.model="torpedo_x2" for x6/x7/x8. If vanilla branches
        // on model=="(custom)" (name-based model lookup) vs a named model
        // (direct key lookup, which misses), OUR rewrite broke our own parts.
        // Now: legacy airwing names only; x6/x7/x8 keep "(custom)" like x2.
        private static class Patch_PartLoadModel
        {
            private static void Prefix(Part __instance)
            {
                try
                {
                    var data = __instance != null ? __instance.data : null;
                    var nm = data != null ? data.name : null;
                    if (nm != null && nm.StartsWith("torpedo_airwing_", StringComparison.Ordinal))
                    {
                        data.model = "torpedo_x2";
                    }
                }
                catch { }
            }
            // Harmony entry looked up by name "Run" by PatchOne.
            public static void Run(Part __instance) { Prefix(__instance); }
        }

        // Force torpedo identity for x6/x7/x8 in case PostProcess name-parsing
        // mis-derives isTorpedo/isHull for unknown _xN suffixes.
        private static class Patch_TorpedoPostProcess
        {
            public static void Run(PartData __instance)
            {
                try
                {
                    if (__instance == null) return;
                    string nm = null;
                    try { nm = __instance.name; } catch { }
                    if (nm != null && IsOurTorpedo(nm))
                    {
                        try { __instance.isTorpedo = true; } catch { }
                        try { __instance.isHull = false; } catch { }
                        Log("[CarrierMod] PostProcess forced torpedo identity: " + nm);
                    }
                }
                catch { }
            }
        }

        // Vanilla Ship.TechTorpedoGrade parses the grade from the part; unknown
        // _xN suffixes (x6/x7/x8) yield out-of-range grades and placement
        // rejects them ("Must place on floor"). Remap to our 1/2/3 marks.
        private static class Patch_TorpedoGrade
        {
            public static void Run(PartData torpedo, bool requireValid, ref int __result)
            {
                try
                {
                    if (torpedo == null) return;
                    string nm = null;
                    try { nm = torpedo.name; } catch { }
                    if (nm == null || !IsOurTorpedo(nm)) return;
                    int want = nm == "torpedo_x6" ? 1 : nm == "torpedo_x7" ? 2 : 3;
                    if (__result != want)
        LogVerbose("[CarrierMod] TechTorpedoGrade " + nm + " vanilla=" + __result + " valid=" + requireValid + " -> " + want);
                    // Unconditional: vanilla returns max/default (5) for unknown
                    // suffixes; models[5] is a nation-specific variant that may not
                    // exist here, breaking the placement ghost ("No Floor").
                    __result = want;
                }
                catch { }
            }
        }

        // Part.GetModelNameScale throws Any(null) for x6/x7/x8 (some name-keyed
        // collection inside the vanilla body). Answer with x2's data: call the
        // vanilla body once with valid args, copy the produced struct, skip the
        // crashing invocation. struct copy is managed-side; TAF texture cache
        // is seeded separately so no null is ever cached downstream.
        private static class Patch_ModelNameScale
        {
            public static bool Run(PartData data, Ship ship, Il2CppSystem.Nullable<int> gunGradeOverride, ref Part.ModelInfo __result)
            {
                try
                {
                    string nm = null;
                    try { nm = data != null ? data.name : null; } catch { }
                    if (nm == null || !IsOurTorpedo(nm)) return true;
                    try
                    {
                        var x2 = G.GameData.parts["torpedo_x2"];
                        if (x2 == null) return true;
                        __result = Part.GetModelNameScale(x2, ship, gunGradeOverride);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        Log("[CarrierMod] ModelNameScale v2 failed: " + ex.Message);
                        return true;
                    }
                }
                catch { }
                return true;
            }
        }

        // If category resolution yields hull/null for our tubes, recompute via x2.
        private static class Patch_TorpedoCategory
        {
            public static void Run(Ship ship, PartData partData, ref PartCategoryData __result)
            {
                try
                {
                    if (partData == null) return;
                    string nm = null;
                    try { nm = partData.name; } catch { }
                    if (nm == null || !IsOurTorpedo(nm)) return;
                    string rc = "null";
                    try { rc = __result != null ? __result.name : "null"; } catch { }
                    Log("[CarrierMod] CalcCategory " + nm + " -> " + rc);
                    if (__result == null)
                    {
                        try
                        {
                            var gd = G.GameData;
                            object x2obj = null;
                            if (gd != null)
                            {
                                var dict = GetMember(gd, "parts") as System.Collections.IDictionary;
                                if (dict != null && dict.Contains("torpedo_x2")) x2obj = dict["torpedo_x2"];
                            }
                            if (x2obj is PartData)
                            {
                                __result = Part.CalcCategory(ship, (PartData)x2obj);
                                Log("[CarrierMod] CalcCategory " + nm + " remapped via x2.");
                            }
                        }
                        catch (Exception ex) { Log("[CarrierMod] CalcCategory remap failed: " + ex.Message); }
                    }
                }
                catch { }
            }
        }

        // Hard guard: our torpedo parts must NEVER trigger a hull swap.
        private static class Patch_HullSwapGuard
        {
            public static bool Run(PartData part, ref IEnumerator __result)
            {
                try
                {
                    string nm = null;
                    try { nm = part != null ? part.name : null; } catch { }
                    if (nm != null && IsOurTorpedo(nm))
                    {
                        Log("[CarrierMod] BLOCKED hull swap for " + nm);
                        __result = EmptyCoroutine();
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        private static class Patch_ChoosePartLog
        {
            public static void Run(PartData part)
            {
                try
                {
                    string nm = null;
                    try { nm = part != null ? part.name : null; } catch { }
                    if (nm != null && IsOurTorpedo(nm))
                    {
                        Log("[CarrierMod] ChoosePart fired for " + nm);
                        try { TrySeedPreviewCache(); } catch { }
                    }
                }
                catch { }
            }
        }

        // TAF caches part previews by model-guid string and serves them without
        // touching vanilla GetModelNameScale (which throws for our names).
        // Our guids coincide with vanilla x1/x2/x3 model guids, so rendering the
        // vanilla tubes once seeds hits for our air wings. All managed-side.
        private static void TrySeedPreviewCache()
        {
            try
            {
                Log("[CarrierMod] seed: attempt");
                var tafUi = SafeFindType("TweaksAndFixes", "TweaksAndFixes.Patch_Ui");
                if (tafUi == null) { Log("[CarrierMod] seed: no Patch_Ui"); return; }
                Log("[CarrierMod] seed: Patch_Ui found");
                var cacheObj = GetMemberStatic(tafUi, "PartPreviewCache");
                var cache = cacheObj as System.Collections.IDictionary;
                if (cache == null) { Log("[CarrierMod] seed: no cache"); return; }
                Log("[CarrierMod] seed: cache keys=" + cache.Count);
                var tafShip = SafeFindType("TweaksAndFixes", "TweaksAndFixes.Patch_Ship");
                object ship = tafShip != null ? GetMemberStatic(tafShip, "LastCreatedShip") : null;
                if (ship == null) { Log("[CarrierMod] seed: no ship"); return; }
                Log("[CarrierMod] seed: ship found");
                System.Reflection.MethodInfo guidM = null, previewM = null;
                try { guidM = HarmonyLib.AccessTools.Method(tafUi, "GetPartPreviewGuid"); }
                catch (Exception ex) { Log("[CarrierMod] seed: guidM lookup threw " + ex.GetType().Name); }
                try { previewM = HarmonyLib.AccessTools.Method(typeof(Ui), "GetPartPreviewTex"); }
                catch (Exception ex) { Log("[CarrierMod] seed: previewM lookup threw " + ex.GetType().Name); }
                if (guidM == null || previewM == null) { Log("[CarrierMod] seed: no methods"); return; }
                var ui = G.ui;
                if (ui == null) { Log("[CarrierMod] seed: no ui"); return; }
                int seeded = 0;
                // Pair our parts with the vanilla tube whose grade we remap to.
                // Copy that entry's texture under our guid (same managed dict TAF
                // reads; pure reference copy, no rendering, no Il2Cpp calls).
                var pairs = new string[][] {
                    new string[] { "torpedo_x6", "torpedo_x1" },
                    new string[] { "torpedo_x7", "torpedo_x2" },
                    new string[] { "torpedo_x8", "torpedo_x3" },
                };
                foreach (var pair in pairs)
                {
                    try
                    {
                        var oursData = G.GameData.parts[pair[0]];
                        var vanData = G.GameData.parts[pair[1]];
                        if (oursData == null || vanData == null) continue;
                        string kOurs = null, kVan = null;
                        try { kOurs = (string)guidM.Invoke(null, new object[] { oursData, ship }); }
                        catch (Exception ex)
                        {
                            if (!_guidErrLogged) { _guidErrLogged = true; Log("[CarrierMod] seed: guid invoke threw " + ex.GetType().Name + ": " + ex.Message); }
                            continue;
                        }
                        try { kVan = (string)guidM.Invoke(null, new object[] { vanData, ship }); } catch { continue; }
                        if (kOurs == null || kVan == null) continue;
                        if (!cache.Contains(kVan))
                        {
                            // Render the vanilla tube once through the safe path;
                            // TAF's postfix caches it under kVan.
                            try { previewM.Invoke(ui, new object[] { vanData, ship }); }
                            catch (Exception ex) { Log("[CarrierMod] seed: vanilla render failed: " + ex.Message); }
                        }
                        if (cache.Contains(kVan) && !cache.Contains(kOurs))
                        {
                            cache[kOurs] = cache[kVan];
                            seeded++;
                            Log("[CarrierMod] seed: " + kOurs + " <= " + kVan);
                        }
                    }
                    catch { }
                }
                if (seeded > 0) Log("[CarrierMod] seeded " + seeded + " preview cache entries.");
            }
            catch { }
        }

        private static object GetMemberStatic(System.Type t, string name)
        {
            // Manual lookup (AccessTools logs a WARNING on every miss; these
            // probes are best-effort by design).
            try
            {
                var p = t.GetProperty(name, System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Instance);
                if (p != null) return p.GetValue(null, null);
            }
            catch { }
            try
            {
                var f = t.GetField(name, System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Instance);
                if (f != null && f.IsStatic) return f.GetValue(null);
            }
            catch { }
            return null;
        }

        private static class Patch_PreChoosePartLog
        {
            public static void Run(PartData part)
            {
                try
                {
                    string nm = null;
                    try { nm = part != null ? part.name : null; } catch { }
                    if (nm != null && IsOurTorpedo(nm))
                        Log("[CarrierMod] PreChoosePart fired for " + nm);
                }
                catch { }
            }
        }

        // Wire discovery via the game's own helper (replaces AddListener hook,
        // which missed because the game funnels through Ui.OnClick).
        private static class Patch_UiOnClick
        {
            public static void Run(UnityEngine.UI.Button button)
            {
                if (!_wiringHookActive) return;
                if (_wiringHits >= 40) return;
                try
                {
                    string bn = "?";
                    try { bn = button != null ? button.gameObject.name : "null"; } catch { }
                    var st = new System.Diagnostics.StackTrace(2, false);
                    var frames = st.GetFrames();
                    if (frames == null) return;
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var fr in frames)
                    {
                        var m = fr.GetMethod();
                        if (m == null) continue;
                        names.Add((m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name);
                    }
                    _wiringHits++;
                    Log("[CarrierMod] Ui.OnClick hit " + _wiringHits + " button=" + bn + ": " + string.Join(" <- ", names.ToArray()));
                }
                catch { }
            }
        }

        // Re-assert CV button visibility AND position on every constructor refresh.
        private static class Patch_RefreshConstructorInfo
        {
            public static void Run()
            {
                try
                {
                    var ui = G.ui;
                    if (ui == null || ui.conShipTypeButtons == null) return;
                    var root = ui.conShipTypeButtons.transform;
                    for (int i = 0; i < root.childCount; i++)
                    {
                        var ch = root.GetChild(i);
                        if (ch.gameObject.name == "ShipTypeCV" && !ch.gameObject.activeSelf)
                        {
                            ch.gameObject.SetActive(true);
                            Log("[CarrierMod] re-asserted ShipTypeCV visible.");
                            break;
                        }
                        for (int j = 0; j < ch.childCount; j++)
                        {
                            var g = ch.GetChild(j);
                            if (g.gameObject.name != "ShipTypeCV") continue;
                            if (!g.gameObject.activeSelf)
                            {
                                g.gameObject.SetActive(true);
                                Log("[CarrierMod] re-asserted ShipTypeCV visible (nested).");
                            }
                            // Hold end-of-row position against layout resets.
                            try
                            {
                                float maxX = -9999f;
                                for (int k = 0; k < ch.childCount; k++)
                                {
                                    var sib = ch.GetChild(k).gameObject;
                                    if (sib.name == "ShipTypeCV") continue;
                                    var r = sib.GetComponent<UnityEngine.RectTransform>();
                                    if (r != null && r.anchoredPosition.x > maxX) maxX = r.anchoredPosition.x;
                                }
                                var cvRt = g.gameObject.GetComponent<UnityEngine.RectTransform>();
                                if (cvRt != null && maxX > -9998f && cvRt.anchoredPosition.x < maxX)
                                {
                                    var p = cvRt.anchoredPosition;
                                    p.x = maxX + 42f;
                                    cvRt.anchoredPosition = p;
                                }
                            }
                            catch { }
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        // Skirmish int<->type map: vanilla list is fixed; cv rides one past the end.
        private static class Patch_IntToType
        {
            public static bool Run(int intShipType, ref ShipType __result)
            {
                try { if (IsCvIndex(intShipType)) { __result = (ShipType)CvObject(); return __result != null ? false : true; } } catch { }
                return true;
            }
        }
        private static class Patch_TypeToInt
        {
            public static bool Run(ShipType shipTypeTemp, ref int __result)
            {
                try
                {
                    string nm = null;
                    try { nm = shipTypeTemp != null ? shipTypeTemp.name : null; } catch { }
                    if (nm == "cv") { int c = CvIndex(); if (c >= 0) { __result = c; return false; } }
                }
                catch { }
                return true;
            }
        }
        private static class Patch_IsActive
        {
            public static void Run(int numberInList, ref bool __result)
            {
                try { if (IsCvIndex(numberInList)) __result = true; } catch { }
            }
        }

        // Append a CV button to the custom-battle type row after vanilla builds it.
        private static class Patch_SkirmishButtons
        {
            public static void Run()
            {
                // Setup scene: purge before the fleet snapshot.
                try { PurgeTafPlaneDesigns(); } catch { }
                try
                {
                    var ui = G.ui;
                    if (ui == null) return;
                    int c = CvIndex();
                    if (c < 0) return;
                    // customBattleButtonList is an Il2Cpp List<Button>; access via
                    // Count/indexer reflection (IList casts are unreliable).
                    object listObj = GetMember(ui, "customBattleButtonList");
                    if (listObj == null) return;
                    int count = 0;
                    try { count = (int)listObj.GetType().GetProperty("Count").GetValue(listObj, null); } catch { return; }
                    if (count == 0) return;
                    System.Reflection.MethodInfo getItem = null;
                    try { getItem = listObj.GetType().GetMethod("get_Item"); } catch { }
                    if (getItem == null) return;
                    UnityEngine.GameObject tpl = null;
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            var b = getItem.Invoke(listObj, new object[] { i }) as UnityEngine.UI.Button;
                            if (b == null) continue;
                            if (b.gameObject.name == "CV") return; // already added
                            tpl = b.gameObject;
                        }
                        catch { }
                    }
                    if (tpl == null) return;
                    var go = UnityEngine.Object.Instantiate(tpl, tpl.transform.parent);
                    go.name = "CV";
                    go.SetActive(true);
                    try
                    {
                        foreach (var t in go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                        { t.text = "CV"; break; }
                    }
                    catch { }
                    int idx = c;
                    System.Action managed = () =>
                    {
                        try
                        {
                            var m = HarmonyLib.AccessTools.Method(typeof(Ui), "ChangeShipTypeInSkirmish");
                            if (m != null) m.Invoke(ui, new object[] { idx });
                            else Log("[CarrierMod] ChangeShipTypeInSkirmish not found.");
                        }
                        catch (Exception ex) { Log("[CarrierMod] CV skirmish select failed: " + ex.Message); }
                    };
                    var il2cppAction = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(managed);
                    var nb = go.GetComponent<UnityEngine.UI.Button>();
                    Ui.OnClick(nb, il2cppAction, true);
                    Log("[CarrierMod] CV skirmish button added at index " + idx);
                }
                catch (Exception ex) { Log("[CarrierMod] skirmish button postfix failed: " + ex.Message); }
            }
        }

        // Vanilla (non-TAF) skirmish dicts use isHullAvailable; inject cv there.
        // Battle prep (void method, iterator-safe): arms the plane spawn experiment.
        private static class Patch_PreInitBattle
        {
            public static void Run()
            {
                try
                {
                    // Replay entry: any live planes from the previous battle
                    // must die BEFORE the fleet save/load reads them. (The
                    // teardown prefix should have caught them; this is the
                    // backstop at the exact choke point.)
                    try { RetirePlanes("replay entry"); } catch { }
                    ArmSpawnExperiment();
                }
                catch { }
            }
        }

        private static class Patch_SkirmishInit
        {
            public static void Run()
            {
                // Setup scene: purge before the fleet snapshot (Reiniting reads
                // the same registries moments later).
                try { PurgeTafPlaneDesigns(); } catch { }
                try { DumpSetupHolders(); } catch { }
                try { TryInjectVanillaSkirmish(); } catch { }
            }
        }

        // ONE-SHOT DIAGNOSTIC: dump the real holder structures (types, fields,
        // collection counts) for both vanilla setup players and TAF's
        // SkPlayers, so we can see exactly where plane designs live.
        private static bool _holdersDumped = false;
        private static void DumpSetupHolders()
        {
            if (_holdersDumped) return;
            _holdersDumped = true;
            try
            {
                Log("[CarrierMod] === SETUP HOLDER DUMP ===");
                object sk = null;
                try { sk = GetMember(G.ui, "skirmishSetup"); } catch { }
                Log("[CarrierMod] G.ui.skirmishSetup type=" + (sk != null ? sk.GetType().FullName : "null"));
                if (sk != null)
                {
                    foreach (var pn in new string[] { "player1", "player2" })
                    {
                        try
                        {
                            var vp = GetMember(sk, pn);
                            Log("[CarrierMod] vanilla " + pn + " type=" + (vp != null ? vp.GetType().FullName : "null"));
                            if (vp == null) continue;
                            var t = vp.GetType();
                            foreach (var prop in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                            {
                                try
                                {
                                    var v = prop.GetValue(vp, null);
                                    string desc = v == null ? "null" : v.GetType().FullName;
                                    try
                                    {
                                        var ie = v as Il2CppSystem.Collections.IEnumerable;
                                        if (ie != null)
                                        {
                                            int c = 0;
                                            var en2 = ie.GetEnumerator();
                                            string sample = "";
                                            while (true)
                                            {
                                                bool more = false;
                                                try { more = en2.MoveNext(); } catch { break; }
                                                if (!more || c >= 30) break;
                                                try
                                                {
                                                    var cur = en2.Current;
                                                    string cn2 = "?";
                                                    try
                                                    {
                                                        var sh = cur as Ship;
                                                        if (sh != null) cn2 = (sh.name ?? "?") + "/" + (sh.hull != null && sh.hull.name != null ? sh.hull.name : "?");
                                                        else
                                                        {
                                                            var st2 = cur as Ship.Store;
                                                            if (st2 != null) cn2 = (st2.vesselName ?? "?") + "/" + (st2.shipType ?? "?");
                                                            else cn2 = cur != null ? cur.GetType().Name : "null";
                                                        }
                                                    }
                                                    catch { }
                                                    if (c < 6) sample += "[" + cn2 + "]";
                                                    c++;
                                                }
                                                catch { c++; }
                                            }
                                            desc += " count~" + c + " " + sample;
                                        }
                                    }
                                    catch { }
                                    Log("[CarrierMod]   vanilla." + pn + "." + prop.Name + " = " + desc);
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                Log("[CarrierMod] === END HOLDER DUMP ===");
            }
            catch (Exception ex) { Log("[CarrierMod] holder dump error: " + ex.Message); }
        }

        // The part-list preview resolves the icon via GetModelNameScale (static,        // returns ModelInfo ValueType). For air-wing parts, force the resolved
        // model name/scale to the existing torpedo_x2 mesh so the icon + preview
        // render instead of a white box.
        // NOTE: ModelInfo struct marshalling proved fragile; kept LoadModel patch
        // only. The preview icon may remain generic, but placed parts render.
        // [HarmonyPatch(typeof(Part), "GetModelNameScale")] removed pending struct fix.

        // AccessTools.TypeByName scans EVERY loaded assembly (GetTypes) and throws
        // noisy TypeLoadExceptions on Unity modules. Resolve via single-assembly
        // GetType instead. Results cached.
        private static readonly System.Collections.Generic.Dictionary<string, System.Type> _typeCache =
            new System.Collections.Generic.Dictionary<string, System.Type>();
        private static System.Type SafeFindType(string assemblyName, string typeName)
        {
            string key = assemblyName + "|" + typeName;
            try
            {
                if (_typeCache.ContainsKey(key)) return _typeCache[key];
            }
            catch { }
            System.Type found = null;
            try
            {
                foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (a.GetName().Name != assemblyName) continue;
                        found = a.GetType(typeName, false);
                        if (found != null) break;
                    }
                    catch { }
                }
            }
            catch { }
            try { _typeCache[key] = found; } catch { }
            return found;
        }

        private static object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            // Manual lookup (AccessTools logs a WARNING on every miss; these
            // probes are best-effort by design).
            try
            {
                var p = obj.GetType().GetProperty(name, System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Instance);
                if (p != null) return p.GetValue(obj, null);
            }
            catch { }
            try
            {
                var f = obj.GetType().GetField(name, System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Instance);
                if (f != null) return f.GetValue(obj);
            }
            catch { }
            return null;
        }

        // Vanilla skirmish setup: Ui.skirmishSetup.player1/2 dictionaries keyed by
        // ShipType (shipAmounts + isHullAvailable). Inject cv alongside the TAF hook.
        private static void TryInjectVanillaSkirmish()
        {
            try
            {
                var ui = G.ui;
                if (ui == null) return;
                object ss = GetMember(ui, "skirmishSetup");
                if (ss == null) return;
                object cvObj = CvObject();
                if (cvObj == null) return;
                foreach (var playerName in new string[] { "player1", "player2" })
                {
                    object player = GetMember(ss, playerName);
                    if (player == null) continue;
                    try
                    {
                        var sa = GetMember(player, "shipAmounts") as System.Collections.IDictionary;
                        if (sa != null && !sa.Contains(cvObj))
                        {
                            sa.Add(cvObj, 0);
                            Log("[CarrierMod] injected cv into vanilla " + playerName + ".shipAmounts");
                        }
                    }
                    catch (Exception ex) { Log("[CarrierMod] vanilla shipAmounts inject: " + ex.Message); }
                    try
                    {
                        var av = GetMember(player, "isHullAvailable") as System.Collections.IDictionary;
                        if (av != null && !av.Contains(cvObj))
                        {
                            av.Add(cvObj, true);
                            Log("[CarrierMod] injected cv into vanilla " + playerName + ".isHullAvailable");
                        }
                    }
                    catch (Exception ex) { Log("[CarrierMod] vanilla isHullAvailable inject: " + ex.Message); }
                }
            }
            catch { }
        }

        private void InjectIntoTafSnapshot()
        {
            // TAF snapshots ship types once into UiM.skirmishSetupMod.player1/2
            // dicts; if that snapshot predates our cv registration, custom battle
            // setup never shows CV. Re-assert cv there (idempotent). Pure reflection
            // so no hard dependency on TAF; silently skips if TAF absent/changed.
            try
            {
                var uiMType = SafeFindType("TweaksAndFixes", "TweaksAndFixes.Modified.UiM");
                if (uiMType == null) return;
                var modField = HarmonyLib.AccessTools.Field(uiMType, "skirmishSetupMod");
                if (modField == null) return;
                object mod = modField.GetValue(null);
                if (mod == null) return;
                var gd = G.GameData;
                if (gd == null || gd.shipTypes == null || !gd.shipTypes.ContainsKey("cv")) return;
                object cvObj = gd.shipTypes["cv"];
                foreach (var playerName in new string[] { "player1", "player2" })
                {
                    object player = null;
                    try
                    {
                        var pf = HarmonyLib.AccessTools.Field(mod.GetType(), playerName);
                        if (pf == null) continue;
                        player = pf.GetValue(mod);
                        if (player == null) continue;
                    }
                    catch { continue; }
                    // shipAmounts: IDictionary<ShipType, Dictionary<Guid,int>>
                    try
                    {
                        var saF = HarmonyLib.AccessTools.Field(player.GetType(), "shipAmounts");
                        var sa = saF != null ? saF.GetValue(player) as System.Collections.IDictionary : null;
                        if (sa != null && !sa.Contains(cvObj))
                        {
                            System.Type valType = null;
                            try
                            {
                                var ga = sa.GetType().GetGenericArguments();
                                if (ga != null && ga.Length == 2) valType = ga[1];
                            }
                            catch { }
                            object empty = null;
                            try { empty = valType != null ? System.Activator.CreateInstance(valType) : new System.Collections.Hashtable(); } catch { }
                            if (empty != null)
                            {
                                sa.Add(cvObj, empty);
                                Log("[CarrierMod] injected cv into TAF " + playerName + ".shipAmounts");
                            }
                        }
                    }
                    catch { }
                    // shipTypeAvailible: IDictionary<ShipType,bool> -> default true
                    try
                    {
                        var avF = HarmonyLib.AccessTools.Field(player.GetType(), "shipTypeAvailible");
                        var av = avF != null ? avF.GetValue(player) as System.Collections.IDictionary : null;
                        if (av != null && !av.Contains(cvObj))
                        {
                            av.Add(cvObj, true);
                            Log("[CarrierMod] injected cv into TAF " + playerName + ".shipTypeAvailible");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ScanForSetupRows()
        {
            // Find custom-battle setup class rows ("0 Battleship Classes" etc.)
            // to map that UI's container for later row injection.
            try
            {
                var found = new System.Collections.Generic.List<string>();
                // Resolve TMP UI text type (name varies by build).
                System.Type textType = null;
                foreach (var cand in new string[] { "TMPro.TextMeshProUGUI", "TMPro.TMP_Text", "TMPro.TextMeshPro" })
                {
                    try { textType = SafeFindType("Unity.TextMeshPro", cand); } catch { }
                    if (textType != null) break;
                }
                if (textType == null)
                {
                    Log("[CarrierMod] setup scan: no TMP text type resolved");
                }
                else
                {
                    Log("[CarrierMod] setup scan: using " + textType.FullName);
                    var textProp = HarmonyLib.AccessTools.Property(textType, "text");
                    if (textProp == null) { Log("[CarrierMod] setup scan: no text property"); }
                    else
                    {
                        UnityEngine.Object[] all = null;
                        try
                        {
                            var il2cppT = Il2CppInterop.Runtime.Il2CppType.From(textType);
                            all = UnityEngine.Object.FindObjectsOfType(il2cppT);
                        }
                        catch { }
                        if (all != null)
                        {
                            foreach (var o in all)
                            {
                                if (o == null) continue;
                                string txt = "";
                                try { txt = (string)textProp.GetValue(o, null); } catch { continue; }
                                if (txt != null && txt.Contains("Classes"))
                                {
                                    var go = (o as UnityEngine.Component).gameObject;
                                    string path = go.name;
                                    try
                                    {
                                        var p = go.transform.parent;
                                        int depth = 0;
                                        while (p != null && depth < 4) { path = p.gameObject.name + "/" + path; p = p.parent; depth++; }
                                    }
                                    catch { }
                                    found.Add(path + " :: " + txt);
                                }
                            }
                        }
                    }
                }
                if (found.Count > 0)
                {
                    found.Sort();
                    string sig = string.Join("|", found.ToArray());
                    if (sig != _lastScanSig)
                    {
                        _lastScanSig = sig;
                        Log("[CarrierMod] setup rows found: " + found.Count);
                        foreach (var f in found) Log("[CarrierMod] setup row: " + f);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[CarrierMod] setup scan failed: " + ex.Message);
            }
        }

        private IEnumerator DisableWiringHookAfter(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += UnityEngine.Time.deltaTime;
                yield return null;
            }
            _wiringHookActive = false;
            Log("[CarrierMod] wiring hook disabled after " + seconds + "s (" + _wiringHits + " hits).");
            yield break;
        }

        private void RegisterShipTypeCv()
        {
            var shipTypes = G.GameData.shipTypes;
            if (shipTypes.ContainsKey("cv"))
            {
                Log("[CarrierMod] cv ship type already registered, skipping.");
                return;
            }

            // Values mirror csv/shipTypes_carrier.csv (the human-editable source of truth).
            var cv = new ShipType();
            cv.name = "cv";
            cv.nameFull = "Aircraft Carrier";
            cv.similar = "ca, cl";
            cv.power = 50000000f;
            cv.armor = 2.5f;
            cv.armorMin = 0f;
            cv.armorMax = 4f;
            cv.speedMin = 25f;
            cv.speedMax = 34f;
            cv.buildRatio = 5f;
            cv.tonnageStartingLimit = 8000f;
            cv.optimalEnemyDistance = 1f;
            cv.aiControl = false;
            cv.shapeExpTonnage = 0.13f;
            cv.shapeExpSpeed = 4.5f;
            cv.shapeMult = 0.0053f;
            cv.engineWeightMod = 1.1f;
            cv.mainFrom = 2f;
            cv.mainTo = 5f;
            cv.secFrom = 2f;
            cv.secTo = 4f;
            cv.power_projection = 30f;
            cv.raidingPower = 4f;
            cv.escortPower = 6f;
            cv.requirements = "tower_main(1;1), funnel(1), gun_main(1), torpedo(1)";
            cv.param = "armor_min_hint(1.0), range_min(Medium), max_range(12000), range_min_modifier(0.5), torpedo_damage(-20), gun_damage(-85)";

            // Build derived data (requirementsx/paramx/damage mods) exactly as the
            // game's own CSV loader does for file-backed types.
            cv.PostProcess();
            // BaseData.enabled gates designer visibility for file-backed types;
            // runtime-constructed objects must set it explicitly.
            try { cv.enabled = true; } catch { }

            shipTypes["cv"] = cv;
            Log("[CarrierMod] registered cv ship type (Aircraft Carrier).");
        }

        // ===== v2 Stage 1: planes as miniature ships =====
        // Ship capture: battle ships are invisible to FindObjectsOfType, so
        // collect references as the game Inits them (void method, safe patch).
        private static readonly System.Collections.Generic.List<Ship> _battleShips =
            new System.Collections.Generic.List<Ship>();
        private static object _planeTypeObj = null;
        // MULTI-CARRIER: per-battle tracking of carriers already queued for a
        // squadron (ship native Pointer), plus a global unique plane index so
        // two carriers never collide on PlaneAI idx / tube pairing.
        private static readonly System.Collections.Generic.List<long> _spawnedCarrierPtrs =
            new System.Collections.Generic.List<long>();
        private static int _nextPlaneIdx = 0;

        private static class Patch_ShipInit
        {
            public static void Run(Ship __instance)
            {
                try
                {
                    if (__instance == null || __instance.isDesign) return;
                    if (_battleShips.Contains(__instance)) return;
                    _battleShips.Add(__instance);
                    if (_battleShips.Count <= 12)
                    {
                        string hullNm = null;
                        try { hullNm = __instance.hull != null ? __instance.hull.name : null; } catch { }
                        Log("[CarrierMod] Ship.Init captured #" + _battleShips.Count + ": " + (__instance.name ?? "?") + " hull=" + (hullNm ?? "?"));
                    }
                }
                catch { }
            }
        }

        // The actual factory for battle ships (static, returns Ship).
        // NOTE: battle ships are "temp for battle" clones and may carry
        // isDesign==true — do NOT filter on it; we clear the list at arm time
        // so everything captured post-arm belongs to the battle.
        // Wing-only strike boost: faster + longer-ranged strikes (same lifetime,
        // higher speed = more ground covered). Keyed on the firing tube part.
        // Set (briefly) around plane drops so the gate lets OUR launches pass.
        internal static bool _planeDrop = false;

        private static class Patch_TorpedoCreate
        {
            private static float FactorFor(string tubeName)
            {
                if (tubeName == "torpedo_x6") return 2.0f;
                if (tubeName == "torpedo_x7") return 2.5f;
                if (tubeName == "torpedo_x8") return 3.0f;
                return 1f;
            }
            // Extra multiplier for PLANE drops only (_planeDrop is set around
            // our Torpedo.Create invoke; ship launches never set it). Ship
            // torpedo speeds remain vanilla. 1.8x was TOO deadly once
            // intercept lead landed (user-tuned back down to 1 = ship-era ~90).
            // Wing tubes deliver ordnance ONLY via planes now: block the
            // game's own launches from x6/x7/x8 (tube cycles empty instead).
            // Plane drops set CarrierMod._planeDrop to pass through.
            public static bool Gate(Part from)
            {
                if (_planeDrop) return true;
                try
                {
                    if (from != null && from.data != null)
                    {
                        string n = from.data.name;
                        if (n == "torpedo_x6" || n == "torpedo_x7" || n == "torpedo_x8")
                        {
        LogVerbose("[CarrierMod] wing tube launch BLOCKED (planes only): " + n);
                            return false;
                        }
                    }
                }
                catch { }
                return true;
            }
            public static void Run(Torpedo __result, Part from)
            { Apply(__result, from); }
            private static void Apply(Torpedo __result, Part part)
            {
                try
                {
                    if (__result == null || part == null) return;
                    string nm = null;
                    try { nm = part.data != null ? part.data.name : null; } catch { }
                    float sp = -1f;
                    try { sp = __result.speed; } catch { }
                    if (nm == null || !IsOurTorpedo(nm))
                    {
                        // Ground-truth log: every non-wing launch with its speed.
        LogVerbose("[CarrierMod] torp launch from=" + (nm ?? "?") + " speed=" + sp.ToString("F1"));
                        return;
                    }
                    float f = FactorFor(nm);
                    try { __result.speed = __result.speed * f; } catch (Exception ex) { Log("[CarrierMod] wing speed set failed: " + ex.Message); }
                    if (_planeDrop && PLANE_TORP_BOOST != 1f)
                    {
                        try { __result.speed = __result.speed * PLANE_TORP_BOOST; } catch { }
                    }
                    float sp2 = -1f;
                    try { sp2 = __result.speed; } catch { }
                    Log("[CarrierMod] " + (_planeDrop ? "PLANE TORPEDO" : "WING TORPEDO") + ": " + nm + " x" + f + (_planeDrop ? " x" + PLANE_TORP_BOOST : "") + " (base " + sp.ToString("F1") + " -> " + sp2.ToString("F1") + ")");
                }
                catch { }
            }
        }

        private static class Patch_ShipCreate
        {
            public static void Run(Ship __result)
            {
                try
                {
                    if (__result == null) return;
                    if (_battleShips.Contains(__result)) return;
                    _battleShips.Add(__result);
                    if (_battleShips.Count <= 12)
                    {
                        string hullNm = null;
                        bool isD = false;
                        try { hullNm = __result.hull != null ? __result.hull.name : null; } catch { }
                        try { isD = __result.isDesign; } catch { }
                        Log("[CarrierMod] Ship.Create captured #" + _battleShips.Count + ": " + (__result.name ?? "?") + " hull=" + (hullNm ?? "?") + " isDesign=" + isD);
                    }
                }
                catch { }
            }
        }

        // Wing strike speed boost (ship-level; see registration note).
        private static class Patch_ModifySpeedTorpedo
        {
            public static void Run(Ship __instance, ref float __result)
            {
                try
                {
                    if (__instance == null) return;
                    string hullNm = null;
                    try { hullNm = __instance.hull != null ? __instance.hull.name : null; } catch { }
                    if (!_spdDiagLogged)
                    {
                        _spdDiagLogged = true;
                        Log("[CarrierMod] ModifySpeedTorpedo called (first, hull=" + (hullNm ?? "<threw>") + ")");
                    }
                    if (hullNm == null || !hullNm.StartsWith("cv_")) return;
                    float f = hullNm.Contains("cv_3") ? 3.0f : hullNm.Contains("cv_2") ? 2.5f : 2.0f;
                    __result = __result * f;
                    if (!_wingSpeedLogged) { _wingSpeedLogged = true; Log("[CarrierMod] wing speed boost active (" + hullNm + " x" + f + ")"); }
                }
                catch { }
            }
        }
        private static bool _wingSpeedLogged = false;
        private static bool _spdDiagLogged = false;

        private static class Patch_PlaneMaxRange
        {
            public static bool Run(Ship __instance, ref int __result)
            {
                try
                {
                    if (!IsSpawnedPlaneShip(__instance)) return true;
                    __result = 30; // 30 km op range for the plane
                    return false;
                }
                catch { return true; }
            }
        }
        private static class Patch_PlaneMinRange
        {
            public static bool Run(Ship __instance, ref int __result)
            {
                try
                {
                    if (!IsSpawnedPlaneShip(__instance)) return true;
                    __result = 0;
                    return false;
                }
                catch { return true; }
            }
        }
        // Planes have no stat-effect collections (parts=0), so stat queries NRE.
        // Answer op-range queries for OUR PLANES only (any plane_strike_1 ship);
        // all other ships keep vanilla behavior.
        private static bool IsSpawnedPlaneShip(Ship s)
        {
            try
            {
                if (s == null) return false;
                string h = null;
                try { h = s.hull != null ? s.hull.name : null; } catch { }
                return h == "plane_strike_1";
            }
            catch { return false; }
        }

        // ===== TAF SKIRMISH DESIGN PURGE =====
        // TAF's UiM.SkirmishSetupMod.player1/player2.shipDesigns accumulates
        // every CreateRandom plane design we mint. The replay/constructor flow
        // ("Reiniting Player Ships") rebuilds those designs as real ships, and
        // the vanilla PrepareBattle spawns them ("spawn france: ... plane: ...")
        // — dying on plane stats (RefreshHull/BeamMin/CreateVisualForSections
        // NREs) and freezing the loader. Purge plane entries so the pipeline
        // never sees them. (User diagnosis: "ships are cached and recycled" —
        // confirmed: managed dicts in TAF's skirmish setup mod.)
        private static void PurgeTafPlaneDesigns()
        {
            try
            {
                var uimT = SafeFindType("TweaksAndFixes", "TweaksAndFixes.UiM");
                if (uimT == null) { Log("[CarrierMod] TAF purge: UiM type missing."); return; }
                var mod = GetMemberStatic(uimT, "skirmishSetupMod");
                if (mod == null) { Log("[CarrierMod] TAF purge: skirmishSetupMod missing."); return; }
                int purged = 0;
                string report = "";
                foreach (var playerName in new string[] { "player1", "player2" })
                {
                    try
                    {
                        var sp = GetMember(mod, playerName);
                        if (sp == null) { report += " [" + playerName + ": null]"; continue; }
                        int pDesigns = 0, pInst = 0, pAmounts = 0, haveDesigns = -1, haveAmounts = -1;
                        var designs = GetMember(sp, "shipDesigns") as System.Collections.Generic.Dictionary<Guid, Ship.Store>;
                        if (designs != null)
                        {
                            haveDesigns = designs.Count;
                            var dead = new System.Collections.Generic.List<Guid>();
                            foreach (var kvp in designs)
                            {
                                try
                                {
                                    var st = kvp.Value;
                                    if (st != null && st.shipType == "plane") dead.Add(kvp.Key);
                                }
                                catch { }
                            }
                            foreach (var k in dead) { designs.Remove(k); purged++; pDesigns++; }
                        }
                        var instances = GetMember(sp, "shipInstances") as System.Collections.Generic.Dictionary<Guid, Ship>;
                        if (instances != null)
                        {
                            var deadI = new System.Collections.Generic.List<Guid>();
                            foreach (var kvp in instances)
                            {
                                try
                                {
                                    var s = kvp.Value;
                                    if (s == null) { deadI.Add(kvp.Key); continue; }
                                    string h = null;
                                    try { h = s.hull != null ? s.hull.name : null; } catch { }
                                    if (h == "plane_strike_1") deadI.Add(kvp.Key);
                                }
                                catch { }
                            }
                            foreach (var k in deadI) { instances.Remove(k); pInst++; }
                        }
                        // shipAmounts: TAF's is Dictionary<ShipType,
                        // Dictionary<Guid, int>> (per-type per-design counts) —
                        // prep regenerates designs for every type present, so
                        // the plane TYPE KEY must go (a count-only entry still
                        // rebuilds the whole squadron).
                        var amounts = GetMember(sp, "shipAmounts") as System.Collections.Generic.Dictionary<ShipType, System.Collections.Generic.Dictionary<Guid, int>>;
                        if (amounts != null)
                        {
                            haveAmounts = amounts.Count;
                            var deadT = new System.Collections.Generic.List<ShipType>();
                            foreach (var kvp in amounts)
                            {
                                try { if (kvp.Key != null && kvp.Key.name == "plane") deadT.Add(kvp.Key); } catch { }
                            }
                            foreach (var t in deadT) { amounts.Remove(t); purged++; pAmounts++; }
                        }
                        report += " [" + playerName + ": designs " + pDesigns + "/" + haveDesigns + ", inst " + pInst + ", amounts " + pAmounts + "/" + haveAmounts + "]";
                    }
                    catch { }
                }
                if (purged > 0) Log("[CarrierMod] TAF purge: removed " + purged + " plane entries." + report);
                else Log("[CarrierMod] TAF purge: nothing to remove." + report);
                // Vanilla layer: G.ui.skirmishSetup.player1/2.shipAmounts
                // (Dictionary<ShipType, int>) syncs INTO the TAF dicts during
                // setup — purge the plane type key there too.
                try
                {
                    var sk = GetMember(G.ui, "skirmishSetup");
                    if (sk != null)
                    {
                        foreach (var playerName in new string[] { "player1", "player2" })
                        {
                            try
                            {
                                var vp = GetMember(sk, playerName);
                                if (vp == null) continue;
                                var va = GetMember(vp, "shipAmounts") as System.Collections.Generic.Dictionary<ShipType, int>;
                                if (va == null) continue;
                                var deadV = new System.Collections.Generic.List<ShipType>();
                                foreach (var kvp in va)
                                {
                                    try { if (kvp.Key != null && kvp.Key.name == "plane") deadV.Add(kvp.Key); } catch { }
                                }
                                foreach (var t in deadV) { va.Remove(t); purged++; }
                            }
                            catch { }
                        }
                        if (purged > 0) Log("[CarrierMod] TAF purge: total with vanilla layer " + purged + ".");
                    }
                }
                catch { }
                // BattleManager.restartBattleShips feeds the replay path
                // directly — strip plane hulls there too.
                try
                {
                    var bmT = typeof(BattleManager);
                    var instProp = bmT.GetProperty("Instance", System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static);
                    var inst = instProp != null ? instProp.GetValue(null, null) : null;
                    if (inst != null)
                    {
                        var rb = GetMember(inst, "restartBattleShips") as Il2CppSystem.Collections.Generic.List<Ship>;
                        if (rb != null)
                        {
                            int rbKilled = 0;
                            var deadRb = new System.Collections.Generic.List<int>();
                            for (int i = 0; i < rb.Count; i++)
                            {
                                try
                                {
                                    var sh = rb[i];
                                    if (sh == null) continue;
                                    string hn = null;
                                    try { hn = sh.hull != null ? sh.hull.name : null; } catch { }
                                    if (hn == "plane_strike_1") deadRb.Add(i);
                                }
                                catch { }
                            }
                            for (int i = deadRb.Count; i-- > 0; )
                            {
                                try { rb.RemoveAt(deadRb[i]); rbKilled++; } catch { }
                            }
                            if (rbKilled > 0) Log("[CarrierMod] TAF purge: removed " + rbKilled + " planes from restartBattleShips.");
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex) { Log("[CarrierMod] PurgeTafPlaneDesigns error: " + ex.Message); }
        }

        // ===== TAF REINIT GATE =====
        // TAF's UiM.SkirmishSetupMod.InitializePlayerMadeShips() enumerates
        // shipDesigns and rebuilds missing instances as REAL ships — plane
        // designs die there (BeamMin/CWeight NREs) and then kill PrepareBattle.
        // Prefix: purge first (order-independent), so the enumeration never
        // sees a plane regardless of who added it or when.
        private static class Patch_TafReinit
        {
            public static void Run()
            {
                try { PurgeTafPlaneDesigns(); } catch { }
            }
        }

        // Belt: per-ship rebuild gate — even if a plane design somehow
        // survives into the rebuild call, skip it (vanilla for the rest).
        private static class Patch_RebuildShip
        {
            public static bool Run(object skirmish, Ship rebuildShip)
            {
                try
                {
                    if (rebuildShip != null)
                    {
                        bool isPlane = false;
                        try
                        {
                            string h = rebuildShip.hull != null ? rebuildShip.hull.name : null;
                            isPlane = h == "plane_strike_1";
                        }
                        catch { }
                        if (!isPlane)
                        {
                            try
                            {
                                string nm = rebuildShip.name;
                                isPlane = nm != null && nm.StartsWith("PLANE ");
                            }
                            catch { }
                        }
                        if (isPlane)
                        {
                            Log("[CarrierMod] RebuildShipInSkirmish SKIPPED plane design (replay-safe).");
                            return false;
                        }
                    }
                }
                catch { }
                return true;
            }
        }

        // ===== TEARDOWN ERASE =====
        // CampaignController.CleanupShips fires at every battle exit (custom +
        // campaign). Erase ALL our plane objects through the destroy path
        // BEFORE the teardown sweeps run (same DestroyAllPlanes the save
        // scrub uses — the scene is reused, so nothing may survive).
        private static class Patch_TeardownErase
        {
            public static void Run(Player exceptPlayer, Player exceptEnemy)
            {
                try { RetirePlanes("teardown"); } catch { }
            }
        }

        private static System.Collections.IEnumerator StartupPurgeWhenReady()
        {
            while (G.GameData == null) yield return new UnityEngine.WaitForSeconds(1f);
            yield return new UnityEngine.WaitForSeconds(3f);
            PurgeTafPlaneDesigns();
        }

        // The battle scene is REUSED across replays ("Skipping config: new
        // Battle == current Battle") — battle #1's plane objects are still
        // physically present when battle #2 loads, and every loader sweep
        // (GetAllShips, design saves, PrepareBattle) trips over them. No
        // registry purge can fix live objects: destroy strays at arm. Planes
        // registered as alive in the CURRENT battle are spared.
        // The battle scene is REUSED across replays — live plane objects (not
        // registries) are what every loader sweep finds (TAF's Reiniting lists
        // them via GetAllShips). Destroy them before the save/teardown reads.
        // Save scrub: CustomBattleSavePlayerDesigns snapshots LIVE battle
        // ships into the next battle's fleet. Prefix retires our planes first
        // so the snapshot can never contain them (order guaranteed: prefix
        // always runs before the method body enumerates).
        private static class Patch_SaveScrub
        {
            public static void Run()
            {
                try
                {
                    try { _planeRecs.Clear(); } catch { }
                    try { _squadronStates.Clear(); } catch { }
                    RetirePlanes("save scrub");
                }
                catch { }
            }
        }

        // The battle scene is REUSED across replays — live plane objects (not
        // registries) are what every loader sweep finds (TAF's Reiniting lists
        // them via GetAllShips). RETIRE (not destroy, not park): Erased status
        // + disabled + DEACTIVATED GameObject. Deactivated objects are skipped
        // by Unity enumerations (FindObjectsOfType/GetAllShips default to
        // active-only), the object stays intact (no dangling refs — raw
        // Destroy froze the next loader), and parked husks are still found.
        // Returns kill count. Safe to call anywhere (setup, teardown, arm).
        private static int RetirePlanes(string why)
        {
            int killed = 0;
            try
            {
                Ship[] ships = null;
                try { ships = UnityEngine.Object.FindObjectsOfType<Ship>(); } catch { }
                if (ships == null) return 0;
                foreach (var s in ships)
                {
                    try
                    {
                        if (s == null) continue;
                        string h = null;
                        try { h = s.hull != null ? s.hull.name : null; } catch { }
                        if (h != "plane_strike_1") continue;
                        try { s.status = VesselEntity.Status.Erased; } catch { }
                        try { s.enabled = false; } catch { }
                        try
                        {
                            var go = s.gameObject;
                            if (go != null)
                            {
                                go.transform.position = new UnityEngine.Vector3(0f, -2000f, 0f);
                                go.SetActive(false);
                                killed++;
                            }
                        }
                        catch { }
                    }
                    catch { }
                }
            }
            catch { }
            if (killed > 0) Log("[CarrierMod] " + why + ": retired " + killed + " plane objects.");
            return killed;
        }

        private static bool DISABLE_REPLAY_BUTTON = true;

        // REPLAY KILL (user request): after a custom carrier battle the game
        // shows a "Play again?" Yes/No dialog; Yes reloads the poisoned fleet
        // save and freezes on plane divisions. Decline it for the user: find
        // the dialog by its PROMPT text (buttons just say Yes/No), disable
        // everything except the No button. Scoped to custom battles with our
        // carriers only. Runs 3 passes (dialog may build late).
        // Shared dialog killer: returns true if it handled a dialog.
        private static bool KillPlayAgainYes()
        {
            bool handled = false;
            try
            {
                if (!DISABLE_REPLAY_BUTTON) return false;
                try { if (GameManager.IsCampaign) return false; } catch { }
                if (_spawnedCarrierPtrs.Count == 0 && _planeRecs.Count == 0) return false;
                    // 1) find the dialog by prompt text
                    UnityEngine.Transform dlgRoot = null;
                    string dlgText = "";
                    try
                    {
                        var tmps = UnityEngine.Object.FindObjectsOfType<Il2CppTMPro.TMP_Text>();
                        if (tmps != null)
                        {
                            foreach (var t in tmps)
                            {
                                try
                                {
                                    if (t == null || t.gameObject == null || !t.gameObject.activeInHierarchy) continue;
                                    string tx = t.text ?? "";
                                    if (tx.ToLowerInvariant().Contains("play again") || tx.ToLowerInvariant().Contains("replay") || tx.ToLowerInvariant().Contains("rematch"))
                                    {
                                        dlgRoot = t.gameObject.transform;
                                        dlgText = tx.Trim();
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                    if (dlgRoot == null)
                    {
                        try
                        {
                            var texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
                            if (texts != null)
                            {
                                foreach (var t in texts)
                                {
                                    try
                                    {
                                        if (t == null || t.gameObject == null || !t.gameObject.activeInHierarchy) continue;
                                        string tx = t.text ?? "";
                                        if (tx.ToLowerInvariant().Contains("play again") || tx.ToLowerInvariant().Contains("replay") || tx.ToLowerInvariant().Contains("rematch"))
                                        {
                                            dlgRoot = t.gameObject.transform;
                                            dlgText = tx.Trim();
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }
                    if (dlgRoot == null) return false; // no dialog up right now
                    // ascend to the dialog container (first ancestor holding 2+ buttons)
                    UnityEngine.Transform root = dlgRoot;
                    try
                    {
                        var p = dlgRoot.parent;
                        int depth = 0;
                        while (p != null && depth < 8)
                        {
                            int btns = 0;
                            try
                            {
                                var bs = p.gameObject.GetComponentsInChildren<UnityEngine.UI.Button>();
                                if (bs != null) btns = bs.Length;
                            }
                            catch { }
                            root = p;
                            if (btns >= 2) break;
                            p = p.parent;
                            depth++;
                        }
                    }
                    catch { }
                    // disable everything except No-like buttons
                    try
                    {
                        var btns = root.gameObject.GetComponentsInChildren<UnityEngine.UI.Button>();
                        if (btns != null)
                        {
                            foreach (var b in btns)
                            {
                                try
                                {
                                    if (b == null || b.gameObject == null || !b.gameObject.activeInHierarchy) continue;
                                    string bt = "";
                                    try
                                    {
                                        var tt = b.gameObject.GetComponentInChildren<Il2CppTMPro.TMP_Text>();
                                        if (tt != null) bt = tt.text ?? "";
                                    }
                                    catch { }
                                    if (string.IsNullOrEmpty(bt))
                                    {
                                        try
                                        {
                                            var tu = b.gameObject.GetComponentInChildren<UnityEngine.UI.Text>();
                                            if (tu != null) bt = tu.text ?? "";
                                        }
                                        catch { }
                                    }
                                    string blo = bt.Trim().ToLowerInvariant();
                                    bool isNo = blo == "no" || blo.StartsWith("no ") || blo.StartsWith("no\n");
                                    string bpath = b.gameObject.name;
                                    try
                                    {
                                        var pp = b.gameObject.transform.parent;
                                        int dd = 0;
                                        while (pp != null && dd < 4) { bpath = pp.name + "/" + bpath; pp = pp.parent; dd++; }
                                    }
                                    catch { }
                                    Log("[CarrierMod] play-again dialog button: '" + bt.Trim() + "' at " + bpath);
                                    if (!isNo)
                                    {
                                        try { b.interactable = false; Log("[CarrierMod] replay option disabled (saying No for you)."); } catch { }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                    Log("[CarrierMod] play-again dialog handled: '" + dlgText.Substring(0, System.Math.Min(80, dlgText.Length)) + "'");
                    handled = true;
            }
            catch { }
            return handled;
        }

        private static System.Collections.IEnumerator DisableReplayButtonSoon()
        {
            for (int pass = 0; pass < 3; pass++)
            {
                yield return new UnityEngine.WaitForSeconds(pass == 0 ? 6f : 10f);
                try { if (KillPlayAgainYes()) yield break; } catch { }
            }
        }

        private static void DestroyStrayPlanes()
        {
            // Retire (not destroy) unregistered planes: same visibility rules
            // as RetirePlanes, but spares planes flying for the current battle.
            try
            {
                var live = new System.Collections.Generic.HashSet<long>();
                try
                {
                    foreach (var r in _planeRecs)
                    {
                        try { if (r.alive && r.ship != null) live.Add((long)r.ship.Pointer); } catch { }
                    }
                }
                catch { }
                int killed = 0;
                var ships = UnityEngine.Object.FindObjectsOfType<Ship>();
                if (ships == null) return;
                foreach (var s in ships)
                {
                    try
                    {
                        if (s == null) continue;
                        string h = null;
                        try { h = s.hull != null ? s.hull.name : null; } catch { }
                        if (h != "plane_strike_1") continue;
                        long ptr = 0;
                        try { ptr = (long)s.Pointer; } catch { }
                        if (live.Contains(ptr)) continue;
                        try { s.status = VesselEntity.Status.Erased; } catch { }
                        try { s.enabled = false; } catch { }
                        try
                        {
                            var go = s.gameObject;
                            if (go != null)
                            {
                                go.transform.position = new UnityEngine.Vector3(0f, -2000f, 0f);
                                go.SetActive(false);
                                killed++;
                            }
                        }
                        catch { }
                    }
                    catch { }
                }
                if (killed > 0) Log("[CarrierMod] retired " + killed + " stray planes from previous battle.");
            }
            catch { }
        }

        private static void ArmSpawnExperiment()
        {
            try
            {
                // New battle detection: PreInitCustomBattle fires several times
                // per battle (seconds apart) and once per new battle. Arms more
                // than 60s apart = new battle -> reset spawn guard + plane.
                // Reset ONLY on first arm of a battle — re-arms must not clear
                // the spawn guard (that raced 4 watchers into 4 planes).
                var now = System.DateTime.Now;
                if ((now - _lastArm).TotalSeconds > 60)
                {
                    if (!_wasArmed)
                    {
                        _spawnedCarrierPtrs.Clear(); _nextPlaneIdx = 0; _planeRecs.Clear();
                        _squadronStates.Clear(); _nextWaveId = 0;
                        // Skirmish battles re-arm here; campaign never fires
                        // PreInitCustomBattle, so the campaign latch (set when
                        // CreateRandom throws) persists there and resets here —
                        // skirmish always retries the real factory.
                        _campaignMode = false; _sharedPlaneDesignCache = null;
                        _saveAttempted = false;
                        _battleShips.Clear();
                        _secCountCache.Clear(); _secCountTime.Clear();
                        Log("[CarrierMod] new battle detected; spawn guard reset.");
                    }
                }
                _wasArmed = true;
                _lastArm = now;
                PurgeTafPlaneDesigns();
                try { DestroyStrayPlanes(); } catch { }
        LogVerbose("[CarrierMod] PreInitCustomBattle: spawn experiment armed; starting watcher.");
                EnsureSpawnWatcher();
            }
            catch { }
        }

        private static bool _watcherStarted = false;
        private static void EnsureSpawnWatcher()
        {
            if (_watcherStarted) return;
            _watcherStarted = true;
            MelonCoroutines.Start(SpawnPlanesWhenReady());
        }
        private static System.DateTime _lastArm = System.DateTime.MinValue;
        private static bool _wasArmed = false;

        private static IEnumerator SpawnPlanesWhenReady()
        {
            // ETERNAL watcher (5s polls, negligible cost): started once at
            // init, it serves EVERY battle mode — custom, campaign, missions
            // (campaign battles load via BattleManager.PrepareBattle, which
            // never fires PreInitCustomBattle; we don't care which loader
            // ran, we just watch for deployed CVs). One-time arming guard
            // TAF-purge tick: minted plane designs must not survive the battle
            // in TAF's skirmish registry (replays rebuild them as real ships).
            // Runs every ~60s so timing never depends on how the battle ends.
            int waits = 0;
            int purgeWaits = 0;
            bool wasBattle = true; // assume battle so the first exit purges
            while (true)
            {
                yield return null;
                waits++;
                purgeWaits++;
                if (purgeWaits >= 3600)
                {
                    purgeWaits = 0;
                    try { PurgeTafPlaneDesigns(); } catch { }
                }
                // Fast dialog-kill tick (~1s): the Play-again dialog appears
                // while the battle is still technically live, so battle-exit
                // triggers fire too late. Runs whenever carriers spawned.
                if (waits % 60 == 0) { try { KillPlayAgainYes(); } catch { } }
                // Fast dialog-kill tick (~1s): the Play-again dialog appears
                // while the battle is still technically live, so exit-based
                // triggers fire too late. Gate is inside KillPlayAgainYes.
                if (waits % 60 == 0) { try { KillPlayAgainYes(); } catch { } }
                if (waits % 300 != 0) continue; // poll ~every 5s
                // Transition purge: must run OUTSIDE the scene gate below
                // (it fires exactly when leaving battle state).
                try
                {
                    bool inBattle = false;
                    try { inBattle = GameManager.IsBattle; } catch { }
                    if (wasBattle && !inBattle)
                    {
                        Log("[CarrierMod] left battle; purging plane designs before setup snapshot.");
                        try { PurgeTafPlaneDesigns(); } catch { }
                        try { MelonCoroutines.Start(DisableReplayButtonSoon()); } catch { }
                    }
                    wasBattle = inBattle;
                }
                catch { }
                // SCENE GATE (user diagnosis confirmed): the watcher must only
                // fire in live battles. Designer preview ships, cached refit
                // hulls, and teardown wrecks all look "deployed" — spawning
                // off them injects planes into the designer/setup flows.
                bool inBattleNow = false;
                try { inBattleNow = GameManager.IsBattle; } catch { }
                if (!inBattleNow) continue;
                try
                {
                    var ships = UnityEngine.Object.FindObjectsOfType<Ship>();
                    if (ships == null || ships.Length == 0) { continue; }
        LogVerbose("[CarrierMod] scan: " + ships.Length + " Ship objects in scene");
                    // MID-BATTLE spawn only: setup-time planes stall the
                    // deployment/CPU-creation loader (proven across rounds).
                    // Deployed carrier = far from origin. MULTI-CARRIER: every
                    // not-yet-queued deployed CV starts its own pipeline.
                    foreach (var s in ships)
                    {
                        try
                        {
                            if (s == null) continue;
                            try { if (s.isDesign) continue; } catch { } // design ghosts stay home
                            string hullNm = "?";
                            try { hullNm = s.hull != null ? s.hull.name : "null"; } catch { }
                            if (hullNm == null || !hullNm.StartsWith("cv_")) continue;
                            var p = s.transform.position;
                            if (Math.Abs(p.x) + Math.Abs(p.z) < 1000f) continue;
                            long ptr = (long)s.Pointer;
                            if (_spawnedCarrierPtrs.Contains(ptr)) continue;
                            _spawnedCarrierPtrs.Add(ptr); // atomic within this tick
                            Log("[CarrierMod] DEPLOYED CARRIER FOUND: " + (s.name ?? "?") + " -> queuing squadron.");
                            MelonCoroutines.Start(SpawnAfterSettle(s));
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Log("[CarrierMod] spawn loop error: " + ex.Message);
                }
            }
        }

        // Waits out the battle-settle window, THEN generates this carrier's
        // squadron (see FREEZE FIX in the watcher above). Multi-carrier: one
        // pipeline per carrier, global unique plane idx.
        private static IEnumerator SpawnAfterSettle(Ship carrier)
        {
            _activeSettles++;
            // Capture the name NOW: scene teardown can destroy the carrier
            // while this coroutine still runs (NRE on .name killed the whole
            // pipeline in the 04:50 session).
            string carrierName = "?";
            try { carrierName = carrier != null && carrier.name != null ? carrier.name : "?"; } catch { }
            for (int sw = 0; sw < 180; sw++) yield return new UnityEngine.WaitForSeconds(0.25f);
            // LOADING GATE: the 45s timer is not enough for heavy fleets —
            // starting design factories while the loading screen is up
            // deadlocks the loader ("Starting Battle" forever, 01:37
            // session). Hold until the game reports loading finished.
            float gateUntil = UnityEngine.Time.time + 300f;
            while (UnityEngine.Time.time < gateUntil)
            {
                bool loading = false;
                try { loading = GameManager.IsLoadingAny; } catch { }
                if (!loading) break;
                yield return new UnityEngine.WaitForSeconds(0.5f);
            }
            // SCENE RE-CHECK: the 45s settle can span a scene change (battle
            // end, quit to setup). Never spawn outside a live battle.
            try
            {
                bool stillBattle = false;
                try { stillBattle = GameManager.IsBattle; } catch { }
                if (!stillBattle) { Log("[CarrierMod] left battle during settle; aborting squadron for " + carrierName + "."); yield break; }
            }
            catch { }
            try { if (carrier == null || carrier.isSinking || carrier.isDead) { Log("[CarrierMod] carrier lost during settle; skipping squadron."); yield break; } } catch { }
            Log("[CarrierMod] battle settled; spawning squadron for " + carrierName + " now.");
            object player = null;
            try { player = carrier.player; } catch (Exception ex) { Log("[CarrierMod] carrier.player threw: " + ex.Message); }
            if (player == null) { Log("[CarrierMod] spawn: carrier player null; done."); yield break; }
            // SEQUENTIAL DESIGN PIPELINE (one design per plane). Instantiate
            // COPIES of parts NRE in AddPart -> NeedRecalcCache: Unity's clone
            // drops non-serialized Il2Cpp state. So every plane gets a FRESH
            // design and donates that design's ORIGINAL part — the only path
            // proven to render (validated single-plane sessions).
            // WAVE LOOP (materialization-aware): AI/TAF ships stream their
            // parts in over MINUTES (proven: Borbone had 1 of 6 tubes at the
            // 45s settle, all 6 by +90s). So recount each wave and keep
            // launching until every wing tube has a plane (1 per tube,
            // grouped by mark). 10-min cap.
            var launchedPerMark = new System.Collections.Generic.Dictionary<string, int>();
            float deadline = UnityEngine.Time.time + WAVE_TIME_LIMIT;
            while (UnityEngine.Time.time < deadline)
            {
                try { if (carrier == null || carrier.isSinking || carrier.isDead) { Log("[CarrierMod] carrier lost; wave loop ends."); break; } } catch { break; }
                string bestMark = null;
                int bestRem = 0;
                try
                {
                    var tubes = new System.Collections.Generic.List<Part>();
                    FindWingTubes(carrier, tubes);
                    var perMark = new System.Collections.Generic.Dictionary<string, int>();
                    foreach (var t in tubes)
                    {
                        try
                        {
                            var nm = t != null && t.data != null ? t.data.name : null;
                            if (nm == null) continue;
                            int c; perMark.TryGetValue(nm, out c); perMark[nm] = c + 1;
                        }
                        catch { }
                    }
                    foreach (var kvp in perMark)
                    {
                        int launched = 0;
                        launchedPerMark.TryGetValue(kvp.Key, out launched);
                        int rem = kvp.Value - launched;
                        if (rem > bestRem) { bestRem = rem; bestMark = kvp.Key; }
                    }
                }
                catch { }
                if (bestMark == null)
                {
                    // all found tubes covered; parts may still be streaming in
                    yield return new UnityEngine.WaitForSeconds(15f);
                    continue;
                }
                int waveId = _nextWaveId++;
                Log("[CarrierMod] wave " + waveId + " (" + bestMark + "): " + bestRem + " planes launching from " + carrierName + ".");
                int l0 = 0; launchedPerMark.TryGetValue(bestMark, out l0);
                launchedPerMark[bestMark] = l0 + bestRem;
                for (int slot = 0; slot < bestRem; slot++)
                {
                    int idx = _nextPlaneIdx++;
                    var en = SpawnOnePlane(carrier, idx, bestMark, waveId, slot);
                    while (true)
                    {
                        bool next = false;
                        try { next = en.MoveNext(); }
                        catch (Exception ex) { Log("[CarrierMod] plane " + idx + " pipeline threw: " + ex.Message); next = false; }
                        if (!next) break;
                        yield return en.Current;
                    }
                    // Deck-launch cadence: spreads factory hitch AND takeoff.
                    yield return new UnityEngine.WaitForSeconds(LAUNCH_GAP);
                }
                yield return new UnityEngine.WaitForSeconds(WAVE_GAP);
            }
            Log("[CarrierMod] all waves airborne for " + carrierName + ".");
            _activeSettles--;
            // Leak sweep (last carrier standing only): CreateRandom throws
            // leave design shells with no callback - RETIRE them (parked
            // husks are still enumerable; destroyed ones dangle).
            if (!_campaignMode && _activeSettles <= 0)
            {
                try { RetirePlanes("leak sweep"); } catch { }
                // Purge the designs we just minted from TAF's skirmish setup —
                // otherwise the constructor/replay "Reiniting" rebuilds them as
                // real ships and the loader dies on plane stats.
                PurgeTafPlaneDesigns();
            }
            yield break;
        }

        // Builds ONE plane. SKIRMISH: design factory (pumped across frames).
        // CAMPAIGN: the factory NREs for campaign players (inlined designer
        // state missing mid-battle) — on first throw we flip to the shared-
        // design path: the plane design saved automatically by skirmish
        // battles ('CarrierMod Plane' in Designs\), fetched via
        // CampaignController.GetSharedDesign.
        private static bool _campaignMode = false;
        private static Ship _sharedPlaneDesignCache = null;
        private static bool _planeDesignSaved = false;
        private static bool _saveAttempted = false;
        private static int _activeSettles = 0;
        private static int _nextWaveId = 0;
        private static readonly System.Collections.Generic.Dictionary<int, SquadronState> _squadronStates =
            new System.Collections.Generic.Dictionary<int, SquadronState>();
        private class SquadronState { public Ship target; }

        private static IEnumerator SpawnOnePlane(Ship carrier, int idx, string mark, int waveId, int slot)
        {
            object player = null;
            try { player = carrier.player; } catch { }
            if (player == null) { Log("[CarrierMod] plane " + idx + ": carrier player null; skip."); yield break; }
            // Plane shipType: fetch per plane (the multi-carrier watcher
            // rewrite accidentally dropped the old per-carrier lookup and a
            // fresh session shipped a null shipType -> instant NRE).
            try
            {
                var st = G.GameData.shipTypes;
                if (st != null && st.ContainsKey("plane")) _planeTypeObj = st["plane"];
            }
            catch { }
            if (_planeTypeObj == null) { Log("[CarrierMod] plane " + idx + ": plane shipType unavailable; skip."); yield break; }
            if (_campaignMode)
            {
                CampaignDesignFor(carrier, idx, mark, waveId, slot);
                yield break;
            }
            var m = HarmonyLib.AccessTools.Method(typeof(Ship), "CreateRandom");
            bool fired = false;
            System.Action<Ship> onCreated = (Ship created) =>
            {
                fired = true;
                try { HandleDesignCreated(created, carrier, idx, mark, waveId, slot); }
                catch (Exception ex) { Log("[CarrierMod] callback error: " + ex.Message); }
            };
            var il2cppCallback = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Ship>>(onCreated);
            // Build args from the method's own parameter types: shipType,
            // player, boxed Nullable<bool> default (NOT null!), then
            // ignoreHullAvailability=true, isTempForBattle=true (auto-
            // cleanup at battle end), onDone, checkMainGunsCount=false,
            // canUseShared=false, useSmallAmountTries=true.
            var pars = m.GetParameters();
            var args = new object[pars.Length];
            args[0] = _planeTypeObj;
            args[1] = player;
            try { args[2] = Activator.CreateInstance(pars[2].ParameterType); } catch { args[2] = null; }
            args[3] = true;  // ignoreHullAvailability
            args[4] = true;  // isTempForBattle
            args[5] = il2cppCallback;
            args[6] = false; // checkMainGunsCount
            args[7] = false; // canUseShared
            args[8] = true;  // useSmallAmountTries
            var enumObj = m.Invoke(null, args);
            // Il2Cpp returns its OWN IEnumerator type — a C# `as
            // System.Collections.IEnumerator` cast FAILS (this exact
            // bug stalled the spawn for a round). Use the interop
            // interface and drive MoveNext through it.
            var e = enumObj as Il2CppSystem.Collections.IEnumerator;
            if (e != null)
            {
                int ticks = 0;
                while (true)
                {
                    bool next = false;
                    try { next = e.MoveNext(); }
                    catch (Exception ex)
                    {
                        _campaignMode = true; // factory is dead in this mode
                        var msg = ex.Message ?? "";
                        if (msg.Length > 80) msg = msg.Substring(0, 80);
                        Log("[CarrierMod] plane " + idx + " CreateRandom threw (" + msg + "...); switching to shared-design path.");
                        break;
                    }
                    if (!next) break;
                    ticks++;
                    if (ticks <= 5 || ticks % 120 == 0) Log("[CarrierMod] plane " + idx + " CreateRandom tick " + ticks);
                    yield return null;
                }
            }
            else Log("[CarrierMod] plane " + idx + " CreateRandom returned " + (enumObj != null ? enumObj.GetType().Name : "null"));
            if (!fired)
            {
                var d = CampaignDesignFor(carrier, idx, mark, waveId, slot);
                if (d == null)
                    Log("[CarrierMod] plane " + idx + ": NO plane design available. Run ONE skirmish battle with a carrier — the mod saves its plane design ('CarrierMod Plane') — then campaign works.");
            }
        }

        // CAMPAIGN PATH: fetch the plane design from the shared-design pool
        // (auto-saved by skirmish battles) and run the normal clone pipeline.
        private static Ship CampaignDesignFor(Ship carrier, int idx, string mark, int waveId, int slot)
        {
            if (_sharedPlaneDesignCache != null)
            {
                HandleDesignCreated(_sharedPlaneDesignCache, carrier, idx, mark, waveId, slot);
                return _sharedPlaneDesignCache;
            }
            var d = TryLoadSharedPlaneDesign(carrier);
            if (d == null) return null;
            _sharedPlaneDesignCache = d;
            HandleDesignCreated(d, carrier, idx, mark, waveId, slot);
            return d;
        }

        private static Ship TryLoadSharedPlaneDesign(Ship carrier)
        {
            try
            {
                var st = G.GameData.shipTypes;
                if (st == null || !st.ContainsKey("plane")) { Log("[CarrierMod] campaign: plane shipType missing."); return null; }
                var planeType = st["plane"];
                var cc = CampaignController.Instance;
                if (cc == null) { Log("[CarrierMod] campaign: no CampaignController."); return null; }
                int year = 1930;
                try
                {
                    var y = GetMember(cc, "year");
                    if (y == null) { var cd = GetMember(cc, "CampaignData"); if (cd != null) y = GetMember(cd, "year"); }
                    if (y is int) year = (int)y;
                }
                catch { }
                try
                {
                    var d = cc.GetSharedDesign(carrier.player, planeType, year, false, false);
                    if (d != null) { try { d.UpdateOwner(); } catch { } Log("[CarrierMod] campaign: shared plane design fetched (carrier nation)."); return d; }
                }
                catch (Exception ex) { Log("[CarrierMod] GetSharedDesign threw: " + ex.Message); }
                // Nation fallback: try every DISTINCT battle player (campaign
                // battles have two nations — one may hold the saved design).
                try
                {
                    var tried = new System.Collections.Generic.List<long>();
                    foreach (var s in ScanShips())
                    {
                        try
                        {
                            if (s == null || s.player == null) continue;
                            long ptr = (long)s.player.Pointer;
                            if (tried.Contains(ptr)) continue;
                            tried.Add(ptr);
                            var d = cc.GetSharedDesign(s.player, planeType, year, false, false);
                            if (d != null) { try { d.UpdateOwner(); } catch { } Log("[CarrierMod] campaign: shared plane design fetched via battle nation '" + (s.player.data != null ? s.player.data.name : "?") + "'."); return d; }
                        }
                        catch { }
                    }
                }
                catch (Exception ex) { Log("[CarrierMod] shared pool battle-player scan failed: " + ex.Message); }
            }
            catch (Exception ex) { Log("[CarrierMod] TryLoadSharedPlaneDesign error: " + ex.Message); }
            return null;
        }

        // Persist the plane design as a shared design ('CarrierMod Plane'):
        // campaign battles reload THIS file via CampaignController. Stepwise
        // logging: the design-shell store path NRE'd in playtests (22:21
        // session) — clone.ToStore is the fallback source.
        private static void SaveSharedPlaneDesign(Ship design, Ship clone)
        {
            // NOTE: do NOT set IsSharedDesign on the shell — flagging leaked
            // design shells surfaces them in the designer + CPU-creator sweep
            // and hangs battle replays (22:20 session). The clone (with parts
            // + visuals) is tried first — a partless shell has nothing
            // serializable and ToStore NREs on it.
            try
            {
                Ship.Store store = null;
                if (clone != null)
                {
                    try { store = clone.ToStore(); Log("[CarrierMod] save step1: CLONE store ok."); }
                    catch (Exception ex) { Log("[CarrierMod] save step1 (clone.ToStore) threw: " + ex.Message); }
                }
                if (store == null)
                {
                    try { store = design.ToStore(); Log("[CarrierMod] save step2: design store ok."); }
                    catch (Exception ex) { Log("[CarrierMod] save step2 (design.ToStore) threw: " + ex.Message); }
                }
                if (store == null) { Log("[CarrierMod] save failed: no store (campaign design not minted)."); return; }
                byte[] bytes = null;
                try { bytes = Util.SerializeObjectByte(store); } catch (Exception ex) { Log("[CarrierMod] save step3 (serialize) threw: " + ex.Message); }
                if (bytes == null) { Log("[CarrierMod] save failed: bytes null."); return; }
                string path = null;
                try { string fname; path = Storage.GetSavedDesignPath("CarrierMod Plane", out fname); Log("[CarrierMod] save step4: path=" + path); }
                catch (Exception ex) { Log("[CarrierMod] save step4 (path) threw: " + ex.Message); }
                if (path == null) { Log("[CarrierMod] save failed: no path."); return; }
                try { Storage.SaveSharedDesignShipByte(path, bytes); }
                catch (Exception ex) { Log("[CarrierMod] save step5 (write) threw: " + ex.Message); return; }
                try { G.GameData.LoadSharedDesigns(); } catch (Exception ex) { Log("[CarrierMod] save step6 (reload) threw: " + ex.Message); }
                _planeDesignSaved = true;
                Log("[CarrierMod] plane design saved as shared design '" + path + "'.");
            }
            catch (Exception ex) { Log("[CarrierMod] shared-design save error: " + ex.Message); }
        }

        // Design is ready (factory callback OR shared pool): probe-log it,
        // persist it as a shared design (skirmish generations), clone it,
        // berth, evict/park the ghost, queue the original-part transfer.
        private static void HandleDesignCreated(Ship created, Ship carrier, int idx, string mark, int waveId, int slot)
        {
            try
            {
                if (created == null) { Log("[CarrierMod] design: null ship"); return; }
                bool isD = false;
                try { isD = created.isDesign; } catch { }
                int dParts = -1;
                string dHull = "?";
                try { dParts = created.hullAndParts != null ? created.hullAndParts.Count : -1; } catch { dParts = -2; }
                try { dHull = created.hull != null ? created.hull.name : "null"; } catch { dHull = "<threw>"; }
                Log("[CarrierMod] plane " + idx + " DESIGN received: " + created.name + " isDesign=" + isD + " parts=" + dParts + " hull=" + dHull);
                Ship clone = null;
                try
                {
                    var cm = HarmonyLib.AccessTools.Method(typeof(Ship), "Create");
                    object player2 = null;
                    try { player2 = carrier.player; } catch { }
                    var cloneObj = cm.Invoke(null, new object[] { created, player2, true, false, false });
                    clone = cloneObj as Ship;
                }
                catch (Exception ex) { Log("[CarrierMod] plane " + idx + " clone threw: " + ex.Message); }
                if (clone == null) { Log("[CarrierMod] plane " + idx + ": clone failed."); return; }
                // FLEET HYGIENE (critical): the game auto-divisions temp battle
                // ships. Plane divisions leak into the custom-battle settings
                // store when the player's designs are saved — and the REPLAY
                // loader then rebuilds them ("Division of PLANE X (1/3)"),
                // NREing on plane stats (BeamMin/CWeight) and freezing.
                // Remove the clone from its division immediately.
                try
                {
                    var dv = clone.division;
                    if (dv != null)
                    {
                        var rm = HarmonyLib.AccessTools.Method(typeof(Division), "RemoveShip");
                        if (rm != null) rm.Invoke(dv, new object[] { clone, false, null });
                        Log("[CarrierMod] plane " + idx + ": removed from auto-division.");
                    }
                }
                catch (Exception ex) { Log("[CarrierMod] plane " + idx + " division removal: " + ex.Message); }
                // line-astern berth near the carrier
                try
                {
                    var p0 = carrier.transform.position;
                    clone.transform.position = new UnityEngine.Vector3(p0.x + 60f + (idx % 3) * 45f, p0.y, p0.z - (idx / 3) * 45f);
                }
                catch { }
                // CRITICAL: evict + park the design ghost (CPU sweep + UI
                // raycast hardening; see earlier rounds).
                try
                {
                    int evicted = 0;
                    object player3 = null;
                    try { player3 = carrier.player; } catch { }
                    if (player3 != null)
                    {
                        foreach (var collName in new string[] { "designsAll", "designs", "fleetAll", "fleet" })
                        {
                            try
                            {
                                var collObj = GetMember(player3, collName);
                                // NOTE: these are Il2Cpp IEnumerables (computed
                                // properties) — a System.Collections.IEnumerable
                                // cast silently fails (why eviction found 0 for
                                // months). Pump the Il2Cpp enumerator, collect
                                // our plane designs, and try IList removal.
                                var ien = collObj as Il2CppSystem.Collections.IEnumerable;
                                if (ien == null) continue;
                                var victims = new System.Collections.Generic.List<int>();
                                var lstCast = collObj as Il2CppSystem.Collections.Generic.IList<Ship>;
                                var e = ien.GetEnumerator();
                                int walk = 0;
                                while (true)
                                {
                                    bool more = false;
                                    try { more = e.MoveNext(); } catch { break; }
                                    if (!more) break;
                                    try
                                    {
                                        var cur = e.Current as Ship;
                                        if (cur == null) { walk++; continue; }
                                        string hn = null;
                                        try { hn = cur.hull != null ? cur.hull.name : null; } catch { }
                                        if (hn == "plane_strike_1" && lstCast != null) victims.Add(walk);
                                        walk++;
                                    }
                                    catch { walk++; }
                                }
                                if (lstCast != null)
                                {
                                    for (int vi = victims.Count; vi-- > 0; )
                                    {
                                        try { lstCast.RemoveAt(victims[vi]); evicted++; } catch { }
                                    }
                                }
                                // computed enumerable: nothing to remove from
                            }
                            catch { }
                        }
                    }
                    try { created.enabled = false; } catch { }
                    DisarmColliders(created, "design ghost");
                    try
                    {
                        var cp = carrier.transform.position;
                        created.transform.position = new UnityEngine.Vector3(cp.x, cp.y - 2000f, cp.z);
                    }
                    catch { }
                    Log("[CarrierMod] plane " + idx + ": design ghost parked (evicted " + evicted + " containers).");
                }
                catch (Exception ex) { Log("[CarrierMod] plane " + idx + " eviction error: " + ex.Message); }
                Log("[CarrierMod] plane " + idx + " ready; queuing original-part transfer.");
                MelonCoroutines.Start(AttachOneAfterDelay(created, clone, carrier, idx, mark, waveId, slot));
            }
            catch (Exception ex) { Log("[CarrierMod] HandleDesignCreated error: " + ex.Message); }
        }

        // The PROVEN attach path: donate the design's ORIGINAL part (copies
        // NRE in AddPart -> NeedRecalcCache) after a 1s settle, then hand the
        // plane to its strike-loop AI.
        private static IEnumerator AttachOneAfterDelay(Ship design, Ship clone, Ship carrier, int idx, string mark, int waveId, int slot)
        {
            yield return new UnityEngine.WaitForSeconds(1f);
            try
            {
                var parts = design.hullAndParts;
                Part original = null;
                if (parts != null && parts.Count > 0) original = parts[0];
                if (original == null)
                {
                    Log("[CarrierMod] plane " + idx + ": design has no parts; plane stays invisible (still armed).");
                }
                else
                {
                    // RENDER GEOMETRY FIX: in every session where planes
                    // rendered, the ghost sat AT the ship when AddPart/
                    // LoadModel ran (the old callback moved it back up after
                    // parking). Parked at -2000m, the part transfers 2000m
                    // underwater and rides below its plane — invisible.
                    // Bring the ghost to the clone for the attach.
                    try { design.transform.position = clone.transform.position; } catch { }
                    var ap = HarmonyLib.AccessTools.Method(typeof(Ship), "AddPart");
                    var lm = HarmonyLib.AccessTools.Method(typeof(Part), "LoadModel");
                    if (TryAttachPart(clone, original, ap, lm))
                    {
                        Log("[CarrierMod] plane " + idx + ": transferred 1/1 parts (original).");
                        // PERSIST (once per battle): the clone is only a valid
                        // ToStore candidate AFTER it has parts + visuals. A
                        // partless shell has nothing serializable and ToStore
                        // NREs on it. Campaign battles reload this file.
                        if (!_planeDesignSaved && !_saveAttempted)
                        {
                            _saveAttempted = true;
                            SaveSharedPlaneDesign(design, clone);
                        }
                    }
                    else
                        Log("[CarrierMod] plane " + idx + ": attach failed; plane stays invisible (still armed).");
                }
                MakeKinematic(clone);
                // SHELL DISPOSAL (reverted): destroying shells left dangling
                // references that froze the NEXT battle's loader (01:54
                // session). Parked husks are the proven-safe state — the
                // 22:20 session ran three battles in a row with them.
                try
                {
                    design.transform.position = new UnityEngine.Vector3(
                        carrier.transform.position.x, carrier.transform.position.y - 2000f, carrier.transform.position.z);
                }
                catch { }
            }
            catch (Exception ex) { Log("[CarrierMod] plane " + idx + " attach pipeline error: " + ex.Message); }
            MelonCoroutines.Start(PlaneAI(clone, carrier, idx, mark, waveId, slot));
            yield break;
        }


        // Shared ship scan: N planes x 4Hz FindObjectsOfType would churn;
        // refresh at most every 0.5s regardless of caller.
        private static Ship[] _shipScanCache = new Ship[0];
        private static float _shipScanTime = -999f;
        private static Ship[] ScanShips()
        {
            if (UnityEngine.Time.time - _shipScanTime > 0.5f)
            {
                try { _shipScanCache = UnityEngine.Object.FindObjectsOfType<Ship>(); }
                catch { }
                _shipScanTime = UnityEngine.Time.time;
            }
            return _shipScanCache;
        }

        // ===== ANTI-AIR DEFENSE (secondaries-as-AA) =====
        // Enemy ships' SECONDARY/casemate batteries roll against planes in
        // range. Native gunnery is untouched (planes are collider-less
        // teleporters); this is our own per-mount probability model. This
        // gives the desired build incentive: no secondaries = no AA cover.
        private class PlaneRecord
        {
            public Ship ship; public Ship carrier; public int idx;
            public int hp = PLANE_HP;
            public bool alive = true;
            public bool damaged = false;
            public string state = "Outbound";
            public int ammo = 1;
        }
        private static readonly System.Collections.Generic.List<PlaneRecord> _planeRecs =
            new System.Collections.Generic.List<PlaneRecord>();
        private static bool _aaRunning = false;
        // Per-ship secondary-count cache: CalcCategory per part per plane per
        // cycle adds up (20 planes x 10 ships x 15 parts); mounts change at
        // most when parts die, so a 10s TTL is plenty.
        private static readonly System.Collections.Generic.Dictionary<long, int> _secCountCache =
            new System.Collections.Generic.Dictionary<long, int>();
        private static readonly System.Collections.Generic.Dictionary<long, float> _secCountTime =
            new System.Collections.Generic.Dictionary<long, float>();
        private const float SEC_CACHE_TTL = 10f;

        private static PlaneRecord RegisterPlane(Ship plane, Ship carrier, int idx)
        {
            // idx is globally unique per battle (see _nextPlaneIdx); records
            // clear on new-battle reset.
            var rec = new PlaneRecord { ship = plane, carrier = carrier, idx = idx };
            _planeRecs.Add(rec);
            if (!_aaRunning)
            {
                _aaRunning = true;
                MelonCoroutines.Start(AAManager());
            }
            return rec;
        }

        // Hit chance per mount roll by distance; most lethal close in.
        private static float AaHitChance(float dist)
        {
            if (dist > 2500f) return 0.003f;
            if (dist > 1000f) return UnityEngine.Mathf.Lerp(0.03f, 0.005f, (dist - 1000f) / 1500f);
            if (dist > 300f) return UnityEngine.Mathf.Lerp(0.08f, 0.03f, (dist - 300f) / 700f);
            return 0.10f;
        }

        // AA-qualifying guns: secondary turrets + casemate secondaries
        // (categories confirmed via Part.CalcCategory in playtests).
        private static int CountSecondaries(Ship s)
        {
            try
            {
                long key = (long)s.Pointer;
                float now = UnityEngine.Time.time;
                float t;
                if (_secCountTime.TryGetValue(key, out t) && now - t < SEC_CACHE_TTL)
                {
                    int cached;
                    if (_secCountCache.TryGetValue(key, out cached)) return cached;
                }
                int n = 0;
                var parts = s.hullAndParts;
                if (parts != null)
                {
                    foreach (var p in parts)
                    {
                        try
                        {
                            if (p == null || p.data == null) continue;
                            var cat = Part.CalcCategory(s, p.data);
                            if (cat == null) continue;
                            string cn = null;
                            try { cn = cat.name; } catch { }
                            if (cn == "gun_sec" || cn == "gun_casemate") n++;
                        }
                        catch { }
                    }
                }
                _secCountCache[key] = n;
                _secCountTime[key] = now;
                return n;
            }
            catch { return 0; }
        }

        // (FireExplosion/Puff flak visuals REMOVED per user — the primitive
        // spheres rendered at ship gun barrels and made a visual mess. AA
        // mechanics are pure probability; kills show only the plane's dive.)

        private static System.Collections.IEnumerator AAManager()
        {
            Log("[CarrierMod] AA manager running (secondaries-as-AA).");
            while (true)
            {
                yield return new UnityEngine.WaitForSeconds(AA_CYCLE);
                try
                {
                    bool anyAlive = false;
                    foreach (var r in _planeRecs) { if (r.alive && r.ship != null) { anyAlive = true; break; } }
                    if (!anyAlive) continue;
                    var ships = ScanShips();
                    foreach (var r in _planeRecs)
                    {
                        if (!r.alive || r.ship == null) continue;
                        try
                        {
                            var pos = r.ship.transform.position;
                            float worst = float.MaxValue;
                            foreach (var s in ships)
                            {
                                try
                                {
                                    if (s == null || s.isDesign) continue;
                                    string h = null;
                                    try { h = s.hull != null ? s.hull.name : null; } catch { continue; }
                                    if (h == null || h.StartsWith("plane_")) continue; // carriers shoot AA too
                                    try { if (s.isSinking || s.isDead) continue; } catch { }
                                    try { if (r.carrier != null && s.player != null && r.carrier.player != null && s.player.Pointer == r.carrier.player.Pointer) continue; } catch { continue; }
                                    var sp = s.transform.position;
                                    float d = UnityEngine.Vector3.Distance(new UnityEngine.Vector3(pos.x, 0, pos.z), new UnityEngine.Vector3(sp.x, 0, sp.z));
                                    if (d > AA_RANGE) continue;
                                    int mounts = CountSecondaries(s);
                                    if (mounts <= 0) continue;
                                    if (d < worst) worst = d;
                                    // Final approach: outbound with ordnance inside
                                    // 1500m = most vulnerable (user spec).
                                    bool approach = r.state == "Outbound" && r.ammo > 0 && d < 1500f;
                                    int rolls = System.Math.Min(mounts, 6);
                                    bool hit = false;
                                    for (int i = 0; i < rolls && !hit; i++)
                                    {
                                        float p = AaHitChance(d) * (approach ? 3f : 1f) * AA_HIT_MULT;
                                        if (UnityEngine.Random.value < p) hit = true;
                                    }
                                    // (flak visuals removed per user — primitive
                                    // puffs rendered at gun barrels and made a mess)
                                    if (hit)
                                    {
                                        r.hp--;
                                        if (r.hp <= 0 || UnityEngine.Random.value < AA_CRIT)
                                        {
                                            r.alive = false;
                                            MelonCoroutines.Start(PlaneDeath(r));
                                            Log("[CarrierMod] AA KILL: plane " + r.idx + " shot down by " + s.name + " secondaries at " + d.ToString("F0") + "m.");
                                        }
                                        else
                                        {
                                            r.damaged = true;
                                            Log("[CarrierMod] AA HIT: plane " + r.idx + " damaged by " + s.name + " at " + d.ToString("F0") + "m (hp=" + r.hp + ").");
                                        }
                                    }
                                    if (!r.alive) break;
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private static System.Collections.IEnumerator PlaneDeath(PlaneRecord r)
        {
            // Tip over, dive, splash. GameObject is hidden below the map
            // rather than destroyed — teardown keeps its references.
            try
            {
                var t = r.ship != null ? r.ship.transform : null;
                if (t == null) yield break;
                var startPos = t.position;
                var startRot = t.rotation;
                float elapsed = 0f;
                float dur = 2.2f;
                while (elapsed < dur)
                {
                    yield return new UnityEngine.WaitForSeconds(0.1f);
                    elapsed += 0.1f;
                    if (r.ship == null) yield break;
                    float k = elapsed / dur;
                    t.position = startPos + UnityEngine.Vector3.down * (30f * k);
                    t.rotation = startRot * UnityEngine.Quaternion.Euler(0f, 0f, 80f * k);
                }
                t.position = new UnityEngine.Vector3(0f, -2000f, 0f);
                Log("[CarrierMod] plane " + r.idx + " hit the water. Lost from deck strength.");
                try { if (r.ship != null) { r.ship.status = VesselEntity.Status.Erased; r.ship.enabled = false; } } catch { }
                try { if (r.ship != null && r.ship.gameObject != null) r.ship.gameObject.SetActive(false); } catch { }
            }
            finally { }
        }

        private static void FindWingTubes(Ship s, System.Collections.Generic.List<Part> into)
        {
            into.Clear();
            try
            {
                var parts = s.hullAndParts;
                if (parts == null) return;
                foreach (var p in parts)
                {
                    try
                    {
                        if (p == null || p.data == null) continue;
                        string nm = p.data.name;
                        if (nm == "torpedo_x6" || nm == "torpedo_x7" || nm == "torpedo_x8") into.Add(p);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // One AddPart + LoadModel attach with per-step inner-exception logging.
        private static bool TryAttachPart(Ship clone, Part p, System.Reflection.MethodInfo ap, System.Reflection.MethodInfo lm)
        {
            try
            {
                if (ap != null) ap.Invoke(clone, new object[] { p });
                else { Log("[CarrierMod] AddPart method missing!"); return false; }
            }
            catch (Exception ex)
            {
                var inn = ex.InnerException != null ? ex.InnerException : ex;
                Log("[CarrierMod] AddPart threw: " + inn.GetType().Name + ": " + inn.Message);
                return false;
            }
            try
            {
                if (lm != null) lm.Invoke(p, new object[] { clone, true });
                else { Log("[CarrierMod] LoadModel method missing!"); return false; }
            }
            catch (Exception ex)
            {
                var inn = ex.InnerException != null ? ex.InnerException : ex;
                Log("[CarrierMod] LoadModel threw: " + inn.GetType().Name + ": " + inn.Message);
                return false;
            }
            try
            {
                var pt = p.transform;
                if (pt != null && clone.transform != null) pt.SetParent(clone.transform, true);
            }
            catch { }
            return true;
        }

        // ===== PLANE STRIKE STATE MACHINE =====
        // find enemy -> approach -> launch torpedo (Torpedo.Create from the
        // wing tube, so the per-tube speed boost applies) -> RTB -> rearm
        // alongside carrier -> repeat. Movement: kinematic stepping (the
        // clone has no division, so the game does not process move orders).
        private enum PlaneState { Outbound, Retreat, Rearm }

        private static IEnumerator PlaneAI(Ship plane, Ship carrier, int idx, string mark, int waveId, int slot)
        {
            Log("[CarrierMod] PlaneAI" + idx + " started: find enemy -> attack -> RTB -> rearm.");
            // Kill physics interference: buoyancy/waves/drag otherwise fight
            // the transform stepping each tick (plane wallows, loses speed).
            MakeKinematic(plane);
            float spd = SpeedForMark(mark);
            Log("[CarrierMod] PlaneAI" + idx + " speed=" + spd.ToString("F0") + " (" + mark + ").");
            var rec = RegisterPlane(plane, carrier, idx);
            var myPlayer = carrier.player;
            // SQUADRON COHESION: same-mark wave shares one target; the first
            // plane to acquire sets it, the rest follow.
            SquadronState sq = null;
            try
            {
                if (!_squadronStates.TryGetValue(waveId, out sq) || sq == null)
                {
                    sq = new SquadronState();
                    _squadronStates[waveId] = sq;
                }
            }
            catch { }
            // DECK-LAUNCH CADENCE: planes leave the deck one by one; same
            // speed keeps the wave bunched behind the leader.
            yield return new UnityEngine.WaitForSeconds(2f + slot * 2.5f);
            var state = PlaneState.Outbound;
            Ship target = null;
            Ship live = carrier;
            int ammo = 1;
            float rearmEnd = 0f;
            int logTick = 0;

            for (int t = 0; t < 14400; t++) // ~60 min at 0.25s
            {
                yield return new UnityEngine.WaitForSeconds(0.25f);
                try
                {
                    if (plane == null) { Log("[CarrierMod] PlaneAI" + idx + ": plane destroyed."); yield break; }
                    if (!rec.alive) yield break; // shot down by AA

                    // re-acquire FRIENDLY live carrier (setup ship is a ghost at
                    // origin). Player filter: without it, planes could rearm at
                    // an ENEMY carrier once theirs sinks.
                    var all = ScanShips();
                    Ship liveFound = null;
                    foreach (var s in all)
                    {
                        try
                        {
                            if (s == null) continue;
                            string h = null;
                            try { h = s.hull != null ? s.hull.name : null; } catch { }
                            if (h == null || !h.StartsWith("cv_")) continue;
                            try { if (myPlayer == null || s.player == null || s.player.Pointer != myPlayer.Pointer) continue; } catch { continue; }
                            var pp = s.transform.position;
                            if (Math.Abs(pp.x) + Math.Abs(pp.z) > 1000f) { liveFound = s; break; }
                        }
                        catch { }
                    }
                    if (liveFound != null) live = liveFound;
                    try
                    {
                        if (live == null || live.isSinking || live.isDead)
                        {
                            Log("[CarrierMod] PlaneAI" + idx + ": no friendly carrier left; ditching.");
                            rec.alive = false;
                            MelonCoroutines.Start(PlaneDeath(rec));
                            yield break;
                        }
                    }
                    catch { }

                    // target: nearest real enemy with readable hull
                    target = null;
                    float bestD = float.MaxValue;
                    var scanFrom = plane.transform.position;
                    foreach (var s in all)
                    {
                        try
                        {
                            if (s == null || s.isDesign) continue;
                            string h = null;
                            try { h = s.hull != null ? s.hull.name : null; } catch { continue; }
                            if (h == null) continue;
                            // Carriers ARE valid targets (CV-vs-CV duels);
                            // the player filter above keeps us off friendly
                            // decks. Only other PLANES are off-limits.
                            if (h.StartsWith("plane_")) continue;
                            // Don't waste fish on corpses: skip sinking/dead
                            // vessels (game's own liveness properties).
                            try { if (s.isSinking || s.isDead) continue; } catch { }
                            try { if (s.player.Pointer == live.player.Pointer) continue; } catch { continue; }
                            var sp = s.transform.position;
                            float dd = UnityEngine.Vector3.Distance(new UnityEngine.Vector3(scanFrom.x, 0, scanFrom.z), new UnityEngine.Vector3(sp.x, 0, sp.z));
                            if (dd < bestD) { bestD = dd; target = s; }
                        }
                        catch { }
                    }

                    // SQUADRON COHESION: adopt/follow the wave's shared target.
                    try
                    {
                        if (sq != null)
                        {
                            bool sharedOk = false;
                            try { sharedOk = sq.target != null && !sq.target.isSinking && !sq.target.isDead; } catch { }
                            if (!sharedOk && target != null) sq.target = target;
                            else if (sharedOk && target != sq.target) target = sq.target;
                        }
                    }
                    catch { }

                    var pos = plane.transform.position;
                    var cpos = live.transform.position;
                    float distC = UnityEngine.Vector3.Distance(new UnityEngine.Vector3(pos.x, 0, pos.z), new UnityEngine.Vector3(cpos.x, 0, cpos.z));
                    float curSpd = rec.damaged ? spd * 0.75f : spd; // smoke trail = slower

                    var tPos = UnityEngine.Vector3.zero;
                    float distT = float.MaxValue;
                    if (target != null)
                    {
                        try { tPos = target.transform.position; distT = UnityEngine.Vector3.Distance(new UnityEngine.Vector3(pos.x, 0, pos.z), new UnityEngine.Vector3(tPos.x, 0, tPos.z)); } catch { target = null; }
                    }

                    switch (state)
                    {
                        case PlaneState.Outbound:
                            if (target == null) break;
                            StepToward(plane, ref pos, tPos, curSpd, 0.25f);
                            if (distT <= RELEASE_DIST && ammo > 0)
                            {
                                // The generated plane design carries only a hull
                                // part (no tube), so drop via a carrier wing tube:
                                // same part family, full x8 damage, boost keys off
                                // it. Plane idx pairs with its own tube (1 plane
                                // per tube). Boost is x2.0-3.0, so pass a base
                                // speed (~45/36/30 by tube) -> ~90 effective.
                                var tubes = new System.Collections.Generic.List<Part>();
                                FindWingTubes(live, tubes);
                                // keep only THIS plane's mark's tubes
                                for (int ti = tubes.Count; ti-- > 0; )
                                {
                                    string tn = null;
                                    try { tn = tubes[ti].data != null ? tubes[ti].data.name : null; } catch { }
                                    if (tn != mark) tubes.RemoveAt(ti);
                                }
                                string src = "carrier wing tube";
                                if (tubes.Count == 0) { FindWingTubes(plane, tubes); src = "plane tube"; }
                                Part tube = null;
                                string tubeNm = "?";
                                if (tubes.Count > 0)
                                {
                                    tube = tubes[slot % tubes.Count];
                                    try { tubeNm = tube.data != null ? tube.data.name : "?"; } catch { }
                                }
                                if (tube == null && plane.hullAndParts != null && plane.hullAndParts.Count > 0)
                                { tube = plane.hullAndParts[0]; src = "plane hull part"; }
                                if (tube == null)
                                    Log("[CarrierMod] STRIKE ABORTED: no torpedo part on plane or carrier; RTB.");
                                else
                                {
                                    // base speed so the drop lands at the configured
                                    // EFFECTIVE speed: base = drop_speed / tube factor
                                    // (the Torpedo.Create postfix re-applies the factor).
                                    float baseSpd = PLANE_DROP_SPEED / FactorForTube(tubeNm);
                                    // INTERCEPT LEAD: aim where the target WILL be
                                    // when the fish arrives (aiming at its current
                                    // position = guaranteed stern miss on movers).
                                    UnityEngine.Vector3 tVel = UnityEngine.Vector3.zero;
                                    try { tVel = target.velocityCurrent; } catch { }
                                    try
                                    {
                                        if (tVel.sqrMagnitude < 0.0001f)
                                        {
                                            var rb = target.GetComponent<UnityEngine.Rigidbody>();
                                            if (rb != null) tVel = rb.velocity;
                                        }
                                    }
                                    catch { }
                                    var flatP = new UnityEngine.Vector3(pos.x, 0f, pos.z);
                                    var flatT = new UnityEngine.Vector3(tPos.x, 0f, tPos.z);
                                    var flatV = new UnityEngine.Vector3(tVel.x, 0f, tVel.z);
                                    float TORP_EFF = PLANE_DROP_SPEED * PLANE_TORP_BOOST; // matches the in-flight speed
                                    float tof = distT / TORP_EFF;
                                    var aim = flatT;
                                    for (int it = 0; it < 3; it++)
                                    {
                                        aim = flatT + flatV * tof;
                                        tof = UnityEngine.Vector3.Distance(flatP, aim) / TORP_EFF;
                                    }
                                    var aimFlat = new UnityEngine.Vector3(aim.x - flatP.x, 0f, aim.z - flatP.z);
                                    if (aimFlat.sqrMagnitude < 1f)
                                        aimFlat = new UnityEngine.Vector3(tPos.x - pos.x, 0f, tPos.z - pos.z);
                                    var dir = aimFlat.normalized;
                                    float lead = UnityEngine.Vector3.Distance(flatT, aim);
                                    var start = new UnityEngine.Vector3(pos.x + dir.x * 10f, pos.y + 2f, pos.z + dir.z * 10f);
                                    var tc = HarmonyLib.AccessTools.Method(typeof(Torpedo), "Create");
                                    if (tc != null)
                                    {
                                        _planeDrop = true;
                                        try { tc.Invoke(null, new object[] { tube, start, dir, TORP_LIFE, baseSpd }); }
                                        finally { _planeDrop = false; }
                                        ammo--;
                                        Log("[CarrierMod] STRIKE" + idx + ": torpedo dropped from " + src + " (" + tubeNm + ") at " + distT.ToString("F0") + "m from target, lead " + lead.ToString("F0") + "m (tof " + tof.ToString("F1") + "s); ammo=" + ammo + "; RTB.");
                                    }
                                    else Log("[CarrierMod] STRIKE ABORTED: Torpedo.Create missing.");
                                }
                                state = PlaneState.Retreat;
                            }
                            break;

                        case PlaneState.Retreat:
                            if (distC > REARM_DIST)
                                StepToward(plane, ref pos, cpos, curSpd, 0.25f);
                            if (distC <= REARM_DIST)
                            {
                                state = PlaneState.Rearm;
                                rearmEnd = UnityEngine.Time.time + REARM_TIME;
                                Log("[CarrierMod] PlaneAI: on deck, rearming (" + REARM_TIME + "s).");
                            }
                            break;

                        case PlaneState.Rearm:
                            if (distC > REARM_DIST * 1.5f)
                                StepToward(plane, ref pos, cpos, curSpd * 0.5f, 0.25f);
                            if (UnityEngine.Time.time >= rearmEnd)
                            {
                                ammo = 1;
                                state = PlaneState.Outbound;
                                Log("[CarrierMod] PlaneAI: rearmed; outbound again.");
                            }
                            break;
                    }

                    rec.state = state.ToString();
                    rec.ammo = ammo;
                    logTick++;
                    if (logTick % 20 == 0)
                        Log("[CarrierMod] PlaneAI" + idx + "[" + state + "] tgtDist=" + (target != null ? distT.ToString("F0") : "-") + " carrierDist=" + distC.ToString("F0") + " ammo=" + ammo);
                }
                catch (Exception ex)
                {
                    Log("[CarrierMod] PlaneAI error: " + ex.Message);
                    yield break;
                }
            }
            Log("[CarrierMod] PlaneAI: lifetime expired.");
            yield break;
        }

        private static void MakeKinematic(Ship plane)
        {
            try
            {
                var rbs = plane.GetComponentsInChildren<UnityEngine.Rigidbody>();
                int n = 0;
                foreach (var rb in rbs)
                {
                    try
                    {
                        if (rb == null) continue;
                        rb.isKinematic = true;      // forces/buoyancy can't move it
                        rb.detectCollisions = false; // no ramming, no wave contact
                        rb.useGravity = false;
                        n++;
                    }
                    catch { }
                }
                Log("[CarrierMod] plane rigidbodies kinematic: " + n);
            }
            catch (Exception ex) { Log("[CarrierMod] kinematic failed: " + ex.Message); }
            // Collision exclusion (user req): guns/torpedoes/rams must not
            // interact with the plane physically this stage.
            DisarmColliders(plane, "plane");
        }

        private static void DisarmColliders(Ship s, string tag)
        {
            try
            {
                if (s == null) return;
                var cols = s.GetComponentsInChildren<UnityEngine.Collider>(true);
                int n = 0;
                foreach (var c in cols)
                {
                    try { if (c != null) { c.enabled = false; n++; } } catch { }
                }
                if (n > 0) Log("[CarrierMod] " + tag + " colliders disabled: " + n);
            }
            catch (Exception ex) { Log("[CarrierMod] " + tag + " collider disarm failed: " + ex.Message); }
        }

        private static void StepToward(Ship plane, ref UnityEngine.Vector3 pos, UnityEngine.Vector3 target, float speed, float dt)
        {
            var dir = new UnityEngine.Vector3(target.x - pos.x, 0f, target.z - pos.z);
            float len = dir.magnitude;
            if (len < 0.01f) return;
            dir /= len;
            var np = pos + dir * speed * dt;
            plane.transform.position = new UnityEngine.Vector3(np.x, pos.y, np.z);
            pos = plane.transform.position;
            plane.transform.rotation = UnityEngine.Quaternion.LookRotation(dir);
        }

    }
}
