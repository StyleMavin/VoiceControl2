// VoiceControl2 — trigger events via spoken keywords.
// Main plugin class. Compiled together with Backends.cs and TriggerSupport.cs
// via VoiceControl2.cslist.
//
// Speech backends (selectable in the UI):
//   - Windows Speech (Legacy): original KeywordRecognizer / WSR / SAPI
//   - Vosk Companion: talks to VoskCompanion.exe over localhost UDP (future-proof)
//
// Credits: Almadiel (original VoiceTrigger), MacGruber/Acidbubbles (TriggerHandler),
//          Acidbubbles (text input field), MeshedVR (HUD example)

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Windows.Speech;
using SimpleJSON;

namespace StyleMavin {

    public class VoiceControl2 : MVRScript {

        private const string pluginName    = "VoiceControl2";
        private const string pluginAuthor  = "StyleMavin";
        private const string pluginVersion = "v2.0";
        private const string pluginDate    = "2025";
        private const string aboutText     = "\n<b><size=40><color=brown>" + pluginName + " " + pluginVersion
                                             + "</color></size></b>\n<i>" + pluginAuthor + "\n" + pluginDate + "</i>";
        private const string chooserDefault = "Select";
        private const string titleDefault   = "Enter New Trigger Name...";
        private const string popupDefault   = "VAM window does not have focus\nClick anywhere inside VAM's window to enable Voice Control";
        private const float  hudDuration    = 3.0f;
        private const int    defaultPort    = 19547;

        private static readonly List<string> backendNames    = new List<string>() { "Windows Speech (Legacy)", "Vosk Companion" };
        private static readonly List<string> confidenceNames = new List<string>() { "Low", "Medium", "High" };

        protected JSONStorableBool          enableLog;
        protected JSONStorableBool          enableFocusWarning;
        protected JSONStorableBool          enableHUD;
        protected JSONStorableStringChooser selectBackend;
        protected JSONStorableStringChooser setConfidence;
        protected JSONStorableString        companionPortText;
        protected JSONStorableStringChooser editTrigger;
        protected JSONStorableStringChooser renameTrigger;
        protected JSONStorableStringChooser deleteTrigger;
        protected JSONStorableString        triggerNameText;

        protected UIDynamicButton     sharedButton;
        protected UIDynamicButton     cancelButton;
        protected UIDynamicTextField  triggerNameTextInput;
        protected Canvas              popupCanvas;
        protected Text                popupText;
        protected Text                inputTitleText;
        protected InputField          inputField;
        protected UIDynamicPopup      confidencePopup;
        protected UIDynamicTextField  portField;

        private IBackend             _backend;
        private VoskCompanionBackend _voskBackend;
        private ConfidenceLevel      _confidence  = ConfidenceLevel.Medium;
        private int                  _companionPort = defaultPort;

        private VoiceTrigger       voiceTrigger;
        private List<VoiceTrigger> voiceTriggerList;
        private List<string>       triggerNameList;
        private List<string>       commandList;
        private List<string>       micDeviceList;
        private int                listIndex;
        private bool               renaming  = false;
        private bool               deleting  = false;
        private bool               addOrEdit = false;
        private bool               firstPass = true;
        private bool               showHUD   = false;
        private float              hudTimer  = 0f;

        public override void Init() {
            try {
                pluginLabelJSON.val = pluginName + " " + pluginVersion + " - " + pluginAuthor;
                SimpleTriggerHandler.LoadAssets();
                SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRename;

                micDeviceList    = new List<string>();
                commandList      = new List<string>();
                voiceTriggerList = new List<VoiceTrigger>();
                triggerNameList  = new List<string>();
                triggerNameList.Add(chooserDefault);

                foreach (string d in Microphone.devices) micDeviceList.Add(d);

                BuildLeftUI();
                BuildRightUI();
                initHUD();
            }
            catch (Exception e) { SuperController.LogError("Exception in Init(): " + e); }
        }

