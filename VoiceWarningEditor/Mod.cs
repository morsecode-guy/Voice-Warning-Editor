using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;
using Il2CppCraftEditor;

[assembly: MelonInfo(typeof(VoiceWarningEditor.VoiceWarningEditorMod), "Voice Warning Editor", "1.1.0", "Morse Code Guy")]
[assembly: MelonGame("Stonext Games", "Flyout")]

namespace VoiceWarningEditor
{
    // voice warning system for flyout
    // uses winmm so discord can actually hear it >:(
    public partial class VoiceWarningEditorMod : MelonMod
    {
        // win32 audio imports

        [DllImport("winmm.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hwo);
        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);
        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);
        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, int cbwh);
        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hwo);

        // waveout constants

        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const uint CALLBACK_NULL = 0x00000000;
        private const uint WHDR_BEGINLOOP = 0x00000004;
        private const uint WHDR_ENDLOOP = 0x00000008;

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        // playsound flags

        private const uint SND_FILENAME  = 0x00020000;
        private const uint SND_ASYNC     = 0x00000001;
        private const uint SND_NODEFAULT = 0x00000002;
        private const uint SND_PURGE     = 0x00000040;

        // threshold metadata for events that have numeric values
        internal static readonly Dictionary<WarningEvent, (float defaultVal, string unit, bool greaterThan)> THRESHOLD_META =
            new Dictionary<WarningEvent, (float, string, bool)>
        {
            { WarningEvent.OverG,             (DEFAULT_OVER_G_THRESHOLD,       "G",    true) },
            { WarningEvent.Overspeed,         (DEFAULT_OVERSPEED_MACH,         "Mach", true) },
            { WarningEvent.CriticalAltitude,  (DEFAULT_CRITICAL_ALTITUDE,      "m",    false) },
            { WarningEvent.LowAltitude,       (DEFAULT_LOW_ALTITUDE,           "m",    false) },
            { WarningEvent.CriticalFuel,      (DEFAULT_CRITICAL_FUEL_FRACTION, "%",    false) },
            { WarningEvent.BingoFuel,         (DEFAULT_BINGO_FUEL_FRACTION,    "%",    false) },
            { WarningEvent.Stall,             (DEFAULT_CRITICAL_SPEED,         "m/s",  false) },
            { WarningEvent.CriticalAOA,       (DEFAULT_CRITICAL_AOA,           "°",    true) },
            { WarningEvent.BankLeft,          (DEFAULT_BANK_ANGLE_WARN,        "°",    true) },
            { WarningEvent.BankRight,         (DEFAULT_BANK_ANGLE_WARN,        "°",    true) },
            { WarningEvent.HighRollRate,      (DEFAULT_ROLL_RATE_WARN,         "°/s",  true) },            // inputs
            { WarningEvent.InputThrottle,     (0.95f,  "",    true) },
            { WarningEvent.InputPitch,        (0.90f,  "",    true) },
            { WarningEvent.InputRoll,         (0.90f,  "",    true) },
            { WarningEvent.InputYaw,          (0.90f,  "",    true) },
            { WarningEvent.InputCollective,   (0.90f,  "",    true) },
            { WarningEvent.InputBrake,        (0.50f,  "",    true) },
            { WarningEvent.InputFlaps,        (1.0f,   "",    true) },
            // custom axes
            { WarningEvent.CustomAxis0,       (0.50f,  "",    true) },
            { WarningEvent.CustomAxis1,       (0.50f,  "",    true) },
            { WarningEvent.CustomAxis2,       (0.50f,  "",    true) },
            { WarningEvent.CustomAxis3,       (0.50f,  "",    true) },
            { WarningEvent.CustomAxis4,       (0.50f,  "",    true) },
            { WarningEvent.CustomAxis5,       (0.50f,  "",    true) },
            { WarningEvent.CustomAxis6,       (0.50f,  "",    true) },
            { WarningEvent.CustomAxis7,       (0.50f,  "",    true) },        };

        // ui display names
        private static readonly Dictionary<WarningEvent, string> EVENT_DISPLAY_NAMES = new Dictionary<WarningEvent, string>
        {
            { WarningEvent.OverG, "Over G" },
            { WarningEvent.Overspeed, "Overspd" },
            { WarningEvent.CriticalAltitude, "Crit Alt" },
            { WarningEvent.LowAltitude, "Low Alt" },
            { WarningEvent.CriticalFuel, "Crit Fuel" },
            { WarningEvent.BingoFuel, "Bingo" },
            { WarningEvent.Stall, "Stall" },
            { WarningEvent.CriticalAOA, "Crit AOA" },
            { WarningEvent.BankLeft, "Bank L" },
            { WarningEvent.BankRight, "Bank R" },
            { WarningEvent.HighRollRate, "Roll Rate" },
            { WarningEvent.EngineFailure, "Eng Fail" },
            { WarningEvent.EngineFire, "Eng Fire" },
            { WarningEvent.BattleDamage, "Dmg" },
            { WarningEvent.GeneratorFailure, "Gen Fail" },
            { WarningEvent.HydraulicFailure, "Hydro" },
            { WarningEvent.GearNotDown, "Gear Up" },
            { WarningEvent.MissileIncoming, "Missile" },
            { WarningEvent.RadarLock, "Radar" },
            // inputs
            { WarningEvent.InputThrottle, "Throttle" },
            { WarningEvent.InputPitch, "Pitch" },
            { WarningEvent.InputRoll, "Roll" },
            { WarningEvent.InputYaw, "Yaw" },
            { WarningEvent.InputCollective, "Collective" },
            { WarningEvent.InputBrake, "Brake" },
            { WarningEvent.InputFlaps, "Flaps" },
            // custom axes
            { WarningEvent.CustomAxis0, "Axis 0" },
            { WarningEvent.CustomAxis1, "Axis 1" },
            { WarningEvent.CustomAxis2, "Axis 2" },
            { WarningEvent.CustomAxis3, "Axis 3" },
            { WarningEvent.CustomAxis4, "Axis 4" },
            { WarningEvent.CustomAxis5, "Axis 5" },
            { WarningEvent.CustomAxis6, "Axis 6" },
            { WarningEvent.CustomAxis7, "Axis 7" },
        };

        internal static string GetEventDisplayName(WarningEvent evt)
        {
            // try to get the real axis name from the craft
            if (evt >= WarningEvent.CustomAxis0 && evt <= WarningEvent.CustomAxis7)
            {
                int idx = evt - WarningEvent.CustomAxis0;
                try
                {
                    var craft = Craft.active;
                    if (craft?.customAxes != null && idx < craft.customAxes.Count)
                        return craft.customAxes[idx].name;
                }
                catch { }
                return "Axis " + idx;
            }
            return EVENT_DISPLAY_NAMES.TryGetValue(evt, out var name) ? name : evt.ToString();
        }

        internal static readonly WarningEvent[] ALL_EVENTS = (WarningEvent[])Enum.GetValues(typeof(WarningEvent));

        // default thresholds

        internal const string DATA_FOLDER_NAME = "VoiceWarningEditor";
        private const string TARGET_SCENE = "PlanetScene2";

        internal const float DEFAULT_OVER_G_THRESHOLD = 7.0f;
        internal const float DEFAULT_OVERSPEED_MACH = 1.2f;
        internal const float DEFAULT_CRITICAL_ALTITUDE = 150f;
        internal const float DEFAULT_LOW_ALTITUDE = 300f;
        internal const float DEFAULT_BINGO_FUEL_FRACTION = 0.15f;
        internal const float DEFAULT_CRITICAL_FUEL_FRACTION = 0.05f;
        internal const float DEFAULT_CRITICAL_AOA = 25f;
        internal const float DEFAULT_CRITICAL_SPEED = 50f;
        internal const float DEFAULT_BANK_ANGLE_WARN = 60f;
        internal const float DEFAULT_ROLL_RATE_WARN = 60f;
        internal const float DEFAULT_ENGINE_COMP_CONDITION = 0.7f;
        internal const float DEFAULT_HYDRAULIC_LOW = 0.2f;
        internal const float DEFAULT_ELECTRIC_LOW = 0.1f;

        // cooldowns so warnings dont spam
        internal const float COOLDOWN_SHORT = 3.0f;
        internal const float COOLDOWN_MEDIUM = 5.0f;
        internal const float COOLDOWN_LONG = 10.0f;

        // runtime state

        private bool _isInTargetScene;
        internal string _dataFolderPath;

        // clip name -> file path and duration
        internal Dictionary<string, string> _clipPaths = new Dictionary<string, string>();
        internal Dictionary<string, float> _clipDurations = new Dictionary<string, float>();
        private bool _clipsReady;

        // test sound on main menu
        private bool _testSoundPending;
        private float _testSoundTimer;

        // missile alarm stuff >:(
        private int _prevMissileCount;
        private bool _wasRadarLocked;
        internal bool _missileAlarmPlaying;
        internal IntPtr _waveOutHandle;
        internal IntPtr _waveHdrPtr;
        internal IntPtr _pcmDataPtr;
        internal Signature _ourSignature;

        // per-warning cooldowns
        private Dictionary<string, float> _warningCooldowns = new Dictionary<string, float>();

        // sequential playback queue
        private Queue<string> _warningQueue = new Queue<string>();
        private float _queueCooldown;

        // playback tracking
        private float _lastPlayTime;
        private float _currentClipLength;

        // debug log throttle
        private float _debugLogTimer;
        private const float DEBUG_LOG_INTERVAL = 5.0f;

        // per-craft config

        private const string CRAFTS_FOLDER_NAME = "crafts";
        internal string _craftConfigBasePath;
        internal string _currentCraftName = "";
        internal string _configCraftName = "";
        internal Dictionary<string, string> _craftOverrides = new Dictionary<string, string>();

        private int _testSoundIndex;

        // event system state

        internal HashSet<WarningEvent> _activeEvents = new HashSet<WarningEvent>();
        internal Dictionary<WarningEvent, float> _eventValues = new Dictionary<WarningEvent, float>();
        internal List<EventRule> _eventRules = new List<EventRule>();
        private bool _rulesInitialized;
        internal EventRule _editingRule;
        internal Transform _rulesListContainer;
        internal Transform _ruleEditorContainer;
        internal HashSet<WarningEvent> _editorSelectedEvents = new HashSet<WarningEvent>();
        internal RuleLogic _editorLogic = RuleLogic.Any;
        internal string _editorSoundClip = "";
        internal float _editorCooldown = COOLDOWN_MEDIUM;
        internal string _editorGroup = "";
        internal Dictionary<WarningEvent, float> _editorThresholds = new Dictionary<WarningEvent, float>();
        internal Dictionary<WarningEvent, CompareOp> _editorComparisons = new Dictionary<WarningEvent, CompareOp>();

        // custom sounds

        internal List<(string clipName, string displayName, string filePath)> _customSounds =
            new List<(string, string, string)>();

        // craft editor ui state

        private bool _isInCraftEditor;
        private bool _craftEditorUICreated;
        internal GameObject _warningEditorButton;
        internal GameObject _warningCreatorPanel;
        private bool _warningEditorActive;
        internal string _editorCraftName = "";
        internal string _pendingFileBrowserClip;
        internal TMP_InputField _presetNameInput;
        internal Transform _presetsContainer;
        internal Sprite _vwsIconSprite;
        internal Sprite _folderIconSprite;
        internal Sprite _speakerBtnSprite;
        internal bool _advancedMode;
        internal GameObject _eventRulesSection;
        internal Transform _warningPanelContent;
        internal Transform _customSoundsContainer;

        // built-in warning sounds :3
        internal static readonly (string clipName, string displayName)[] WARNING_SOUNDS = new[]
        {
            ("vws_master_caution_f18", "Master Caution"),
            ("vws_over_g_f18", "Over G"),
            ("vws_critical_speed_f18", "Overspeed"),
            ("vws_critical_altitude_f18", "Critical Altitude"),
            ("vws_altitude_f18", "Low Altitude"),
            ("vws_bingo1_f18", "Bingo Fuel 1"),
            ("vws_bingo2_f18", "Bingo Fuel 2"),
            ("vws_check_fuel_f18", "Check Fuel"),
            ("vws_caution_f18", "Caution / Stall"),
            ("vws_critical_angle_f18", "Critical AOA"),
            ("vws_roll_left_f18", "Roll Left"),
            ("vws_roll_right_f18", "Roll Right"),
            ("vws_roll_out_f18", "Roll Out"),
            ("vws_engine_failure_f18", "Engine Failure"),
            ("vws_engine_fire_f18", "Engine Fire"),
            ("vws_fire_f18", "Fire"),
            ("vws_generator_failure_f18", "Generator Failure"),
            ("vws_hydrosystem_failure_f18", "Hydraulic Failure"),
            ("vws_check_gear_f18", "Check Gear"),
            ("missile_lauch_f18", "Missile Alarm"),
            ("radar_lock_f18", "Radar Lock"),
            ("vws_engine_fire_left_f18", "Engine Fire (Left)"),
            ("vws_engine_fire_right_f18", "Engine Fire (Right)"),
            ("vws_left_engine_failure_f18", "Engine Failure (Left)"),
            ("vws_right_engine_failure_f18", "Engine Failure (Right)"),
            ("vws_left_generator_failure_f18", "Gen Failure (Left)"),
            ("vws_right_generator_failure_f18", "Gen Failure (Right)"),
        };

        // all sounds including custom imports
        internal List<(string clipName, string displayName)> GetAllSounds()
        {
            var all = new List<(string, string)>(WARNING_SOUNDS);
            foreach (var cs in _customSounds)
                all.Add((cs.clipName, cs.displayName));
            return all;
        }

        // get display name for a clip
        internal string GetSoundDisplayName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return "(none)";
            foreach (var ws in WARNING_SOUNDS)
                if (ws.clipName == clipName) return ws.displayName;
            foreach (var cs in _customSounds)
                if (cs.clipName == clipName) return cs.displayName;
            return clipName;
        }

        // lifecycle

        public override void OnInitializeMelon()
        {
            string userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
            _dataFolderPath = Path.Combine(userDataDir, DATA_FOLDER_NAME);

            // migrate old folder name if it exists
            string oldDataPath = Path.Combine(userDataDir, "BitchinBetty");
            if (Directory.Exists(oldDataPath) && !Directory.Exists(_dataFolderPath))
            {
                try
                {
                    Directory.Move(oldDataPath, _dataFolderPath);
                    LoggerInstance.Msg($"Migrated data folder from BitchinBetty → {DATA_FOLDER_NAME}");
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning($"Could not migrate old data folder: {ex.Message}");
                }
            }

            if (!Directory.Exists(_dataFolderPath))
            {
                Directory.CreateDirectory(_dataFolderPath);
                LoggerInstance.Msg($"Created data folder at: {_dataFolderPath}");
                LoggerInstance.Msg("Place your .wav warning sound files in this folder.");
            }

            // per-craft config folder
            _craftConfigBasePath = Path.Combine(_dataFolderPath, CRAFTS_FOLDER_NAME);
            if (!Directory.Exists(_craftConfigBasePath))
            {
                Directory.CreateDirectory(_craftConfigBasePath);
                LoggerInstance.Msg($"Created per-craft config folder at: {_craftConfigBasePath}");
            }

            // set up audio backend (winmm on linux/wine, unity on windows)
            InitAudioBackend();

            LoggerInstance.Msg("Voice Warning Editor v1.1.0 initialized.");
            LoggerInstance.Msg($"Sound folder: {_dataFolderPath}");
            LoggerInstance.Msg($"Per-craft overrides: {_craftConfigBasePath}");
            LoggerInstance.Msg("Open CraftEditor → click VWS button to customize sounds per-craft.");

            IndexAudioClips();
            LoadCustomSounds();
            InitDefaultRules();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            LoggerInstance.Msg($"[Scene] Loaded: '{sceneName}' (index={buildIndex})");

            if (sceneName == "MainMenu")
            {
                _testSoundPending = true;
                _testSoundTimer = 3.0f;
                LoggerInstance.Msg("[Test] Test sound scheduled for 3s after MainMenu load.");
            }

            if (sceneName == TARGET_SCENE)
            {
                _isInTargetScene = true;
                LoggerInstance.Msg($"Entered {TARGET_SCENE} — activating voice warnings.");
            }

            // look for CEUI to know if we're in craft editor
            if (!_isInCraftEditor)
            {
                try
                {
                    var ceui = UnityEngine.Object.FindObjectOfType<CEUI>();
                    if (ceui != null)
                    {
                        _isInCraftEditor = true;
                        _craftEditorUICreated = false;
                        LoggerInstance.Msg("[CraftEditor] Detected CraftEditor scene.");
                    }
                }
                catch { }
            }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName == TARGET_SCENE)
            {
                LoggerInstance.Msg($"[Scene] Left {TARGET_SCENE} — deactivating.");
                _isInTargetScene = false;
                Cleanup();
            }

            _isInCraftEditor = false;
            _craftEditorUICreated = false;
            _warningEditorButton = null;
            _warningCreatorPanel = null;
            _warningEditorActive = false;
        }

        public override void OnUpdate()
        {
            // deferred ui creation
            if (_isInCraftEditor && !_craftEditorUICreated)
            {
                try { TryCreateCraftEditorUI(); }
                catch (Exception ex) { LoggerInstance.Warning($"[CraftEditor] UI creation failed: {ex.Message}"); }
            }

            // track craft name changes in editor
            if (_isInCraftEditor)
            {
                // close vws when a native mode gets selected
                if (_warningEditorActive)
                {
                    try
                    {
                        var cem = CEManager.instance;
                        if (cem != null && cem.Mode != Mode.Other)
                        {
                            _warningEditorActive = false;
                            if (_warningCreatorPanel != null)
                                _warningCreatorPanel.SetActive(false);
                            UpdateVwsButtonColor();
                            LoggerInstance.Msg("[CraftEditor] VWS closed (native mode selected)");
                        }
                    }
                    catch { }
                }

                try
                {
                    var ceui = UnityEngine.Object.FindObjectOfType<CEUI>();
                    if (ceui != null && ceui.craftTitleField != null)
                    {
                        string title = ceui.craftTitleField.text;
                        if (!string.IsNullOrEmpty(title) && title != _editorCraftName)
                        {
                            _editorCraftName = title;
                            _configCraftName = title;
                            LoadCraftOverrides(title);
                            RefreshWarningPanelButtons();
                            LoggerInstance.Msg($"[CraftEditor] Craft changed to '{title}'");
                        }
                    }
                }
                catch { }
            }

            // tilde key toggles mute (new input system)
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.backquoteKey.wasPressedThisFrame)
                    ToggleMute();
            }
            catch { }

            // try to link our audio to the game's mixer
            TryLinkMixerGroup();

            // tick notification timer
            if (_notificationTimer > 0f)
                _notificationTimer -= Time.deltaTime;

            // test sound on menu load
            if (_testSoundPending)
            {
                _testSoundTimer -= Time.deltaTime;
                if (_testSoundTimer <= 0f)
                {
                    _testSoundPending = false;
                    string testClip = _clipPaths.ContainsKey("vws_master_caution_f18")
                        ? "vws_master_caution_f18" : _clipPaths.Keys.FirstOrDefault();
                    if (testClip != null)
                    {
                        PlayWarningSound(testClip);
                        LoggerInstance.Msg($"[Test] Played '{testClip}' via winmm");
                    }
                    else
                        LoggerInstance.Error("[Test] No clips available!");
                }
            }

            if (!_isInTargetScene) return;

            // tick cooldowns
            float dt = Time.deltaTime;
            var keys = _warningCooldowns.Keys.ToList();
            foreach (var key in keys)
            {
                _warningCooldowns[key] -= dt;
                if (_warningCooldowns[key] <= 0f)
                    _warningCooldowns.Remove(key);
            }

            // play queued warnings
            _queueCooldown -= dt;
            if (_queueCooldown <= 0f && _warningQueue.Count > 0 && !IsAudioPlaying())
            {
                string nextWarning = _warningQueue.Dequeue();
                PlayWarningSound(nextWarning);
                _queueCooldown = 1.5f;
            }

            // throttled debug logging
            _debugLogTimer -= dt;
            bool doDebugLog = _debugLogTimer <= 0f;
            if (doDebugLog) _debugLogTimer = DEBUG_LOG_INTERVAL;

            // grab the active craft
            Craft craft = null;
            try { craft = Craft.active; }
            catch (Exception ex)
            {
                if (doDebugLog) LoggerInstance.Warning($"Failed to get Craft.active: {ex.GetType().Name}: {ex.Message}");
            }

            if (craft == null)
            {
                if (doDebugLog) LoggerInstance.Msg("[Debug] Craft.active is null");
                return;
            }

            try { if (!craft.initialized) { if (doDebugLog) LoggerInstance.Msg("[Debug] Craft not initialized"); return; } }
            catch { }

            Command cmd = null;
            try { cmd = craft.command; }
            catch (Exception ex)
            {
                if (doDebugLog) LoggerInstance.Warning($"Failed to get craft.command: {ex.GetType().Name}: {ex.Message}");
            }

            if (doDebugLog)
            {
                try
                {
                    float speed = craft.velocity.magnitude;
                    float fuelFrac = GetFuelFraction(craft);
                    bool landed = IsLandedOrLanding(craft, cmd);
                    string status = $"[Debug] Craft='{craft.vName}' alt={craft.altitude:F0}m radarAlt={craft.radarAlt:F0}m mach={craft.mach:F2} speed={speed:F1}m/s fuel={fuelFrac:P0}";
                    if (cmd != null)
                    {
                        float throttle = 0f;
                        try { throttle = craft.craftControls.Throttle; } catch { }
                        float teleBankAngle = 0f;
                        try { teleBankAngle = cmd.Bank; } catch { }
                        status += $" yG={cmd.yG:F1} alpha={cmd.alpha:F1} ias={cmd.ias:F0} rollRate={cmd.rollRate:F1} bank={teleBankAngle:F1} flaps={cmd.flapSetup} throttle={throttle:F2}";
                    }
                    else
                        status += " cmd=NULL";
                    status += $" landed={landed} wheelsDown={AreWheelsGrounded(craft)} playing={IsAudioPlaying()} clips={_clipPaths.Count}";
                    LoggerInstance.Msg(status);
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning($"[Debug] Telemetry read failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // pick up craft name changes at runtime
            try
            {
                string craftName = craft.vName ?? "";
                if (craftName != _currentCraftName)
                {
                    _currentCraftName = craftName;
                    _configCraftName = craftName;
                    LoadCraftOverrides(craftName);
                }
            }
            catch { }

            // reset per-frame flags
            _activeEvents.Clear();
            _eventValues.Clear();

            // run evaluators
            EvaluateOverG(craft, cmd);
            EvaluateOverspeed(craft, cmd);
            EvaluateAltitude(craft, cmd);
            EvaluateFuel(craft, cmd);
            EvaluateStall(craft, cmd);
            EvaluateAngleOfAttack(craft, cmd);
            EvaluateRollRate(craft, cmd);
            EvaluateEngineFailure(craft);
            EvaluateEngineFire(craft);
            EvaluateGeneratorFailure(craft);
            EvaluateHydraulicFailure(craft);
            EvaluateGearWarning(craft, cmd);
            EvaluateMissileThreat(craft);
            EvaluateRadarLock(craft, cmd);
            EvaluateInputs(craft);

            // check rules
            EvaluateEventRules();
        }

        public override void OnGUI()
        {
            DrawNotification();
        }
    }
}
