using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;
using Il2CppCraftEditor;
using Il2CppInterop.Runtime;
using Il2CppSimpleFileBrowser;

namespace VoiceWarningEditor
{
    // craft editor panel, warnings, rules, presets
    public partial class VoiceWarningEditorMod
    {
        // find CEUI and build our ui
        internal void TryCreateCraftEditorUI()
        {
            var ceui = UnityEngine.Object.FindObjectOfType<CEUI>();
            if (ceui == null) return;

            // find canvas root
            GameObject uiRoot = GameObject.Find("UI");
            if (uiRoot == null)
            {
                LoggerInstance.Warning("[CraftEditor] 'UI' root not found");
                return;
            }
            Canvas uiCanvas = uiRoot.GetComponentInParent<Canvas>();
            if (uiCanvas == null) uiCanvas = uiRoot.GetComponent<Canvas>();

            LoggerInstance.Msg($"[CraftEditor] Found UI root: '{uiRoot.name}' (Canvas: {uiCanvas != null})");

            // find the mode bar and grab a button to clone
            Transform modeSelectTr = uiRoot.transform.Find("ModeSelect");
            if (modeSelectTr == null)
            {
                if (ceui.modeSelect != null) modeSelectTr = ceui.modeSelect.transform;
                else { LoggerInstance.Warning("[CraftEditor] ModeSelect not found"); return; }
            }

            Transform defaultModeTr = modeSelectTr.Find("DefaultMode");
            GameObject templateButton = null;
            if (defaultModeTr != null)
            {
                templateButton = defaultModeTr.gameObject;
            }
            else
            {
                for (int i = modeSelectTr.childCount - 1; i >= 0; i--)
                {
                    var child = modeSelectTr.GetChild(i).gameObject;
                    if (child.GetComponent<Button>() != null) { templateButton = child; break; }
                }
            }

            if (templateButton == null)
            {
                LoggerInstance.Warning("[CraftEditor] No template button in ModeSelect");
                return;
            }

            // find paint panel to clone as our panel
            Transform paintPanelTr = uiRoot.transform.Find("PaintPanel");
            if (paintPanelTr == null)
            {
                if (ceui.paintMode != null && ceui.paintMode.paintTools != null)
                    paintPanelTr = ceui.paintMode.paintTools.transform;
                else { LoggerInstance.Warning("[CraftEditor] PaintPanel not found"); return; }
            }

            LoggerInstance.Msg("[CraftEditor] Building VWS UI...");

            // load or generate icons
            if (_vwsIconSprite == null) _vwsIconSprite = LoadOrCreateVwsIcon();
            if (_folderIconSprite == null) _folderIconSprite = LoadOrCreateFolderIcon();
            if (_speakerBtnSprite == null) _speakerBtnSprite = _vwsIconSprite;

            // step 1: clone mode button with speaker icon

            _warningEditorButton = UnityEngine.Object.Instantiate(templateButton, modeSelectTr);
            _warningEditorButton.name = "WarningEditorMode";

            // set tooltip to VWS Editor
            try
            {
                var tooltip = _warningEditorButton.GetComponent<ButtonTooltip>();
                if (tooltip != null)
                    tooltip.text = "VWS Editor";
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[CraftEditor] Failed to set tooltip: {ex.Message}");
            }

            // replace text with speaker icon
            try
            {
                var tmpTexts = _warningEditorButton.GetComponentsInChildren<TMP_Text>(true);
                if (tmpTexts != null)
                {
                    for (int i = 0; i < tmpTexts.Count; i++)
                    {
                        tmpTexts[i].text = "";
                        tmpTexts[i].gameObject.SetActive(false);
                    }
                }

                if (_vwsIconSprite != null)
                {
                    var existingImages = _warningEditorButton.GetComponentsInChildren<Image>(true);
                    if (existingImages != null)
                    {
                        for (int i = 0; i < existingImages.Count; i++)
                        {
                            if (existingImages[i].gameObject != _warningEditorButton)
                            {
                                existingImages[i].sprite = _vwsIconSprite;
                                existingImages[i].preserveAspect = true;
                                existingImages[i].raycastTarget = false;
                                existingImages[i].color = Color.white;
                                existingImages[i].gameObject.SetActive(true);
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            var btn = _warningEditorButton.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener((UnityAction)OnWarningEditorButtonClick);
            }

            _warningEditorButton.SetActive(true);
            LoggerInstance.Msg("[CraftEditor] VWS mode button created");

            // step 2: clone paint panel as our panel

            _warningCreatorPanel = UnityEngine.Object.Instantiate(paintPanelTr.gameObject, paintPanelTr.parent);
            _warningCreatorPanel.name = "WarningCreatorPanel";

            // strip paint mode scripts from the cloned panel
            StripGameComponents(_warningCreatorPanel);

            Transform scrollContent = FindChildRecursive(_warningCreatorPanel.transform, "Content");

            if (scrollContent != null)
            {
                // clear existing children
                var childrenToDestroy = new List<GameObject>();
                for (int i = 0; i < scrollContent.childCount; i++)
                    childrenToDestroy.Add(scrollContent.GetChild(i).gameObject);
                foreach (var child in childrenToDestroy)
                    UnityEngine.Object.Destroy(child);

                LoggerInstance.Msg($"[CraftEditor] Cleared {childrenToDestroy.Count} children from scroll content");

                var vlg = scrollContent.gameObject.GetComponent<VerticalLayoutGroup>();
                if (vlg == null) vlg = scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 2;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.padding = new RectOffset();
                vlg.padding.left = 4;
                vlg.padding.right = 4;
                vlg.padding.top = 4;
                vlg.padding.bottom = 4;

                var csf = scrollContent.gameObject.GetComponent<ContentSizeFitter>();
                if (csf == null) csf = scrollContent.gameObject.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            else
            {
                LoggerInstance.Warning("[CraftEditor] Could not find Content in cloned PaintPanel");
            }

            _warningCreatorPanel.SetActive(false);

            // step 3: populate

            float uiScale = 1f;
            try
            {
                var canvas = uiRoot.GetComponent<Canvas>();
                if (canvas == null) canvas = uiRoot.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    var scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler != null)
                        LoggerInstance.Msg($"[CraftEditor] CanvasScaler refRes={scaler.referenceResolution} mode={scaler.uiScaleMode} scaleFactor={canvas.scaleFactor}");
                    LoggerInstance.Msg($"[CraftEditor] Canvas scaleFactor={canvas.scaleFactor}");
                }
            }
            catch { }

            if (scrollContent != null)
                BuildWarningButtons(scrollContent);

            // load current craft config
            try
            {
                if (ceui.craftTitleField != null && !string.IsNullOrEmpty(ceui.craftTitleField.text))
                {
                    _editorCraftName = ceui.craftTitleField.text;
                    _configCraftName = _editorCraftName;
                    LoadCraftOverrides(_editorCraftName);
                    RefreshWarningPanelButtons();
                }
            }
            catch { }

            _craftEditorUICreated = true;
            LoggerInstance.Msg("[CraftEditor] VWS UI creation complete!");
        }

        // build all the panel content
        private void BuildWarningButtons(Transform content)
        {
            _warningPanelContent = content;

            GameObject buttonTemplate = null;
            try
            {
                var ceui = UnityEngine.Object.FindObjectOfType<CEUI>();
                if (ceui != null && ceui.categoryPanel != null && ceui.categoryPanel.buttonPrefab != null)
                {
                    buttonTemplate = ceui.categoryPanel.buttonPrefab;
                    LoggerInstance.Msg("[CraftEditor] Using CategoryPanel.buttonPrefab as template");
                }
            }
            catch { }

            // simple / advanced toggle
            CreateModeToggle(content);

            CreateSectionHeader(content, "Voice Warning Sounds");

            for (int i = 0; i < WARNING_SOUNDS.Length; i++)
            {
                var (clipName, displayName) = WARNING_SOUNDS[i];
                CreateWarningRow(content, buttonTemplate, clipName, displayName);
            }

            // save/load/reset
            CreateSectionHeader(content, "Configuration");
            CreateActionButton(content, buttonTemplate, "Save Config", () =>
            {
                if (!string.IsNullOrWhiteSpace(_configCraftName))
                {
                    GetCraftConfigDir(_configCraftName, true);
                    SaveCraftConfig(_configCraftName);
                    LoggerInstance.Msg($"[CraftEditor] Config saved for '{_configCraftName}'");
                }
            });
            CreateActionButton(content, buttonTemplate, "Reset All to Default", () =>
            {
                _craftOverrides.Clear();
                RefreshWarningPanelButtons();
                LoggerInstance.Msg("[CraftEditor] All overrides cleared — using defaults");
            });

            // preset name input + save
            CreatePresetNameRow(content);

            // preset selector
            CreateSectionHeader(content, "Load Preset");
            BuildPresetButtons(content, buttonTemplate);

            // event rules (advanced only)
            _eventRulesSection = new GameObject("EventRulesSection");
            _eventRulesSection.AddComponent<RectTransform>();
            _eventRulesSection.transform.SetParent(content, false);

            var erLayout = _eventRulesSection.AddComponent<VerticalLayoutGroup>();
            erLayout.spacing = 2;
            erLayout.childForceExpandWidth = true;
            erLayout.childForceExpandHeight = false;
            erLayout.childControlWidth = true;
            erLayout.childControlHeight = true;

            var erLE = _eventRulesSection.AddComponent<LayoutElement>();
            erLE.flexibleWidth = 1;

            var erFitter = _eventRulesSection.AddComponent<ContentSizeFitter>();
            erFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateSectionHeader(_eventRulesSection.transform, "Event Rules");
            BuildEventRulesSection(_eventRulesSection.transform);

            _eventRulesSection.SetActive(_advancedMode);
        }

        // mode toggle

        private void CreateModeToggle(Transform parent)
        {
            var rowGO = new GameObject("ModeToggleRow");
            rowGO.AddComponent<RectTransform>();
            rowGO.transform.SetParent(parent, false);

            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 2;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;

            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.minHeight = 26;
            rowLE.preferredHeight = 26;
            rowLE.flexibleWidth = 1;

            CreateModeButton(rowGO.transform, "Simple", false);
            CreateModeButton(rowGO.transform, "Advanced", true);
        }

        private void CreateModeButton(Transform parent, string label, bool isAdvanced)
        {
            bool active = (_advancedMode == isAdvanced);

            var btnGO = new GameObject("Mode_" + label);
            btnGO.AddComponent<RectTransform>();
            btnGO.transform.SetParent(parent, false);

            var btnLE = btnGO.AddComponent<LayoutElement>();
            btnLE.flexibleWidth = 1;
            btnLE.minHeight = 24;

            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = active
                ? new Color(0.2f, 0.45f, 0.7f, 1f)
                : new Color(0.18f, 0.18f, 0.22f, 1f);

            var btnComp = btnGO.AddComponent<Button>();
            var cols = btnComp.colors;
            cols.normalColor = btnImg.color;
            cols.highlightedColor = active
                ? new Color(0.3f, 0.55f, 0.8f, 1f)
                : new Color(0.28f, 0.28f, 0.35f, 1f);
            cols.pressedColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            btnComp.colors = cols;

            btnComp.onClick.AddListener((UnityAction)(() =>
            {
                _advancedMode = isAdvanced;
                if (_eventRulesSection != null)
                    _eventRulesSection.SetActive(_advancedMode);
                RebuildModeToggle();
            }));

            var textGO = new GameObject("Text");
            textGO.AddComponent<RectTransform>();
            textGO.transform.SetParent(btnGO.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = active ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
            tmp.raycastTarget = false;
            var tRect = textGO.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = Vector2.zero;
            tRect.offsetMax = Vector2.zero;
        }

        // rebuild toggle to update highlight
        private void RebuildModeToggle()
        {
            if (_warningPanelContent == null) return;

            // destroy old toggle
            for (int i = 0; i < _warningPanelContent.childCount; i++)
            {
                var child = _warningPanelContent.GetChild(i);
                if (child.name == "ModeToggleRow")
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                    break;
                }
            }

            CreateModeToggle(_warningPanelContent);

            // new toggle is last child, move to top
            // (Destroy is deferred so name search would find the old one)
            _warningPanelContent.GetChild(_warningPanelContent.childCount - 1).SetAsFirstSibling();
        }

        // event rules ui

        // build event rules: rule list, add rule/sound, editor
        private void BuildEventRulesSection(Transform content)
        {
            // dynamic rule rows
            var containerGO = new GameObject("RulesListContainer");
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

            _rulesListContainer = containerGO.transform;
            PopulateRulesList();

            // add rule button
            var addBtnGO = new GameObject("AddRuleBtn");
            addBtnGO.AddComponent<RectTransform>();
            addBtnGO.transform.SetParent(content, false);

            var addBtnLayout = addBtnGO.AddComponent<LayoutElement>();
            addBtnLayout.minHeight = 28;
            addBtnLayout.preferredHeight = 28;
            addBtnLayout.flexibleWidth = 1;

            var addBtnImg = addBtnGO.AddComponent<Image>();
            addBtnImg.color = new Color(0.15f, 0.4f, 0.15f, 1f);

            var addBtn = addBtnGO.AddComponent<Button>();
            var addColors = addBtn.colors;
            addColors.normalColor = new Color(0.15f, 0.4f, 0.15f, 1f);
            addColors.highlightedColor = new Color(0.2f, 0.55f, 0.2f, 1f);
            addColors.pressedColor = new Color(0.1f, 0.25f, 0.1f, 1f);
            addColors.selectedColor = new Color(0.18f, 0.45f, 0.18f, 1f);
            addBtn.colors = addColors;
            addBtn.onClick.AddListener((UnityAction)(() =>
            {
                _editingRule = null;
                _editorSelectedEvents.Clear();
                _editorLogic = RuleLogic.Any;
                _editorSoundClip = "";
                _editorCooldown = COOLDOWN_MEDIUM;
                _editorGroup = "";
                _editorThresholds.Clear();
                _editorComparisons.Clear();
                ShowRuleEditor();
            }));

            var addTextGO = new GameObject("Text");
            addTextGO.AddComponent<RectTransform>();
            addTextGO.transform.SetParent(addBtnGO.transform, false);
            var addText = addTextGO.AddComponent<TextMeshProUGUI>();
            addText.text = "+ Add Rule";
            addText.fontSize = 13;
            addText.enableAutoSizing = true;
            addText.fontSizeMin = 10;
            addText.fontSizeMax = 13;
            addText.alignment = TextAlignmentOptions.Center;
            addText.color = Color.white;
            addText.raycastTarget = false;
            var addTextRect = addTextGO.GetComponent<RectTransform>();
            addTextRect.anchorMin = Vector2.zero;
            addTextRect.anchorMax = Vector2.one;
            addTextRect.offsetMin = new Vector2(4, 2);
            addTextRect.offsetMax = new Vector2(-4, -2);

            // add custom sound button
            var addSoundBtnGO = new GameObject("AddSoundBtn");
            addSoundBtnGO.AddComponent<RectTransform>();
            addSoundBtnGO.transform.SetParent(content, false);

            var addSoundLayout = addSoundBtnGO.AddComponent<LayoutElement>();
            addSoundLayout.minHeight = 24;
            addSoundLayout.preferredHeight = 24;
            addSoundLayout.flexibleWidth = 1;

            var addSoundImg = addSoundBtnGO.AddComponent<Image>();
            addSoundImg.color = new Color(0.15f, 0.3f, 0.45f, 1f);

            var addSoundBtn = addSoundBtnGO.AddComponent<Button>();
            var addSoundColors = addSoundBtn.colors;
            addSoundColors.normalColor = new Color(0.15f, 0.3f, 0.45f, 1f);
            addSoundColors.highlightedColor = new Color(0.2f, 0.4f, 0.55f, 1f);
            addSoundColors.pressedColor = new Color(0.1f, 0.2f, 0.35f, 1f);
            addSoundBtn.colors = addSoundColors;
            addSoundBtn.onClick.AddListener((UnityAction)(() => { BrowseForCustomSound(); }));

            var addSoundTextGO = new GameObject("Text");
            addSoundTextGO.AddComponent<RectTransform>();
            addSoundTextGO.transform.SetParent(addSoundBtnGO.transform, false);
            var addSoundText = addSoundTextGO.AddComponent<TextMeshProUGUI>();
            addSoundText.text = "Add Sound";
            addSoundText.fontSize = 11;
            addSoundText.alignment = TextAlignmentOptions.Center;
            addSoundText.color = Color.white;
            addSoundText.raycastTarget = false;
            var addSoundTextRect = addSoundTextGO.GetComponent<RectTransform>();
            addSoundTextRect.anchorMin = Vector2.zero;
            addSoundTextRect.anchorMax = Vector2.one;
            addSoundTextRect.offsetMin = new Vector2(4, 2);
            addSoundTextRect.offsetMax = new Vector2(-4, -2);

            // custom sounds list (rebuilt dynamically)
            var customSoundsGO = new GameObject("CustomSoundsContainer");
            customSoundsGO.AddComponent<RectTransform>();
            customSoundsGO.transform.SetParent(content, false);

            var csLayout = customSoundsGO.AddComponent<VerticalLayoutGroup>();
            csLayout.spacing = 2;
            csLayout.childForceExpandWidth = true;
            csLayout.childForceExpandHeight = false;
            csLayout.childControlWidth = true;
            csLayout.childControlHeight = true;

            customSoundsGO.AddComponent<LayoutElement>().flexibleWidth = 1;

            var csFitter = customSoundsGO.AddComponent<ContentSizeFitter>();
            csFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _customSoundsContainer = customSoundsGO.transform;
            RefreshCustomSoundsList();

            // rule editor (hidden by default)
            var editorGO = new GameObject("RuleEditorContainer");
            editorGO.AddComponent<RectTransform>();
            editorGO.transform.SetParent(content, false);

            var editorLayout = editorGO.AddComponent<VerticalLayoutGroup>();
            editorLayout.spacing = 3;
            editorLayout.childForceExpandWidth = true;
            editorLayout.childForceExpandHeight = false;
            editorLayout.childControlWidth = true;
            editorLayout.childControlHeight = true;
            editorLayout.padding = new RectOffset();
            editorLayout.padding.left = 4;
            editorLayout.padding.right = 4;
            editorLayout.padding.top = 4;
            editorLayout.padding.bottom = 4;

            editorGO.AddComponent<LayoutElement>().flexibleWidth = 1;

            var editorFitter = editorGO.AddComponent<ContentSizeFitter>();
            editorFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var editorBg = editorGO.AddComponent<Image>();
            editorBg.color = new Color(0.1f, 0.12f, 0.18f, 0.9f);
            editorBg.raycastTarget = false;

            _ruleEditorContainer = editorGO.transform;
            editorGO.SetActive(false);
        }

        // fill rules list with a row per rule
        internal void PopulateRulesList()
        {
            if (_rulesListContainer == null) return;

            var toDestroy = new List<GameObject>();
            for (int i = 0; i < _rulesListContainer.childCount; i++)
                toDestroy.Add(_rulesListContainer.GetChild(i).gameObject);
            foreach (var go in toDestroy)
                UnityEngine.Object.Destroy(go);

            for (int idx = 0; idx < _eventRules.Count; idx++)
                CreateRuleRow(_rulesListContainer, _eventRules[idx], idx);
        }

        // compact row: [toggle] [events -> sound] [edit] [delete]
        private void CreateRuleRow(Transform parent, EventRule rule, int index)
        {
            var rowGO = new GameObject("Rule_" + rule.Id);
            rowGO.AddComponent<RectTransform>();
            rowGO.transform.SetParent(parent, false);

            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 2;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.padding = new RectOffset();
            rowLayout.padding.left = 2;
            rowLayout.padding.right = 2;
            rowLayout.padding.top = 1;
            rowLayout.padding.bottom = 1;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            var rowBg = rowGO.AddComponent<Image>();
            rowBg.color = rule.Enabled
                ? new Color(0.15f, 0.18f, 0.22f, 0.7f)
                : new Color(0.12f, 0.12f, 0.12f, 0.5f);
            rowBg.raycastTarget = false;

            var rowSize = rowGO.AddComponent<LayoutElement>();
            rowSize.minHeight = 26;
            rowSize.preferredHeight = 26;
            rowSize.flexibleWidth = 1;

            // toggle on/off
            var capturedRule = rule;
            string toggleLabel = rule.Enabled ? "ON" : "--";
            Color toggleColor = rule.Enabled ? new Color(0.3f, 0.8f, 0.3f, 1f) : new Color(0.5f, 0.5f, 0.5f, 1f);

            var toggleGO = new GameObject("Toggle");
            toggleGO.AddComponent<RectTransform>();
            toggleGO.transform.SetParent(rowGO.transform, false);
            var toggleLayout = toggleGO.AddComponent<LayoutElement>();
            toggleLayout.minWidth = 18;
            toggleLayout.preferredWidth = 18;
            toggleLayout.flexibleWidth = 0;

            toggleGO.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);

            var toggleBtn = toggleGO.AddComponent<Button>();
            toggleBtn.onClick.AddListener((UnityAction)(() =>
            {
                capturedRule.Enabled = !capturedRule.Enabled;
                PopulateRulesList();
            }));

            var toggleTextGO = new GameObject("Text");
            toggleTextGO.AddComponent<RectTransform>();
            toggleTextGO.transform.SetParent(toggleGO.transform, false);
            var toggleText = toggleTextGO.AddComponent<TextMeshProUGUI>();
            toggleText.text = toggleLabel;
            toggleText.fontSize = 14;
            toggleText.alignment = TextAlignmentOptions.Center;
            toggleText.color = toggleColor;
            toggleText.raycastTarget = false;
            var tRect = toggleTextGO.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = Vector2.zero;
            tRect.offsetMax = Vector2.zero;

            // rule description
            string eventsStr = string.Join(
                rule.Logic == RuleLogic.All ? " & " : " | ",
                rule.Conditions.Select(c => GetEventDisplayName(c)));
            string soundDisplay = GetSoundDisplayName(rule.SoundClip);
            string cooldownStr = rule.Cooldown <= COOLDOWN_SHORT ? "S"
                : rule.Cooldown <= COOLDOWN_MEDIUM ? "M" : "L";
            string descText = $"{eventsStr} > {soundDisplay} [{cooldownStr}]";
            if (!rule.Enabled) descText = $"<color=#666>{descText}</color>";

            var descGO = new GameObject("Desc");
            descGO.AddComponent<RectTransform>();
            descGO.transform.SetParent(rowGO.transform, false);
            var descLayout = descGO.AddComponent<LayoutElement>();
            descLayout.minWidth = 40;
            descLayout.preferredWidth = 120;
            descLayout.flexibleWidth = 1;
            var descTmp = descGO.AddComponent<TextMeshProUGUI>();
            descTmp.text = descText;
            descTmp.fontSize = 10;
            descTmp.enableAutoSizing = true;
            descTmp.fontSizeMin = 7;
            descTmp.fontSizeMax = 10;
            descTmp.alignment = TextAlignmentOptions.MidlineLeft;
            descTmp.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            descTmp.overflowMode = TextOverflowModes.Ellipsis;
            descTmp.richText = true;
            descTmp.raycastTarget = false;

            // edit button
            CreateSmallButton(rowGO.transform, "...", 20, () =>
            {
                _editingRule = capturedRule;
                _editorSelectedEvents = new HashSet<WarningEvent>(capturedRule.Conditions);
                _editorLogic = capturedRule.Logic;
                _editorSoundClip = capturedRule.SoundClip;
                _editorCooldown = capturedRule.Cooldown;
                _editorGroup = capturedRule.Group ?? "";
                _editorThresholds = new Dictionary<WarningEvent, float>(capturedRule.Thresholds);
                _editorComparisons = new Dictionary<WarningEvent, CompareOp>(capturedRule.Comparisons);
                ShowRuleEditor();
            });

            // delete (custom only)
            if (!rule.IsDefault)
            {
                CreateSmallButton(rowGO.transform, "X", 20, () =>
                {
                    _eventRules.Remove(capturedRule);
                    PopulateRulesList();
                });
            }
        }

        // show the rule editor
        private void ShowRuleEditor()
        {
            if (_ruleEditorContainer == null) return;

            var toDestroy = new List<GameObject>();
            for (int i = 0; i < _ruleEditorContainer.childCount; i++)
                toDestroy.Add(_ruleEditorContainer.GetChild(i).gameObject);
            foreach (var go in toDestroy)
                UnityEngine.Object.Destroy(go);

            _ruleEditorContainer.gameObject.SetActive(true);

            string headerText = _editingRule != null ? "Edit Rule" : "New Rule";
            CreateSectionHeader(_ruleEditorContainer, headerText);

            // event selector label
            var eventsLabel = new GameObject("EventsLabel");
            eventsLabel.AddComponent<RectTransform>();
            eventsLabel.transform.SetParent(_ruleEditorContainer, false);
            var evtLblLayout = eventsLabel.AddComponent<LayoutElement>();
            evtLblLayout.minHeight = 18;
            evtLblLayout.preferredHeight = 18;
            var evtLblText = eventsLabel.AddComponent<TextMeshProUGUI>();
            evtLblText.text = "Events (select one or more):";
            evtLblText.fontSize = 9;
            evtLblText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            evtLblText.alignment = TextAlignmentOptions.MidlineLeft;
            evtLblText.raycastTarget = false;

            // event grid (3 cols, skip unused custom axes)
            int cols = 3;
            int customAxisCount = 0;
            try
            {
                var craft = Craft.active;
                if (craft?.customAxes != null)
                    customAxisCount = craft.customAxes.Count;
            }
            catch { }

            var visibleEvents = new List<WarningEvent>();
            for (int i = 0; i < ALL_EVENTS.Length; i++)
            {
                var evt = ALL_EVENTS[i];
                // hide custom axis slots beyond the craft's actual axes
                if (evt >= WarningEvent.CustomAxis0 && evt <= WarningEvent.CustomAxis7)
                {
                    int axisIdx = evt - WarningEvent.CustomAxis0;
                    if (axisIdx >= customAxisCount)
                        continue;
                }
                visibleEvents.Add(evt);
            }

            int evtIdx = 0;
            while (evtIdx < visibleEvents.Count)
            {
                var rowGO = new GameObject("EvtRow");
                rowGO.AddComponent<RectTransform>();
                rowGO.transform.SetParent(_ruleEditorContainer, false);

                var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 2;
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = true;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;

                var rowLE = rowGO.AddComponent<LayoutElement>();
                rowLE.minHeight = 22;
                rowLE.preferredHeight = 22;
                rowLE.flexibleWidth = 1;

                for (int c = 0; c < cols && evtIdx < visibleEvents.Count; c++, evtIdx++)
                    CreateEventToggleButton(rowGO.transform, visibleEvents[evtIdx]);
            }

            // thresholds for selected events
            var thresholdEvents = _editorSelectedEvents
                .Where(e => THRESHOLD_META.ContainsKey(e))
                .OrderBy(e => (int)e)
                .ToList();

            if (thresholdEvents.Count > 0)
            {
                var threshLbl = new GameObject("ThresholdLabel");
                threshLbl.AddComponent<RectTransform>();
                threshLbl.transform.SetParent(_ruleEditorContainer, false);
                var threshLblLayout = threshLbl.AddComponent<LayoutElement>();
                threshLblLayout.minHeight = 18;
                threshLblLayout.preferredHeight = 18;
                var threshLblText = threshLbl.AddComponent<TextMeshProUGUI>();
                threshLblText.text = "Thresholds (blank = default):";
                threshLblText.fontSize = 9;
                threshLblText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                threshLblText.alignment = TextAlignmentOptions.MidlineLeft;
                threshLblText.raycastTarget = false;

                foreach (var evt in thresholdEvents)
                    CreateThresholdRow(_ruleEditorContainer, evt, THRESHOLD_META[evt]);
            }

            // logic selector (any/all)
            var logicRow = new GameObject("LogicRow");
            logicRow.AddComponent<RectTransform>();
            logicRow.transform.SetParent(_ruleEditorContainer, false);
            var logicRowLayout = logicRow.AddComponent<HorizontalLayoutGroup>();
            logicRowLayout.spacing = 3;
            logicRowLayout.childForceExpandWidth = false;
            logicRowLayout.childForceExpandHeight = true;
            logicRowLayout.childControlWidth = true;
            logicRowLayout.childControlHeight = true;
            logicRowLayout.childAlignment = TextAnchor.MiddleLeft;
            var logicRowLE = logicRow.AddComponent<LayoutElement>();
            logicRowLE.minHeight = 24;
            logicRowLE.preferredHeight = 24;
            logicRowLE.flexibleWidth = 1;

            var logicLbl = new GameObject("LogicLabel");
            logicLbl.AddComponent<RectTransform>();
            logicLbl.transform.SetParent(logicRow.transform, false);
            var logicLblLE = logicLbl.AddComponent<LayoutElement>();
            logicLblLE.minWidth = 40;
            logicLblLE.preferredWidth = 40;
            logicLblLE.flexibleWidth = 0;
            var logicLblTmp = logicLbl.AddComponent<TextMeshProUGUI>();
            logicLblTmp.text = "Logic:";
            logicLblTmp.fontSize = 10;
            logicLblTmp.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            logicLblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            logicLblTmp.raycastTarget = false;

            CreateLogicToggle(logicRow.transform, "ANY", RuleLogic.Any);
            CreateLogicToggle(logicRow.transform, "ALL", RuleLogic.All);

            // sound selector
            var soundRow = new GameObject("SoundRow");
            soundRow.AddComponent<RectTransform>();
            soundRow.transform.SetParent(_ruleEditorContainer, false);
            var soundRowLayout = soundRow.AddComponent<HorizontalLayoutGroup>();
            soundRowLayout.spacing = 3;
            soundRowLayout.childForceExpandWidth = false;
            soundRowLayout.childForceExpandHeight = true;
            soundRowLayout.childControlWidth = true;
            soundRowLayout.childControlHeight = true;
            soundRowLayout.childAlignment = TextAnchor.MiddleLeft;
            var soundRowLE = soundRow.AddComponent<LayoutElement>();
            soundRowLE.minHeight = 24;
            soundRowLE.preferredHeight = 24;
            soundRowLE.flexibleWidth = 1;

            var soundLbl = new GameObject("SoundLabel");
            soundLbl.AddComponent<RectTransform>();
            soundLbl.transform.SetParent(soundRow.transform, false);
            var soundLblLE = soundLbl.AddComponent<LayoutElement>();
            soundLblLE.minWidth = 40;
            soundLblLE.preferredWidth = 40;
            soundLblLE.flexibleWidth = 0;
            var soundLblTmp = soundLbl.AddComponent<TextMeshProUGUI>();
            soundLblTmp.text = "Sound:";
            soundLblTmp.fontSize = 10;
            soundLblTmp.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            soundLblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            soundLblTmp.raycastTarget = false;

            string currentSoundDisplay = GetSoundDisplayName(_editorSoundClip);

            var soundBtnGO = new GameObject("SoundSelector");
            soundBtnGO.AddComponent<RectTransform>();
            soundBtnGO.transform.SetParent(soundRow.transform, false);
            var soundBtnLE = soundBtnGO.AddComponent<LayoutElement>();
            soundBtnLE.minWidth = 60;
            soundBtnLE.preferredWidth = 100;
            soundBtnLE.flexibleWidth = 1;
            soundBtnLE.minHeight = 20;

            soundBtnGO.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);

            var soundBtn = soundBtnGO.AddComponent<Button>();
            var soundBtnColors = soundBtn.colors;
            soundBtnColors.normalColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            soundBtnColors.highlightedColor = new Color(0.3f, 0.3f, 0.4f, 1f);
            soundBtnColors.pressedColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            soundBtn.colors = soundBtnColors;
            soundBtn.onClick.AddListener((UnityAction)(() =>
            {
                // cycle through sounds
                var allSounds = GetAllSounds();
                int currentIdx = -1;
                for (int si = 0; si < allSounds.Count; si++)
                {
                    if (allSounds[si].clipName == _editorSoundClip) { currentIdx = si; break; }
                }
                currentIdx = (currentIdx + 1) % allSounds.Count;
                _editorSoundClip = allSounds[currentIdx].clipName;
                ShowRuleEditor();
            }));

            var soundTextGO = new GameObject("Text");
            soundTextGO.AddComponent<RectTransform>();
            soundTextGO.transform.SetParent(soundBtnGO.transform, false);
            var soundTmp = soundTextGO.AddComponent<TextMeshProUGUI>();
            soundTmp.text = currentSoundDisplay;
            soundTmp.fontSize = 10;
            soundTmp.enableAutoSizing = true;
            soundTmp.fontSizeMin = 7;
            soundTmp.fontSizeMax = 10;
            soundTmp.alignment = TextAlignmentOptions.Center;
            soundTmp.color = new Color(0.5f, 0.9f, 1f, 1f);
            soundTmp.overflowMode = TextOverflowModes.Ellipsis;
            soundTmp.raycastTarget = false;
            var soundTextRect = soundTextGO.GetComponent<RectTransform>();
            soundTextRect.anchorMin = Vector2.zero;
            soundTextRect.anchorMax = Vector2.one;
            soundTextRect.offsetMin = new Vector2(3, 1);
            soundTextRect.offsetMax = new Vector2(-3, -1);

            // test sound button
            CreateSmallImageButton(soundRow.transform, _speakerBtnSprite, 20, () =>
            {
                if (!string.IsNullOrEmpty(_editorSoundClip))
                    PlayWarningSound(_editorSoundClip);
            });

            // cooldown selector
            var cdRow = new GameObject("CooldownRow");
            cdRow.AddComponent<RectTransform>();
            cdRow.transform.SetParent(_ruleEditorContainer, false);
            var cdRowLayout = cdRow.AddComponent<HorizontalLayoutGroup>();
            cdRowLayout.spacing = 3;
            cdRowLayout.childForceExpandWidth = false;
            cdRowLayout.childForceExpandHeight = true;
            cdRowLayout.childControlWidth = true;
            cdRowLayout.childControlHeight = true;
            cdRowLayout.childAlignment = TextAnchor.MiddleLeft;
            var cdRowLE = cdRow.AddComponent<LayoutElement>();
            cdRowLE.minHeight = 24;
            cdRowLE.preferredHeight = 24;
            cdRowLE.flexibleWidth = 1;

            var cdLbl = new GameObject("CooldownLabel");
            cdLbl.AddComponent<RectTransform>();
            cdLbl.transform.SetParent(cdRow.transform, false);
            var cdLblLE = cdLbl.AddComponent<LayoutElement>();
            cdLblLE.minWidth = 40;
            cdLblLE.preferredWidth = 40;
            cdLblLE.flexibleWidth = 0;
            var cdLblTmp = cdLbl.AddComponent<TextMeshProUGUI>();
            cdLblTmp.text = "Delay:";
            cdLblTmp.fontSize = 10;
            cdLblTmp.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            cdLblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            cdLblTmp.raycastTarget = false;

            CreateCooldownButton(cdRow.transform, "3s", COOLDOWN_SHORT);
            CreateCooldownButton(cdRow.transform, "5s", COOLDOWN_MEDIUM);
            CreateCooldownButton(cdRow.transform, "10s", COOLDOWN_LONG);

            // group selector
            var groupRow = new GameObject("GroupRow");
            groupRow.AddComponent<RectTransform>();
            groupRow.transform.SetParent(_ruleEditorContainer, false);
            var groupRowLayout = groupRow.AddComponent<HorizontalLayoutGroup>();
            groupRowLayout.spacing = 3;
            groupRowLayout.childForceExpandWidth = false;
            groupRowLayout.childForceExpandHeight = true;
            groupRowLayout.childControlWidth = true;
            groupRowLayout.childControlHeight = true;
            groupRowLayout.childAlignment = TextAnchor.MiddleLeft;
            var groupRowLE = groupRow.AddComponent<LayoutElement>();
            groupRowLE.minHeight = 24;
            groupRowLE.preferredHeight = 24;
            groupRowLE.flexibleWidth = 1;

            var groupLbl = new GameObject("GroupLabel");
            groupLbl.AddComponent<RectTransform>();
            groupLbl.transform.SetParent(groupRow.transform, false);
            var groupLblLE = groupLbl.AddComponent<LayoutElement>();
            groupLblLE.minWidth = 40;
            groupLblLE.preferredWidth = 40;
            groupLblLE.flexibleWidth = 0;
            var groupLblTmp = groupLbl.AddComponent<TextMeshProUGUI>();
            groupLblTmp.text = "Group:";
            groupLblTmp.fontSize = 10;
            groupLblTmp.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            groupLblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            groupLblTmp.raycastTarget = false;

            string[] groupOptions = { "", "bingo", "bank_dir", "fire", "custom" };
            string currentGroup = _editorGroup ?? "";

            var groupBtnGO = new GameObject("GroupSelector");
            groupBtnGO.AddComponent<RectTransform>();
            groupBtnGO.transform.SetParent(groupRow.transform, false);
            var groupBtnLE = groupBtnGO.AddComponent<LayoutElement>();
            groupBtnLE.minWidth = 50;
            groupBtnLE.preferredWidth = 80;
            groupBtnLE.flexibleWidth = 1;
            groupBtnLE.minHeight = 20;

            groupBtnGO.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);

            var groupBtnComp = groupBtnGO.AddComponent<Button>();
            var groupBtnCol = groupBtnComp.colors;
            groupBtnCol.normalColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            groupBtnCol.highlightedColor = new Color(0.3f, 0.3f, 0.4f, 1f);
            groupBtnComp.colors = groupBtnCol;
            groupBtnComp.onClick.AddListener((UnityAction)(() =>
            {
                int gi = Array.IndexOf(groupOptions, _editorGroup);
                gi = (gi + 1) % groupOptions.Length;
                _editorGroup = groupOptions[gi];
                ShowRuleEditor();
            }));

            var groupTextGO = new GameObject("Text");
            groupTextGO.AddComponent<RectTransform>();
            groupTextGO.transform.SetParent(groupBtnGO.transform, false);
            var groupTmp = groupTextGO.AddComponent<TextMeshProUGUI>();
            groupTmp.text = string.IsNullOrEmpty(currentGroup) ? "(none)" : currentGroup;
            groupTmp.fontSize = 10;
            groupTmp.alignment = TextAlignmentOptions.Center;
            groupTmp.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            groupTmp.raycastTarget = false;
            var groupTextRect = groupTextGO.GetComponent<RectTransform>();
            groupTextRect.anchorMin = Vector2.zero;
            groupTextRect.anchorMax = Vector2.one;
            groupTextRect.offsetMin = new Vector2(3, 1);
            groupTextRect.offsetMax = new Vector2(-3, -1);

            // save / cancel
            var actionRow = new GameObject("ActionsRow");
            actionRow.AddComponent<RectTransform>();
            actionRow.transform.SetParent(_ruleEditorContainer, false);
            var actionLayout = actionRow.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 4;
            actionLayout.childForceExpandWidth = true;
            actionLayout.childForceExpandHeight = true;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            var actionLE = actionRow.AddComponent<LayoutElement>();
            actionLE.minHeight = 28;
            actionLE.preferredHeight = 28;
            actionLE.flexibleWidth = 1;

            CreateSmallButton(actionRow.transform, "Save Rule", 65, () =>
            {
                if (_editorSelectedEvents.Count == 0)
                {
                    LoggerInstance.Warning("[CraftEditor] Cannot save — no events selected");
                    return;
                }
                if (string.IsNullOrEmpty(_editorSoundClip))
                {
                    LoggerInstance.Warning("[CraftEditor] Cannot save — no sound selected");
                    return;
                }

                if (_editingRule != null)
                {
                    _editingRule.Conditions = new List<WarningEvent>(_editorSelectedEvents);
                    _editingRule.Logic = _editorLogic;
                    _editingRule.SoundClip = _editorSoundClip;
                    _editingRule.Cooldown = _editorCooldown;
                    _editingRule.Group = _editorGroup;
                    _editingRule.Thresholds = new Dictionary<WarningEvent, float>(_editorThresholds);
                    _editingRule.Comparisons = new Dictionary<WarningEvent, CompareOp>(_editorComparisons);
                    LoggerInstance.Msg($"[CraftEditor] Updated rule '{_editingRule.Id}'");
                }
                else
                {
                    string newId = "custom_" + DateTime.Now.Ticks.ToString().Substring(10);
                    _eventRules.Add(new EventRule
                    {
                        Id = newId,
                        Conditions = new List<WarningEvent>(_editorSelectedEvents),
                        Logic = _editorLogic,
                        SoundClip = _editorSoundClip,
                        Cooldown = _editorCooldown,
                        Enabled = true,
                        Group = _editorGroup,
                        IsDefault = false,
                        Thresholds = new Dictionary<WarningEvent, float>(_editorThresholds),
                        Comparisons = new Dictionary<WarningEvent, CompareOp>(_editorComparisons),
                    });
                    LoggerInstance.Msg($"[CraftEditor] Created rule '{newId}'");
                }

                _ruleEditorContainer.gameObject.SetActive(false);
                PopulateRulesList();
            });

            CreateSmallButton(actionRow.transform, "Cancel", 50, () =>
            {
                _ruleEditorContainer.gameObject.SetActive(false);
            });
        }

