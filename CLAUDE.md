# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

Harvest-Sorter is a Unity **6000.3.17f1** mobile game project (URP, new Input System). The folder layout
from skeleton §2 exists (empty folders hold a `.gitkeep`) and the only scene is
`Assets/Game Assets/Scenes/Game.unity`. The art pack has been imported (see "Art pack" below). No game code
has been written yet. `Essentials/` is empty, because
the framework files from skeleton §13 step 2 haven't been copied in. `C:\Project\Shift-Sort` is the
reference project built on the same skeleton.

Unity agent skills (from `npx skills add Unity-Technologies/skills`) live in `.agents/skills/`, tracked by
`skills-lock.json`. `.claude/skills/` is a Windows junction pointing at the same files.

The intended architecture is defined in **`skeleton.md`** (the structure of a previous game, Color Farm,
stripped of its gameplay). Read it before creating any game code — it is the spec for folder layout,
singletons, boot order, save data, popups and levels. It references an `ARCHITECTURE.md` from that other
project; that file does not exist here.

## Commands

There is no CLI build or lint script. Work happens in the Unity Editor
(`C:\Program Files\Unity\Hub\Editor\6000.3.17f1\Editor\Unity.exe`). Useful headless invocations:

```powershell
# Compile check / run EditMode tests (Unity must not already have the project open)
& "C:\Program Files\Unity\Hub\Editor\6000.3.17f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Temp/results.xml -logFile -
# Single test: add  -testFilter "Namespace.ClassName.MethodName"
# PlayMode: -testPlatform PlayMode
```

`Assembly-CSharp.csproj` / `.sln` are Unity-generated — don't edit them; `dotnet build` on them is only a
rough compile check. `Library/`, `Temp/`, `Logs/`, `UserSettings/` are Unity caches.

## Target architecture (summary of skeleton.md)

- **One scene** (`Game.unity`) with roots: `Managers` (every `Manager_*` + `SingletonManager` + `GameTuning`),
  `GamePlay` (hand-placed world objects), `Canvas` (HUD, PopUps container, dark backdrop, loading/transition screens).
- Code under `Assets/Game Assets/Scripts/` in layers: `Gameplay/Essentials/` (framework), `Managers/`
  (own rules + state), `GameElements/` (prefab views), `UI/` (HUD + popups), `Editor/` (re-runnable scene
  builders under a `<CodeName>/` menu). All in `Assembly-CSharp`, global namespace.
- **Managers decide, views display.** Views never mutate state; they call a manager or raise an event.
- `Singleton<T>`: override `Awake2()`, never `Awake()`. `Instance` is null until initialized. Init order is
  the hand-wired `SingletonManager._singletonsToInitialize` Inspector list; it calls `OfflineData.Load()`
  first (once — the original called it twice; fix that and the duplicate-instance guard, see skeleton §3–4).
- `Manager_Game` owns the `GameState` machine and the per-frame order: other managers expose `Tick(dt)`
  and do not run gameplay in their own `Update`. Pause is **derived** (`IsPaused` from popups/intro/tutorial),
  never a flag; `ApplyInputGate()` sets `Manager_Input.AcceptInput`.
- `Manager_Input` reads `Pointer.current` (new Input System only) and raises intent events.
- Events default to `delegate { }`; listeners **subscribe then immediately read** the current value.
- Gameplay numbers: `Tuning` (const defaults + static facade) over `GameTuning` (scene Inspector overrides,
  these are what ship). Assets: `FileRef` (`ScriptableSingleton<T>` in `Resources/FileRef.asset`) is the only
  place prefabs/sounds/palettes are assigned.
- Save: `OfflineData` — public fields, JSON in one PlayerPrefs key, **append-only**, one writer per field,
  migrations in `Load()`. `LiveData` = static non-persisted session state.
- Popups: `Popup` → `BasePopUp` → `PopUp_X`; subclasses override `InitButtons()` and use `Bind(...)`.
  `Manager_PopUp` registers all popups under `Canvas/Container/PopUps` by `PopupName`.
- Levels: `Resources/Levels/level_N.json` + auto-generated `level_index.json`, 1-based, wrap around.
- Every enum that is saved, serialized, or used as a bit index is append-only with explicit values.
- Animation and delays use DOTween (no coroutines); UI text uses TextMesh Pro.
- Scene object names are load-bearing (builders find by path). Don't hand-edit scene YAML; use Inspector
  wiring or Editor builders. Runtime code never touches `UnityEditor` without `#if UNITY_EDITOR`.
- Naming: `Manager_X`, `PopUp_X`, `UI_X`; `[SerializeField] private _camelCase` exposed via expression-bodied properties.

## Scene