        private void BuildLeftUI() {
            CreateSpacer(false).height = 15f;

            var about = new JSONStorableString("About", aboutText);
            var dtfAbout = CreateTextField(about, false);
            dtfAbout.UItext.alignment = TextAnchor.MiddleCenter;
            dtfAbout.UItext.supportRichText = true;
            dtfAbout.UItext.fontSize = 30;
            dtfAbout.UItext.color = Color.black;
            dtfAbout.height = 185;

            string defaultMic = "<b><size=24><color=blue>Active Microphone</color></size></b><i>\n";
            defaultMic += micDeviceList.Count > 0 ? micDeviceList[0] : "None";
            defaultMic += "</i>";
            var activeMic = new JSONStorableString("Active Mic", defaultMic);
            var dtfMic = CreateTextField(activeMic, false);
            dtfMic.UItext.alignment = TextAnchor.MiddleCenter;
            dtfMic.UItext.fontSize = 20;
            dtfMic.UItext.color = Color.black;
            dtfMic.UItext.supportRichText = true;
            dtfMic.height = 85;

            enableLog = new JSONStorableBool("Log Plugin Messages", true);
            enableLog.storeType = JSONStorableParam.StoreType.Full;
            RegisterBool(enableLog);
            CreateToggle(enableLog, false);

            enableFocusWarning = new JSONStorableBool("HUD - Window Focus Warning", true);
            enableFocusWarning.storeType = JSONStorableParam.StoreType.Full;
            RegisterBool(enableFocusWarning);
            CreateToggle(enableFocusWarning, false);

            enableHUD = new JSONStorableBool("HUD - Recognizer Feedback", true);
            enableHUD.storeType = JSONStorableParam.StoreType.Full;
            RegisterBool(enableHUD);
            CreateToggle(enableHUD, false);

            CreateSpacer(false).height = 10f;

            selectBackend = new JSONStorableStringChooser("Speech Backend", backendNames, backendNames[0], "Speech Backend", OnBackendChanged);
            selectBackend.storeType = JSONStorableParam.StoreType.Full;
            RegisterStringChooser(selectBackend);
            CreateScrollablePopup(selectBackend, false).popupPanelHeight = 180f;

            setConfidence = new JSONStorableStringChooser("Confidence Level", confidenceNames, "Medium", "Required Confidence Level", SetConfidenceLevel);
            RegisterStringChooser(setConfidence);
            confidencePopup = CreateScrollablePopup(setConfidence, false);
            confidencePopup.popupPanelHeight = 220f;

            companionPortText = new JSONStorableString("Companion Port", defaultPort.ToString());
            companionPortText.storeType = JSONStorableParam.StoreType.Full;
            RegisterString(companionPortText);
            portField = CreateTextField(companionPortText, false);
            portField.height = 60;

            RefreshBackendUI(selectBackend.val);
        }

        private void BuildRightUI() {
            triggerNameText = new JSONStorableString("triggerNameText", "");
            triggerNameTextInput = CreateTextInput(triggerNameText, titleDefault, true);

            CreateSpacer(true).height = 0f;

            sharedButton = CreateButton("Add Trigger", true);
            sharedButton.buttonText.color = Color.black;
            sharedButton.buttonColor = Color.green;
            sharedButton.button.onClick.AddListener(doTrigger);

            cancelButton = CreateButton("Cancel", true);
            cancelButton.buttonText.color = Color.white;
            cancelButton.buttonColor = Color.blue;
            cancelButton.button.onClick.AddListener(resetUI);

            string availMics = "<b><size=24><color=blue>Available Microphones</color></size></b><i>\n";
            foreach (string mic in micDeviceList) availMics += mic + "\n";
            if (micDeviceList.Count == 0) availMics += "None";
            availMics += "</i>";
            var dtfAvail = CreateTextField(new JSONStorableString("Available Mics", availMics), true);
            dtfAvail.UItext.alignment = TextAnchor.MiddleCenter;
            dtfAvail.UItext.fontSize = 20;
            dtfAvail.UItext.color = Color.black;
            dtfAvail.UItext.supportRichText = true;
            dtfAvail.height = 85;

            editTrigger = new JSONStorableStringChooser("Edit Trigger", triggerNameList, chooserDefault, "Edit   Trigger", TriggerEdit);
            CreateScrollablePopup(editTrigger, true).popupPanelHeight = 780f;
            editTrigger.popup.labelTextColor = Color.blue;

            renameTrigger = new JSONStorableStringChooser("Rename Trigger", triggerNameList, chooserDefault, "Rename Trigger", TriggerRename);
            CreateScrollablePopup(renameTrigger, true).popupPanelHeight = 665f;
            renameTrigger.popup.labelTextColor = Color.yellow;

            deleteTrigger = new JSONStorableStringChooser("Delete Trigger", triggerNameList, chooserDefault, "Delete Trigger", TriggerDelete);
            CreateScrollablePopup(deleteTrigger, true).popupPanelHeight = 550f;
            deleteTrigger.popup.labelTextColor = Color.red;
        }

