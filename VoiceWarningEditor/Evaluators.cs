using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;

namespace VoiceWarningEditor
{
    // evaluators for all the warning conditions
    public partial class VoiceWarningEditorMod
    {
        // any wheel touching ground?
        private bool AreWheelsGrounded(Craft craft)
        {
            try
            {
                var wheels = craft.wheels;
                if (wheels != null)
                {
                    for (int i = 0; i < wheels.Length; i++)
                    {
                        if (wheels[i] != null && wheels[i].grounded)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // on ground or coming in to land
        internal bool IsLandedOrLanding(Craft craft, Command cmd)
        {
            try
            {
                if (AreWheelsGrounded(craft))
                    return true;

                float speed = craft.velocity.magnitude;
                if (craft.radarAlt < 10f && speed < 5f)
                    return true;

                if (cmd != null)
                {
                    bool flapsOut = cmd.flapSetup > 0;
                    float throttle = 1.0f;
                    try { throttle = craft.craftControls.Throttle; } catch { }
                    bool lowThrottle = throttle < 0.4f;
                    bool lowAlt = craft.radarAlt < 300f;
                    bool descending = craft.velocity.y < 0f;

                    if (flapsOut && lowThrottle && lowAlt && descending)
                        return true;
                }
            }
            catch { }
            return false;
        }

        // actually flying, not parked or landing
        private bool IsInFlight(Craft craft, Command cmd)
        {
            return !IsLandedOrLanding(craft, cmd);
        }

        // flight parameter checks

        private void EvaluateOverG(Craft craft, Command cmd)
        {
            if (cmd == null) return;
            try
            {
                float gForce = Mathf.Abs(cmd.yG);
                _eventValues[WarningEvent.OverG] = gForce;

                float threshold = DEFAULT_OVER_G_THRESHOLD;
                try { if (cmd.warnings != null && cmd.warnings.overG > 0) threshold = cmd.warnings.overG; }
                catch { }

                if (gForce >= threshold)
                    _activeEvents.Add(WarningEvent.OverG);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"EvaluateOverG: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void EvaluateOverspeed(Craft craft, Command cmd)
        {
            if (cmd == null) return;
            try
            {
                float mach = craft.mach;
                _eventValues[WarningEvent.Overspeed] = mach;

                float threshold = DEFAULT_OVERSPEED_MACH;
                try { if (cmd.warnings != null && cmd.warnings.overSpeed > 0) threshold = cmd.warnings.overSpeed; }
                catch { }

                if (mach >= threshold)
                    _activeEvents.Add(WarningEvent.Overspeed);
            }
            catch { }
        }

        private void EvaluateAltitude(Craft craft, Command cmd)
        {
            try
            {
                // suppress when on the ground
                if (!IsInFlight(craft, cmd)) return;

                float alt = craft.radarAlt;
                _eventValues[WarningEvent.CriticalAltitude] = alt;
                _eventValues[WarningEvent.LowAltitude] = alt;

                if (alt <= DEFAULT_CRITICAL_ALTITUDE && alt > 0)
                    _activeEvents.Add(WarningEvent.CriticalAltitude);
                else if (alt <= DEFAULT_LOW_ALTITUDE && alt > DEFAULT_CRITICAL_ALTITUDE)
                    _activeEvents.Add(WarningEvent.LowAltitude);
            }
            catch { }
        }

        private void EvaluateFuel(Craft craft, Command cmd)
        {
            try
            {
                float fuelFraction = GetFuelFraction(craft);
                _eventValues[WarningEvent.CriticalFuel] = fuelFraction;
                _eventValues[WarningEvent.BingoFuel] = fuelFraction;

                float bingThreshold = DEFAULT_BINGO_FUEL_FRACTION;
                try { if (cmd != null && cmd.warnings != null && cmd.warnings.bingo > 0) bingThreshold = cmd.warnings.bingo; }
                catch { }

                if (fuelFraction <= DEFAULT_CRITICAL_FUEL_FRACTION && fuelFraction >= 0f)
                    _activeEvents.Add(WarningEvent.CriticalFuel);
                else if (fuelFraction <= bingThreshold && fuelFraction > DEFAULT_CRITICAL_FUEL_FRACTION)
                    _activeEvents.Add(WarningEvent.BingoFuel);
            }
            catch { }
        }

        private void EvaluateStall(Craft craft, Command cmd)
        {
            if (cmd == null) return;
            try
            {
                if (!IsInFlight(craft, cmd)) return;
                if (craft.altitude < 50f) return;

                // dont stall-warn vtol/helo craft that are basically hovering
                // or any craft moving slowly near the ground
                if (craft.radarAlt < 100f && craft.velocity.magnitude < 40f)
                    return;

                float ias = cmd.ias;
                _eventValues[WarningEvent.Stall] = ias;

                // only warn if actually moving-ish but too slow for sustained flight
                if (ias > 5f && ias < DEFAULT_CRITICAL_SPEED)
                    _activeEvents.Add(WarningEvent.Stall);
            }
            catch { }
        }

        private void EvaluateAngleOfAttack(Craft craft, Command cmd)
        {
            if (cmd == null) return;
            try
            {
                if (!IsInFlight(craft, cmd)) return;

                float alpha = Mathf.Abs(cmd.alpha);
                _eventValues[WarningEvent.CriticalAOA] = alpha;

                if (alpha >= DEFAULT_CRITICAL_AOA)
                    _activeEvents.Add(WarningEvent.CriticalAOA);
            }
            catch { }
        }

        private void EvaluateRollRate(Craft craft, Command cmd)
        {
            if (cmd == null) return;
            try
            {
                if (!IsInFlight(craft, cmd)) return;

                // bank angle
                float bankAngle = cmd.Bank;
                float absBankAngle = Mathf.Abs(bankAngle);
                _eventValues[WarningEvent.BankLeft] = absBankAngle;
                _eventValues[WarningEvent.BankRight] = absBankAngle;

                if (absBankAngle >= DEFAULT_BANK_ANGLE_WARN)
                {
                    // positive bank = right, tell them to roll right to recover
                    if (bankAngle > 0)
                        _activeEvents.Add(WarningEvent.BankRight);
                    else
                        _activeEvents.Add(WarningEvent.BankLeft);
                }

                // roll rate
                float rollRate = Mathf.Abs(cmd.rollRate);
                _eventValues[WarningEvent.HighRollRate] = rollRate;
                if (rollRate >= DEFAULT_ROLL_RATE_WARN)
                    _activeEvents.Add(WarningEvent.HighRollRate);
            }
            catch { }
        }

        // engine and systems checks

        // turbine spool condition or flameout
        // only fires in flight to avoid false positives on the ground
        private void EvaluateEngineFailure(Craft craft)
        {
            try
            {
                // dont yell about engines on the ground
                if (craft.radarAlt < 20f && craft.velocity.magnitude < 30f)
                    return;

                var turbines = craft.turbines;
                if (turbines == null) return;

                for (int i = 0; i < turbines.Count; i++)
                {
                    var turbine = turbines[i];
                    if (turbine == null) continue;

                    // skip engines that arent running (throttle near zero)
                    if (turbine.mainThrottle < 0.15f) continue;

                    // lost ignition while throttle is up
                    if (!turbine.ignition && turbine.mainThrottle > 0.3f)
                    {
                        _activeEvents.Add(WarningEvent.EngineFailure);
                        return;
                    }

                    // compressor severely degraded (not just a little worn)
                    var spools = turbine.spool;
                    if (spools != null)
                    {
                        for (int j = 0; j < spools.Length; j++)
                        {
                            if (spools[j] != null && spools[j].compCondition < 0.3f)
                            {
                                _activeEvents.Add(WarningEvent.EngineFailure);
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // fire or battle damage from combat
        private void EvaluateEngineFire(Craft craft)
        {
            try
            {
                var parts = craft.parts;
                if (parts == null) return;

                bool hasFire = false;
                bool hasDamage = false;

                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i] == null) continue;

                    try
                    {
                        var fireTrail = parts[i].GetComponentInChildren<FireTrail>();
                        if (fireTrail != null && fireTrail.gameObject.activeInHierarchy)
                        {
                            hasFire = true;
                            break;
                        }
                    }
                    catch { }

                    // only trigger master caution for actually destroyed parts
                    // 0.5 was way too sensitive, lots of parts sit below that normally
                    try
                    {
                        var part = parts[i].GetComponent<Part>();
                        if (part != null && part.health < 0.2f && part.health > 0f)
                            hasDamage = true;
                    }
                    catch { }
                }

                if (hasFire)
                    _activeEvents.Add(WarningEvent.EngineFire);
                else if (hasDamage)
                    _activeEvents.Add(WarningEvent.BattleDamage);
            }
            catch { }
        }

        // electrical system charge level
        private void EvaluateGeneratorFailure(Craft craft)
        {
            try
            {
                var resources = craft.resources;
                if (resources == null || resources.electricSystems == null) return;

                for (int i = 0; i < resources.electricSystems.Count; i++)
                {
                    var elec = resources.electricSystems[i];
                    if (elec == null) continue;

                    if (elec.ChargeFrac < DEFAULT_ELECTRIC_LOW)
                    {
                        _activeEvents.Add(WarningEvent.GeneratorFailure);
                        return;
                    }
                }
            }
            catch { }
        }

        // hydraulic system check
        private void EvaluateHydraulicFailure(Craft craft)
        {
            try
            {
                var resources = craft.resources;
                if (resources == null || resources.hydraulicSystem == null) return;

                var hydro = resources.hydraulicSystem;
                if (hydro.Power < DEFAULT_HYDRAULIC_LOW || hydro.energy < 0.1f)
                    _activeEvents.Add(WarningEvent.HydraulicFailure);
            }
            catch { }
        }

        // gear up while slow and low
        private void EvaluateGearWarning(Craft craft, Command cmd)
        {
            try
            {
                if (cmd == null) return;

                float radarAlt = craft.radarAlt;
                float ias = cmd.ias;
                float rateOfClimb = cmd.rateOfClimb;

                // not landing config, skip
                if (radarAlt > 300f || ias > 150f || rateOfClimb > 10f)
                    return;
                if (AreWheelsGrounded(craft))
                    return;

                var gears = UnityEngine.Object.FindObjectsOfType<LandingGear>();
                if (gears == null || gears.Count == 0) return;

                bool anyRetracted = false;
                bool hasRetractable = false;

                for (int i = 0; i < gears.Count; i++)
                {
                    if (gears[i] == null) continue;
                    if (!gears[i].retractable) continue;
                    hasRetractable = true;
                    if (gears[i].gearRetracted)
                    {
                        anyRetracted = true;
                        break;
                    }
                }

                if (hasRetractable && anyRetracted)
                    _activeEvents.Add(WarningEvent.GearNotDown);
            }
            catch { }
        }

        // threat evaluators

        // incoming missiles — legacy Missile + ProcMissile types >:(
        private void EvaluateMissileThreat(Craft craft)
        {
            try
            {
                int guidedAtUs = 0;
                Rigidbody ourBody = null;

                try
                {
                    if (craft.command != null)
                    {
                        var rbs = craft.command.rigidbodies;
                        if (rbs != null && rbs.Count > 0)
                            ourBody = rbs[0];
                    }
                }
                catch { }

                if (_ourSignature == null)
                {
                    try { _ourSignature = craft.GetComponentInChildren<Signature>(); }
                    catch { }
                }

                // scan legacy missiles
                try
                {
                    var missiles = UnityEngine.Object.FindObjectsOfType<Missile>();
                    if (missiles != null)
                    {
                        for (int i = 0; i < missiles.Count; i++)
                        {
                            var m = missiles[i];
                            if (m == null || !m.launched || !m.IsGuiding || m.target == null)
                                continue;

                            bool targetingUs = ourBody != null && m.target == ourBody;

                            // fallback: close + heading towards us
                            if (!targetingUs)
                            {
                                try
                                {
                                    float dist = Vector3.Distance(m.transform.position, craft.transform.position);
                                    if (dist < 5000f)
                                    {
                                        Vector3 toUs = (craft.transform.position - m.transform.position).normalized;
                                        if (Vector3.Dot(m.transform.forward, toUs) > 0.8f)
                                            targetingUs = true;
                                    }
                                }
                                catch { }
                            }

                            if (targetingUs) guidedAtUs++;
                        }
                    }
                }
                catch { }

                // scan proc missiles
                try
                {
                    var procMissiles = UnityEngine.Object.FindObjectsOfType<ProcMissile>();
                    if (procMissiles != null)
                    {
                        for (int i = 0; i < procMissiles.Count; i++)
                        {
                            var pm = procMissiles[i];
                            if (pm == null || pm.IsUnguided) continue;

                            Signature locked = null;
                            try { locked = pm.Locked; } catch { continue; }
                            if (locked == null) continue;

                            bool targetingUs = _ourSignature != null && locked == _ourSignature;

                            if (!targetingUs)
                            {
                                try
                                {
                                    float dist = Vector3.Distance(pm.transform.position, craft.transform.position);
                                    if (dist < 5000f)
                                    {
                                        Vector3 toUs = (craft.transform.position - pm.transform.position).normalized;
                                        if (Vector3.Dot(pm.transform.forward, toUs) > 0.8f)
                                            targetingUs = true;
                                    }
                                }
                                catch { }
                            }

                            if (targetingUs) guidedAtUs++;
                        }
                    }
                }
                catch { }

                if (guidedAtUs > 0)
                    _activeEvents.Add(WarningEvent.MissileIncoming);

                if (guidedAtUs > 0 && !_missileAlarmPlaying)
                    StartMissileAlarm();
                else if (guidedAtUs == 0 && _missileAlarmPlaying)
                    StopMissileAlarm();

                _prevMissileCount = guidedAtUs;
            }
            catch { }
        }

        // rwr — radar painting us or active seekers
        // ir missiles dont trigger this (passive heat seekers)
        private void EvaluateRadarLock(Craft craft, Command cmd)
        {
            try
            {
                if (cmd == null) return;
                bool isLocked = false;

                // primary: rwr from our signature
                try
                {
                    if (_ourSignature == null)
                        _ourSignature = craft.GetComponentInChildren<Signature>();

                    if (_ourSignature != null)
                    {
                        var rwr = _ourSignature.RwrMaw;
                        if (rwr != null)
                        {
                            if (rwr.Radar > 0.1f || rwr.Approach > 0.1f)
                                isLocked = true;

                            // check for active seekers tracking us
                            try
                            {
                                var sigs = rwr.Signatures;
                                if (sigs != null)
                                {
                                    foreach (var kvp in sigs)
                                    {
                                        // active radar seeker on us
                                        if (kvp.Value.Item3) { isLocked = true; break; }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                // fallback: heat lock flag
                if (!isLocked)
                {
                    try { isLocked = cmd.heatLocked; } catch { }
                }

                // fallback: sam sites nearby
                if (!isLocked)
                {
                    try
                    {
                        var sams = UnityEngine.Object.FindObjectsOfType<AISam>();
                        if (sams != null)
                        {
                            for (int i = 0; i < sams.Count; i++)
                            {
                                if (sams[i] == null) continue;
                                try
                                {
                                    float dist = Vector3.Distance(sams[i].transform.position, craft.transform.position);
                                    if (dist < 20000f) { isLocked = true; break; }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                // edge-triggered, only fires on state change
                if (isLocked && !_wasRadarLocked)
                    _activeEvents.Add(WarningEvent.RadarLock);

                _wasRadarLocked = isLocked;
            }
            catch { }
        }

        // check craft inputs against thresholds
        private void EvaluateInputs(Craft craft)
        {
            try
            {
                var controls = craft.GetComponentInChildren<CraftControls>();
                if (controls == null) return;

                // built-in inputs
                SetInputEvent(WarningEvent.InputThrottle, controls.Throttle);
                SetInputEvent(WarningEvent.InputPitch, Math.Abs(controls.Pitch));
                SetInputEvent(WarningEvent.InputRoll, Math.Abs(controls.Roll));
                SetInputEvent(WarningEvent.InputYaw, Math.Abs(controls.Yaw));
                SetInputEvent(WarningEvent.InputCollective, controls.Collective);
                SetInputEvent(WarningEvent.InputBrake, controls.Brake);
                SetInputEvent(WarningEvent.InputFlaps, controls.FlapSettings);
            }
            catch { }

            // custom axes by index
            try
            {
                var axes = craft.customAxes;
                if (axes == null) return;

                WarningEvent baseEvent = WarningEvent.CustomAxis0;
                int count = Math.Min(axes.Count, 8);
                for (int i = 0; i < count; i++)
                {
                    var axis = axes[i];
                    if (axis == null) continue;
                    SetInputEvent(baseEvent + i, axis.Value);
                }
            }
            catch { }
        }

        // register an input event with its value
        private void SetInputEvent(WarningEvent evt, float value)
        {
            _activeEvents.Add(evt);
            _eventValues[evt] = value;
        }
    }
}
