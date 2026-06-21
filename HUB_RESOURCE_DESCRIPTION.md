[SIZE=6][B]VoiceControl2 — hands-free voice triggers for VAM[/B][/SIZE]

Trigger anything in VAM with your voice. Speak a keyword or short phrase and fire any VAM trigger — load looks, start animations, switch scenes, toggle plugins, whatever you can wire to a trigger.

This is a modernized successor to the classic VoiceControl / VoiceTrigger plugins, rebuilt to survive Windows updates.

[SIZE=5][B]Why this version?[/B][/SIZE]
Older voice plugins relied on Windows Speech Recognition (WSR), which recent Windows updates have been removing or replacing — silently breaking those plugins. VoiceControl2 adds a second, future-proof speech engine that runs in a small companion app [I]outside[/I] of VAM using the offline open-source [B]Vosk[/B] engine. It doesn't depend on any Windows speech feature, so Windows updates can't break it — and because recognition runs in its own app, it doesn't compete with VAM for CPU.

[SIZE=5][B]Two speech backends — your choice[/B][/SIZE]
[LIST]
[*][B]Windows Speech (Legacy)[/B] — the classic in-VAM recognizer. Works if your Windows still has WSR. No extra setup.
[*][B]Vosk Companion (recommended)[/B] — fully offline recognition via the free VoskCompanion app (included). Independent of Windows speech services. Future-proof.
[/LIST]
Switch between them anytime from a dropdown in the plugin UI.

[SIZE=5][B]Features[/B][/SIZE]
[LIST]
[*]Add / rename / delete named voice triggers, each with full VAM trigger actions
[*]On-screen HUD feedback when a command is recognized
[*]Accurate keyword matching (grammar-based with Vosk; adjustable confidence with WSR)
[*]Active-microphone display, window-focus warning, full scene save/restore
[/LIST]

[SIZE=5][B]What's included in this download[/B][/SIZE]
[LIST]
[*][B]StyleMavin.VoiceControl2.[/B][I]1[/I][B].var[/B] — the VAM plugin (install in AddonPackages)
[*][B]VoskCompanion-v1.0.zip[/B] — the optional companion app for the Vosk backend (bundles everything, including a speech model — no extra downloads)
[/LIST]

[SIZE=5][B]Setup[/B][/SIZE]
[B]Plugin:[/B]
[LIST=1]
[*]Install the .var (drop it in AddonPackages).
[*]Add the plugin to an atom or as a Scene plugin. When choosing the file, pick [B]VoiceControl2.cslist[/B].
[*]Pick your Speech Backend in the plugin UI, add triggers, assign actions.
[/LIST]
[B]For the Vosk Companion backend:[/B]
[LIST=1]
[*]Extract VoskCompanion-v1.0.zip anywhere and run [B]VoskCompanion.exe[/B].
[*]Click [B]Start Listening[/B]. (The mic stays off until you do — opening the app never touches your audio.)
[*]In VAM, set the plugin's backend to [B]Vosk Companion[/B]. The default port (19547) already matches.
[/LIST]
[SIZE=5][B]How commands work (important)[/B][/SIZE]
As in Almadiel's original, the plugin repurposes the trigger action panel's [B]"Name"[/B] field to enter and store the voice command(s) that fire each event.
[LIST]
[*]A trigger can have a [B]single command[/B], or [B]multiple alternatives[/B] for the same command — any one of which fires the same event action(s).
[*]A trigger can define [B]one event action or several[/B]. The extra actions don't each need a command word.
[*]As long as [B]at least one[/B] event action has a command defined, the trigger is active and listening.
[/LIST]
Full instructions are in the README inside the zip.

[SIZE=5][B]Privacy[/B][/SIZE]
All speech recognition is 100% offline and local. Nothing is ever sent to the internet — the companion talks to VAM only over a local-only (127.0.0.1) connection on your own machine.

[SIZE=5][B]Note on the companion app[/B][/SIZE]
VoskCompanion is an unsigned program, so Windows SmartScreen will warn on first run — click [I]More info → Run anyway[/I]. The full source code is open if you'd like to review or build it yourself (links below).

[SIZE=5][B]GitHub (source & alternate downloads)[/B][/SIZE]
[LIST]
[*]Plugin: [URL]https://github.com/StyleMavin/VoiceControl2[/URL]
[*]Companion app + releases: [URL]https://github.com/StyleMavin/VoskCompanion/releases[/URL]
[/LIST]

[SIZE=5][B]Credits[/B][/SIZE]
[LIST]
[*]Based on [B]VeeRifter[/B]'s VoiceControl plugin...
[*]...which was itself based on [B]Almadiel[/B]'s original VoiceTrigger plugin
[*]Custom trigger handler: MacGruber / Acidbubbles
[*]Custom text input field: Acidbubbles
[*]HUD example: MeshedVR
[*]Offline speech: Vosk (Apache-2.0); audio capture: NAudio (MIT)
[/LIST]

[SIZE=5][B]License[/B][/SIZE]
Plugin: CC BY-SA. Companion app: MIT. Bundled third-party licenses (Vosk, NAudio) are included in the companion download.
