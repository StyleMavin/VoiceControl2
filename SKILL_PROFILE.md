# VAM Plugin Development & Scene Engineering — Skill Profile

Dense reference distilled from the VoiceControl2 / VoskCompanion development sessions.
Written for a fresh session to inherit the domain knowledge without rediscovery.

## 1. Core Domain Rules

**VAM plugin sandbox (Unity 2018.1.9f2, Mono, DynamicCSharp security scanner):**
- `System.IO` is hard-banned (SecurityException at compile). `System.Collections.Concurrent` is absent. Managed DLLs that P/Invoke native code are rejected at compile ("Unmanaged dll references not allowed") — Vosk/Whisper/etc. cannot run in-process.
- `System.Net` / `System.Net.Sockets` ARE allowed. The proven pattern (Yoooi's BusDriver/ToySerialController): **non-blocking `UdpClient` polled from `Update()`**, catching `SocketError.WouldBlock`. No threads, no queues, no stream readers.
- Multi-file plugins require a **`.cslist`** (plain text, relative `.cs` paths, MVRScript-class file first); VAM compiles listed files into one assembly. Load the `.cslist`, not a `.cs`. Multiple `.cs` in `meta.json` contentList without a `.cslist` = each file treated as a separate plugin. A directly-loaded single `.cs` keys off the **first type declared** — the `MVRScript` subclass must be first.
- `.var` = zip archive; package version comes **only from the filename** (`Creator.Package.N.var`), not meta.json. `programVersion` = VAM build, unrelated. Renaming the file renames the version.
- Scenes bind plugin data by storable id `plugin#N_<Creator>.<ClassName>` and reference by exact path string — both must be rewritten to migrate a scene between plugin identities; the payload (e.g. `VoiceTriggers`) transfers untouched if the save format is preserved.
- BodyLanguage's penetrator collider sweep skips colliders whose GameObject name starts with `_` — the de-facto interop convention for helper objects attached to anatomy bones.

**Companion-app architecture (out-of-process speech):**
- Offline STT belongs in an external .NET 4.8 exe (no runtime install on Win10/11) talking localhost UDP. Plugin sends `PHRASES:a|b|c` at 1 Hz (heartbeat doubles as return-address registration); companion replies `MATCH:phrase` to last sender. Only the companion's listen port is configured.
- Vosk must run **grammar-constrained** (`["red","white","[unk]"]`) — free dictation mis-hears short keywords. Rebuild the recognizer only when the phrase set changes (the heartbeat otherwise resets it every second). Keyword↔command comparison must be case-insensitive end-to-end.
- Audio capture: **`WasapiCapture` at device-native format**, resampled in software (`BufferedWaveProvider → StereoToMono → WdlResampling → SampleToWaveProvider16` → 16 kHz/16-bit/mono). `BufferedWaveProvider.ReadFully` must be `false`. Microphone open is **user-opt-in**, never on launch. Single-instance = held named mutex **plus** `Process.GetProcessesByName` backstop.

**VAM ops facts:**
- Live log: `%USERPROFILE%\AppData\LocalLow\MeshedVR\VaM\output_log.txt` (truncated per launch). All `SuperController.Log*` output lands there — it is the remote-debugging channel.
- Wedged Windows audio stack (mic+playback dead): restart **AudioEndpointBuilder** service. Win11 removed the legacy troubleshooter.
- White video screen + log `WindowsVideoMedia error 0xc00d36b4` = unsupported codec, almost always **HEVC**; VAM/Unity WMF path plays H.264 yuv420p only.
- Character scale alters mass ~scale³ with unchanged spring/damper settings: overshoot at stroke extremes + slow recovery = heavier body on same springs.
- A physics value observed at runtime may have **multiple writers**: atom storable (scene file) ⊕ PoseMe pose captures (full control-node physics embedded per pose) ⊕ trigger/SequenceMachine actions. All writers must agree or the setting won't persist.

## 2. Anti-Patterns

- **In-plugin native STT** (bundling Vosk.dll/libvosk in a .var): rejected by the security scanner. Reflection loading and HTTP fallbacks are dead ends; UDP companion is the architecture.
- **`WaveInEvent` (WinMM) at a forced 16 kHz format**: wedged the audio driver system-wide (mic and render both dead, survives app exit). Legacy capture APIs are hazardous on modern drivers.
- **`while ((n = provider.Read(...)) > 0)` on NAudio buffers with default `ReadFully=true`**: infinite silence-padded loop → froze the UI (WASAPI raises `DataAvailable` on the starting thread). Always set `ReadFully=false` and keep a loop guard.
- **`Global\` mutex alone for single-instance**: lost the rapid-double-click race (2–4 processes). Pair with a process-name check.
- **Guessing which reference is null** from a `NullReferenceException` in `Physics.IgnoreCollision(a, b)`: both args are candidates; two wrong guesses (abdomen, glutes) before instrumentation named the real one (penetrator collider). Ship a diagnostic build that null-guards *and logs the name once* instead of theorizing.
- **Deferred `Destroy()` on colliders attached to anatomy bones** (PPA): the collider lives to end-of-frame; another plugin's `GetComponentsInChildren<Collider>(true)` sweep captures it → permanent null in a cached list. Use `DestroyImmediate` and/or `_`-prefixed names.
- **`GetComponentsInChildren<Collider>()` without `(true)` during init**: misses not-yet-activated GameObjects; load-order race leaves a permanently null cached reference.
- **Recursive `Copy-Item`/`Remove-Item` on junction/symlink-laced trees** (BrowserAssist folders, AddonPackages): follows links into the master library (a "38 MB" backup ballooned to 60 GB). Use `robocopy /XJ`.
- **Editing runtime values that a pose/trigger will re-stamp**: PoseMe captures spring+damper for every control node in every pose; setting the atom value alone silently reverts on next pose apply.
- **Assuming a same-name JSON key count equals atom count**: `"type":"Person"` regex matched 26; the real atom array held 2. Parse structure, don't grep counts.
- **Unity `.First()` fixed-format lookups in plugin init**: throw or silently null on non-default characters; every collider lookup needs `FirstOrDefault` + null tolerance at use time.

## 3. Optimized Workflow

**A. Sandbox-constrained plugin development loop:**
1. Before building on an API, write a **minimal one-file probe .var** that exercises only the risky call; read the result from `output_log.txt`. (DLL-load probe killed the in-process Vosk plan in one test.)
2. When blocked, **unpack a proven .var that already does it** (Yoooi → UDP, MacGruber → .cslist) and copy its exact pattern; community plugins are the real API documentation.
3. Iterate: edit `.cs` → repack .var → reload in VAM → read log. VAM is the only compiler; keep each change small so the next compile error is attributable.
4. Instrument, don't speculate: add `SuperController.LogMessage` markers (build-active marker + named-null/once logging), reproduce, read log tail.

**B. Scene JSON surgery (any bulk edit):**
1. Locate targets structurally (`ConvertFrom-Json`, walk atoms/storables) — never by raw string counts.
2. Inventory first, print expected match counts; identify a **unique anchor** distinguishing target blocks from look-alikes (e.g. female headControl via `holdPositionSpring:"1125.51"`; owner check = nearest preceding `"id"` line).
3. Timestamped backup copy per file, outside the VAM tree.
4. Apply as line-window or block-regex replacements on raw text (preserves VAM formatting; no full JSON round-trip in PS 5.1).
5. Verify by re-parsing: assert edited values, assert non-targets unchanged, assert match counts equal expectations. Report counts.
6. Test one file in VAM before batching; batch only named files.

**C. Cross-plugin bug isolation (the BL/PPA method):**
1. Reduce to a minimal delta: diff working vs broken scene configs structurally (plugin lists, storables).
2. Read both plugins' sources from their .vars; find the exact throwing call and every reference it uses.
3. Patch the distributed source .var locally (edit `.cs`, repack same filename, keep pristine copy) with **defensive guards + one-shot diagnostic logging**.
4. Let the diagnostic name the culprit; only then write the upstream report with repro, evidence line, and a minimal suggested fix for each author.

## 4. Code Snippets

**A. Sandbox-safe UDP backend (drop-in for any VAM plugin ↔ companion IPC):**
```csharp
// using System.Net; System.Net.Sockets; System.Text;  — all sandbox-legal
public class UdpLink {
    UdpClient _udp; EndPoint _from = new IPEndPoint(IPAddress.Any, 0);
    readonly byte[] _buf = new byte[8192]; int _port; float _beat;
    public event Action<string> OnMessage;
    public void Start(int companionPort) {
        _port = companionPort;
        _udp = new UdpClient { ExclusiveAddressUse = false };
        _udp.Client.Blocking = false;
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0)); // ephemeral; replies come here
    }
    public void Stop() { try { _udp?.Close(); } catch {} _udp = null; }
    public void Send(string msg) { if (_udp == null) return;
        try { var b = Encoding.UTF8.GetBytes(msg); _udp.Send(b, b.Length, "127.0.0.1", _port); } catch {} }
    public void Poll(float dt, string heartbeatPayload) {          // call from Update()
        if (_udp == null) return;
        while (true) {
            int n; try { n = _udp.Client.ReceiveFrom(_buf, ref _from); }
            catch (SocketException e) { if (e.SocketErrorCode == SocketError.WouldBlock) break; break; }
            if (n <= 0) break;
            OnMessage?.Invoke(Encoding.UTF8.GetString(_buf, 0, n));
        }
        _beat += dt; if (_beat >= 1f) { _beat = 0f; Send(heartbeatPayload); } // re-registers return addr
    }
}
```

**B. Scene-surgery template (backup → anchored edit → parsed verification):**
```powershell
$f = "<scene>.json"
Copy-Item -LiteralPath $f -Destination "<backupDir>\$([IO.Path]::GetFileNameWithoutExtension($f)).PRE-$(Get-Date -f yyyy-MM-dd).json" -Force
$lines = [IO.File]::ReadAllText($f) -split "`n"; $hits = 0
for ($i=0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -notmatch '<ANCHOR-REGEX>') { continue }          # unique value identifying target blocks
    $owner = $null                                                   # confirm block ownership upward
    for ($k=$i; $k -ge [Math]::Max(0,$i-80); $k--) { if ($lines[$k] -match '"id"\s*:\s*"([^"]+)"') { $owner=$Matches[1]; break } }
    if ($owner -ne '<EXPECTED-ID>') { continue }
    for ($j=$i+1; $j -le [Math]::Min($i+8,$lines.Count-1); $j++) {   # edits live near the anchor
        if ($lines[$j] -match '("<KEY>"\s*:\s*)"<OLD>"') { $lines[$j] = $lines[$j] -replace '("<KEY>"\s*:\s*)"<OLD>"','${1}"<NEW>"'; $hits++ }
    }
}
[IO.File]::WriteAllText($f, ($lines -join "`n"), (New-Object Text.UTF8Encoding($false)))  # UTF-8 no BOM
"edits=$hits (expect N)"                                             # then re-parse with ConvertFrom-Json and assert
```

**C. .var pack/repack (build or patch any package; entries verified against original):**
```powershell
Add-Type -AssemblyName System.IO.Compression; Add-Type -AssemblyName System.IO.Compression.FileSystem
$tree = "<extracted-root>"; $out = "<Creator.Package.N.var>"
try { [IO.File]::Delete($out) } catch {}
$z = [IO.Compression.ZipFile]::Open($out, [IO.Compression.ZipArchiveMode]::Create)
$len = $tree.Length + 1
Get-ChildItem $tree -Recurse -File | ForEach-Object {
    $entry = $_.FullName.Substring($len).Replace('\','/')            # forward slashes mandatory
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $_.FullName, $entry,
        [IO.Compression.CompressionLevel]::Optimal) | Out-Null }
$z.Dispose()
# Verify: entry count vs original, then stream-read the patched file inside the zip and regex-assert the change.
```
