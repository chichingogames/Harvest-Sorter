# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

Harvest-Sorter is a Unity **6000.3.17f1** mobile game project (URP, new Input System). The folder layout
from skeleton §2 exists (empty folders hold a `.gitkeep`) and the only scene is
`Assets/Game Assets/Scenes/Game.unity`. No game code has been written yet. `Essentials/` is empty, because
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

## Third-party setup

- **DOTween** is in `Assets/Plugins/Demigiant/`, with `Resources/DOTweenSettings.asset`, copied from Shift-Sort
  after its setup was already done. The `DOTWEEN` define is set for Android, Standalone and iPhone.
- **TMP Essential Resources** are in `Assets/TextMesh Pro/`.
- **Haptic Feedback** is installed as a git package (`xyz.candycoded.hapticfeedback`). It only compiles
  for Android, iOS and the Editor, so wrap calls in `#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR`.
- **Sortify `[Button]`** source is in `Assets/Game Assets/Scripts/Sortify/`.
- Active Input Handling is set to the new Input System only.
- The Android `applicationIdentifier` is still the template default.