        private void RefreshBackendUI(string backendName) {
            bool isVosk = backendName == "Vosk Companion";
            if (confidencePopup != null) confidencePopup.gameObject.SetActive(!isVosk);
            if (portField       != null) portField.gameObject.SetActive(isVosk);
        }

        private void OnBackendChanged(string selected) {
            RefreshBackendUI(selected);
            startRecognizer();
        }

        private void SetConfidenceLevel(string level) {
            switch (level) {
                case "Low":  _confidence = ConfidenceLevel.Low;  break;
                case "High": _confidence = ConfidenceLevel.High; break;
                default:     _confidence = ConfidenceLevel.Medium; break;
            }
            startRecognizer();
        }

        private IBackend CreateBackend() {
            if (selectBackend.val == "Vosk Companion") {
                int port;
                if (!int.TryParse(companionPortText.val, out port)) port = defaultPort;
                _companionPort = port;
                var vb = new VoskCompanionBackend(port);
                vb.OnStatusMessage += msg => logMessage(msg);
                _voskBackend = vb;
                return vb;
            }
            _voskBackend = null;
            var wb = new WSRBackend();
            wb.OnStatusMessage += msg => logMessage(msg);
            return wb;
        }

        private void startRecognizer() {
            if (commandList.Count == 0) return;
            removeRecognizer();
            _backend = CreateBackend();
            _backend.OnPhraseRecognized += phraseRecognized;
            _backend.Start(commandList, _confidence);
            logMessage($"Backend \"{_backend.Name}\" started with {commandList.Count} command(s).");
        }

        private void removeRecognizer() {
            if (_backend == null) return;
            _backend.OnPhraseRecognized -= phraseRecognized;
            _backend.Stop();
            _backend     = null;
            _voskBackend = null;
        }

        private void phraseRecognized(string command) {
            string cmd = (command ?? "").Trim();
            bool matched = false;
            for (int i = 0; i < voiceTriggerList.Count; i++) {
                JSONClass json = voiceTriggerList[i].GetJSON();
                foreach (JSONNode node in (JSONArray)json["startActions"]) {
                    string keyword = ((string)node["name"] ?? "").Trim();
                    // Case-insensitive: the Vosk backend normalizes phrases, and users
                    // may capitalize keywords. Legacy WSR returned exact-case, so this
                    // also keeps both backends behaving identically.
                    if (string.Equals(keyword, cmd, StringComparison.OrdinalIgnoreCase)) {
                        voiceTriggerList[i].Trigger();
                        logMessage($"Command \"{cmd}\" recognized. Trigger \"{voiceTriggerList[i].Name}\" activated.");
                        HUDMessage($"Command \"{cmd}\" recognized.");
                        matched = true;
                    }
                }
            }
            if (!matched)
                logMessage($"Recognized \"{cmd}\" but no trigger keyword matched it.");
        }

