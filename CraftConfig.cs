using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VoiceWarningEditor
{
    // per-craft config saving and loading
    public partial class VoiceWarningEditorMod
    {
        // get sound path, per-craft override first then default
        internal string GetSoundPath(string clipName)
        {
            if (_craftOverrides.ContainsKey(clipName))
            {
                string overridePath = _craftOverrides[clipName];
                if (File.Exists(overridePath))
                    return overridePath;
            }

            if (_clipPaths.ContainsKey(clipName))
                return _clipPaths[clipName];

            return null;
        }

        // make craft name safe for folders
        private string SanitizeCraftName(string craftName)
        {
            if (string.IsNullOrWhiteSpace(craftName))
                return "_default";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in craftName)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }

        // get config dir for a craft
        internal string GetCraftConfigDir(string craftName, bool create = true)
        {
            string safeName = SanitizeCraftName(craftName);
            string dir = Path.Combine(_craftConfigBasePath, safeName);
            if (create && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                LoggerInstance.Msg($"[Config] Created craft config folder: {dir}");
            }
            return dir;
        }

        // load overrides from craft config folder
        internal void LoadCraftOverrides(string craftName)
        {
            _craftOverrides.Clear();

            if (string.IsNullOrWhiteSpace(craftName))
            {
                LoggerInstance.Msg("[Config] No craft name — using defaults only");
                return;
            }

            string safeName = SanitizeCraftName(craftName);
            string craftDir = Path.Combine(_craftConfigBasePath, safeName);

            if (!Directory.Exists(craftDir))
            {
                LoggerInstance.Msg($"[Config] No config folder for '{craftName}' — using defaults");
                return;
            }

            // read config.json mappings
            string configFile = Path.Combine(craftDir, "config.json");
            if (File.Exists(configFile))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configFile);
                    foreach (string line in lines)
                    {
                        string l = line.Trim().TrimEnd(',');
                        if (l.StartsWith("\""))
                        {
                            int colonIdx = l.IndexOf("\": \"", StringComparison.Ordinal);
                            if (colonIdx > 0)
                            {
                                string key = l.Substring(1, colonIdx - 1);
                                string value = l.Substring(colonIdx + 4).TrimEnd('"');

                                string resolvedPath = Path.IsPathRooted(value)
                                    ? value.Replace("\\\\", "\\")
                                    : Path.GetFullPath(Path.Combine(craftDir, value));

                                if (File.Exists(resolvedPath))
                                    _craftOverrides[key] = resolvedPath;
                                else
                                    LoggerInstance.Warning($"[Config] Override file not found: {resolvedPath}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning($"[Config] Error reading config.json: {ex.Message}");
                }
            }

            // also grab audio files matching clip names
            string[] audioExts = { "*.wav", "*.ogg", "*.mp3", "*.flac" };
            foreach (string pattern in audioExts)
            {
                string[] files = Directory.GetFiles(craftDir, pattern);
                foreach (string filePath in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    if (!_craftOverrides.ContainsKey(fileName))
                        _craftOverrides[fileName] = Path.GetFullPath(filePath);
                }
            }

            if (_craftOverrides.Count > 0)
                LoggerInstance.Msg($"[Config] Loaded {_craftOverrides.Count} override(s) for '{craftName}'");
            else
                LoggerInstance.Msg($"[Config] No overrides for '{craftName}' — using defaults");

            LoadEventRules(craftDir);
        }

        // save overrides to craft folder, copies sound files too
        internal void SaveCraftConfig(string craftName)
        {
            if (string.IsNullOrWhiteSpace(craftName))
            {
                LoggerInstance.Warning("[Config] Cannot save — no craft name");
                return;
            }

            string craftDir = GetCraftConfigDir(craftName);
            string configFile = Path.Combine(craftDir, "config.json");

            try
            {
                var savedOverrides = new Dictionary<string, string>();

                foreach (var kvp in _craftOverrides)
                {
                    string clipName = kvp.Key;
                    string sourcePath = kvp.Value;

                    if (!File.Exists(sourcePath))
                    {
                        LoggerInstance.Warning($"[Config] Source missing for '{clipName}': {sourcePath}");
                        continue;
                    }

                    // skip sounds from the main data folder
                    string sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
                    string mainDataDir = Path.GetFullPath(_dataFolderPath);
                    if (string.Equals(sourceDir, mainDataDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // copy into craft folder
                    string ext = Path.GetExtension(sourcePath);
                    string destFileName = clipName + ext;
                    string destPath = Path.Combine(craftDir, destFileName);

                    if (Path.GetFullPath(sourcePath) != Path.GetFullPath(destPath))
                    {
                        File.Copy(sourcePath, destPath, true);
                        LoggerInstance.Msg($"[Config] Copied '{Path.GetFileName(sourcePath)}' -> '{destFileName}'");
                    }

                    savedOverrides[clipName] = destFileName;
                }

                // write config.json
                var sb = new StringBuilder();
                sb.AppendLine("{");

                string presetDisplayName = craftName;
                if (_presetNameInput != null && !string.IsNullOrWhiteSpace(_presetNameInput.text))
                    presetDisplayName = _presetNameInput.text;
                sb.AppendLine($"  \"presetName\": \"{presetDisplayName}\",");

                int i = 0;
                foreach (var kvp in savedOverrides)
                {
                    sb.Append($"  \"{kvp.Key}\": \"{kvp.Value}\"");
                    if (++i < savedOverrides.Count) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("}");
                File.WriteAllText(configFile, sb.ToString());
                LoggerInstance.Msg($"[Config] Saved {savedOverrides.Count} override(s) for '{craftName}'");

                SaveEventRules(craftDir);
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Config] Save failed: {ex.Message}");
            }
        }

        // event rules persistence

        // save rules to rules.cfg
        private void SaveEventRules(string craftDir)
        {
            string rulesFile = Path.Combine(craftDir, "rules.cfg");
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# VWS Event Rules Configuration");
                sb.AppendLine("# -id disables a default rule");
                sb.AppendLine("# +id;events;logic;sound;cooldown;group;enabled;thresholds;comparisons adds custom rules");
                sb.AppendLine("# ~id;... modifies a default rule's thresholds/enabled state");
                sb.AppendLine();

                // disabled defaults without threshold changes
                foreach (var rule in _eventRules)
                {
                    if (rule.IsDefault && !rule.Enabled && rule.Thresholds.Count == 0 && rule.Comparisons.Count == 0)
                        sb.AppendLine($"-{rule.Id}");
                }

                // defaults with custom thresholds or disabled+thresholds
                foreach (var rule in _eventRules)
                {
                    if (!rule.IsDefault) continue;
                    if (rule.Thresholds.Count == 0 && rule.Comparisons.Count == 0 && rule.Enabled) continue;
                    if (!rule.Enabled && rule.Thresholds.Count == 0 && rule.Comparisons.Count == 0) continue;

                    string events = string.Join(",", rule.Conditions.Select(c => c.ToString()));
                    string threshStr = SerializeThresholds(rule.Thresholds);
                    string compStr = SerializeComparisons(rule.Comparisons);
                    sb.AppendLine($"~{rule.Id};{events};{rule.Logic};{rule.SoundClip};{rule.Cooldown:F1};{rule.Group};{rule.Enabled};{threshStr};{compStr}");
                }

                // custom rules
                foreach (var rule in _eventRules)
                {
                    if (rule.IsDefault) continue;
                    string events = string.Join(",", rule.Conditions.Select(c => c.ToString()));
                    string threshStr = SerializeThresholds(rule.Thresholds);
                    string compStr = SerializeComparisons(rule.Comparisons);
                    sb.AppendLine($"+{rule.Id};{events};{rule.Logic};{rule.SoundClip};{rule.Cooldown:F1};{rule.Group};{rule.Enabled};{threshStr};{compStr}");
                }

                File.WriteAllText(rulesFile, sb.ToString());
                int customCount = _eventRules.Count(r => !r.IsDefault);
                int disabledCount = _eventRules.Count(r => r.IsDefault && !r.Enabled);
                LoggerInstance.Msg($"[Config] Saved rules ({customCount} custom, {disabledCount} disabled)");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Config] Failed to save rules: {ex.Message}");
            }
        }

        // serialize comparisons to string
        private static string SerializeComparisons(Dictionary<WarningEvent, CompareOp> comparisons)
        {
            if (comparisons == null || comparisons.Count == 0) return "";
            return string.Join(",", comparisons.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        // parse comparisons back from string
        private static Dictionary<WarningEvent, CompareOp> ParseComparisons(string compStr)
        {
            var result = new Dictionary<WarningEvent, CompareOp>();
            if (string.IsNullOrEmpty(compStr)) return result;

            string[] pairs = compStr.Split(',');
            foreach (string pair in pairs)
            {
                int eq = pair.IndexOf('=');
                if (eq < 1) continue;
                string evtName = pair.Substring(0, eq).Trim();
                string opStr = pair.Substring(eq + 1).Trim();
                if (Enum.TryParse<WarningEvent>(evtName, out var evt) &&
                    Enum.TryParse<CompareOp>(opStr, out var op))
                {
                    result[evt] = op;
                }
            }
            return result;
        }

        // serialize thresholds to string
        private static string SerializeThresholds(Dictionary<WarningEvent, float> thresholds)
        {
            if (thresholds == null || thresholds.Count == 0) return "";
            return string.Join(",", thresholds.Select(kv =>
                $"{kv.Key}={kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        }

        // parse thresholds back from string
        private static Dictionary<WarningEvent, float> ParseThresholds(string threshStr)
        {
            var result = new Dictionary<WarningEvent, float>();
            if (string.IsNullOrEmpty(threshStr)) return result;

            string[] pairs = threshStr.Split(',');
            foreach (string pair in pairs)
            {
                int eq = pair.IndexOf('=');
                if (eq < 1) continue;
                string evtName = pair.Substring(0, eq).Trim();
                string valStr = pair.Substring(eq + 1).Trim();
                if (Enum.TryParse<WarningEvent>(evtName, out var evt) &&
                    float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var val))
                {
                    result[evt] = val;
                }
            }
            return result;
        }

        // load rules from file, reset defaults first
        private void LoadEventRules(string craftDir)
        {
            _rulesInitialized = false;
            InitDefaultRules();

            string rulesFile = Path.Combine(craftDir, "rules.cfg");
            if (!File.Exists(rulesFile)) return;

            try
            {
                string[] lines = File.ReadAllLines(rulesFile);
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    if (line.StartsWith("-"))
                    {
                        string id = line.Substring(1).Trim();
                        var rule = _eventRules.FirstOrDefault(r => r.Id == id);
                        if (rule != null) rule.Enabled = false;
                    }
                    else if (line.StartsWith("~"))
                    {
                        // modified default: apply overrides
                        string data = line.Substring(1);
                        string[] parts = data.Split(';');
                        if (parts.Length < 5) continue;

                        string id = parts[0];
                        var rule = _eventRules.FirstOrDefault(r => r.Id == id && r.IsDefault);
                        if (rule == null) continue;

                        rule.Enabled = parts.Length > 6 ? parts[6] == "True" : true;
                        if (parts.Length > 7)
                            rule.Thresholds = ParseThresholds(parts[7]);
                        if (parts.Length > 8)
                            rule.Comparisons = ParseComparisons(parts[8]);
                    }
                    else if (line.StartsWith("+"))
                    {
                        // custom rule
                        string data = line.Substring(1);
                        string[] parts = data.Split(';');
                        if (parts.Length < 5) continue;

                        var customRule = new EventRule
                        {
                            Id = parts[0],
                            Conditions = new List<WarningEvent>(),
                            Logic = parts[2] == "All" ? RuleLogic.All : RuleLogic.Any,
                            SoundClip = parts[3],
                            Cooldown = float.TryParse(parts[4], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var cd) ? cd : 5.0f,
                            Group = parts.Length > 5 ? parts[5] : "",
                            Enabled = parts.Length > 6 ? parts[6] == "True" : true,
                            IsDefault = false,
                            Thresholds = parts.Length > 7 ? ParseThresholds(parts[7]) : new Dictionary<WarningEvent, float>(),
                            Comparisons = parts.Length > 8 ? ParseComparisons(parts[8]) : new Dictionary<WarningEvent, CompareOp>(),
                        };

                        string[] eventNames = parts[1].Split(',');
                        foreach (string evtName in eventNames)
                        {
                            if (Enum.TryParse<WarningEvent>(evtName.Trim(), out var evt))
                                customRule.Conditions.Add(evt);
                        }

                        if (customRule.Conditions.Count > 0)
                            _eventRules.Add(customRule);
                    }
                }

                LoggerInstance.Msg($"[Config] Loaded event rules from {rulesFile}");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Config] Failed to load rules: {ex.Message}");
            }
        }

        // save custom sounds list
        internal void SaveCustomSounds()
        {
            try
            {
                string soundsFile = Path.Combine(_dataFolderPath, "sounds.cfg");
                var sb = new StringBuilder();
                sb.AppendLine("# VWS Custom Sounds");
                sb.AppendLine("# Format: clipName;displayName;filePath");
                foreach (var cs in _customSounds)
                    sb.AppendLine($"{cs.clipName};{cs.displayName};{cs.filePath}");
                File.WriteAllText(soundsFile, sb.ToString());
                LoggerInstance.Msg($"[Config] Saved {_customSounds.Count} custom sounds");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Config] Failed to save custom sounds: {ex.Message}");
            }
        }

        // load custom sounds list
        internal void LoadCustomSounds()
        {
            string soundsFile = Path.Combine(_dataFolderPath, "sounds.cfg");
            if (!File.Exists(soundsFile)) return;

            try
            {
                _customSounds.Clear();
                string[] lines = File.ReadAllLines(soundsFile);
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    string[] parts = line.Split(';');
                    if (parts.Length < 3) continue;

                    string clipName = parts[0];
                    string displayName = parts[1];
                    string filePath = parts[2];

                    if (!File.Exists(filePath))
                    {
                        LoggerInstance.Warning($"[Config] Custom sound file missing: {filePath}");
                        continue;
                    }

                    _customSounds.Add((clipName, displayName, filePath));
                    _clipPaths[clipName] = filePath;
                }

                LoggerInstance.Msg($"[Config] Loaded {_customSounds.Count} custom sounds");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Config] Failed to load custom sounds: {ex.Message}");
            }
        }
    }
}