        // nuke all game scripts from cloned objects so they dont do game stuff
        private void StripGameComponents(GameObject go)
        {
            try
            {
                // grab every monobehaviour on the object and its children
                var allBehaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
                if (allBehaviours == null) return;

                for (int i = 0; i < allBehaviours.Count; i++)
                {
                    var comp = allBehaviours[i];
                    if (comp == null) continue;

                    string typeName = comp.GetIl2CppType().FullName;

                    // keep standard unity ui stuff plus sound/tooltip
                    if (typeName.StartsWith("UnityEngine.UI.") ||
                        typeName.StartsWith("TMPro.") ||
                        typeName.StartsWith("UnityEngine.EventSystems.") ||
                        typeName == "Il2Cpp.ButtonSound" ||
                        typeName == "Il2CppCraftEditor.ButtonTooltip")
                        continue;

                    UnityEngine.Object.Destroy(comp);
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[CraftEditor] StripGameComponents: {ex.Message}");
            }
        }

        // panel toggle and file browser

        // toggle panel on/off
        private void OnWarningEditorButtonClick()
        {
            _warningEditorActive = !_warningEditorActive;
            LoggerInstance.Msg($"[CraftEditor] Warning editor: {(_warningEditorActive ? "ON" : "OFF")}");

            if (_warningCreatorPanel != null)
                _warningCreatorPanel.SetActive(_warningEditorActive);

            try
            {
                if (_warningEditorActive)
                {
                    // switch to Other mode so game hides its panels
                    var cem = CEManager.instance;
                    if (cem != null)
                        cem.SetMode(Mode.Other);

                    // kill paint mode and hide any lingering native panels
                    var ceui = UnityEngine.Object.FindObjectOfType<CEUI>();
                    if (ceui != null)
                    {
                        // disable paint mode component so it fires OnDisable and stops painting
                        if (ceui.paintMode != null)
                        {
                            ceui.paintMode.enabled = false;
                            if (ceui.paintMode.paintTools != null)
                                ceui.paintMode.paintTools.SetActive(false);
                        }
                        if (ceui.categoryPanel != null)
                            ceui.categoryPanel.gameObject.SetActive(false);
                        if (ceui.partTools != null)
                            ceui.partTools.SetActive(false);
                    }
                }
            }
            catch { }

            UpdateVwsButtonColor();
        }

        // update button color
        private void UpdateVwsButtonColor()
        {
            if (_warningEditorButton == null) return;
            try
            {
                var img = _warningEditorButton.GetComponent<Image>();
                if (img != null)
                    img.color = _warningEditorActive
                        ? new Color(0.6f, 0.6f, 0.6f, 1f)
                        : new Color(1f, 1f, 1f, 1f);
            }
            catch { }
        }

        // open file browser for custom sound import
        private void BrowseForCustomSound()
        {
            _pendingFileBrowserClip = "__custom_import__";
            try
            {
                FileBrowser.SetFilters(true, new string[] { ".wav", ".ogg", ".mp3", ".flac" });
                FileBrowser.SetDefaultFilter(".wav");

                var onSuccess = DelegateSupport.ConvertDelegate<FileBrowser.OnSuccess>(
                    new System.Action<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray>(
                        (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray paths) =>
                        {
                            string[] managedPaths = new string[paths.Length];
                            for (int i = 0; i < paths.Length; i++)
                                managedPaths[i] = paths[i];
                            OnCustomSoundImport(managedPaths);
                        }));

                var onCancel = DelegateSupport.ConvertDelegate<FileBrowser.OnCancel>(
                    new System.Action(OnFileBrowserCancel));

                FileBrowser.ShowLoadDialog(onSuccess, onCancel, FileBrowser.PickMode.Files,
                    true, _dataFolderPath, null, "Import Custom Sounds", "Import");

                LoggerInstance.Msg("[CraftEditor] File browser opened for custom sound import");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[CraftEditor] Failed to open file browser: {ex.Message}");
            }
        }

        // handle custom sound import
        private void OnCustomSoundImport(string[] paths)
        {
            try
            {
                if (paths == null || paths.Length == 0) return;

                foreach (string selectedPath in paths)
                {
                    if (string.IsNullOrEmpty(selectedPath) || !File.Exists(selectedPath)) continue;

                    string fileName = Path.GetFileNameWithoutExtension(selectedPath);
                    string ext = Path.GetExtension(selectedPath);
                    string clipName = "custom_" + fileName.Replace(" ", "_").ToLowerInvariant();

                    if (_customSounds.Any(cs => cs.clipName == clipName))
                    {
                        LoggerInstance.Warning($"[CraftEditor] '{clipName}' already exists, skipping");
                        continue;
                    }

                    string destPath = Path.Combine(_dataFolderPath, clipName + ext);
                    if (Path.GetFullPath(selectedPath) != Path.GetFullPath(destPath))
                        File.Copy(selectedPath, destPath, true);

                    _clipPaths[clipName] = Path.GetFullPath(destPath);
                    _customSounds.Add((clipName, fileName, Path.GetFullPath(destPath)));
                    LoggerInstance.Msg($"[CraftEditor] Imported '{fileName}' -> {clipName}");
                }

                SaveCustomSounds();
                try { PopulateRulesList(); } catch { }
                try { RefreshCustomSoundsList(); } catch { }
                try { RefreshWarningPanelButtons(); } catch { }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[CraftEditor] Import error: {ex.Message}");
            }
        }

        // open file browser for a specific clip override
        private void OnBrowseForSound(string clipName)
        {
            _pendingFileBrowserClip = clipName;
            try
            {
                FileBrowser.SetFilters(true, new string[] { ".wav", ".ogg", ".mp3", ".flac" });
                FileBrowser.SetDefaultFilter(".wav");

                string initialPath = _dataFolderPath;
                if (!string.IsNullOrWhiteSpace(_configCraftName))
                {
                    string craftDir = GetCraftConfigDir(_configCraftName, false);
                    if (Directory.Exists(craftDir)) initialPath = craftDir;
                }

                var onSuccess = DelegateSupport.ConvertDelegate<FileBrowser.OnSuccess>(
                    new System.Action<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray>(
                        (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray paths) =>
                        {
                            string[] managedPaths = new string[paths.Length];
                            for (int i = 0; i < paths.Length; i++)
                                managedPaths[i] = paths[i];
                            OnFileBrowserSuccess(managedPaths);
                        }));

                var onCancel = DelegateSupport.ConvertDelegate<FileBrowser.OnCancel>(
                    new System.Action(OnFileBrowserCancel));

                FileBrowser.ShowLoadDialog(onSuccess, onCancel, FileBrowser.PickMode.Files,
                    false, initialPath, null, "Select Warning Sound", "Select");

                LoggerInstance.Msg($"[CraftEditor] File browser opened for '{clipName}'");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[CraftEditor] Failed to open file browser: {ex.Message}");
            }
        }

        // file browser picked a file
        private void OnFileBrowserSuccess(string[] paths)
        {
            try
            {
                if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0]))
                {
                    LoggerInstance.Msg("[CraftEditor] File browser returned no selection");
                    return;
                }

