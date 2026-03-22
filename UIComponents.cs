using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Il2CppTMPro;

namespace VoiceWarningEditor
{
    // reusable ui bits - buttons, rows, labels
    public partial class VoiceWarningEditorMod
    {
        // event toggle for rule editor grid
        private void CreateEventToggleButton(Transform parent, WarningEvent evt)
        {
            bool selected = _editorSelectedEvents.Contains(evt);
            string label = GetEventDisplayName(evt);

            var btnGO = new GameObject("Evt_" + evt);
            btnGO.AddComponent<RectTransform>();
            btnGO.transform.SetParent(parent, false);

            btnGO.AddComponent<LayoutElement>().flexibleWidth = 1;
            btnGO.GetComponent<LayoutElement>().minHeight = 20;

            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = selected
                ? new Color(0.2f, 0.45f, 0.7f, 1f)
                : new Color(0.2f, 0.2f, 0.25f, 1f);

            var btn = btnGO.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = btnImg.color;
            cols.highlightedColor = selected
                ? new Color(0.3f, 0.55f, 0.8f, 1f)
                : new Color(0.3f, 0.3f, 0.35f, 1f);
            cols.pressedColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            btn.colors = cols;

            var capturedEvt = evt;
            btn.onClick.AddListener((UnityAction)(() =>
            {
                if (_editorSelectedEvents.Contains(capturedEvt))
                    _editorSelectedEvents.Remove(capturedEvt);
                else
                    _editorSelectedEvents.Add(capturedEvt);
                ShowRuleEditor();
            }));

            var textGO = new GameObject("Text");
            textGO.AddComponent<RectTransform>();
            textGO.transform.SetParent(btnGO.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 9;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 6;
            tmp.fontSizeMax = 9;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = selected ? Color.white : new Color(0.7f, 0.7f, 0.7f, 1f);
            tmp.raycastTarget = false;
            var tRect = textGO.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = new Vector2(1, 1);
            tRect.offsetMax = new Vector2(-1, -1);
        }

        // any/all toggle for rule editor
        private void CreateLogicToggle(Transform parent, string label, RuleLogic mode)
        {
            bool active = _editorLogic == mode;

            var btnGO = new GameObject("Logic_" + label);
            btnGO.AddComponent<RectTransform>();
            btnGO.transform.SetParent(parent, false);

            var btnLE = btnGO.AddComponent<LayoutElement>();
            btnLE.minWidth = 36;
            btnLE.preferredWidth = 36;
            btnLE.flexibleWidth = 0;
            btnLE.minHeight = 20;

            btnGO.AddComponent<Image>().color = active
                ? new Color(0.3f, 0.5f, 0.7f, 1f)
                : new Color(0.2f, 0.2f, 0.25f, 1f);

            var btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener((UnityAction)(() =>
            {
                _editorLogic = mode;
                ShowRuleEditor();
            }));

            var textGO = new GameObject("Text");
            textGO.AddComponent<RectTransform>();
            textGO.transform.SetParent(btnGO.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 10;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = active ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
            tmp.raycastTarget = false;
            var tRect = textGO.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = Vector2.zero;
            tRect.offsetMax = Vector2.zero;
        }

        // cooldown button
        private void CreateCooldownButton(Transform parent, string label, float cooldownValue)
        {
            bool active = Mathf.Approximately(_editorCooldown, cooldownValue);

            var btnGO = new GameObject("CD_" + label);
            btnGO.AddComponent<RectTransform>();
            btnGO.transform.SetParent(parent, false);

            var btnLE = btnGO.AddComponent<LayoutElement>();
            btnLE.minWidth = 30;
            btnLE.preferredWidth = 30;
            btnLE.flexibleWidth = 0;
            btnLE.minHeight = 20;

            btnGO.AddComponent<Image>().color = active
                ? new Color(0.3f, 0.5f, 0.7f, 1f)
                : new Color(0.2f, 0.2f, 0.25f, 1f);

            var btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener((UnityAction)(() =>
            {
                _editorCooldown = cooldownValue;
                ShowRuleEditor();
            }));

            var textGO = new GameObject("Text");
            textGO.AddComponent<RectTransform>();
            textGO.transform.SetParent(btnGO.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 10;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = active ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
            tmp.raycastTarget = false;
            var tRect = textGO.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = Vector2.zero;
            tRect.offsetMax = Vector2.zero;
        }

        // threshold row: [name] [dir] [-] [value] [+] [unit] [reset]
        private void CreateThresholdRow(Transform parent, WarningEvent evt, (float defaultVal, string unit, bool greaterThan) meta)
        {
            bool hasCustom = _editorThresholds.ContainsKey(evt);
            float currentVal = hasCustom ? _editorThresholds[evt] : meta.defaultVal;

            float step = meta.defaultVal >= 100f ? 10f
                       : meta.defaultVal >= 10f ? 1f
                       : meta.defaultVal >= 1f ? 0.1f
                       : 0.01f;

            var rowGO = new GameObject("Thresh_" + evt);
            rowGO.AddComponent<RectTransform>();
            rowGO.transform.SetParent(parent, false);

            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 2;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.minHeight = 22;
            rowLE.preferredHeight = 22;
            rowLE.flexibleWidth = 1;

            // event name
            var lblGO = new GameObject("Label");
            lblGO.AddComponent<RectTransform>();
            lblGO.transform.SetParent(rowGO.transform, false);
            var lblLE = lblGO.AddComponent<LayoutElement>();
            lblLE.minWidth = 55;
            lblLE.preferredWidth = 55;
            lblLE.flexibleWidth = 0;
            var lblTmp = lblGO.AddComponent<TextMeshProUGUI>();
            lblTmp.text = GetEventDisplayName(evt) + ":";
            lblTmp.fontSize = 9;
            lblTmp.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            lblTmp.raycastTarget = false;

            // comparison direction (clickable to cycle ops)
            CompareOp currentOp = _editorComparisons.TryGetValue(evt, out var edOp)
                ? edOp
                : (meta.greaterThan ? CompareOp.Gte : CompareOp.Lte);
            string opText = CompareOpToString(currentOp);

            var dirGO = new GameObject("Dir");
            dirGO.AddComponent<RectTransform>();
            dirGO.transform.SetParent(rowGO.transform, false);
            var dirLE = dirGO.AddComponent<LayoutElement>();
            dirLE.minWidth = 18;
            dirLE.preferredWidth = 18;
            dirLE.flexibleWidth = 0;

            var dirImg = dirGO.AddComponent<Image>();
            dirImg.color = _editorComparisons.ContainsKey(evt)
                ? new Color(0.2f, 0.35f, 0.5f, 1f)
                : new Color(0.2f, 0.2f, 0.25f, 1f);

            var dirBtn = dirGO.AddComponent<Button>();
            var dirBtnCols = dirBtn.colors;
            dirBtnCols.normalColor = dirImg.color;
            dirBtnCols.highlightedColor = new Color(0.3f, 0.45f, 0.6f, 1f);
            dirBtnCols.pressedColor = new Color(0.15f, 0.25f, 0.4f, 1f);
            dirBtn.colors = dirBtnCols;

            var capturedEvtDir = evt;
            dirBtn.onClick.AddListener((UnityAction)(() =>
            {
                CompareOp cur = _editorComparisons.TryGetValue(capturedEvtDir, out var c)
                    ? c
                    : (meta.greaterThan ? CompareOp.Gte : CompareOp.Lte);
                _editorComparisons[capturedEvtDir] = NextCompareOp(cur);
                ShowRuleEditor();
            }));

            var dirTextGO = new GameObject("Text");
            dirTextGO.AddComponent<RectTransform>();
            dirTextGO.transform.SetParent(dirGO.transform, false);
            var dirTmp = dirTextGO.AddComponent<TextMeshProUGUI>();
            dirTmp.text = opText;
            dirTmp.fontSize = 10;
            dirTmp.color = new Color(0.5f, 0.8f, 1f, 1f);
            dirTmp.alignment = TextAlignmentOptions.Center;
            dirTmp.raycastTarget = false;
            var dirTRect = dirTextGO.GetComponent<RectTransform>();
            dirTRect.anchorMin = Vector2.zero;
            dirTRect.anchorMax = Vector2.one;
            dirTRect.offsetMin = Vector2.zero;
            dirTRect.offsetMax = Vector2.zero;

            // decrement
            var capturedEvt = evt;
            var capturedStep = step;
            CreateSmallButton(rowGO.transform, "-", 18, () =>
            {
                float val = _editorThresholds.ContainsKey(capturedEvt) ? _editorThresholds[capturedEvt] : meta.defaultVal;
                val = Mathf.Max(0f, val - capturedStep);
                _editorThresholds[capturedEvt] = (float)Math.Round(val, 3);
                ShowRuleEditor();
            });

            // value display
            string valText = hasCustom
                ? currentVal.ToString(step < 0.1f ? "F3" : step < 1f ? "F1" : "F0")
                : meta.defaultVal.ToString(step < 0.1f ? "F3" : step < 1f ? "F1" : "F0");

            var valGO = new GameObject("Value");
            valGO.AddComponent<RectTransform>();
            valGO.transform.SetParent(rowGO.transform, false);
            var valLE = valGO.AddComponent<LayoutElement>();
            valLE.minWidth = 40;
            valLE.preferredWidth = 45;
            valLE.flexibleWidth = 0;

            var valBg = valGO.AddComponent<Image>();
            valBg.color = hasCustom ? new Color(0.2f, 0.35f, 0.5f, 1f) : new Color(0.2f, 0.2f, 0.25f, 1f);
            valBg.raycastTarget = false;

            var valTextGO = new GameObject("Text");
            valTextGO.AddComponent<RectTransform>();
            valTextGO.transform.SetParent(valGO.transform, false);
            var valTmp = valTextGO.AddComponent<TextMeshProUGUI>();
            valTmp.text = valText;
            valTmp.fontSize = 10;
            valTmp.alignment = TextAlignmentOptions.Center;
            valTmp.color = hasCustom ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
            valTmp.raycastTarget = false;
            var valTRect = valTextGO.GetComponent<RectTransform>();
            valTRect.anchorMin = Vector2.zero;
            valTRect.anchorMax = Vector2.one;
            valTRect.offsetMin = new Vector2(2, 0);
            valTRect.offsetMax = new Vector2(-2, 0);

            // increment
            CreateSmallButton(rowGO.transform, "+", 18, () =>
            {
                float val = _editorThresholds.ContainsKey(capturedEvt) ? _editorThresholds[capturedEvt] : meta.defaultVal;
                val += capturedStep;
                _editorThresholds[capturedEvt] = (float)Math.Round(val, 3);
                ShowRuleEditor();
            });

            // unit label
            var unitGO = new GameObject("Unit");
            unitGO.AddComponent<RectTransform>();
            unitGO.transform.SetParent(rowGO.transform, false);
            var unitLE = unitGO.AddComponent<LayoutElement>();
            unitLE.minWidth = 24;
            unitLE.preferredWidth = 24;
            unitLE.flexibleWidth = 0;
            var unitTmp = unitGO.AddComponent<TextMeshProUGUI>();
            unitTmp.text = meta.unit;
            unitTmp.fontSize = 9;
            unitTmp.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            unitTmp.alignment = TextAlignmentOptions.MidlineLeft;
            unitTmp.raycastTarget = false;

            // reset
            if (hasCustom || _editorComparisons.ContainsKey(evt))
            {
                CreateSmallButton(rowGO.transform, "R", 18, () =>
                {
                    _editorThresholds.Remove(capturedEvt);
                    _editorComparisons.Remove(capturedEvt);
                    ShowRuleEditor();
                });
            }
        }

        // cycle compare ops
        private static CompareOp NextCompareOp(CompareOp current) => current switch
        {
            CompareOp.Gte => CompareOp.Lte,
            CompareOp.Lte => CompareOp.Gt,
            CompareOp.Gt  => CompareOp.Lt,
            CompareOp.Lt  => CompareOp.Eq,
            CompareOp.Eq  => CompareOp.Gte,
            _ => CompareOp.Gte,
        };

        // compare op to string
        private static string CompareOpToString(CompareOp op) => op switch
        {
            CompareOp.Gte => ">=",
            CompareOp.Lte => "<=",
            CompareOp.Gt  => ">",
            CompareOp.Lt  => "<",
            CompareOp.Eq  => "=",
            _ => ">=",
        };

        // preset name + save row
        private void CreatePresetNameRow(Transform parent)
        {
            var rowGO = new GameObject("PresetNameRow");
            rowGO.AddComponent<RectTransform>();
            rowGO.transform.SetParent(parent, false);

            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 3;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.padding = new RectOffset();
            rowLayout.padding.left = 3;
            rowLayout.padding.right = 3;
            rowLayout.padding.top = 2;
            rowLayout.padding.bottom = 2;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            var rowSize = rowGO.AddComponent<LayoutElement>();
            rowSize.minHeight = 30;
            rowSize.preferredHeight = 30;
            rowSize.flexibleWidth = 1;

            // label
            var labelGO = new GameObject("Label");
            labelGO.AddComponent<RectTransform>();
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelLayout = labelGO.AddComponent<LayoutElement>();
            labelLayout.minWidth = 40;
            labelLayout.preferredWidth = 40;
            labelLayout.flexibleWidth = 0;
            var labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = "Name:";
            labelText.fontSize = 10;
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 8;
            labelText.fontSizeMax = 10;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            labelText.raycastTarget = false;

            // input field
            var inputGO = new GameObject("PresetNameInput");
            inputGO.AddComponent<RectTransform>();
            inputGO.transform.SetParent(rowGO.transform, false);

            var inputLayout = inputGO.AddComponent<LayoutElement>();
            inputLayout.minWidth = 60;
            inputLayout.preferredWidth = 100;
            inputLayout.flexibleWidth = 1;

            inputGO.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f, 1f);

            var textAreaGO = new GameObject("Text Area");
            textAreaGO.AddComponent<RectTransform>();
            textAreaGO.transform.SetParent(inputGO.transform, false);
            var textAreaRect = textAreaGO.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(4, 2);
            textAreaRect.offsetMax = new Vector2(-4, -2);

            var inputTextGO = new GameObject("Text");
            inputTextGO.AddComponent<RectTransform>();
            inputTextGO.transform.SetParent(textAreaGO.transform, false);
            var inputText = inputTextGO.AddComponent<TextMeshProUGUI>();
            inputText.fontSize = 10;
            inputText.enableAutoSizing = true;
            inputText.fontSizeMin = 8;
            inputText.fontSizeMax = 10;
            inputText.color = Color.white;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
            var inputTextRect = inputTextGO.GetComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = Vector2.zero;
            inputTextRect.offsetMax = Vector2.zero;

            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.AddComponent<RectTransform>();
            placeholderGO.transform.SetParent(textAreaGO.transform, false);
            var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholder.text = "Preset name...";
            placeholder.fontSize = 10;
            placeholder.enableAutoSizing = true;
            placeholder.fontSizeMin = 8;
            placeholder.fontSizeMax = 10;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.raycastTarget = false;
            var phRect = placeholderGO.GetComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = Vector2.zero;
            phRect.offsetMax = Vector2.zero;

            _presetNameInput = inputGO.AddComponent<TMP_InputField>();
            _presetNameInput.textViewport = textAreaRect;
            _presetNameInput.textComponent = inputText;
            _presetNameInput.placeholder = placeholder;
            _presetNameInput.characterLimit = 40;
            _presetNameInput.contentType = TMP_InputField.ContentType.Standard;

            if (!string.IsNullOrWhiteSpace(_configCraftName))
                _presetNameInput.text = _configCraftName;

            // save button
            CreateSmallButton(rowGO.transform, "Save", 35, () =>
            {
                string presetName = _presetNameInput != null ? _presetNameInput.text : "";
                if (string.IsNullOrWhiteSpace(presetName))
                    presetName = _configCraftName;
                if (string.IsNullOrWhiteSpace(presetName))
                {
                    LoggerInstance.Warning("[CraftEditor] Cannot save — no preset name");
                    return;
                }
                SaveCraftConfig(presetName);
                LoggerInstance.Msg($"[CraftEditor] Preset saved as '{presetName}'");
                RefreshPresetButtons();
            });
        }

        // preset buttons

        // build preset button container
        private void BuildPresetButtons(Transform content, GameObject buttonTemplate)
        {
            var containerGO = new GameObject("PresetsContainer");
            containerGO.AddComponent<RectTransform>();
            containerGO.transform.SetParent(content, false);

            var containerLayout = containerGO.AddComponent<VerticalLayoutGroup>();
            containerLayout.spacing = 2;
            containerLayout.childForceExpandWidth = true;
            containerLayout.childForceExpandHeight = false;
            containerLayout.childControlWidth = true;
            containerLayout.childControlHeight = true;

            containerGO.AddComponent<LayoutElement>().flexibleWidth = 1;

            var containerFitter = containerGO.AddComponent<ContentSizeFitter>();
            containerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _presetsContainer = containerGO.transform;
            PopulatePresetButtons();
        }

        // build a button for each preset folder
        private void PopulatePresetButtons()
        {
            if (_presetsContainer == null) return;

            var toDestroy = new List<GameObject>();
            for (int i = 0; i < _presetsContainer.childCount; i++)
                toDestroy.Add(_presetsContainer.GetChild(i).gameObject);
            foreach (var go in toDestroy)
                UnityEngine.Object.Destroy(go);

            if (!Directory.Exists(_craftConfigBasePath))
            {
                CreateSmallInfoLabel(_presetsContainer, "No presets yet");
                return;
            }

            string[] presetDirs = Directory.GetDirectories(_craftConfigBasePath);
            if (presetDirs.Length == 0)
            {
                CreateSmallInfoLabel(_presetsContainer, "No presets yet");
                return;
            }

            foreach (string dir in presetDirs)
            {
                string folderName = Path.GetFileName(dir);
                string displayName = GetPresetDisplayName(dir);
                string capturedDir = folderName;
                string capturedName = displayName;

                var rowGO = new GameObject("Preset_" + folderName);
                rowGO.AddComponent<RectTransform>();
                rowGO.transform.SetParent(_presetsContainer, false);

                var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 3;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = true;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.padding = new RectOffset();
                rowLayout.padding.left = 3;
                rowLayout.padding.right = 3;
                rowLayout.padding.top = 1;
                rowLayout.padding.bottom = 1;
                rowLayout.childAlignment = TextAnchor.MiddleLeft;

                var rowSize = rowGO.AddComponent<LayoutElement>();
                rowSize.minHeight = 26;
                rowSize.preferredHeight = 26;
                rowSize.flexibleWidth = 1;

                rowGO.AddComponent<Image>().color = new Color(0.18f, 0.2f, 0.25f, 0.7f);
                rowGO.GetComponent<Image>().raycastTarget = false;

                // name label
                var nameLabelGO = new GameObject("Name");
                nameLabelGO.AddComponent<RectTransform>();
                nameLabelGO.transform.SetParent(rowGO.transform, false);
                var nameLayout = nameLabelGO.AddComponent<LayoutElement>();
                nameLayout.minWidth = 30;
                nameLayout.preferredWidth = 100;
                nameLayout.flexibleWidth = 1;
                var nameText = nameLabelGO.AddComponent<TextMeshProUGUI>();
                nameText.text = capturedName;
                nameText.fontSize = 11;
                nameText.enableAutoSizing = true;
                nameText.fontSizeMin = 8;
                nameText.fontSizeMax = 11;
                nameText.alignment = TextAlignmentOptions.MidlineLeft;
                nameText.color = new Color(0.8f, 0.85f, 1f, 1f);
                nameText.overflowMode = TextOverflowModes.Ellipsis;
                nameText.raycastTarget = false;

                // load button
                CreateSmallButton(rowGO.transform, "Load", 32, () =>
                {
                    LoadCraftOverrides(capturedDir);
                    RefreshWarningPanelButtons();
                    LoggerInstance.Msg($"[CraftEditor] Loaded preset '{capturedName}'");
                });

                // delete button
                CreateSmallButton(rowGO.transform, "X", 20, () =>
                {
                    try
                    {
                        string dirPath = Path.Combine(_craftConfigBasePath, capturedDir);
                        if (Directory.Exists(dirPath))
                        {
                            Directory.Delete(dirPath, true);
                            LoggerInstance.Msg($"[CraftEditor] Deleted preset '{capturedName}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerInstance.Error($"[CraftEditor] Delete preset failed: {ex.Message}");
                    }
                    RefreshPresetButtons();
                });
            }
        }

        private void RefreshPresetButtons() => PopulatePresetButtons();

        // get display name from config or folder name
        private string GetPresetDisplayName(string presetDir)
        {
            string configFile = Path.Combine(presetDir, "config.json");
            if (File.Exists(configFile))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configFile);
                    foreach (string line in lines)
                    {
                        string l = line.Trim().TrimEnd(',');
                        if (l.Contains("\"presetName\""))
                        {
                            int colonIdx = l.IndexOf("\": \"", StringComparison.Ordinal);
                            if (colonIdx > 0)
                            {
                                string value = l.Substring(colonIdx + 4).TrimEnd('"');
                                if (!string.IsNullOrWhiteSpace(value))
                                    return value;
                            }
                        }
                    }
                }
                catch { }
            }
            return Path.GetFileName(presetDir);
        }

        // ui primitives

        // info label
        private void CreateSmallInfoLabel(Transform parent, string text)
        {
            var go = new GameObject("InfoLabel");
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 22;
            layout.preferredHeight = 22;
            layout.flexibleWidth = 1;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 10;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            tmp.raycastTarget = false;
        }

        // section header
        private void CreateSectionHeader(Transform parent, string text)
        {
            var headerGO = new GameObject("Header_" + text);
            headerGO.AddComponent<RectTransform>();
            headerGO.transform.SetParent(parent, false);

            var layout = headerGO.AddComponent<LayoutElement>();
            layout.minHeight = 24;
            layout.preferredHeight = 24;
            layout.flexibleWidth = 1;

            var tmpText = headerGO.AddComponent<TextMeshProUGUI>();
            tmpText.text = text;
            tmpText.fontSize = 13;
            tmpText.fontStyle = FontStyles.Bold;
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            tmpText.enableAutoSizing = true;
            tmpText.fontSizeMin = 10;
            tmpText.fontSizeMax = 13;
            tmpText.raycastTarget = false;
        }

        // warning row: label + status + browse + play + reset
        private void CreateWarningRow(Transform parent, GameObject buttonTemplate, string clipName, string displayName)
        {
            var rowGO = new GameObject("Row_" + clipName);
            rowGO.AddComponent<RectTransform>();
            rowGO.transform.SetParent(parent, false);

            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 2;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.padding = new RectOffset();
            rowLayout.padding.left = 3;
            rowLayout.padding.right = 3;
            rowLayout.padding.top = 1;
            rowLayout.padding.bottom = 1;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            rowGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f, 0.6f);
            rowGO.GetComponent<Image>().raycastTarget = false;

            var rowSize = rowGO.AddComponent<LayoutElement>();
            rowSize.minHeight = 28;
            rowSize.preferredHeight = 28;
            rowSize.flexibleWidth = 1;

            // warning name
            var labelGO = new GameObject("Label");
            labelGO.AddComponent<RectTransform>();
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelLayout = labelGO.AddComponent<LayoutElement>();
            labelLayout.minWidth = 30;
            labelLayout.preferredWidth = 90;
            labelLayout.flexibleWidth = 1;
            var labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = displayName;
            labelText.fontSize = 12;
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 8;
            labelText.fontSizeMax = 12;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            labelText.raycastTarget = false;

            // status label
            var statusGO = new GameObject("Status_" + clipName);
            statusGO.AddComponent<RectTransform>();
            statusGO.transform.SetParent(rowGO.transform, false);
            var statusLayout = statusGO.AddComponent<LayoutElement>();
            statusLayout.minWidth = 25;
            statusLayout.preferredWidth = 50;
            statusLayout.flexibleWidth = 0.5f;
            var statusText = statusGO.AddComponent<TextMeshProUGUI>();
            statusText.fontSize = 10;
            statusText.enableAutoSizing = true;
            statusText.fontSizeMin = 7;
            statusText.fontSizeMax = 10;
            statusText.alignment = TextAlignmentOptions.MidlineLeft;
            statusText.overflowMode = TextOverflowModes.Ellipsis;
            statusText.raycastTarget = false;

            if (_craftOverrides.ContainsKey(clipName))
            {
                statusText.text = Path.GetFileName(_craftOverrides[clipName]);
                statusText.color = new Color(0.5f, 1f, 0.5f, 1f);
            }
            else if (_clipPaths.ContainsKey(clipName))
            {
                statusText.text = "Default";
                statusText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            }
            else
            {
                statusText.text = "Missing!";
                statusText.color = new Color(1f, 0.4f, 0.4f, 1f);
            }

            string capturedClipName = clipName;
            CreateSmallImageButton(rowGO.transform, _folderIconSprite, 22, () => { OnBrowseForSound(capturedClipName); });
            CreateSmallImageButton(rowGO.transform, _speakerBtnSprite, 20, () => { PlayWarningSound(capturedClipName); });
            CreateSmallButton(rowGO.transform, "X", 20, () =>
            {
                _craftOverrides.Remove(capturedClipName);
                RefreshWarningPanelButtons();
                LoggerInstance.Msg($"[CraftEditor] Removed override for '{capturedClipName}'");
            });
        }