`Game.unity` is built by `Scripts/Editor/SceneBuilder.cs` (menu **Harvest Sorter > Build Base Scene**, or
**Rebuild Level Art > Level N**). It is re-runnable: missing roots (`Managers`, `GamePlay`, `Canvas`
with `Container/{InGame, PopUps}`, `Dark Area`, `Loading Screen`, `Transition Screen`, and `EventSystem`
with `InputSystemUIInputModule`) are created and existing ones are kept. `GamePlay/Level Art` is deleted
and regenerated from the "board" and "dressing" rows of `Art Data/Level_0N_Art_Layout.json`. The camera
is orthographic and placed from the layout's camera block (size 6.625, portrait 1080×1920 canvas).
`Managers` is empty until the manager scripts exist.

The Pipeline package (`com.unity.pipeline`) is installed, so a running Editor can be driven with the
`unity` CLI: `unity command eval --code '...'`, `unity recompile`, `unity command game_view --source camera`,
and `unity command console get`.

## Art pack

The art was exported from Blender (the source `.blend` isn't in the repo). Reference renders, including
the level mock-ups and the model sheet, are in `Docs/Art/`.

- **Materials (shared, one per surface):** the pack originally coloured everything through one palette
  texture. The FBX files in `Game Assets/Models/` have since been re-exported with
  `Tools/Blender/split_palette_materials.py`, which gives each face the material of the palette swatch its
  UVs sat in, named after that swatch's surface in `Art Data/HS_Surface_Materials.json` (for example
  `HS_Belt_deep_teal`). Every model using a colour therefore shares one material: all belt models use the
  same `HS_Belt_*` and `HS_Ivory_enamel` materials, so editing a material changes it on every model that
  uses it. The materials are URP Lit, at `Materials/<Family>/<name>.mat` (Enamel, Wood, Rubber, Foliage, and
  so on). There are 45, and 42 of them are used. Each model's `.fbx.meta` remaps its slots to them by name
  (`externalObjects`).
- **Surface textures:** each surface has a tileable albedo and a normal map family in `Textures/Surfaces/`,
  with its `tileMeters`, metallic and roughness listed in the surface JSON. Menu
  **Harvest Sorter > Apply Surface Materials** (`Scripts/Editor/SurfaceMaterialBuilder.cs`, re-runnable)
  sets the texture import settings and creates or updates the 45 materials. Each gets its albedo, normal
  map, metallic and smoothness (1 - roughness), with `_BaseColor` white because the albedo already carries
  the colour. The builder then rewrites every model's material remaps. Materials are updated in place, so
  their GUIDs survive. The textures repeat at real-world size because the Blender script rewrites
  **UV0 ("Surface")** as a box projection in model space, in units of that face's `tileMeters`, so
  material tiling stays (1, 1). The original palette UVs are kept in **UV1 ("Palette")**;
  `Textures/HS_Palette.png` is unused.
- **New models** need the same treatment: run the Blender script in `convert` mode on them
  (`-- <models_dir> <HS_Swatches.json> <out_dir> convert <HS_Surface_Materials.json>`), copy the FBX files in,
  then run Apply Surface Materials. A slot whose name matches no surface logs a warning and falls back to
  the material Unity generates from the FBX.
- **Axes and scale:** the FBX files are exported with Blender's "Apply Transform" and "FBX All" scaling, so
  every model root imports with no rotation, scale 1, real-world metres and Y up. A default Blender export
  gives roots a (90, 0, 0) rotation and ×100 scale; the artist's original files had this. Blender
  coordinates map to Unity as **(x, y, z) → (x, z, y)**, and a Blender yaw becomes the **negative** Unity Y
  rotation. `Art Data/*.json` uses Blender coordinates.
- **Anchor nodes:** models contain gameplay anchor transforms as named child nodes, for example `Entry`/`Exit`/`Port`
  on belts, `Pocket_1..6` and `LidSeat` on `Tray_Six_Pocket`, `OrderSlot_N` on trucks, and `CropExit`/`DrumPivot`
  on `Mint_Harvester`. `Art Data/Model_Manifest.json` lists them with positions.
- **Level art layouts:** `Art Data/Level_0N_Art_Layout.json` gives the placements and ortho camera for each
  level's art. As the files themselves say, puzzle data belongs in the game's level JSON, not here.
- **Sprites:** UI sprites in `Sprites/UI/` are 100 PPU and 9-sliced using the borders from
  `Docs/Art/Sprite_Import_Settings.json`. VFX sprites are in `Sprites/VFX/`.

## Third-party setup

- **DOTween** is in `Assets/Plugins/Demigiant/`, with `Resources/DOTweenSettings.asset`, copied from Shift-Sort
  after its setup was already done. The `DOTWEEN` define is set for Android, Standalone and iPhone.
- **TMP Essential Resources** are in `Assets/TextMesh Pro/`.
- **Haptic Feedback** is installed as a git package (`xyz.candycoded.hapticfeedback`). It only compiles
  for Android, iOS and the Editor, so wrap calls in `#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR`.
- **Sortify `[Button]`** source is in `Assets/Game Assets/Scripts/Sortify/`.
- Active Input Handling is set to the new Input System only.
- The Android `applicationIdentifier` is still the template default.
