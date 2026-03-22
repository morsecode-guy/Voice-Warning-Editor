using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceWarningEditor
{
    // rules engine, maps conditions to sounds :3
    public partial class VoiceWarningEditorMod
    {
        // set up default rules like vanilla
        internal void InitDefaultRules()
        {
            if (_rulesInitialized) return;
            _rulesInitialized = true;
            _eventRules.Clear();

            AddDefaultRule("def_over_g", WarningEvent.OverG, "vws_over_g_f18", COOLDOWN_SHORT);
            AddDefaultRule("def_overspeed", WarningEvent.Overspeed, "vws_critical_speed_f18", COOLDOWN_MEDIUM);
            AddDefaultRule("def_crit_alt", WarningEvent.CriticalAltitude, "vws_critical_altitude_f18", COOLDOWN_SHORT);
            AddDefaultRule("def_low_alt", WarningEvent.LowAltitude, "vws_altitude_f18", COOLDOWN_MEDIUM);
            AddDefaultRule("def_crit_fuel", WarningEvent.CriticalFuel, "vws_check_fuel_f18", COOLDOWN_MEDIUM);
            AddDefaultRule("def_bingo1", WarningEvent.BingoFuel, "vws_bingo1_f18", COOLDOWN_LONG, "bingo");
            AddDefaultRule("def_bingo2", WarningEvent.BingoFuel, "vws_bingo2_f18", COOLDOWN_LONG, "bingo");
            AddDefaultRule("def_stall", WarningEvent.Stall, "vws_caution_f18", COOLDOWN_MEDIUM);
            AddDefaultRule("def_crit_aoa", WarningEvent.CriticalAOA, "vws_critical_angle_f18", COOLDOWN_SHORT);
            AddDefaultRule("def_bank_left", WarningEvent.BankLeft, "vws_roll_left_f18", COOLDOWN_SHORT, "bank_dir");
            AddDefaultRule("def_bank_right", WarningEvent.BankRight, "vws_roll_right_f18", COOLDOWN_SHORT, "bank_dir");
            AddDefaultRule("def_roll_out", WarningEvent.HighRollRate, "vws_roll_out_f18", COOLDOWN_MEDIUM);
            AddDefaultRule("def_eng_fail", WarningEvent.EngineFailure, "vws_engine_failure_f18", COOLDOWN_LONG);
            AddDefaultRule("def_eng_fire", WarningEvent.EngineFire, "vws_engine_fire_f18", COOLDOWN_LONG);
            AddDefaultRule("def_fire_call", WarningEvent.EngineFire, "vws_fire_f18", COOLDOWN_LONG);
            AddDefaultRule("def_damage", WarningEvent.BattleDamage, "vws_master_caution_f18", COOLDOWN_LONG);
            AddDefaultRule("def_gen_fail", WarningEvent.GeneratorFailure, "vws_generator_failure_f18", COOLDOWN_LONG);
            AddDefaultRule("def_hydro", WarningEvent.HydraulicFailure, "vws_hydrosystem_failure_f18", COOLDOWN_LONG);
            AddDefaultRule("def_gear", WarningEvent.GearNotDown, "vws_check_gear_f18", COOLDOWN_MEDIUM);
            // missile uses waveout alarm, but the event flag is still set for custom rules
            AddDefaultRule("def_radar", WarningEvent.RadarLock, "radar_lock_f18", COOLDOWN_MEDIUM);

            LoggerInstance.Msg($"[EventSystem] Initialized {_eventRules.Count} default rules");
        }

        private void AddDefaultRule(string id, WarningEvent evt, string soundClip, float cooldown, string group = "")
        {
            _eventRules.Add(new EventRule
            {
                Id = id,
                Conditions = new List<WarningEvent> { evt },
                Logic = RuleLogic.Any,
                SoundClip = soundClip,
                Cooldown = cooldown,
                Enabled = true,
                Group = group,
                IsDefault = true,
            });
        }

        // check if a condition passes, using custom threshold or the default flag
        private bool IsConditionMet(WarningEvent condition, EventRule rule)
        {
            if (rule.Thresholds.TryGetValue(condition, out float customThreshold) &&
                _eventValues.TryGetValue(condition, out float rawValue) &&
                THRESHOLD_META.TryGetValue(condition, out var meta))
            {
                // use rule-specific comparison or fall back to meta default
                CompareOp op = rule.Comparisons.TryGetValue(condition, out var ruleOp)
                    ? ruleOp
                    : (meta.greaterThan ? CompareOp.Gte : CompareOp.Lte);

                return op switch
                {
                    CompareOp.Gte => rawValue >= customThreshold,
                    CompareOp.Lte => rawValue <= customThreshold,
                    CompareOp.Gt  => rawValue > customThreshold,
                    CompareOp.Lt  => rawValue < customThreshold,
                    CompareOp.Eq  => Math.Abs(rawValue - customThreshold) < 0.001f,
                    _ => rawValue >= customThreshold,
                };
            }

            return _activeEvents.Contains(condition);
        }

        // run all rules against active events
        // same-group rules only fire once per frame (random pick)
        internal void EvaluateEventRules()
        {
            if (_activeEvents.Count == 0 && _eventValues.Count == 0) return;

            var groupFired = new HashSet<string>();

            // shuffle for random group selection
            var indices = new int[_eventRules.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            for (int i = indices.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            for (int idx = 0; idx < indices.Length; idx++)
            {
                var rule = _eventRules[indices[idx]];
                if (!rule.Enabled || rule.Conditions.Count == 0) continue;

                if (!string.IsNullOrEmpty(rule.Group) && groupFired.Contains(rule.Group))
                    continue;

                bool matches = rule.Logic == RuleLogic.All
                    ? rule.Conditions.All(c => IsConditionMet(c, rule))
                    : rule.Conditions.Any(c => IsConditionMet(c, rule));

                if (matches)
                {
                    TriggerWarning(rule.SoundClip, rule.Cooldown);
                    if (!string.IsNullOrEmpty(rule.Group))
                        groupFired.Add(rule.Group);
                }
            }
        }
    }
}
