// GradientCUADriver v2.0.0
// 7 channels (A..G) driving CUA lights & emissive materials from 1D gradient strip textures.
//
// v2 highlights:
//   * Bundled default gradients (A..G) ship inside the .var and AUTO-LOAD on init — zero setup.
//   * Targets auto-refresh after init (retries while the CUA asset finishes async loading).
//   * Condensed UI: Main (everyday controls) | Gradients (swap strips) | Targeting | Advanced.
//   * Per-channel Enable toggles + global Speed multiplier.
//   * Gradient paths react to changes (scene restore / browse / paste all reload automatically).
//   * Editable text fields (InputField attached) instead of clipboard-paste workarounds.
//   * Keeps v1 storable names — existing scenes restore cleanly.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine.Rendering;
using MVR.FileManagementSecure;

public class GradientCUADriver : MVRScript
{
    const string VERSION = "GradientCUADriver v2.0.0";
    const string DEFAULT_EXCLUDE_EMISSIVE_TOKEN = "[@NOE]";
    const string GRAD_DIR = "Custom/Assets/StyleMavin/Gradients/";

    static readonly string[] DEFAULT_GRADIENTS = new string[]
    {
        "gradient_Crazy_Neon_01_6000x1_CH-A.png",   // A
        "gradient_Blue_Green_01_6000x1_CH-B.png",   // B
        "gradient_Reds_01_6000x1_CH-C.png",         // C
        "gradient_neon_candy01_6000x1_CH-D.png",    // D
        "gradient_neon_candy02_6000x1_CH-E.png",    // E
        "gradient_Blue_01_6000x1_CH-F.png",         // F
        "gradient_neon_6000x1_CH-G.png"             // G
    };

    // Package prefix ("StyleMavin.GradientCUADriver.2:/") or "" when running loose.
    string packagePrefix = "";

    // A/B/C exact lists (legacy targeting, kept for back-compat)
    JSONStorableString exactA, exactB, exactC;

    // Globals
    JSONStorableString versionInfo, statusInfo, rootName;
    JSONStorableBool  affectLights, affectEmissives, affectAmbient, loop, pingPong, useBrightnessForLightIntensity;
    JSONStorableFloat ambientIntensity, ambientChannel;
    JSONStorableString emissionProp;
    JSONStorableFloat speedMultiplier;
    JSONStorableBool useLinearInterpolation;
    JSONStorableFloat temporalSmooth01;

    // Tag-based auto-assign
    JSONStorableBool useTagAutoAssign, requireRoleTag;
    JSONStorableString channelTokenPrefix, roleTokenLight, roleTokenEmissive;

    JSONStorableBool useAttachedAtomAsRoot;
    JSONStorableBool ignoreGOlevelNOEForEmissive;

    MaterialPropertyBlock mpb;
    Dictionary<Material, string> _emissionPropCache = new Dictionary<Material, string>();

    // Scanned caches for pickers
    List<string> scannedRendererNames = new List<string>();
    List<string> scannedMaterialNames = new List<string>();
    List<string> scannedLightNames    = new List<string>();

    class ChannelData
    {
        public string label;
        public JSONStorableBool   enabled;
        public JSONStorableString gradientPath;
        public JSONStorableFloat  durationSeconds, startOffset01, lightIntensity, emissiveIntensity;

        public JSONStorableString exactLightNames; // A/B/C only

        public JSONStorableString includeLightSubstring,  excludeLightSubstring;
        public JSONStorableString includeRendererSubstring, includeMaterialSubstring, excludeRendererSubstring;
        public JSONStorableString pickedRendererIncludes, pickedMaterialIncludes, pickedLightIncludes;

        public Texture2D strip;
        public Color[]   cached;
        public int       width;

        public List<Light>    lights    = new List<Light>();
        public List<Renderer> renderers = new List<Renderer>();

        public Color lastOut = Color.black;
        public bool  hasLast = false;

        public UIDynamicToggle enableToggle; // Main-tab toggle; label updated with gradient name
    }

    ChannelData chA = new ChannelData { label = "A" };
    ChannelData chB = new ChannelData { label = "B" };
    ChannelData chC = new ChannelData { label = "C" };
    ChannelData chD = new ChannelData { label = "D" };
    ChannelData chE = new ChannelData { label = "E" };
    ChannelData chF = new ChannelData { label = "F" };
    ChannelData chG = new ChannelData { label = "G" };
    ChannelData[] channels;

    // Targeting-tab state (single editor bound to the selected channel)
    JSONStorableStringChooser editChannelChooser;
    JSONStorableStringChooser pickerRenderer, pickerMaterial, pickerLight;
    JSONStorableString proxyIncL, proxyExcL, proxyIncR, proxyExcR, proxyIncM;
    JSONStorableString proxyPickedR, proxyPickedM, proxyPickedL;
    bool _syncingProxies;

    // ===== Tab emulation =====
    Dictionary<string,List<UIDynamic>> _tabControls = new Dictionary<string, List<UIDynamic>>();
    List<UIDynamic> _activeTabList = null;

    void BeginTab(string name)
    {
        if (!_tabControls.TryGetValue(name, out _activeTabList))
        {
            _activeTabList = new List<UIDynamic>();
            _tabControls[name] = _activeTabList;
        }
    }
    void EndTab() { _activeTabList = null; }

    T AddToTab<T>(T u) where T : UIDynamic
    {
        if (u != null && _activeTabList != null) _activeTabList.Add(u);
        return u;
    }

    void ShowTab(string name)
    {
        foreach (var kv in _tabControls)
        {
            bool show = kv.Key == name;
            var list = kv.Value;
            for (int i=0;i<list.Count;i++)
            {
                var u = list[i];
                if (u != null && u.gameObject != null) u.gameObject.SetActive(show);
            }
        }
    }

    UIDynamicTextField TF(JSONStorableString js, bool rightSide = false, bool editable = false)
    {
        var tf = AddToTab(CreateTextField(js, rightSide));
        if (editable && tf != null)
        {
            var input = tf.gameObject.AddComponent<UnityEngine.UI.InputField>();
            input.textComponent = tf.UItext;
            input.lineType = UnityEngine.UI.InputField.LineType.MultiLineNewline;
            js.inputField = input;
        }
        return tf;
    }
    UIDynamicSlider SL(JSONStorableFloat jf, string label, bool rightSide = false)
    { var s = AddToTab(CreateSlider(jf, rightSide)); if (s!=null) s.label = label; return s; }
    UIDynamicToggle TG(JSONStorableBool jb, string label, bool rightSide = false)
    { var t = AddToTab(CreateToggle(jb, rightSide)); if (t!=null) t.label = label; return t; }
    UIDynamicPopup PP(JSONStorableStringChooser jc, bool rightSide = false)
    { return AddToTab(CreatePopup(jc, rightSide)); }
    UIDynamicButton BTN(string text, System.Action onClick, bool rightSide = false)
    {
        var b = AddToTab(CreateButton(text, rightSide));
        if (b != null && onClick != null) b.button.onClick.AddListener(() => onClick());
        return b;
    }
    UIDynamicButton LABEL(string text, bool rightSide = false)
    {
        var b = AddToTab(CreateButton(text, rightSide));
        if (b != null) b.button.interactable = false;
        return b;
    }

