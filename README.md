# MandM

Self-buff tracker for **Monsters & Memories**. EQDM-like Windows desktop app (visual language only — not EverQuest / EQLDMG code).

**v1:** personal buff timers via OCR of Combat Text + Abilities regions. No party/auction/DPS.

## Requirements

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- Windows OCR language pack (English) — usually already present; if OCR fails, install via **Settings → Time & language → Language & region**

## Run (dev)

```powershell
cd C:\Users\sayser\Desktop\MandM
dotnet run --project src\MandM\MandM.csproj -r win-x64
```

Or open `MandM.slnx` in Visual Studio / Cursor and F5.

## Publish (single-file portable exe)

```powershell
cd C:\Users\sayser\Desktop\MandM
dotnet publish src\MandM\MandM.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish
```

Output: `publish\MandM.exe`. Settings are written beside the exe as `settings.json`.

## Usage

1. Launch MandM.
2. **Options** → drag rectangles for **Abilities** (spell bar) and **Combat Text** (chat/combat window).
3. Add/edit buffs (seed includes **Speed of the Pack**).
4. When Combat Text OCR sees **Land text**, the countdown starts; **Fade text** clears it; at **Warn time** the chosen alarm plays.
5. **Overlay** toggles a always-on-top buff list.
6. **Check Updates** queries GitHub releases for `sayser/mandm` (harmless stub when no releases exist).
7. **Test Land** on a tracked buff exercises the timer without OCR.

Matching is case-insensitive substring with light punctuation tolerance.

## Solution layout

```
MandM.slnx
src/MandM/
  App.xaml(.cs)           App host + dark theme
  MainWindow.*            Buff list, abilities OCR, combat preview
  OverlayWindow.*         Topmost buff overlay
  OptionsWindow.*         Capture regions + OCR interval
  RegionPickerWindow.*    Fullscreen drag-rectangle picker
  BuffEditWindow.*        Buff CRUD
  Models/                 Settings, buff defs, active buffs
  Services/               OCR, capture, tracker, alarms, updates
```

## Out of scope (later)

Party buffs, auction watcher, DPS meter, buff-icon template matching.