        void Update() {
            try {
                _voskBackend?.Poll(Time.deltaTime);

                bool active = false;
                for (int i = 0; i < voiceTriggerList.Count; i++) {
                    voiceTriggerList[i].Update();
                    if (voiceTriggerList[i].actionsPanelActive()) { addOrEdit = true; active = true; }
                }
                if (addOrEdit && !active) { updateCommandList(); startRecognizer(); }

                if (showHUD) {
                    popupText.enabled = true;
                    hudTimer += Time.deltaTime;
                    if (hudTimer > hudDuration) {
                        showHUD = false; hudTimer = 0;
                        popupText.text = popupDefault;
                        popupText.enabled = false;
                    }
                }
            }
            catch (Exception e) { SuperController.LogError("Exception in Update(): " + e); }
        }

        void OnEnable()  { startRecognizer(); }
        void OnDisable() { removeRecognizer(); logMessage("VoiceControl2 disabled."); }

        void OnDestroy() {
            SuperController.singleton.onAtomUIDRenameHandlers -= OnAtomRename;
            removeRecognizer();
            if (popupCanvas != null) Destroy(popupCanvas.gameObject);
            foreach (var vt in voiceTriggerList) vt.Remove();
        }

        void OnApplicationFocus(bool hasFocus) {
            popupText.enabled = !hasFocus && enableFocusWarning.val;
            if (!hasFocus) SuperController.LogMessage(popupDefault);
        }

        void OnApplicationPause(bool isPaused) {
            popupText.enabled = isPaused && enableFocusWarning.val;
            if (isPaused) SuperController.LogMessage(popupDefault);
        }

        private void OnAtomRename(string oldid, string newid) {
            foreach (var vt in voiceTriggerList) vt.SyncAtomNames();
        }

        private void doTrigger() {
            if (triggerNameText.val == "") {
                inputTitleText.text = "Name Cannot Be Empty...";
                inputTitleText.color = Color.red;
                return;
            }
            string triggerName = "";
            for (int i = 0; i < voiceTriggerList.Count; i++) {
                triggerName = voiceTriggerList[i].Name;
                if (triggerNameText.val == triggerName && !deleting) {
                    inputTitleText.color = Color.red;
                    inputTitleText.text = renaming ? "Name identical or in use. Retry or Cancel..."
                                                   : "Name in use. Retry or Cancel...";
                    return;
                }
            }
            if (renaming) {
                triggerName = voiceTriggerList[listIndex].Name;
                voiceTriggerList[listIndex].Name = triggerNameText.val;
                triggerNameList[listIndex + 1]   = triggerNameText.val;
                logMessage($"Renamed trigger \"{triggerName}\" to \"{voiceTriggerList[listIndex].Name}\"");
            }
            else if (deleting) {
                triggerName = voiceTriggerList[listIndex].Name;
                voiceTriggerList.RemoveAt(listIndex);
                triggerNameList.RemoveAt(listIndex + 1);
                logMessage($"Deleted trigger \"{triggerName}\"");
                updateCommandList();
                startRecognizer();
            }
            else {
                int count = voiceTriggerList.Count;
                voiceTrigger = new VoiceTrigger(this, triggerNameText.val);
                voiceTriggerList.Add(voiceTrigger);
                triggerNameList.Add(triggerNameText.val);
                logMessage($"Added trigger \"{voiceTrigger.Name}\"");
                voiceTriggerList[count].OpenPanelActionStart();
            }
            resetUI();
        }

        private void TriggerEdit(string selected) {
            if (editTrigger.val == chooserDefault) return;
            for (int i = 0; i < voiceTriggerList.Count; i++)
                if (selected == voiceTriggerList[i].Name) voiceTriggerList[i].OpenPanelActionStart();
            resetUI();
        }

        private void TriggerRename(string selected) {
            if (renameTrigger.val == chooserDefault) return;
            for (int i = 0; i < voiceTriggerList.Count; i++) {
                if (selected == voiceTriggerList[i].Name) {
                    renaming = true;
                    inputTitleText.text  = "Enter New Name...";
                    inputTitleText.color = Color.yellow;
                    triggerNameText.val  = voiceTriggerList[i].Name;
                    sharedButton.label   = "Confirm Rename";
                    sharedButton.buttonText.color = Color.black;
                    sharedButton.buttonColor      = Color.yellow;
                    listIndex = i;
                }
            }
        }

