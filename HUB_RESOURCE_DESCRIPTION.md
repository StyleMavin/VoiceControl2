# VoiceControl2 — Hub resource description (draft)

Paste/adapt this into the VAM Hub resource page. The Hub supports BBCode;
this is written in plain text/markdown so you can format as you prefer.

---

## Short tagline
Trigger anything in VAM with your voice — future-proof offline speech recognition that survives Windows updates.

## Overview

VoiceControl2 lets you fire VAM triggers by speaking keywords or short phrases.
It's a modernized successor to the classic VoiceControl/VoiceTrigger plugins.

Older voice plugins relied on Windows Speech Recognition (WSR), which recent
Windows updates have been removing/replacing — breaking those plugins. To stay
working long-term, VoiceControl2 supports a second speech engine that runs in a
small companion app OUTSIDE of VAM, using the offline open-source Vosk engine.
It does not depend on any Windows speech feature, so Windows updates can't break
it. It also runs off VAM's main thread, so it won't hurt your framerate.

## Two backends, your choice

- **Windows Speech (Legacy):** the classic in-VAM recognizer. Works if your
  Windows still has WSR. No extra setup.
- **Vosk Companion (recommended, future-proof):** offline recognition via the
  free VoskCompanion app (separate download below). Independent of Windows
  speech services.

Switch between them anytime from a dropdown in the plugin.

## Features
- Add/rename/delete named voice triggers, each with full VAM trigger actions
- On-screen HUD feedback when a command is recognized
- Adjustable confidence (legacy) / accurate grammar-based matching (Vosk)
- Mic device display, window-focus warning, scene save/restore

## Installation

1. Install this .var (drop it in AddonPackages).
2. Add the plugin to an atom or as a Session/Scene plugin. When selecting the
   file, choose **VoiceControl2.cslist**.
3. Pick your Speech Backend in the plugin UI.
4. Add triggers and assign actions.

### For the Vosk Companion backend (future-proof option)
You also need the free companion app:

  >> DOWNLOAD: VoskCompanion-v1.0.zip  <<
  (link to GitHub release / attached file)

  - Extract it anywhere and run VoskCompanion.exe (it bundles everything,
    including the speech model — no extra setup).
  - Leave it running, then start VAM and set the plugin backend to
    "Vosk Companion". The default port (19547) already matches.

Full instructions are in the README inside that download.

## Privacy
All recognition is 100% offline and local. Nothing is sent to the internet.
The companion talks to VAM only over a local-only (127.0.0.1) connection.

## Credits
- Original VoiceControl / VoiceTrigger: Almadiel
- Custom trigger handler: MacGruber / Acidbubbles
- Custom text input field: Acidbubbles
- HUD example: MeshedVR
- Offline speech: Vosk (Apache 2.0); audio capture: NAudio (MIT)

## License
CC BY-SA (see meta.json). Companion app license info included in its download.

---

### Notes for you (not for the Hub page)
- Category: Plugins (or Plugins > Misc).
- Upload StyleMavin.VoiceControl2.1.var as the resource file.
- If hosting the companion on GitHub Releases, paste that link where marked.
- If attaching the companion zip to the Hub resource instead, upload
  VoskCompanion-v1.0.zip as an additional file and link it where marked.
- Add 2-3 screenshots: the plugin UI, the companion window showing a match,
  and a trigger setup. Screenshots greatly improve Hub visibility.