        // text button
        private void CreateSmallButton(Transform parent, string label, float width, System.Action onClick)
        {
            var btnGO = new GameObject("Btn_" + label);
            btnGO.AddComponent<RectTransform>();
            btnGO.transform.SetParent(parent, false);

            var btnLayout = btnGO.AddComponent<LayoutElement>();
            btnLayout.minWidth = width;
            btnLayout.preferredWidth = width;
            btnLayout.flexibleWidth = 0;
            btnLayout.minHeight = 22;
            btnLayout.preferredHeight = 22;

            btnGO.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);

            var button = btnGO.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            colors.highlightedColor = new Color(0.4f, 0.4f, 0.5f, 1f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            colors.selectedColor = new Color(0.3f, 0.3f, 0.4f, 1f);
            button.colors = colors;
            button.onClick.AddListener((UnityAction)(() => onClick()));

            var textGO = new GameObject("Text");
            textGO.AddComponent<RectTransform>();
            textGO.transform.SetParent(btnGO.transform, false);
            var tmpText = textGO.AddComponent<TextMeshProUGUI>();
            tmpText.text = label;
            tmpText.fontSize = 14;
            tmpText.enableAutoSizing = true;
            tmpText.fontSizeMin = 8;
            tmpText.fontSizeMax = 14;
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.color = Color.white;
            tmpText.raycastTarget = false;

            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2, 2);
            textRect.offsetMax = new Vector2(-2, -2);
        }

