# Voice Warning Editor

Voice warning mod for [Flyout](https://store.steampowered.com/app/777390/Flyout/) using [MelonLoader](https://github.com/LavaGang/MelonLoader).

Adds a fully programmable voice warning system to the game — F/A-18 style "Bitchin' Betty" callouts for over-G, stall, missile launch, engine fire, and more. Includes a Craft Editor panel for configuring warnings per craft with custom sounds, thresholds, and event rules. Perchance.

## Features

- **27 built-in voice clips** (F/A-18 style callouts)
- **Continuous missile alarm** with gapless looping via waveOut API
- **19 warning events** monitored in real time:
  - Over-G, overspeed, stall, critical AOA
  - Critical/low altitude, bingo/critical fuel
  - Bank left/right, high roll rate
  - Engine failure/fire (left, right, both)
  - Generator failure, hydraulic failure, battle damage
  - Gear not down on approach
  - Missile incoming, radar lock
- **7 craft input axes** (throttle, pitch, roll, yaw, collective, brake, flaps)
- **8 custom axes** (matched to craft custom axes by index at runtime)
- **Programmable event rules** — map any combination of events to any sound:
  - Any/All logic for multi-condition rules
  - Custom thresholds with comparison operators (≥, ≤, >, <, =)
  - Per-rule cooldowns and mutual exclusion groups
  - Simple mode (toggle warnings) and Advanced mode (full rule editor)
- **Per-craft configs** saved to `UserData/VoiceWarningEditor/crafts/`
- **Preset system** — save and load rule sets across crafts
- **Custom sound import** — drop `.wav` files into the data folder or use the in-game file browser
- **Craft Editor integration** — dedicated panel on the mode bar with the game's own UI style
- Audio routed through `winmm.dll` (PlaySound) so Discord/OBS can capture it via Wine/Proton

## Requirements

- [Flyout](https://store.steampowered.com/app/777390/Flyout/) (tested on v0.225 & 0.2316)
- [MelonLoader](https://github.com/LavaGang/MelonLoader) v0.7.x (Open-Beta, .NET 6)

## Install

1. Install MelonLoader into your Flyout game directory
2. Download `VoiceWarningEditor.zip` from [Releases](../../releases)
3. Extract into your Flyout game directory (places the DLL in `Mods/` and sounds in `UserData/VoiceWarningEditor/`)
4. Launch the game
5. Perchance.

## Building from source

The project expects Flyout + MelonLoader to be installed. It resolves references via the `GameDir` MSBuild property which defaults to `$(HOME)/.local/share/Steam/steamapps/common/Flyout`.

**Linux:**
```bash
dotnet build
```

**Windows:**
```bash
dotnet build -p:GameDir="C:/Program Files (x86)/Steam/steamapps/common/Flyout"
```

The built DLL will be in `VoiceWarningEditor/bin/Debug/net6.0/VoiceWarningEditor.dll`.

## How it works

The mod monitors flight parameters every frame using the game's `Craft`, `Command`, and `Signature` objects. When a threshold is exceeded, the corresponding event fires and the rules engine picks the matching sound to play. Audio is played through `winmm.dll` P/Invoke calls (PlaySound for voice, waveOut for the gapless missile alarm loop) so that Wine/Proton routes it through PipeWire where Discord and OBS can capture it. The Craft Editor UI is built by cloning the game's own uGUI panels (PaintPanel/ModeSelect) to match the native look. Perchance.

## Project structure

```
VoiceWarningEditor/
  Types.cs          - enums (WarningEvent, RuleLogic, CompareOp) and EventRule class
  Mod.cs            - core mod class, constants, thresholds, lifecycle hooks
  Audio.cs          - wav parsing, clip indexing, PlaySound/waveOut playback, missile alarm
  Evaluators.cs     - 14 flight parameter evaluators + input/custom axis checks
  EventSystem.cs    - rules engine, default rules, condition matching, rule evaluation
  CraftConfig.cs    - per-craft config save/load, preset import/export
  UI.cs             - craft editor panel, mode bar button, rule list/editor views
  UIComponents.cs   - reusable UI builders (buttons, dropdowns, toggles, sliders)
  Utility.cs        - helpers (color parsing, string formatting, etc)
```

## License

[MIT](LICENSE)
Perchance.