                string selectedPath = paths[0];
                string clipName = _pendingFileBrowserClip;

                if (string.IsNullOrEmpty(clipName))
                {
                    LoggerInstance.Warning("[CraftEditor] No pending clip for file browser result");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_configCraftName))
                {
                    string craftDir = GetCraftConfigDir(_configCraftName, true);
                    string destFileName = clipName + Path.GetExtension(selectedPath);
                    string destPath = Path.Combine(craftDir, destFileName);

                    if (Path.GetFullPath(selectedPath) != Path.GetFullPath(destPath))
                    {
                        File.Copy(selectedPath, destPath, true);
                        LoggerInstance.Msg($"[CraftEditor] Copied '{Path.GetFileName(selectedPath)}' -> '{destPath}'");
                    }

                    _craftOverrides[clipName] = Path.GetFullPath(destPath);
                    // file on disk changed, dump the stale cached AudioClip
                    InvalidateClipCache(Path.GetFullPath(destPath));
                }
                else
                {
                    _craftOverrides[clipName] = Path.GetFullPath(selectedPath);
                    InvalidateClipCache(Path.GetFullPath(selectedPath));
                }

                LoggerInstance.Msg($"[CraftEditor] Override set for '{clipName}': {_craftOverrides[clipName]}");

                if (!string.IsNullOrWhiteSpace(_configCraftName))
                    SaveCraftConfig(_configCraftName);

