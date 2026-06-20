// TriggerSupport.cs — trigger handling support classes for VoiceControl2.
// Part of the VoiceControl2.cslist plugin assembly.
// Credits: MacGruber/Acidbubbles (custom TriggerHandler), Almadiel (original VoiceTrigger)

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using SimpleJSON;
using AssetBundles;

namespace StyleMavin {

    // ── SimpleTriggerHandler ──────────────────────────────────────────────

    public class SimpleTriggerHandler : TriggerHandler {
        public static bool Loaded { get; private set; }
        private static SimpleTriggerHandler myInstance;
        private RectTransform myTriggerActionsPrefab;
        private RectTransform myTriggerActionMiniPrefab;
        private RectTransform myTriggerActionDiscretePrefab;
        private RectTransform myTriggerActionTransitionPrefab;

        public static SimpleTriggerHandler Instance {
            get { if (myInstance == null) myInstance = new SimpleTriggerHandler(); return myInstance; }
        }
        public static void LoadAssets() { SuperController.singleton.StartCoroutine(Instance.LoadAssetsInternal()); }

        private IEnumerator LoadAssetsInternal() {
            foreach (var x in LoadAsset("z_ui2", "TriggerActionsPanel",        p => myTriggerActionsPrefab           = p)) yield return x;
            foreach (var x in LoadAsset("z_ui2", "TriggerActionMiniPanel",     p => myTriggerActionMiniPrefab        = p)) yield return x;
            foreach (var x in LoadAsset("z_ui2", "TriggerActionDiscretePanel", p => myTriggerActionDiscretePrefab    = p)) yield return x;
            foreach (var x in LoadAsset("z_ui2", "TriggerActionTransitionPanel",p=> myTriggerActionTransitionPrefab  = p)) yield return x;
            Loaded = true;
        }

        private IEnumerable LoadAsset(string bundle, string assetName, Action<RectTransform> assign) {
            AssetBundleLoadAssetOperation req = AssetBundleManager.LoadAssetAsync(bundle, assetName, typeof(GameObject));
            if (req == null) throw new NullReferenceException($"Request for {assetName} in {bundle} failed: null request.");
            yield return req;
            GameObject go = req.GetAsset<GameObject>();
            if (go == null) throw new NullReferenceException($"Request for {assetName} in {bundle} failed: null GameObject.");
            RectTransform prefab = go.GetComponent<RectTransform>();
            if (prefab == null) throw new NullReferenceException($"Request for {assetName} in {bundle} failed: null RectTransform.");
            assign(prefab);
        }

        void TriggerHandler.RemoveTrigger(Trigger t) {
            if (t.triggerActionsPanel != null) UnityEngine.Object.Destroy(t.triggerActionsPanel.gameObject);
        }
        void TriggerHandler.DuplicateTrigger(Trigger t) { throw new NotImplementedException(); }
        RectTransform TriggerHandler.CreateTriggerActionsUI() {
            RectTransform clone = UnityEngine.Object.Instantiate(myTriggerActionsPrefab);
            clone.Find("Content/Tab1/Label").GetComponent<Text>().text = "Keyword List";
            clone.Find("Content/Tab1/AddDiscreteStartActionButton/Text").GetComponent<Text>().text = "Add a Keyword";
            clone.Find("Content/Tab2").gameObject.SetActive(false);
            clone.Find("Content/Tab3").gameObject.SetActive(false);
            return clone;
        }
        RectTransform TriggerHandler.CreateTriggerActionMiniUI() {
            RectTransform clone = UnityEngine.Object.Instantiate(myTriggerActionMiniPrefab);
            clone.Find("NameInputField/Placeholder").GetComponent<Text>().text = "Enter Keyword or Phrase...";
            clone.Find("Buttons/Open Button/Text").GetComponent<Text>().text = "Action...";
            clone.Find("Buttons/Duplicate Button").gameObject.SetActive(false);
            return clone;
        }
        RectTransform TriggerHandler.CreateTriggerActionDiscreteUI() {
            RectTransform clone = UnityEngine.Object.Instantiate(myTriggerActionDiscretePrefab);
            clone.Find("NameInputField/Placeholder").GetComponent<Text>().text = "Enter Keyword or Phrase...";
            return clone;
        }
        RectTransform TriggerHandler.CreateTriggerActionTransitionUI() {
            return UnityEngine.Object.Instantiate(myTriggerActionTransitionPrefab);
        }
        void TriggerHandler.RemoveTriggerActionUI(RectTransform rt) { UnityEngine.Object.Destroy(rt?.gameObject); }
    }

    // ── VoiceTrigger ──────────────────────────────────────────────────────

    public class VoiceTrigger : Trigger {
        public string Name {
            get { return name; }
            set { name = value; myNeedInit = true; }
        }
        public MVRScript Owner { get; private set; }
        private string name;
        private bool myNeedInit = true;

        public VoiceTrigger(MVRScript owner, string name) {
            Name = name; Owner = owner; handler = SimpleTriggerHandler.Instance;
        }

        public bool actionsPanelActive() {
            return triggerActionsPanel != null && triggerActionsPanel.gameObject.activeSelf;
        }

        public void OpenPanelActionStart() {
            if (!SimpleTriggerHandler.Loaded) {
                SuperController.LogError("VoiceTrigger: call SimpleTriggerHandler.LoadAssets() first.");
                return;
            }
            triggerActionsParent = Owner.UITransform;
            InitTriggerUI();
            OpenTriggerActionsPanel();
            if (myNeedInit) {
                triggerActionsPanel.Find("Panel/Header Text").GetComponent<Text>().text = Name;
                myNeedInit = false;
            }
        }

        public void RestoreFromJSON(JSONClass jc, string subScenePrefix, bool isMerge, bool setMissingToDefault) {
            if (jc.HasKey(Name)) {
                JSONClass tc = jc[Name].AsObject;
                if (tc != null) base.RestoreFromJSON(tc, subScenePrefix, isMerge);
            }
            else if (setMissingToDefault) {
                base.RestoreFromJSON(new JSONClass());
            }
        }

        public void Trigger() { active = true; active = false; }
    }
}