        // icon button
        private void CreateSmallImageButton(Transform parent, Sprite icon, float width, System.Action onClick)
        {
            var btnGO = new GameObject("ImgBtn");
            btnGO.AddComponent<RectTransform>();
            btnGO.transform.SetParent(parent, false);

            var btnLayout = btnGO.AddComponent<LayoutElement>();
            btnLayout.minWidth = width;
            btnLayout.preferredWidth = width;
            btnLayout.flexibleWidth = 0;
            btnLayout.minHeight = 22;
            btnLayout.preferredHeight = 22;

            btnGO.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);

            var button = btnGO.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            colors.highlightedColor = new Color(0.4f, 0.4f, 0.5f, 1f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            colors.selectedColor = new Color(0.3f, 0.3f, 0.4f, 1f);
            button.colors = colors;
            button.onClick.AddListener((UnityAction)(() => onClick()));

            if (icon != null)
            {
                var iconGO = new GameObject("Icon");
                iconGO.AddComponent<RectTransform>();
                iconGO.transform.SetParent(btnGO.transform, false);

                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                iconImg.color = Color.white;

                var iconRect = iconGO.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.1f, 0.1f);
                iconRect.anchorMax = new Vector2(0.9f, 0.9f);
                iconRect.offsetMin = Vector2.zero;
                iconRect.offsetMax = Vector2.zero;
            }
        }

        // action button
        private void CreateActionButton(Transform parent, GameObject template, string label, System.Action onClick)
        {
            var btnGO = new GameObject("ActionBtn_" + label);
            btnGO.AddComponent<RectTransform>();
            btnGO.transform.SetParent(parent, false);

            var btnLayout = btnGO.AddComponent<LayoutElement>();
            btnLayout.minHeight = 28;
            btnLayout.preferredHeight = 28;
            btnLayout.flexibleWidth = 1;

            btnGO.AddComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f, 1f);

            var button = btnGO.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.35f, 0.5f, 1f);
            colors.highlightedColor = new Color(0.3f, 0.45f, 0.6f, 1f);
            colors.pressedColor = new Color(0.1f, 0.2f, 0.35f, 1f);
            colors.selectedColor = new Color(0.25f, 0.4f, 0.55f, 1f);
            button.colors = colors;
            button.onClick.AddListener((UnityAction)(() => onClick()));

            var textGO = new GameObject("Text");
            textGO.AddComponent<RectTransform>();
            textGO.transform.SetParent(btnGO.transform, false);
            var tmpText = textGO.AddComponent<TextMeshProUGUI>();
            tmpText.text = label;
            tmpText.fontSize = 16;
            tmpText.enableAutoSizing = true;
            tmpText.fontSizeMin = 12;
            tmpText.fontSizeMax = 16;
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.color = Color.white;
            tmpText.raycastTarget = false;

            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4, 2);
            textRect.offsetMax = new Vector2(-4, -2);
        }

        // custom sound row
        private void CreateCustomSoundRow(Transform parent, string clipName, string displayName, string capturedClip)
        {
            var rowGO = new GameObject("CustomSound_" + clipName);
            rowGO.AddComponent<RectTransform>();
            rowGO.transform.SetParent(parent, false);

            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 2;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            rowGO.AddComponent<Image>().color = new Color(0.12f, 0.18f, 0.25f, 0.6f);
            rowGO.GetComponent<Image>().raycastTarget = false;

            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.minHeight = 22;
            rowLE.preferredHeight = 22;
            rowLE.flexibleWidth = 1;

            // sound name
            var nameGO = new GameObject("Name");
            nameGO.AddComponent<RectTransform>();
            nameGO.transform.SetParent(rowGO.transform, false);
            var nameLE = nameGO.AddComponent<LayoutElement>();
            nameLE.minWidth = 40;
            nameLE.flexibleWidth = 1;
            var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
            nameTmp.text = displayName;
            nameTmp.fontSize = 9;
            nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
            nameTmp.color = new Color(0.7f, 0.85f, 1f, 1f);
            nameTmp.overflowMode = TextOverflowModes.Ellipsis;
            nameTmp.raycastTarget = false;

            // play button
            CreateSmallImageButton(rowGO.transform, _speakerBtnSprite, 20, () =>
            {
                PlayWarningSound(capturedClip);
            });

            // delete button
            var rowRef = rowGO;
            CreateSmallButton(rowGO.transform, "X", 20, () =>
            {
                _customSounds.RemoveAll(s => s.clipName == capturedClip);
                SaveCustomSounds();
                UnityEngine.Object.Destroy(rowRef);
            });
        }

        // find child transform by name
        internal Transform FindChildRecursive(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