                RefreshWarningPanelButtons();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[CraftEditor] File browser callback error: {ex.Message}");
            }
        }

        private void OnFileBrowserCancel()
        {
            LoggerInstance.Msg("[CraftEditor] File browser cancelled");
        }

        // update status text on warning rows
        internal void RefreshWarningPanelButtons()
        {
            if (_warningCreatorPanel == null) return;

            try
            {
                for (int i = 0; i < WARNING_SOUNDS.Length; i++)
                {
                    var (clipName, displayName) = WARNING_SOUNDS[i];

                    Transform statusTr = FindChildRecursive(_warningCreatorPanel.transform, "Status_" + clipName);
                    if (statusTr == null) continue;

                    var statusText = statusTr.GetComponent<TMP_Text>();
                    if (statusText == null) continue;

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
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[CraftEditor] RefreshWarningPanelButtons: {ex.Message}");
            }

            try { PopulateRulesList(); } catch { }
            try { RefreshCustomSoundsList(); } catch { }
        }

        // rebuild custom sounds list
        internal void RefreshCustomSoundsList()
        {
            if (_customSoundsContainer == null) return;

            var toDestroy = new List<GameObject>();
            for (int i = 0; i < _customSoundsContainer.childCount; i++)
                toDestroy.Add(_customSoundsContainer.GetChild(i).gameObject);
            foreach (var go in toDestroy)
                UnityEngine.Object.Destroy(go);

            if (_customSounds.Count > 0)
            {
                CreateSectionHeader(_customSoundsContainer, "Custom Sounds");
                foreach (var cs in _customSounds.ToList())
                    CreateCustomSoundRow(_customSoundsContainer, cs.clipName, cs.displayName, cs.clipName);
            }
        }
    }
}