    public override void Init()
    {
        try
        {
            mpb = new MaterialPropertyBlock();
            channels = new ChannelData[] { chA, chB, chC, chD, chE, chF, chG };

            ResolvePackagePrefix();
            CreateStorables();
            BuildUI();
            ShowTab("Main");

            ScanUnderRoot(true);
            StartCoroutine(AutoSetupCo());
        }
        catch (System.Exception e) { SuperController.LogError("Init error: " + e); }
    }

    // ==== Package path resolution ====
    void ResolvePackagePrefix()
    {
        packagePrefix = "";
        try
        {
            // this.name is like "plugin#0_GradientCUADriver"; manager JSON maps the id to the load path.
            string id = name.Substring(0, name.IndexOf('_'));
            JSONClass pluginsJc = manager.GetJSON(true, true)["plugins"].AsObject;
            if (pluginsJc == null) return;
            string path = pluginsJc[id].Value;
            int idx = path.IndexOf(":/");
            if (idx > 0) packagePrefix = path.Substring(0, idx + 2);
        }
        catch (System.Exception e)
        {
            SuperController.LogMessage("[GradientCUADriver] Package prefix not resolved (running loose?): " + e.Message);
        }
    }

    string DefaultPathFor(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= DEFAULT_GRADIENTS.Length) return null;
        string file = DEFAULT_GRADIENTS[channelIndex];

        string packaged = packagePrefix + GRAD_DIR + file;
        if (!string.IsNullOrEmpty(packagePrefix) && FileManagerSecure.FileExists(packaged)) return packaged;

        string loose = GRAD_DIR + file;
        if (FileManagerSecure.FileExists(loose)) return loose;