        private void TriggerDelete(string selected) {
            if (deleteTrigger.val == chooserDefault) return;
            for (int i = 0; i < voiceTriggerList.Count; i++) {
                if (selected == voiceTriggerList[i].Name) {
                    deleting = true;
                    inputTitleText.text  = "Delete Trigger...";
                    inputTitleText.color = Color.red;
                    triggerNameText.val  = voiceTriggerList[i].Name;
                    sharedButton.label   = "Confirm Delete";
                    sharedButton.buttonText.color = Color.white;
                    sharedButton.buttonColor      = Color.red;
                    listIndex = i;
                    inputField.enabled = false;
                }
            }
        }

        private void resetUI() {
            editTrigger.choices   = null; editTrigger.choices   = triggerNameList;
            renameTrigger.choices = null; renameTrigger.choices = triggerNameList;
            deleteTrigger.choices = null; deleteTrigger.choices = triggerNameList;
            renameTrigger.val    = chooserDefault;
            deleteTrigger.val    = chooserDefault;
            editTrigger.val      = chooserDefault;
            inputTitleText.text  = titleDefault;
            inputTitleText.color = Color.green;
            inputField.enabled   = true;
            triggerNameText.val  = "";
            sharedButton.label   = "Add Trigger";
            sharedButton.buttonText.color = Color.black;
            sharedButton.buttonColor      = Color.green;
            deleting = false;
            renaming = false;
        }

        private void updateCommandList() {
            if (commandList.Count > 0) { logMessage("Updating command list."); commandList.Clear(); }
            for (int i = 0; i < voiceTriggerList.Count; i++) {
                JSONClass json = voiceTriggerList[i].GetJSON();
                foreach (JSONNode node in (JSONArray)json["startActions"]) {
                    string cmd = (string)node["name"];
                    if (cmd != null && cmd.Length > 0) {
                        commandList.Add(cmd);
                        logMessage($"Command \"{cmd}\" added from \"{voiceTriggerList[i].Name}\"");
                    }
                }
            }
            _voskBackend?.UpdatePhrases(commandList);
            addOrEdit = false;
        }

        private void logMessage(string msg) {
            if (enableLog.val) SuperController.LogMessage(msg);
        }

        private void HUDMessage(string msg) {
            if (!enableHUD.val) return;
            popupText.text = msg;
            showHUD = true;
        }

        public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false) {
            JSONClass jc = base.GetJSON(includePhysical, includeAppearance, forceStore);
            if (includePhysical || forceStore) {
                needsStore = true;
                JSONArray vta = new JSONArray();
                for (int i = 0; i < voiceTriggerList.Count; i++) {
                    JSONClass vtc = new JSONClass();
                    vtc["Name"] = voiceTriggerList[i].Name;
                    vtc[voiceTriggerList[i].Name] = voiceTriggerList[i].GetJSON(base.subScenePrefix);
                    vta.Add("", vtc);
                }
                jc["VoiceTriggers"] = vta;
            }
            return jc;
        }

