using System.Collections.Generic;

namespace VoiceWarningEditor
{
    // all the events we can watch for :3
    internal enum WarningEvent
    {
        OverG,
        Overspeed,
        CriticalAltitude,
        LowAltitude,
        CriticalFuel,
        BingoFuel,
        Stall,
        CriticalAOA,
        BankLeft,
        BankRight,
        HighRollRate,
        EngineFailure,
        EngineFire,
        BattleDamage,
        GeneratorFailure,
        HydraulicFailure,
        GearNotDown,
        GearUp,      // all retractable gear up
        GearDown,    // all retractable gear down
        MissileIncoming,
        RadarLock,
        // built-in craft inputs
        InputThrottle,
        InputPitch,
        InputRoll,
        InputYaw,
        InputCollective,
        InputBrake,
        InputFlaps,
        // custom axes, matched by index at runtime
        CustomAxis0,
        CustomAxis1,
        CustomAxis2,
        CustomAxis3,
        CustomAxis4,
        CustomAxis5,
        CustomAxis6,
        CustomAxis7,
    }

    // how conditions combine in a rule
    internal enum RuleLogic { Any, All }

    // threshold comparison ops
    internal enum CompareOp { Gte, Lte, Gt, Lt, Eq }

    // maps one or more events to a sound
    internal class EventRule
    {
        public string Id;
        public List<WarningEvent> Conditions = new List<WarningEvent>();
        public RuleLogic Logic = RuleLogic.Any;
        public string SoundClip;
        public float Cooldown = 5.0f;
        public bool Enabled = true;
        public string Group = "";
        public bool IsDefault;
        // custom thresholds per event
        public Dictionary<WarningEvent, float> Thresholds = new Dictionary<WarningEvent, float>();
        // custom comparison ops per event
        public Dictionary<WarningEvent, CompareOp> Comparisons = new Dictionary<WarningEvent, CompareOp>();
    }
}