        return null;
    }

    // ==== Storables ====
    void CreateStorables()
    {
        versionInfo = new JSONStorableString("Version_INFO", VERSION);
        RegisterString(versionInfo);
        statusInfo = new JSONStorableString("Status_INFO", "Initializing...");

        exactA = new JSONStorableString("ExactNamesA", ""); RegisterString(exactA);
        exactB = new JSONStorableString("ExactNamesB", ""); RegisterString(exactB);
        exactC = new JSONStorableString("ExactNamesC", ""); RegisterString(exactC);
        chA.exactLightNames = exactA; chB.exactLightNames = exactB; chC.exactLightNames = exactC;

        useAttachedAtomAsRoot = new JSONStorableBool("UseAttachedAtomAsRoot", true); RegisterBool(useAttachedAtomAsRoot);
        rootName = new JSONStorableString("RootName_CUARoot_Optional", ""); RegisterString(rootName);

        affectLights    = new JSONStorableBool("AffectLights", true); RegisterBool(affectLights);
        affectEmissives = new JSONStorableBool("AffectEmissives", true); RegisterBool(affectEmissives);
        affectAmbient   = new JSONStorableBool("AffectAmbient", false); RegisterBool(affectAmbient);
        ambientIntensity = new JSONStorableFloat("AmbientIntensity", 1f, 0f, 10f, true); RegisterFloat(ambientIntensity);
        ambientChannel   = new JSONStorableFloat("AmbientFromChannelIndex_(1=A..7=G)", 1f, 1f, 7f, true); RegisterFloat(ambientChannel);

        loop     = new JSONStorableBool("Loop", true); RegisterBool(loop);
        pingPong = new JSONStorableBool("PingPong", false); RegisterBool(pingPong);
        useBrightnessForLightIntensity = new JSONStorableBool("UseBrightnessForLightIntensity", false); RegisterBool(useBrightnessForLightIntensity);
        emissionProp = new JSONStorableString("EmissionPropertyName", "_EmissionColor"); RegisterString(emissionProp);

        speedMultiplier = new JSONStorableFloat("SpeedMultiplier", 1f, 0.05f, 10f, false); RegisterFloat(speedMultiplier);
        useLinearInterpolation = new JSONStorableBool("UseLinearInterpolation", true); RegisterBool(useLinearInterpolation);
        temporalSmooth01 = new JSONStorableFloat("TemporalSmoothness_0to1", 0.5f, 0f, 1f, true); RegisterFloat(temporalSmooth01);

        useTagAutoAssign = new JSONStorableBool("UseTagAutoAssign", true); RegisterBool(useTagAutoAssign);
        requireRoleTag   = new JSONStorableBool("RequireRoleTagForAutoAssign", true); RegisterBool(requireRoleTag);
        channelTokenPrefix = new JSONStorableString("ChannelTokenPrefix", "[@CH="); RegisterString(channelTokenPrefix);
        roleTokenLight     = new JSONStorableString("RoleTokenLight", "[@L]"); RegisterString(roleTokenLight);
        roleTokenEmissive  = new JSONStorableString("RoleTokenEmissive", "[@E]"); RegisterString(roleTokenEmissive);
        ignoreGOlevelNOEForEmissive = new JSONStorableBool("IgnoreGOlevelNOEForEmissive", false); RegisterBool(ignoreGOlevelNOEForEmissive);

        for (int i = 0; i < channels.Length; i++)
        {
            var ch = channels[i];
            string L = ch.label;

            ch.enabled = new JSONStorableBool("Enabled_" + L, true, (bool b) => UpdateStatus());
            RegisterBool(ch.enabled);

            var chLocal = ch;
            ch.gradientPath = new JSONStorableString("GradientPath_" + L, "", (string v) => OnGradientPathChanged(chLocal));
            RegisterString(ch.gradientPath);

            ch.durationSeconds   = new JSONStorableFloat("DurationSeconds_" + L, 600f, 1f, 7200f, true); RegisterFloat(ch.durationSeconds);
            ch.startOffset01     = new JSONStorableFloat("StartOffset01_"   + L, 0f, 0f, 1f, true);      RegisterFloat(ch.startOffset01);
            ch.lightIntensity    = new JSONStorableFloat("LightIntensity_"  + L, 2f, 0f, 50f, true);     RegisterFloat(ch.lightIntensity);
            ch.emissiveIntensity = new JSONStorableFloat("EmissiveIntensity_" + L, 2f, 0f, 50f, true);   RegisterFloat(ch.emissiveIntensity);

            ch.includeLightSubstring    = new JSONStorableString("IncludeLightsWithName_" + L, "");    RegisterString(ch.includeLightSubstring);
            ch.excludeLightSubstring    = new JSONStorableString("ExcludeLightsWithName_" + L, "");    RegisterString(ch.excludeLightSubstring);
            ch.includeRendererSubstring = new JSONStorableString("IncludeRenderersWithName_" + L, ""); RegisterString(ch.includeRendererSubstring);
            ch.excludeRendererSubstring = new JSONStorableString("ExcludeRenderersWithName_" + L, ""); RegisterString(ch.excludeRendererSubstring);
            ch.includeMaterialSubstring = new JSONStorableString("IncludeMaterialsWithName_" + L, ""); RegisterString(ch.includeMaterialSubstring);
            ch.pickedRendererIncludes   = new JSONStorableString("PickedRendererIncludes_" + L, "");   RegisterString(ch.pickedRendererIncludes);
            ch.pickedMaterialIncludes   = new JSONStorableString("PickedMaterialIncludes_" + L, "");   RegisterString(ch.pickedMaterialIncludes);
            ch.pickedLightIncludes      = new JSONStorableString("PickedLightIncludes_" + L, "");      RegisterString(ch.pickedLightIncludes);
        }
    }

    void OnGradientPathChanged(ChannelData ch)
    {
        string p = ch.gradientPath.val;
        if (string.IsNullOrEmpty(p))
        {
            ch.strip = null; ch.cached = null; ch.width = 0; ch.hasLast = false;
            UpdateChannelToggleLabel(ch);
            UpdateStatus();
            return;
        }
        string norm = NormalizeMediaPathForVaM(p);
        if (norm != p) { ch.gradientPath.valNoCallback = norm; }
        LoadStrip(ch);
    }

    // ==== Auto setup: load bundled defaults, then keep refreshing targets until the CUA is up ====
    IEnumerator AutoSetupCo()
    {
        yield return null; // let scene restore land first

        int assigned = 0;
        for (int i = 0; i < channels.Length; i++)
        {
            var ch = channels[i];
            if (!string.IsNullOrEmpty(ch.gradientPath.val)) continue; // scene restore already set one
            string def = DefaultPathFor(i);
            if (def != null) { ch.gradientPath.val = def; assigned++; } // callback loads it
        }
        if (assigned > 0)
            SuperController.LogMessage("[GradientCUADriver] Auto-loaded " + assigned + " bundled gradients.");

        // CUA assets load asynchronously; retry until targets appear (or give up quietly).
        for (int tries = 0; tries < 30; tries++)
        {
            RefreshTargets(true);
            if (AnyTargets())
            {
                ScanUnderRoot(true);
                UpdateStatus();
                yield break;
            }
            UpdateStatus();
            yield return new WaitForSeconds(1f);
        }
        UpdateStatus();
    }

    bool AnyTargets()
    {
        for (int i=0;i<channels.Length;i++)
            if (channels[i].lights.Count > 0 || channels[i].renderers.Count > 0) return true;
        return false;
    }

    // ==== UI ====
    void BuildUI()
    {
        // Tab bar (left column, always visible)
        var hdr = CreateButton(VERSION); if (hdr!=null) hdr.button.interactable = false;
        CreateButton("Main").button.onClick.AddListener(()=> ShowTab("Main"));
        CreateButton("Gradients").button.onClick.AddListener(()=> ShowTab("Gradients"));
        CreateButton("Targeting").button.onClick.AddListener(()=> { SyncProxiesFromChannel(); ShowTab("Targeting"); });
        CreateButton("Advanced").button.onClick.AddListener(()=> ShowTab("Advanced"));

        BuildMainTab();
        BuildGradientsTab();
        BuildTargetingTab();
        BuildAdvancedTab();
    }

    void BuildMainTab()
    {
        BeginTab("Main");

        var stf = TF(statusInfo); if (stf!=null) stf.height = 220f;

        BTN("Reload Bundled Gradients (A..G)", () => LoadBundledDefaults(true));
        BTN("Refresh Targets", () => { ScanUnderRoot(true); RefreshTargets(false); UpdateStatus(); });

        LABEL("Channels", true);
        for (int i = 0; i < channels.Length; i++)
        {
            var ch = channels[i];
            ch.enableToggle = TG(ch.enabled, "Channel " + ch.label, true);
            UpdateChannelToggleLabel(ch);
        }

        SL(speedMultiplier, "Speed (x all channels)");
        SL(temporalSmooth01, "Color Smoothing");
        TG(useLinearInterpolation, "Smooth Pixel Blending");
        TG(loop, "Loop");
        TG(pingPong, "Ping-Pong");

        TG(affectLights, "Drive Lights", true);
        TG(affectEmissives, "Drive Emissive Materials", true);
        TG(useBrightnessForLightIntensity, "Brightness Drives Intensity", true);

        EndTab();
    }

    void BuildGradientsTab()
    {
        BeginTab("Gradients");

        LABEL("Swap gradient strips per channel. Defaults are bundled; Browse to use your own PNG (wide 1px-tall strip).");
        BTN("Browse Gradient Folder (auto-assign A..G by filename)", () => BrowseGradientFolderAuto());
        BTN("Reset ALL to Bundled Defaults", () => LoadBundledDefaults(true));

        for (int i = 0; i < channels.Length; i++)
        {
            var ch = channels[i];
            int idx = i;
            bool right = false;

            LABEL("— Channel " + ch.label + " —", right);
            var tf = TF(ch.gradientPath, right); if (tf != null) tf.height = 38f;
            BTN("Browse... (" + ch.label + ")", () => BrowseGradientForChannel(ch), right);
            BTN("Reset " + ch.label + " to Default", () => {
                string def = DefaultPathFor(idx);
                if (def != null) ch.gradientPath.val = def;
                else SuperController.LogError("[GradientCUADriver] Bundled default for " + ch.label + " not found.");
            }, right);
            SL(ch.durationSeconds, "Duration sec (" + ch.label + ")", true);
            SL(ch.lightIntensity, "Light Intensity (" + ch.label + ")", true);
            SL(ch.emissiveIntensity, "Emissive Intensity (" + ch.label + ")", true);
            SL(ch.startOffset01, "Start Offset (" + ch.label + ")", true);
        }

        EndTab();
    }

    void BuildTargetingTab()
    {
        BeginTab("Targeting");

        LABEL("Auto-assign uses name tags on the CUA ([@CH=A], [@L], [@E]). Manual rules below apply to the selected channel.");
        TG(useTagAutoAssign, "Auto-Assign by Tags");
        TG(requireRoleTag, "Require Role Tag ([@L]/[@E])");

        editChannelChooser = new JSONStorableStringChooser("EditChannel",
            new List<string> { "A","B","C","D","E","F","G" }, "A", "Edit Channel",
            (string v) => SyncProxiesFromChannel());
        PP(editChannelChooser);

        proxyIncL = MakeProxy("Include Lights (substring)",   ch => ch.includeLightSubstring);
        proxyExcL = MakeProxy("Exclude Lights (substring)",   ch => ch.excludeLightSubstring);
        proxyIncR = MakeProxy("Include Renderers (substring)",ch => ch.includeRendererSubstring);
        proxyExcR = MakeProxy("Exclude Renderers (substring)",ch => ch.excludeRendererSubstring);
        proxyIncM = MakeProxy("Include Materials (substring)",ch => ch.includeMaterialSubstring);

        BTN("Apply Rules → Refresh Targets", () => { RefreshTargets(false); UpdateStatus(); });

        // Right side: pickers
        LABEL("Pick scene objects and add them to the selected channel:", true);
        BTN("Rescan Scene Objects", () => ScanUnderRoot(false), true);

        pickerLight = new JSONStorableStringChooser("LightPicker", new List<string>(), "", "Lights");
        PP(pickerLight, true);
        BTN("Add Light → Channel", () => AddPicked(pickerLight, EditCh().pickedLightIncludes, "light"), true);

        pickerRenderer = new JSONStorableStringChooser("RendererPicker", new List<string>(), "", "Renderers");
        PP(pickerRenderer, true);
        BTN("Add Renderer → Channel", () => AddPicked(pickerRenderer, EditCh().pickedRendererIncludes, "renderer"), true);

        pickerMaterial = new JSONStorableStringChooser("MaterialPicker", new List<string>(), "", "Materials");
        PP(pickerMaterial, true);
        BTN("Add Material → Channel", () => AddPicked(pickerMaterial, EditCh().pickedMaterialIncludes, "material"), true);

        proxyPickedL = MakeProxy("Picked Lights", ch => ch.pickedLightIncludes, true);
        proxyPickedR = MakeProxy("Picked Renderers", ch => ch.pickedRendererIncludes, true);
        proxyPickedM = MakeProxy("Picked Materials", ch => ch.pickedMaterialIncludes, true);

        BTN("Clear Picked (this channel)", () => {
            var ch = EditCh();
            ch.pickedRendererIncludes.val = ""; ch.pickedMaterialIncludes.val = ""; ch.pickedLightIncludes.val = "";
            SyncProxiesFromChannel();
            RefreshTargets(false); UpdateStatus();
        }, true);

        BTN("Log Channel Membership", () => LogMembership(), true);

        EndTab();
    }

    void BuildAdvancedTab()
    {
        BeginTab("Advanced");

        TG(useAttachedAtomAsRoot, "Use Attached Atom As Root");
        LABEL("RootName (optional CUA root GameObject)");
        TF(rootName, false, true);
        BTN("Set RootName = This Atom", () => {
            if (containingAtom != null) rootName.val = containingAtom.uid;
        });

        LABEL("Emission Property Name");
        TF(emissionProp, false, true);
        TG(ignoreGOlevelNOEForEmissive, "Ignore GO-level [@NOE] for Emissive");

        LABEL("Channel Token Prefix");
        TF(channelTokenPrefix, false, true);
        LABEL("Role Token: Lights");
        TF(roleTokenLight, false, true);
        LABEL("Role Token: Emissives");
        TF(roleTokenEmissive, false, true);

        TG(affectAmbient, "Drive Ambient Light", true);
        SL(ambientIntensity, "Ambient Intensity", true);
        SL(ambientChannel, "Ambient From Channel (1=A..7=G)", true);

        LABEL("Exact light names for A / B / C (comma/semicolon/newline):", true);
        TF(exactA, true, true);
        TF(exactB, true, true);
        TF(exactC, true, true);
        BTN("Paste Clipboard → ExactNames A,B,C", () => {
            string clip = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clip)) { SuperController.LogError("Clipboard empty."); return; }
            string A, B, C;
            if (TrySplitABC(clip, out A, out B, out C)) { exactA.val = A; exactB.val = B; exactC.val = C; }
            else { exactA.val = clip; exactB.val = clip; exactC.val = clip; }
        }, true);

        BTN("Normalize All Gradient Paths (for packaging)", () => NormalizeAllGradientPaths(), true);
        BTN("List All Light Names (Scene)", () => LogAllLightNamesScene(), true);
        BTN("List All Renderer Names", () => LogAllRendererNames(), true);

        EndTab();
    }

    // Proxy fields: one editable UI field re-bound to the selected channel's storable.
    delegate JSONStorableString ChannelField(ChannelData ch);
    Dictionary<JSONStorableString, ChannelField> _proxyMap = new Dictionary<JSONStorableString, ChannelField>();

    JSONStorableString MakeProxy(string label, ChannelField field, bool rightSide = false)
    {
        var proxy = new JSONStorableString("proxy_" + label, "");
        _proxyMap[proxy] = field;
        proxy.setCallbackFunction = (string v) => {
            if (_syncingProxies) return;
            field(EditCh()).val = v;
        };
        LABEL(label, rightSide);
        var tf = TF(proxy, rightSide, true); if (tf != null) tf.height = 38f;
        return proxy;
    }

    ChannelData EditCh()
    {
        string v = editChannelChooser != null ? editChannelChooser.val : "A";
        for (int i=0;i<channels.Length;i++) if (channels[i].label == v) return channels[i];
        return chA;
    }

    void SyncProxiesFromChannel()
    {
        _syncingProxies = true;
        try
        {
            var ch = EditCh();
            foreach (var kv in _proxyMap)
                kv.Key.valNoCallback = kv.Value(ch).val ?? "";
        }
        finally { _syncingProxies = false; }
    }

    void AddPicked(JSONStorableStringChooser picker, JSONStorableString store, string kind)
    {
        string sel = picker != null ? picker.val : null;
        if (string.IsNullOrEmpty(sel)) { SuperController.LogError("Pick a " + kind + " first."); return; }
        AppendUnique(store, sel);
        SyncProxiesFromChannel();
        RefreshTargets(false);
        UpdateStatus();
    }

    // ==== Status / labels ====
    void UpdateChannelToggleLabel(ChannelData ch)
    {
        if (ch.enableToggle == null) return;
        string nm = FileNameOnly(ch.gradientPath != null ? ch.gradientPath.val : "");
        ch.enableToggle.label = "Ch " + ch.label + (string.IsNullOrEmpty(nm) ? "  (no gradient)" : "  " + PrettyGradientName(nm));
    }

    string FileNameOnly(string p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        int i = p.LastIndexOf('/');
        return i >= 0 ? p.Substring(i+1) : p;
    }

    string PrettyGradientName(string file)
    {
        string s = file;
        if (s.EndsWith(".png")) s = s.Substring(0, s.Length - 4);
        s = s.Replace("gradient_", "");
        int idx = s.IndexOf("_6000x1"); if (idx > 0) s = s.Substring(0, idx);
        return s.Replace('_', ' ');
    }

    void UpdateStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(VERSION);
        sb.AppendLine(string.IsNullOrEmpty(packagePrefix) ? "(running loose)" : "Package: " + packagePrefix.TrimEnd('/',':'));
        sb.AppendLine();
        for (int i=0;i<channels.Length;i++)
        {
            var ch = channels[i];
            string nm = FileNameOnly(ch.gradientPath.val);
            sb.Append(ch.enabled.val ? "[on]  " : "[off] ");
            sb.Append("Ch ").Append(ch.label).Append(": ");
            sb.Append(string.IsNullOrEmpty(nm) ? "no gradient" : PrettyGradientName(nm));
            sb.Append("  L=").Append(ch.lights.Count).Append(" R=").Append(ch.renderers.Count);
            sb.AppendLine();
        }
        if (!AnyTargets()) sb.AppendLine("\nNo targets yet — waiting for CUA asset to load...");
        statusInfo.val = sb.ToString();
        for (int i=0;i<channels.Length;i++) UpdateChannelToggleLabel(channels[i]);
    }

    void LoadBundledDefaults(bool force)
    {
        int n = 0;
        for (int i = 0; i < channels.Length; i++)
        {
            var ch = channels[i];
            if (!force && !string.IsNullOrEmpty(ch.gradientPath.val)) continue;
            string def = DefaultPathFor(i);
            if (def != null)
            {
                if (ch.gradientPath.val == def) LoadStrip(ch); else ch.gradientPath.val = def;
                n++;
            }
        }
        if (n == 0) SuperController.LogError("[GradientCUADriver] No bundled gradients found (package prefix: '" + packagePrefix + "').");
        else SuperController.LogMessage("[GradientCUADriver] Loaded " + n + " bundled gradients.");
    }

    // === Path normalizer for packaging ===
    string NormalizeMediaPathForVaM(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        string p = raw.Replace("\\","/");

        if (p.StartsWith("Custom/") ||
            p.StartsWith("Saves/") ||
            p.StartsWith("AddonPackages/") ||
            p.StartsWith("SELF:/") ||
            p.StartsWith("local:/") ||
            p.Contains(":/"))
            return p;

        int idx = p.IndexOf("/Custom/");
        if (idx >= 0 && idx+1 < p.Length) return p.Substring(idx+1);
        idx = p.IndexOf("/Saves/");
        if (idx >= 0 && idx+1 < p.Length) return p.Substring(idx+1);
        return p;
    }

    void NormalizeAllGradientPaths()
    {
        for (int i = 0; i < channels.Length; i++)
        {
            var ch = channels[i];
            if (ch.gradientPath == null) continue;
            string before = ch.gradientPath.val;
            string after  = NormalizeMediaPathForVaM(before);
            if (!string.IsNullOrEmpty(before) && before != after)
            {
                ch.gradientPath.val = after;
                SuperController.LogMessage("[GradientCUADriver] Normalized " + ch.label + ": " + before + " → " + after);
            }
        }
    }

    // === Folder browser + auto A..G loader ===
    void BrowseGradientFolderAuto()
    {
        try
        {
            SuperController.singleton.GetMediaPathDialog(
                (string path) =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    string norm = NormalizeMediaPathForVaM(path);
                    string folder = GetParentFolder(norm);
                    if (string.IsNullOrEmpty(folder))
                    {
                        SuperController.LogError("[GradientCUADriver] Could not determine folder from: " + norm);
                        return;
                    }
                    AutoLoadFromFolder(folder);
                },
                "png"
            );
        }
        catch (System.Exception e) { SuperController.LogError("BrowseGradientFolderAuto error: " + e); }
    }

    string GetParentFolder(string p)
    {
        if (string.IsNullOrEmpty(p)) return null;
        int idx = p.LastIndexOf('/');
        if (idx <= 0) return null;
        return p.Substring(0, idx);
    }

    bool AutoLoadFromFolder(string folder)
    {
        SuperController.LogMessage("[GradientCUADriver] Auto-loading gradients from folder: " + folder);
        int loaded = 0;
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelData ch = channels[i];
            char letter = ch.label[0];
            string best = FindBestGradientForChannel(folder, letter);
            if (!string.IsNullOrEmpty(best))
            {
                ch.gradientPath.val = NormalizeMediaPathForVaM(best);
                loaded++;
            }
        }
        if (loaded == 0)
        {
            SuperController.LogError("[GradientCUADriver] No gradients matched in '" + folder + "'. Expected *CH-A*.png style names.");
            return false;
        }
        SuperController.LogMessage("[GradientCUADriver] Auto Loader: " + loaded + " / " + channels.Length + " channels assigned.");
        return true;
    }

    string FindBestGradientForChannel(string folder, char letter)
    {
        string L = letter.ToString();
        string[] patterns = new string[] { "*CH-" + L + "*.png", "*CH_" + L + "*.png", "*_" + L + "*.png" };
        string best = null;
        for (int i = 0; i < patterns.Length; i++)
        {
            string[] files = null;
            try { files = FileManagerSecure.GetFiles(folder, patterns[i]); } catch { files = null; }
            if (files == null) continue;
            for (int f = 0; f < files.Length; f++)
            {
                string p = files[f];
                if (string.IsNullOrEmpty(p)) continue;
                if (best == null || string.CompareOrdinal(p, best) > 0) best = p;
            }
        }
        return best;
    }

    void BrowseGradientForChannel(ChannelData ch)
    {
        try
        {
            SuperController.singleton.GetMediaPathDialog(
                (string path) =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    ch.gradientPath.val = NormalizeMediaPathForVaM(path);
                },
                "png"
            );
        }
        catch (System.Exception e) { SuperController.LogError("BrowseGradientForChannel error: " + e); }
    }

    void AppendUnique(JSONStorableString store, string token)
    {
        string cur = store.val ?? "";
        var set = ParseExactSet(cur);
        if (!set.Contains(token)) set.Add(token);
        store.val = string.Join(";", new List<string>(set).ToArray());
    }

    // ==== Scan ====
    void ScanUnderRoot(bool quiet)
    {
        scannedRendererNames.Clear();
        scannedMaterialNames.Clear();
        scannedLightNames.Clear();

        Renderer[] rends = null; Light[] lights = null;

        if (useAttachedAtomAsRoot != null && useAttachedAtomAsRoot.val && containingAtom != null)
        {
            Transform t = containingAtom.transform;
            if (t == null) return;
            rends  = t.GetComponentsInChildren<Renderer>(true);
            lights = t.GetComponentsInChildren<Light>(true);
        }
        else
        {
            string root = rootName != null ? rootName.val : "";
            if (!string.IsNullOrEmpty(root))
            {
                GameObject go = GameObject.Find(root);
                if (go == null) { if (!quiet) SuperController.LogError("Root not found: " + root); return; }
                rends  = go.GetComponentsInChildren<Renderer>(true);
                lights = go.GetComponentsInChildren<Light>(true);
            }
            else
            {
                rends  = UnityEngine.Object.FindObjectsOfType<Renderer>();
                lights = UnityEngine.Object.FindObjectsOfType<Light>();
            }
        }

        var rset = new HashSet<string>();
        var mset = new HashSet<string>();
        var lset = new HashSet<string>();

        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i]; if (!r) continue;
            rset.Add(r.gameObject.name);
            var mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++) if (mats[m] != null) mset.Add(mats[m].name);
        }
        for (int i = 0; i < lights.Length; i++) if (lights[i]) lset.Add(lights[i].gameObject.name);

        scannedRendererNames.AddRange(rset); scannedRendererNames.Sort();
        scannedMaterialNames.AddRange(mset); scannedMaterialNames.Sort();
        scannedLightNames.AddRange(lset);    scannedLightNames.Sort();

        SetChooserList(pickerRenderer, scannedRendererNames);
        SetChooserList(pickerMaterial, scannedMaterialNames);
        SetChooserList(pickerLight, scannedLightNames);

        if (!quiet)
            SuperController.LogMessage("Scan: Renderers="+scannedRendererNames.Count+" Materials="+scannedMaterialNames.Count+" Lights="+scannedLightNames.Count);
    }

    void SetChooserList(JSONStorableStringChooser chooser, List<string> items)
    {
        if (chooser == null) return;
        chooser.choices = new List<string>(items);
        chooser.displayChoices = new List<string>(items);
        chooser.valNoCallback = items.Count > 0 ? items[0] : "";
    }

    // ==== Gradient loading ====
    void LoadStrip(ChannelData ch) { StartCoroutine(LoadStripSecureCo(ch)); }

    IEnumerator LoadStripSecureCo(ChannelData ch)
    {
        string p = ch.gradientPath != null ? ch.gradientPath.val : null;
        if (string.IsNullOrEmpty(p)) yield break;

        byte[] data = null;
        try
        {
            if (FileManagerSecure.FileExists(p)) data = FileManagerSecure.ReadAllBytes(p);
            else { SuperController.LogError("Gradient file not found: " + p); yield break; }
        }
        catch (System.Exception e)
        {
            SuperController.LogError("ReadAllBytes failed for " + p + " : " + e);
            yield break;
        }

        yield return null;
        if (data == null || data.Length == 0) { SuperController.LogError("Empty gradient file: " + p); yield break; }

        Texture2D tex = new Texture2D(2,2,TextureFormat.RGBA32,false,false);
        if (!tex.LoadImage(data)) { SuperController.LogError("Failed to decode image: " + p); yield break; }

        ch.strip = tex; ch.width = tex.width;
        try { ch.cached = tex.GetPixels(0,0,ch.width,1); } catch { ch.cached = null; }
        ch.hasLast = false;
        UpdateChannelToggleLabel(ch);
        UpdateStatus();
    }

    // ==== Targeting ====
    void RefreshTargets(bool quiet)
    {
        try
        {
            ClearTargets();

            Light[] allLights = null; Renderer[] allRends = null;

            if (useAttachedAtomAsRoot != null && useAttachedAtomAsRoot.val && containingAtom != null)
            {
                Transform t = containingAtom.transform;
                if (t == null) return;
                allLights = t.GetComponentsInChildren<Light>(true);
                allRends  = t.GetComponentsInChildren<Renderer>(true);
            }
            else
            {
                string root = rootName != null ? rootName.val : "";
                bool anyExact = AnyChannelHasExactList();

                if (!string.IsNullOrEmpty(root))
                {
                    GameObject go = GameObject.Find(root);
                    if (go == null) { if (!quiet) SuperController.LogError("Root not found: " + root); return; }
                    allLights = go.GetComponentsInChildren<Light>(true);
                    allRends  = go.GetComponentsInChildren<Renderer>(true);
                }
                else if (anyExact)
                {
                    allLights = UnityEngine.Object.FindObjectsOfType<Light>();
                    allRends  = UnityEngine.Object.FindObjectsOfType<Renderer>();
                }
                else
                {
                    if (!quiet) SuperController.LogError("Enable 'Use Attached Atom As Root' OR set RootName OR use ExactNames/Picks.");
                    return;
                }
            }

            AssignLightsToChannels(allLights);
            AssignRenderersToChannels(allRends);

            if (!quiet)
                SuperController.LogMessage(
                    "Targets →  A: L=" + chA.lights.Count + " R=" + chA.renderers.Count +
                    " | B: L=" + chB.lights.Count + " R=" + chB.renderers.Count +
                    " | C: L=" + chC.lights.Count + " R=" + chC.renderers.Count +
                    " | D: L=" + chD.lights.Count + " R=" + chD.renderers.Count +
                    " | E: L=" + chE.lights.Count + " R=" + chE.renderers.Count +
                    " | F: L=" + chF.lights.Count + " R=" + chF.renderers.Count +
                    " | G: L=" + chG.lights.Count + " R=" + chG.renderers.Count);
        }
        catch (System.Exception e) { SuperController.LogError("RefreshTargets error: " + e); }
    }

    bool AnyChannelHasExactList()
    {
        return ParseNameList(exactA.val).Count > 0
            || ParseNameList(exactB.val).Count > 0
            || ParseNameList(exactC.val).Count > 0;
    }

    void ClearTargets() { for (int i=0;i<channels.Length;i++) { channels[i].lights.Clear(); channels[i].renderers.Clear(); } }

    void AssignLightsToChannels(Light[] all)
    {
        if (all == null) return;
        AssignLightsForChannel(chA, all, exactA.val);
        AssignLightsForChannel(chB, all, exactB.val);
        AssignLightsForChannel(chC, all, exactC.val);
        AssignLightsForChannel(chD, all, "");
        AssignLightsForChannel(chE, all, "");
        AssignLightsForChannel(chF, all, "");
        AssignLightsForChannel(chG, all, "");
    }

    void AssignLightsForChannel(ChannelData ch, Light[] all, string exactRaw)
    {
        var exact  = ParseNameList(exactRaw);
        var picked = ParseExactSet(ch.pickedLightIncludes != null ? ch.pickedLightIncludes.val : "");
        string inc = ch.includeLightSubstring != null ? ch.includeLightSubstring.val : "";
        string exc = ch.excludeLightSubstring != null ? ch.excludeLightSubstring.val : "";

        bool hasCriteria = exact.Count>0 || picked.Count>0 || !string.IsNullOrEmpty(inc);
        bool doAuto      = (useTagAutoAssign != null && useTagAutoAssign.val);

        string chPrefix  = (channelTokenPrefix != null ? channelTokenPrefix.val : "[@CH=");
        string lToken    = (roleTokenLight != null ? roleTokenLight.val : "[@L]");
        char chLetter    = ch.label.ToUpper()[0];
        bool needRole    = (requireRoleTag != null && requireRoleTag.val);

        if (!hasCriteria && !doAuto) return;

        for (int j=0;j<all.Length;j++)
        {
            var lt = all[j]; if (!lt) continue;
            string nm = lt.gameObject.name;
            bool add = false;

            if (doAuto)
            {
                char found = ParseChannelFromName(nm, chPrefix);
                if (found == chLetter)
                {
                    if (!needRole || NameContainsTokenCI(nm, lToken)) add = true;
                }
            }

            if (!add && hasCriteria)
            {
                if (exact.Count > 0) { if (exact.Contains(nm.ToLower())) add = true; }
                else if (picked.Count > 0) { if (picked.Contains(nm)) add = true; }
                else if (!string.IsNullOrEmpty(inc))
                {
                    if (SubstringCI(nm, inc) && (string.IsNullOrEmpty(exc) || !SubstringCI(nm, exc))) add = true;
                }
            }

            if (add && !ListHasLight(ch.lights, lt))
                ch.lights.Add(lt);
        }
    }

    void AssignRenderersToChannels(Renderer[] all)
    {
        if (all == null) return;

        bool doAuto   = (useTagAutoAssign != null && useTagAutoAssign.val);
        string chPref = (channelTokenPrefix != null ? channelTokenPrefix.val : "[@CH=");
        string eTok   = (roleTokenEmissive != null ? roleTokenEmissive.val : "[@E]");
        bool needRole = (requireRoleTag != null && requireRoleTag.val);

        for (int i=0;i<channels.Length;i++)
        {
            ChannelData ch = channels[i];
            char chLetter  = ch.label.ToUpper()[0];

            var pickedR = ParseExactSet(ch.pickedRendererIncludes != null ? ch.pickedRendererIncludes.val : "");
            var pickedM = ParseExactSet(ch.pickedMaterialIncludes != null ? ch.pickedMaterialIncludes.val : "");

            string incR = ch.includeRendererSubstring != null ? ch.includeRendererSubstring.val : "";
            string excR = ch.excludeRendererSubstring != null ? ch.excludeRendererSubstring.val : "";
            string incM = ch.includeMaterialSubstring != null ? ch.includeMaterialSubstring.val : "";

            bool hasRendererCriteria = pickedR.Count>0 || !string.IsNullOrEmpty(incR);

            for (int j=0;j<all.Length;j++)
            {
                Renderer r = all[j]; if (!r) continue;
                string rname = r.gameObject.name;
                bool add = false;

                if (doAuto)
                {
                    char onGO = ParseChannelFromName(rname, chPref);
                    bool onMat = false;
                    var mats = r.sharedMaterials;
                    for (int m=0;m<mats.Length;m++)
                    {
                        var mat = mats[m];
                        if (mat != null && ParseChannelFromName(mat.name, chPref) == chLetter) { onMat = true; break; }
                    }

                    if (onGO == chLetter || onMat)
                    {
                        if (!needRole || RendererOrMaterialsHaveToken(r, eTok))
                            add = true;
                    }
                }

                if (!add && hasRendererCriteria)
                {
                    bool passRenderer = false;
                    if (pickedR.Count>0) passRenderer = pickedR.Contains(rname);
                    else passRenderer = StringMatch(rname, incR, excR);

                    if (passRenderer)
                    {
                        bool passMaterial = true;
                        if (pickedM.Count>0 || !string.IsNullOrEmpty(incM))
                        {
                            passMaterial = false;
                            var mats2 = r.sharedMaterials;
                            for (int m=0;m<mats2.Length;m++)
                            {
                                var mat2 = mats2[m]; if (mat2 == null) continue;
                                string mn = mat2.name;
                                if ((pickedM.Count>0 && pickedM.Contains(mn)) || (!string.IsNullOrEmpty(incM) && SubstringCI(mn, incM)))
                                { passMaterial = true; break; }
                            }
                        }
                        if (passMaterial) add = true;
                    }
                }

                if (add && !ListHasRenderer(ch.renderers, r))
                    ch.renderers.Add(r);
            }
        }
    }

    // ==== Runtime ====
    public void Update()
    {
        try
        {
            DriveChannel(chA); DriveChannel(chB); DriveChannel(chC);
            DriveChannel(chD); DriveChannel(chE); DriveChannel(chF); DriveChannel(chG);

            if (affectAmbient != null && affectAmbient.val)
            {
                int idx = Mathf.Clamp(Mathf.RoundToInt(ambientChannel.val) - 1, 0, 6);
                ChannelData src = channels[idx];

                Color cAmb;
                if (src.hasLast) cAmb = src.lastOut;
                else
                {
                    cAmb = SampleColor(src);
                    src.lastOut = cAmb; src.hasLast = true;
                }

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = cAmb * ambientIntensity.val;
            }
        }
        catch (System.Exception e) { if (Time.frameCount % 60 == 0) SuperController.LogError("Update error: " + e); }
    }

    void DriveChannel(ChannelData ch)
    {
        if (ch.enabled != null && !ch.enabled.val) return;
        if (ch.cached == null || ch.width <= 0) return;

        Color cRaw = SampleColor(ch);

        float s = (temporalSmooth01 != null) ? Mathf.Clamp01(temporalSmooth01.val) : 0f;
        if (!ch.hasLast) { ch.lastOut = cRaw; ch.hasLast = true; }
        float alpha = 1f - Mathf.Pow(1f - s, Time.deltaTime * 60f);
        Color c = Color.Lerp(ch.lastOut, cRaw, alpha);
        ch.lastOut = c;

        if (affectLights != null && affectLights.val && ch.lights.Count > 0)
        {
            float baseInt = ch.lightIntensity.val;
            bool mod = (useBrightnessForLightIntensity != null && useBrightnessForLightIntensity.val);
            float inten = mod ? baseInt * Mathf.Lerp(0.2f, 1f, Mathf.Max(c.r, Mathf.Max(c.g, c.b))) : baseInt;

            for (int i=0;i<ch.lights.Count;i++)
            {
                var lt = ch.lights[i]; if (!lt) continue;
                lt.color = new Color(c.r, c.g, c.b, 1f);
                lt.intensity = inten;
            }
        }

        if (affectEmissives != null && affectEmissives.val && ch.renderers.Count > 0)
        {
            string userProp = emissionProp != null ? emissionProp.val : "_EmissionColor";
            Color emission = c * ch.emissiveIntensity.val;

            bool needRole = (requireRoleTag != null && requireRoleTag.val);
            string chPref = (channelTokenPrefix != null ? channelTokenPrefix.val : "[@CH=");
            string eTok   = (roleTokenEmissive != null ? roleTokenEmissive.val : "[@E]");
            char chLetter = ch.label.ToUpper()[0];
            bool ignoreGOnoe = (ignoreGOlevelNOEForEmissive != null && ignoreGOlevelNOEForEmissive.val);

            for (int i=0;i<ch.renderers.Count;i++)
            {
                var r = ch.renderers[i]; if (!r) continue;

                var mats = r.materials;
                for (int m=0;m<mats.Length;m++)
                {
                    var mat = mats[m]; if (mat == null) continue;

                    bool drive = ShouldDriveEmissive(r, mat, chLetter, chPref, eTok, needRole, ignoreGOnoe);
                    if (drive)
                    {
                        string prop = ResolveEmissionProperty(mat, userProp);

                        try
                        {
                            r.GetPropertyBlock(mpb, m);
                            mpb.SetColor(prop, emission);
                            r.SetPropertyBlock(mpb, m);
                        }
                        catch
                        {
                            try { mat.SetColor(prop, emission); } catch {}
                        }

                        try
                        {
                            mat.EnableKeyword("_EMISSION");
                            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        }
                        catch {}
                    }
                    else
                    {
                        try { r.SetPropertyBlock(null, m); } catch {}
                        bool goNOE = NameContainsTokenCI(r.gameObject.name, DEFAULT_EXCLUDE_EMISSIVE_TOKEN);
                        bool matNOE = NameContainsTokenCI(mat.name, DEFAULT_EXCLUDE_EMISSIVE_TOKEN);
                        if ((matNOE || (goNOE && !ignoreGOnoe)))
                        {
                            try { mat.DisableKeyword("_EMISSION"); } catch {}
                        }
                    }
                }
            }
        }
    }

    string ResolveEmissionProperty(Material mat, string userDefault)
    {
        string prop = null;
        if (_emissionPropCache.TryGetValue(mat, out prop)) return prop;

        if (!string.IsNullOrEmpty(userDefault) && mat.HasProperty(userDefault))
        {
            _emissionPropCache[mat] = userDefault; return userDefault;
        }

        string[] candidates = new string[] { "_EmissionColor", "_EmissiveColor", "_Emission" };
        for (int i=0;i<candidates.Length;i++)
        {
            string c = candidates[i];
            if (mat.HasProperty(c)) { _emissionPropCache[mat] = c; return c; }
        }

        _emissionPropCache[mat] = userDefault;
        return userDefault;
    }

    bool ShouldDriveEmissive(Renderer r, Material mat, char chLetter, string chPref, string eTok, bool needRole, bool ignoreGOnoe)
    {
        if (r == null || mat == null) return false;

        bool goNOE  = NameContainsTokenCI(r.gameObject.name, DEFAULT_EXCLUDE_EMISSIVE_TOKEN);
        bool matNOE = NameContainsTokenCI(mat.name, DEFAULT_EXCLUDE_EMISSIVE_TOKEN);

        if (matNOE) return false;
        if (goNOE && !ignoreGOnoe) return false;

        bool goHasChannel  = ParseChannelFromName(r.gameObject.name, chPref) == chLetter;
        bool matHasChannel = ParseChannelFromName(mat.name, chPref) == chLetter;
        if (!goHasChannel && !matHasChannel) return false;

        if (!needRole) return true;

        bool goHasRole  = NameContainsTokenCI(r.gameObject.name, eTok);
        bool matHasRole = NameContainsTokenCI(mat.name, eTok);
        return goHasRole || matHasRole;
    }

    Color SampleColor(ChannelData ch)
    {
        float dur = ch.durationSeconds.val;
        float speed = (speedMultiplier != null) ? Mathf.Max(0.001f, speedMultiplier.val) : 1f;
        float u = (dur <= 0f) ? 0f : (Time.time * speed / dur);
        u = u + ch.startOffset01.val;

        if (pingPong != null && pingPong.val) u = Mathf.PingPong(u * 2f, 1f);
        else if (loop != null && loop.val)    u = u - Mathf.Floor(u);
        else                                  u = Mathf.Clamp01(u);

        if (ch.cached == null || ch.width <= 0) return Color.white;

        if (useLinearInterpolation != null && useLinearInterpolation.val && ch.width > 1)
        {
            float t = u * (ch.width - 1);
            int i0 = Mathf.FloorToInt(t);
            int i1 = (i0 < ch.width - 1) ? i0 + 1 : ch.width - 1;
            float a = t - i0;
            return Color.Lerp(ch.cached[i0], ch.cached[i1], a);
        }
        else
        {
            int idx = Mathf.Clamp(Mathf.RoundToInt(u * (ch.width - 1)), 0, ch.width - 1);
            return ch.cached[idx];
        }
    }

    // ====== Helpers ======
    HashSet<string> ParseExactSet(string raw)
    {
        var set = new HashSet<string>();
        if (string.IsNullOrEmpty(raw)) return set;
        string[] parts = raw.Split(';');
        for (int i=0;i<parts.Length;i++) { string t = parts[i].Trim(); if (t.Length>0) set.Add(t); }
        return set;
    }

    void LogAllRendererNames()
    {
        Renderer[] rends = null;

        if (useAttachedAtomAsRoot != null && useAttachedAtomAsRoot.val && containingAtom != null)
        { rends = containingAtom.transform.GetComponentsInChildren<Renderer>(true); }
        else
        {
            string root = rootName != null ? rootName.val : "";
            if (!string.IsNullOrEmpty(root))
            {
                var go = GameObject.Find(root); if (!go) { SuperController.LogError("Root not found: "+root); return; }
                rends = go.GetComponentsInChildren<Renderer>(true);
            }
            else rends = UnityEngine.Object.FindObjectsOfType<Renderer>();
        }

        SuperController.LogMessage("---- Renderer GameObject Names ----");
        for (int i=0;i<rends.Length;i++) if (rends[i]!=null) SuperController.LogMessage(rends[i].gameObject.name);
    }

    void LogAllLightNamesScene()
    {
        var lights = UnityEngine.Object.FindObjectsOfType<Light>();
        SuperController.LogMessage("---- All Light GameObject Names in Scene ----");
        for (int i=0;i<lights.Length;i++) if (lights[i]!=null) SuperController.LogMessage(lights[i].gameObject.name);
    }

    HashSet<string> ParseNameList(string raw)
    {
        var set = new HashSet<string>(); if (string.IsNullOrEmpty(raw)) return set;
        string s = raw.Replace('\n',';').Replace('\r',';').Replace(',',';');
        string[] parts = s.Split(';');
        for (int i=0;i<parts.Length;i++) { string t = parts[i].Trim(); if (t.Length>0) set.Add(t.ToLower()); }
        return set;
    }
    bool StringMatch(string name, string includeSub, string excludeSub)
    {
        if (!string.IsNullOrEmpty(includeSub) && !SubstringCI(name, includeSub)) return false;
        if (!string.IsNullOrEmpty(excludeSub) && SubstringCI(name, excludeSub)) return false;
        return true;
    }
    bool SubstringCI(string hay, string needle)
    {
        if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle)) return false;
        return hay.ToLower().IndexOf(needle.ToLower()) >= 0;
    }
    bool NameContainsTokenCI(string name, string token)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(token)) return false;
        return name.ToLower().IndexOf(token.ToLower()) >= 0;
    }
    char ParseChannelFromName(string name, string prefix)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(prefix)) return '\0';
        string n = name.ToUpper();
        string p = prefix.ToUpper();
        int idx = n.IndexOf(p);
        if (idx < 0) return '\0';
        int chIdx = idx + p.Length;
        if (chIdx >= 0 && chIdx < n.Length)
        {
            char c = n[chIdx];
            if (c >= 'A' && c <= 'G') return c;
        }
        return '\0';
    }
    bool ListHasRenderer(List<Renderer> list, Renderer r)
    {
        if (r == null) return true;
        for (int i=0;i<list.Count;i++) if (list[i] == r) return true;
        return false;
    }
    bool ListHasLight(List<Light> list, Light l)
    {
        if (l == null) return true;
        for (int i=0;i<list.Count;i++) if (list[i] == l) return true;
        return false;
    }
    bool RendererOrMaterialsHaveToken(Renderer r, string token)
    {
        if (r == null || string.IsNullOrEmpty(token)) return false;
        if (NameContainsTokenCI(r.gameObject.name, token)) return true;
        var mats = r.sharedMaterials;
        for (int i=0;i<mats.Length;i++)
        {
            var m = mats[i];
            if (m != null && NameContainsTokenCI(m.name, token)) return true;
        }
        return false;
    }

    void LogMembership()
    {
        for (int i=0;i<channels.Length;i++)
        {
            var ch = channels[i];
            SuperController.LogMessage("Channel " + ch.label + " Lights:");
            for (int j=0;j<ch.lights.Count;j++) if (ch.lights[j]!=null) SuperController.LogMessage("  " + ch.lights[j].gameObject.name);
            SuperController.LogMessage("Channel " + ch.label + " Renderers:");
            for (int j=0;j<ch.renderers.Count;j++) if (ch.renderers[j]!=null) SuperController.LogMessage("  " + ch.renderers[j].gameObject.name);
        }
    }

    bool TrySplitABC(string raw, out string A, out string B, out string C)
    {
        A=B=C=""; if (string.IsNullOrEmpty(raw)) return false;
        string s = raw.Replace("\r\n","\n").Replace('\r','\n');
        if (s.Length>0 && s[0]=='﻿') s = s.Substring(1);
        System.Func<string,char?> header = (line) => {
            string t=line.Trim(); if (t.Length==0) return null; string u=t.ToUpperInvariant();
            if (u=="[A]"||u=="A:"||u=="#A"||u=="# A"||u=="==A==") return 'A';
            if (u=="[B]"||u=="B:"||u=="#B"||u=="# B"||u=="==B==") return 'B';
            if (u=="[C]"||u=="C:"||u=="#C"||u=="# C"||u=="==C==") return 'C';
            return null;
        };
        System.Text.StringBuilder a=new System.Text.StringBuilder(), b=new System.Text.StringBuilder(), c=new System.Text.StringBuilder();
        char cur='\0'; bool saw=false;
        string[] lines = s.Split('\n');
        for (int i=0;i<lines.Length;i++)
        {
            var ln=lines[i]; var h=header(ln);
            if (h.HasValue){ cur=h.Value; saw=true; continue; }
            string t=ln.Trim(); if (t.Length==0) continue; if (t.StartsWith("#")) continue;
            if (cur=='A') a.AppendLine(t); else if (cur=='B') b.AppendLine(t); else if (cur=='C') c.AppendLine(t); else a.AppendLine(t);
        }
        A=a.ToString().Trim(); B=b.ToString().Trim(); C=c.ToString().Trim(); return saw;
    }
}