        public override void LateRestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true) {
            base.LateRestoreFromJSON(jc, restorePhysical, restoreAppearance, setMissingToDefault);
            if (firstPass) { firstPass = false; return; }
            if (!base.physicalLocked && restorePhysical && !IsCustomPhysicalParamLocked("trigger")) {
                JSONArray vta = jc["VoiceTriggers"].AsArray;
                for (int i = 0; i < vta.Count; i++) {
                    JSONClass vtc = vta[i].AsObject;
                    voiceTrigger = new VoiceTrigger(this, vtc["Name"]);
                    voiceTrigger.RestoreFromJSON(vtc, base.subScenePrefix, base.mergeRestore, setMissingToDefault);
                    voiceTriggerList.Add(voiceTrigger);
                    triggerNameList.Add(voiceTrigger.Name);
                }
                editTrigger.choices   = null; editTrigger.choices   = triggerNameList;
                renameTrigger.choices = null; renameTrigger.choices = triggerNameList;
                deleteTrigger.choices = null; deleteTrigger.choices = triggerNameList;
                updateCommandList();
                string enabled = jc.HasKey("enabled") ? jc["enabled"].Value : null;
                if (enabled != "false") startRecognizer();
            }
        }

        private void initHUD() {
            GameObject g1 = new GameObject(); g1.name = "Canvas";
            popupCanvas = g1.AddComponent<Canvas>();
            popupCanvas.renderMode = RenderMode.WorldSpace;
            CanvasScaler cs = g1.AddComponent<CanvasScaler>();
            cs.scaleFactor = 100f; cs.dynamicPixelsPerUnit = 1f;
            RectTransform rt = g1.GetComponent<RectTransform>();
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 500f);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100f);
            g1.transform.localScale = new Vector3(0.0006f, 0.0006f, 0.0006f);
            g1.transform.localPosition = new Vector3(0f, 0f, .5f);
            rt.SetParent(SuperController.singleton.centerCameraTarget.transform, false);

            GameObject g2 = new GameObject(); g2.name = "popupText";
            g2.transform.parent = g1.transform;
            g2.transform.localScale = Vector3.one;
            g2.transform.localPosition = Vector3.zero;
            g2.transform.localRotation = Quaternion.identity;
            popupText = g2.AddComponent<Text>();
            RectTransform rt2 = g2.GetComponent<RectTransform>();
            rt2.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 500f);
            rt2.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100f);
            popupText.alignment = TextAnchor.MiddleCenter;
            popupText.horizontalOverflow = HorizontalWrapMode.Overflow;
            popupText.verticalOverflow = VerticalWrapMode.Overflow;
            popupText.font = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
            popupText.fontSize = 24;
            popupText.text = popupDefault;
            popupText.enabled = false;
            popupText.color = Color.white;
        }

        public UIDynamicTextField CreateTextInput(JSONStorableString jss, string titleText, bool rightSide = false) {
            var container = new GameObject();
            container.transform.SetParent(transform, false);
            var rect = container.AddComponent<RectTransform>(); rect.pivot = new Vector2(0, 1);
            var layout = container.AddComponent<LayoutElement>(); layout.preferredHeight = 70f; layout.flexibleWidth = 1f;

            var textfield = Instantiate(this.manager.configurableTextFieldPrefab).GetComponent<UIDynamicTextField>();
            textfield.gameObject.transform.SetParent(container.transform, false);
            jss.dynamicText = textfield;
            textfield.backgroundColor = Color.white;
            inputField = textfield.gameObject.AddComponent<InputField>();
            inputField.textComponent = textfield.UItext;
            inputField.characterLimit = 45;
            jss.inputField = inputField;
            Destroy(textfield.GetComponent<LayoutElement>());
            var rect2 = textfield.GetComponent<RectTransform>();
            rect2.anchorMin = new Vector2(0, 1); rect2.anchorMax = new Vector2(1, 1);
            rect2.pivot = new Vector2(0, 1); rect2.anchoredPosition = new Vector2(0, -30f); rect2.sizeDelta = new Vector2(0, 50f);

            var title = new GameObject(); title.transform.SetParent(container.transform, false);
            var rect3 = title.AddComponent<RectTransform>();
            rect3.anchorMin = new Vector2(0, 1); rect3.anchorMax = new Vector2(1, 1);
            rect3.pivot = new Vector2(0, 1); rect3.anchoredPosition = new Vector2(0, 0f); rect3.sizeDelta = new Vector2(0, 30f);
            inputTitleText = title.AddComponent<Text>();
            inputTitleText.font = textfield.UItext.font;
            inputTitleText.fontStyle = FontStyle.Bold;
            inputTitleText.text = titleDefault;
            inputTitleText.fontSize = 24;
            inputTitleText.color = Color.green;

            if (rightSide) rightUIElements.Add(container.transform);
            else           leftUIElements.Add(container.transform);
            return textfield;
        }
    }
}
