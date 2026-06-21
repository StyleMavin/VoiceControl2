# VoiceControl2

Hands-free voice triggers for [Virt-A-Mate (VAM)](https://hub.virtamate.com/). Speak a keyword or short phrase and fire any VAM trigger — load looks, start animations, switch scenes, toggle plugins, anything you can wire to a trigger.

A modernized successor to the classic VoiceControl / VoiceTrigger plugins, rebuilt to survive Windows updates.

## Why this version?

Older voice plugins relied on Windows Speech Recognition (WSR), which recent Windows updates have been removing or replacing — silently breaking those plugins. VoiceControl2 adds a second, future-proof speech engine that runs in a small companion app **outside** of VAM using the offline open-source [Vosk](https://alphacephei.com/vosk/) engine. It doesn't depend on any Windows speech feature, so Windows updates can't break it — and because recognition runs in its own app, it doesn't compete with VAM for CPU.

## Two speech backends

- **Windows Speech (Legacy)** — the classic in-VAM recognizer. Works if your Windows still has WSR. No extra setup.
- **Vosk Companion (recommended)** — fully offline recognition via the free [VoskCompanion](https://github.com/StyleMavin/VoskCompanion) app. Independent of Windows speech services. Future-proof.

Switch between them anytime from a dropdown in the plugin UI.

## Install

1. Get the `.var` from the [VAM Hub](https://hub.virtamate.com/) (or build it from `plugin3/`) and drop it in your `AddonPackages` folder.
2. Add the plugin to an atom or as a **Scene** plugin. When choosing the file, pick **`VoiceControl2.cslist`**.
3. Pick your Speech Backend in the plugin UI, add triggers, and assign actions.

For the Vosk backend, also download and run the [VoskCompanion app](https://github.com/StyleMavin/VoskCompanion/releases).

## How commands work

As in Almadiel's original, the plugin repurposes the trigger action panel's **"Name"** field to enter and store the voice command(s) that fire each event.

- A trigger can have a single command, or multiple alternatives for the same command — any one fires the same action(s).
- A trigger can define one event action or several; the extras don't each need a command word.
- As long as at least one event action has a command defined, the trigger is active and listening.

## Repository layout

```
plugin3/                         the plugin source (this is what ships in the .var)
  Custom/Scripts/StyleMavin/
    VoiceControl2.cslist         <- load THIS in VAM (compiles the files below as one)
    VoiceControl2.cs             main plugin + UI
    Backends.cs                  speech backends (WSR + Vosk Companion over UDP)
    TriggerSupport.cs            trigger handler + VoiceTrigger
  meta.json
HUB_RESOURCE_DESCRIPTION.md      the VAM Hub listing text
StyleMavin.VoiceControl2.1.var   packaged plugin
```

The companion app lives in its own repo: [StyleMavin/VoskCompanion](https://github.com/StyleMavin/VoskCompanion).

## Credits

- Based on **VeeRifter**'s VoiceControl plugin, which was itself based on **Almadiel**'s original VoiceTrigger plugin
- Custom trigger handler: MacGruber / Acidbubbles
- Custom text input field: Acidbubbles
- HUD example: MeshedVR
- Offline speech: [Vosk](https://github.com/alphacep/vosk-api) (Apache-2.0); audio capture handled in the companion app

## License

CC BY-SA.
