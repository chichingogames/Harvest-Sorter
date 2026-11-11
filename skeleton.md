# SKELETON — the code structure of Color Farm, as a template for a new game

This file describes the **architecture skeleton** that Color Farm (code name Chichingo) is built
on, stripped of the chicken-specific gameplay. Use it to start a new Unity mobile game with the
same structure. Gameplay examples from Color Farm are marked *(example)*; everything else is
the reusable part.

For the full game-specific map see `ARCHITECTURE.md`. This file is only the bones.

---

## 1  Big picture

```
One scene (Game.unity)
 ├ Managers            ← one GameObject holding every Manager_* singleton + SingletonManager + GameTuning
 ├ GamePlay            ← world objects the managers drive (board, nests, queues…) — placed by hand
 ├ Canvas              ← all UI: HUD, PopUps container, dark backdrop, loading + transition screens
 ├ (feature roots)     ← e.g. "Hint System", "CoinFlyAnimator", "Decoration"
 └ Camera, Light, EventSystem

Code layers
 Essentials/   framework: Singleton<T>, SingletonManager, ScriptableSingleton<T>, OfflineData, LiveData
 Managers/     one singleton per system — they own the RULES and the STATE
 GameElements/ views (MonoBehaviours on prefabs) — they render what they're handed, decide nothing
 UI/           HUD + popups — pure views over manager state and events
 Tuning        static facade of gameplay numbers; GameTuning = Inspector overrides
 FileRef       ScriptableSingleton asset registry (prefabs, palettes, sounds, per-level data)
 Editor/       re-runnable scene builders under a "<CodeName>/" menu
```

The core rule: **managers decide, views display**. A view never changes game state; it calls a
manager or raises an event. One manager (`Manager_Game`) owns the state machine and the order
in which other managers tick each frame.

---

## 2  Folder layout

```
Assets/
  Game Assets/                    ← everything you author
    Scenes/Game.unity             ← the only scene
    Scripts/
      Gameplay/
        Essentials/               ← framework (copy these files as-is)
          Singleton.cs            ← Singleton<T>, ISingleton, SingletonClass<T>
          SingletonManager.cs
          ScriptableSingleton.cs  ← ScriptableSingleton<T> + [AssetPath]
          OfflineData.cs          ← OfflineData (save) + LiveData (session scratch)
          GameObjectLayoutControl.cs
          SafeArea.cs
        Managers/                 ← Manager_Game, Manager_Input, Manager_PopUp, Manager_Level, ...
        GameElements/             ← prefab views
        Common.cs                 ← shared enums + small data structs
        Tuning.cs  GameTuning.cs
        FileRef.cs
      UI/
        Popups/                   ← Popup.cs, BasePopUp.cs, PopUp_*.cs
        UI_InGame.cs, UI_LoadingScreen.cs, UI_TransitionScreen.cs, ...
      Editor/                     ← builders, [MenuItem("<CodeName>/...")]
    Prefabs/Gameobjects/  Prefabs/UI/  Materials/  Sprites/  Audio/  Fonts/  Shaders/
  Resources/                      ← loaded by name at runtime — EVERYTHING here ships
    FileRef.asset
    Levels/level_N.json + level_index.json
  Plugins/                        ← DOTween etc. (package links: §13 "Packages")
```

All game scripts are in `Assembly-CSharp`, global namespace. Naming: classes are
`Manager_X`, `PopUp_X`, `UI_X`; serialized fields are `[SerializeField] private _camelCase`,
exposed via expression-bodied properties.

---

## 3  Singletons — `Singleton<T>`

Every manager is a scene `MonoBehaviour` deriving from `Singleton<T>`:

```csharp
public class Manager_Score : Singleton<Manager_Score>
{
    public event Action<int> OnScoreChanged = delegate { };   // events default to a no-op

    public override void Awake2()          // ← your init goes here, NEVER in Awake()
    {
        // grab references, build caches, subscribe to things that already exist
    }
}

// anywhere:
Manager_Score.Instance.Add(10);
```

How it works (`Essentials/Singleton.cs`):

```csharp
public class Singleton<T> : MonoBehaviour, ISingleton
{
    private static T _instance;
    private static bool _initializedByManager;

    private void Awake()      { if (_initializedByManager) return; _initializedByManager = true;
                                if (_instance == null) _instance = GetComponent<T>(); else Destroy(gameObject);
                                Awake2(); }
    public void Initialize()  { /* identical body — called by SingletonManager */ }
    public virtual void Awake2() { }
    public static T Instance => _instance;
    public void Delete() { _instance = default; Destroy(gameObject); }
}
```

- **Two init paths, first one wins:** `SingletonManager` calls `Initialize()` in a controlled
  order; if a singleton isn't in its list, Unity's `Awake()` initializes it instead. The static
  flag makes sure `Awake2()` runs exactly once.
- `Instance` is a plain field — **`null` until initialized**. Code that might run before a
  manager is up must null-check (`Manager_AudioHaptic.Instance?.PlaySound(...)` style) or retry
  in `Start`.
- **Known trap (fix it in the new project):** because the flag is static, a *duplicate*
  instance's `Awake()` returns early, so the `Destroy(gameObject)` branch never runs and the
  duplicate silently survives with `Awake2()` never called. In a one-scene game this never
  matters; if your new game loads scenes additively or has `DontDestroyOnLoad`, rewrite the
  guard to check `_instance != null && _instance != this` first.

`SingletonClass<T>` is the plain-C# version (no GameObject), lazily created on first
`Instance` access. Used for pure-logic services *(example: `PlayerBehaviourTracker`)*.

---

## 4  `SingletonManager` — boot order

A component on the `Managers` GameObject. Its `OnEnable` is the game's boot:

```
SingletonManager.OnEnable
 ├ OfflineData.Load()                        ← save is ready before any manager inits
 ├ foreach entry in _singletonsToInitialize  ← Inspector list, ORDER MATTERS
 │     (entry as ISingleton).Initialize()    → Awake2()
 ├ OnSingletonsInitialized()                 ← event: "everyone is up"
 ├ Application.targetFrameRate = 60
 └ DOTween.SetTweensCapacity(500, 100)
```

*(Example order in Color Farm: Level → Board → Nest → Queue → Input → Tutorial → Game →
Currency → Bench → AudioHaptic.)* Put data providers first, the central `Manager_Game` near the
end so everything it touches already exists.

Rules:
- **The list is wired by hand in the Inspector.** A new manager does not get ordered init until
  you drag it in. Managers *not* in the list (e.g. `Manager_PopUp`) still work via `Awake`, but
  in arbitrary order — anything depending on them must null-check or retry in `Start`.
- The current code calls `OfflineData.Load()` twice (once inside `InitializeSingletons`, once
  after). The second call replaces the instance, so an **unsaved** write made in some `Awake2`
  would be lost. In the new project, call it once.

---

## 5  Managers — how a system is structured

One `Singleton<T>` per system. Each owns its state and exposes **methods for commands** and
**events for changes**:

| Manager | Job | Reusable? |
|---|---|---|
| `Manager_Game` | Central state machine (`Loading/Playing/Won/Failed`), owns the per-frame step order, opens/ends levels, derives `IsPaused`, gates input | **yes** — rewrite the tick body |
| `Manager_Input` | Raw pointer/touch → *intents* (events like `OnTapRequested`, `OnDragChanged`). Mutates nothing | **yes** |
| `Manager_Level` + `JsonLevelSource` | Loads `Resources/Levels/level_N.json` via an index → a `LevelDefinition` | **yes** — replace the schema |
| `Manager_PopUp` | Registry of popups by enum; show/close; dark backdrop; `IsPopUpActive` | **yes, copy as-is** |
| `Manager_Currency` | **Sole writer** of `OfflineData.Coins`; `Add`, `TrySpend`, `OnCoinsChanged` | **yes** |
| `Manager_AudioHaptic` | `PlaySound(SoundType)`, haptics, music/SFX/vibration switches; clips come from `FileRef` | **yes** |
| `Manager_Tutorial` / `Manager_Hint` | When to show a tutorial / hint (the *what* lives in UI presenters) | pattern reusable |
| `Manager_Board`, `_Nest`, `_Queue`, `_Bench` | *(example)* gameplay systems | no |
| `TestManager` | Editor-only cheat panel with `[Button]`s. **Nothing may call it** | yes |

### 5.1  The state machine and frame order (`Manager_Game`)

```csharp
public enum GameState { Loading = 0, Playing = 1, Won = 2, Failed = 3 }   // append-only

void Update()
{
    if (State != GameState.Playing || IsPaused) return;
    float dt = Mathf.Min(Time.deltaTime, _maxDeltaTime);   // clamp hitches
    // THE ORDER IS A RULE — each step reads the previous step's result:
    Manager_SystemA.Instance.Tick(dt);
    Manager_SystemB.Instance.Tick(dt);
    EvaluateEndConditions();                                // → SetState(Won/Failed)
}
```

Other managers **do not** run gameplay in their own `Update`; `Manager_Game` calls their
`Tick`. That's what keeps resolution order deterministic.

### 5.2  Level lifecycle

```
StartLevel(n)
 ├ Manager_Level.Load(n)               → LevelDefinition
 ├ each system .Build(level) / .ResetAll()
 ├ LiveData per-level counters reset
 ├ optional pre-level popup / intro   → wait for it, then FinishOpening
 ├ SetState(Playing); ApplyInputGate()
 └ OnLevelStarted(n)                   → HUD, hints, analytics listen

SetState(Won|Failed) → ApplyInputGate() → OnStateChanged
 └ UI_InGame hears it → waits a delay → ShowPopup(Victory | LevelFailed)
PopUp_Win "Next"   → transition screen → Manager_Game.AdvanceLevel()
                      (CurrentLevel = n+1; Save(); StartLevel(n+1))   ← the ONLY writer of CurrentLevel
PopUp_Failed "Retry" → RestartLevel()
```

### 5.3  Pause is derived, not a flag

```csharp
public bool IsPaused => _introPlaying
                     || Manager_PopUp.Instance.IsPopUpActive      // open OR still animating closed
                     || (tutorial showing && !tutorial.GameplayAllowed);

void ApplyInputGate() => Manager_Input.Instance.AcceptInput = State == GameState.Playing && !IsPaused;
```

Nobody sets "paused = true". Pause is computed from who is on screen, so it can't get stuck.
`ApplyInputGate` re-runs on state change, level open, popup close-finished and tutorial events.

### 5.4  Input → intents

`Manager_Input` reads the **new Input System** (`Pointer.current`) directly (legacy `Input.*`
is disabled). Press that moves past a pixel threshold = drag; otherwise on release = tap →
`Physics.Raycast` → raise an intent event. `Manager_Game` subscribes and decides whether the
intent is legal (state, pause, rules). Cancel the press on `OnApplicationPause/Focus(false)`.

### 5.5  Subscribe *and* read

Unity doesn't order `Start` across scripts. Every listener **subscribes, then reads the current
value immediately**, so it's correct whichever side came up first:

```csharp
void Start()
{
    Manager_Currency.Instance.OnCoinsChanged += SetCoins;
    SetCoins(Manager_Currency.Instance.Coins);   // don't wait for the next change
}
```

Subscribers that `Manager_Game.Start` relies on subscribe in `Awake` and must sit on
**always-active** GameObjects — deactivating them in the scene silently kills the feature.

---

## 6  Tuning — gameplay numbers

Two files, no literals in managers:

```csharp
// Tuning.cs — const defaults + static read facade
public static class Tuning
{
    public const float FireIntervalDefault = 0.05f;
    private static GameTuning Source => GameTuning.Active;
    public static float FireInterval => Source != null ? Source.FireInterval : FireIntervalDefault;
}

// GameTuning.cs — scene component, Inspector overrides
[DisallowMultipleComponent, DefaultExecutionOrder(-1000), ExecuteAlways]
public class GameTuning : MonoBehaviour
{
    [SerializeField] private float _fireInterval = Tuning.FireIntervalDefault;
    public float FireInterval => _fireInterval;
    public static GameTuning Active { get; private set; }
    private void OnEnable() => Active = this;
}
```

Code always reads `Tuning.X`. Designers tweak `GameTuning` on the `Managers` object, live in
Play mode. `-1000` execution order registers it before anyone reads. Note the **scene values
are what ship**, not the `const` defaults.

---

## 7  `ScriptableSingleton<T>` — asset registry

A `ScriptableObject` with a global `Instance`, loaded from `Resources/` by an attribute path:

```csharp
[AssetPath("FileRef")]                                   // → Assets/Resources/FileRef.asset
[CreateAssetMenu(fileName = "FileRef", menuName = "ScriptableObjects/FileRef")]
public class FileRef : ScriptableSingleton<FileRef>
{
    [SerializeField] private Cube _cubePrefab;
    public Cube CubePrefab => _cubePrefab;
}

Instantiate(FileRef.Instance.CubePrefab);
```

How it works (`Essentials/ScriptableSingleton.cs`):
- First `Instance` access reads `[AssetPath]` via reflection → `Resources.Load<T>(path)`.
- **In the Editor only**, if the asset doesn't exist it is created at
  `Assets/Resources/<path>.asset` (`#if UNITY_EDITOR`). In a build a missing asset = `null`.
- No `[AssetPath]` → `Debug.LogError`.
- The editor-time `Awake` destroys a second copy of the asset.

**`FileRef` is the one place assets are assigned** — prefabs, colour palettes, per-state
materials, the `SoundData[]` table, tutorial-per-level rows, milestone rows. Managers ask
`FileRef.Instance` instead of holding their own prefab fields, so a prefab is never wired in
two places that can drift. It has a right-click **Validate references** context menu — keep
one in the new project; a null here fails far from its cause.

Caveat: everything `FileRef` references ships in the build, used or not.

---

## 8  `OfflineData` — the save

One serializable class, stored as JSON in a single `PlayerPrefs` key:

```csharp
[Serializable]
public class OfflineData
{
    // PUBLIC FIELDS ONLY (JsonUtility ignores properties). APPEND-ONLY.
    public int  CurrentLevel = 1;          // 1-based
    public int  PassedLevels = 0;
    public int  Coins = 0;                 // written only by Manager_Currency
    public bool CoinsSeeded = false;       // "starting coins given" flag (see below)
    public int  TutorialsSeenMask = 0;     // one bit per TutorialType ordinal
    public bool IsMusicOn = true, IsSfxOn = true, IsVibrationOn = true;

    private static OfflineData _instance;
    public static OfflineData Instance { get { if (_instance == null) Load(); return _instance; } }

    public void Save()
    {
        PlayerPrefs.SetString("OfflineData", JsonUtility.ToJson(_instance));
        PlayerPrefs.Save();
    }

    public static void Load()
    {
        _instance = PlayerPrefs.HasKey("OfflineData")
            ? JsonUtility.FromJson<OfflineData>(PlayerPrefs.GetString("OfflineData"))
            : new OfflineData();
        // migrations go here (e.g. derive a new field from old ones)
    }
}

// usage
OfflineData.Instance.IsMusicOn = false;
OfflineData.Instance.Save();
```

Rules:
- **Append-only.** Renaming or repurposing a field reinterprets every existing save. A new
  field gets its initializer value for old saves (JsonUtility keeps the default when the key
  is missing) — choose defaults accordingly.
- **One owner per field.** `Coins` only via `Manager_Currency`; `CurrentLevel` only via
  `Manager_Game.AdvanceLevel`; tutorial bits only via `Manager_Tutorial`. Everyone else reads.
- **Use a flag for one-time grants** (`CoinsSeeded`) instead of a non-zero default, so old
  saves and "spent down to 0" stay distinguishable from "new player".
- **Bitmasks indexed by enum ordinal** make those enums append-only too.
- Migrations live in `Load()`.

`LiveData` (same file) is a **static, non-persisted** scratchpad for session state:
`IsGameOn`, `ReviveCountThisLevel`, ... Reset per-level values in `StartLevel`.

---

## 9  UI system

### 9.1  Canvas layout

```
Canvas                        (Screen Space – Overlay)
 ├ Container                  ← SafeArea component (anchors to Screen.safeArea at runtime)
 │   ├ InGame                 ← HUD: UI_InGame (level label, coins, settings gear)
 │   └ PopUps                 ← Manager_PopUp._popUpParent — every popup prefab instance, INACTIVE
 ├ Dark Area                  ← Manager_PopUp._blackArea, full-screen dimmer behind popups
 ├ Tutorial / Hint            ← overlays (Hint must stay active)
 ├ Loading Screen             ← UI_LoadingScreen (first launch only)
 └ Transition Screen          ← UI_TransitionScreen (between levels, SetAsLastSibling)
```

### 9.2  Popups — three layers

```
Popup            (MonoBehaviour)   identity + show/close animation
 └ BasePopUp     : Popup           staggered pop-in of child elements, close button, Bind helpers
    └ PopUp_Win / PopUp_Failed / PopUp_Settings / ...   one per PopupName, content + buttons
```

**`Popup`** — Inspector: `_popupName` (enum), `_holder` (the panel that scales; keeps the dimmer
from scaling), `_isAnimated`, show/close durations.
- `Show(onClose)` → `SetActive(true)`, scale holder 0→1 (`Ease.OutBack`), then `OnComplete()`.
- `Close(onClose)` → scale 1→0 (`Ease.InBack`) → `SetActive(false)` → the close callback, **exactly
  once**. Handles re-open during close and close during open.

**`BasePopUp`** — `_gameObjectToAnimate[]` pop in one after another after the panel lands;
`_closeButton` auto-bound to `Dismiss()`. Subclasses override **`InitButtons()`** (not
`OnEnable`):

```csharp
public class PopUp_Failed : BasePopUp
{
    [SerializeField] private Button _retryButton;

    protected override void InitButtons()
    {
        Bind(_retryButton, () =>                       // Bind clears old listeners + plays click SFX
        {
            Dismiss();                                 // routes through Manager_PopUp
            Manager_Game.Instance.RestartLevel();
        });
    }
}
```

**`Manager_PopUp`** — on `Awake2` collects every `Popup` under `_popUpParent` (including
inactive) into a list, keyed by `PopupName`. Warns on duplicate names.

```csharp
Manager_PopUp.Instance.ShowPopup(PopupName.Victory, onClose: () => { ... });  // closes others by default
Manager_PopUp.Instance.ClosePopup(PopupName.Victory);
Manager_PopUp.Instance.GetPopup<PopUp_Win>(PopupName.Victory).Setup(reward);   // configure before show
bool blocking = Manager_PopUp.Instance.IsPopUpActive;   // true while open OR still closing
// events: OnPopupShown(name), OnPopupClosed(name), OnPopupCloseFinished()
```

A closing popup stays in a "closing" list until its animation ends, so `IsPopUpActive` stays
true and a dismiss tap can't leak through to gameplay.

**Adding a popup:** add a value to `PopupName` (append-only — it's serialized by ordinal on the
prefab), make a `PopUp_X : BasePopUp`, build the prefab with a `Holder` child, drop an instance
under `Canvas/Container/PopUps` (inactive), set its `_popupName`.

### 9.3  HUD and other views

- `UI_InGame` — pure view: subscribes to `OnLevelStarted`, `OnStateChanged`, `OnCoinsChanged`,
  reads current values in `Start`. On Won/Failed it waits `Tuning.Win/FailPopupDelay` and shows
  the outcome popup.
- `UI_LoadingScreen` — timed fake bar on launch; static `OnFinished` event fires immediately if
  you subscribe when it isn't showing (so `Manager_Game.Start` just subscribes `StartFirstLevel`).
- `UI_TransitionScreen.PlayTransition(Action onCovered)` (static; returns `false` when no
  screen is present, so the caller falls back to a plain delay) — panels close, `onCovered`
  runs (swap level), panels open. Always invokes the callback even if disabled, so the player can't get
  stranded. Uses **unscaled** time.
- `GameObjectLayoutControl` — spaces active children on an axis; call `UpdateLayout()` after
  toggling a child (it only recomputes on `OnEnable`).

---

## 10  Scene placement

- **Everything is placed in the one scene by hand**; managers hold `[SerializeField]`
  references to those objects. Runtime-spawned things (cubes, bullets…) come from `FileRef`
  prefabs and are **pooled** by their owning manager.
- **`Managers` GameObject** carries every `Manager_*`, `SingletonManager`, `GameTuning`,
  `TestManager`. Feature-specific managers can live on their own root (`Hint System`) if they
  pair with a presenter component.
- **`GamePlay` root** holds world objects in fixed slots, e.g. *(example)* `Nests/` with five
  `Nest` instances wired into `Manager_Nest._slots[]` left-to-right, `ChickenQueue/Q1..Q3`
  wired into `Manager_Queue._lines[]`.
- Names are load-bearing: editor builders and `PopupViewBinder` find children **by path**. Don't
  rename scene objects casually.
- Wiring is done through the Inspector or an **Editor builder** (`[MenuItem("<CodeName>/Build X")]`,
  static class, re-runnable: regenerates generated children, keeps authored ones). Don't
  hand-edit scene YAML.

---

## 11  Data-driven levels

```
Resources/Levels/level_1.json ... level_N.json
Resources/Levels/level_index.json      ← levelNumber → file, auto-regenerated by an AssetPostprocessor
```

`Manager_Level` picks an `ILevelSource` (`JsonLevelSource`, with a procedural fallback).
`JsonLevelSource.Load(n)` looks up the index, `Resources.Load`s **one** file, parses, unloads the
`TextAsset`, returns a `LevelDefinition`. Unknown `n` wraps around so the game never runs out.
Levels count from **1**. Drop a new JSON in and the index picks it up.

---

## 12  Shared data in `Common.cs`

Enums and tiny structs used across systems: *(example)* `ChickenColor`, `ChickenState`,
`TutorialType`, `MilestoneType`, `SoundType`, `Haptics`, `SoundData`. **Every enum that is
saved, serialized on a prefab, or used as a bit index is append-only** — never renumber or
reuse a value; give them explicit numbers (`Loading = 0, Playing = 1`).

---

## 13  Checklist to start a new game from this

1. New Unity project (URP, Android, new Input System only). Install the packages below.
2. Copy `Scripts/Gameplay/Essentials/` as-is. (Optionally fix the duplicate-singleton guard
   and the double `OfflineData.Load()`, §3–4.)
3. Copy `Popup.cs`, `BasePopUp.cs`, `Manager_PopUp.cs`; reset `PopupName` to your popups.
4. Copy `Manager_AudioHaptic`, `Manager_Currency`, `Manager_Input`, `UI_LoadingScreen`,
   `UI_TransitionScreen` if you want them; strip game-specific references.
5. Write a fresh `Common.cs`, `Tuning.cs` + `GameTuning.cs`, `FileRef.cs`, and clear
   `OfflineData` down to the fields you need.
6. Write `Manager_Game` with your `GameState`, `StartLevel/AdvanceLevel/RestartLevel`, derived
   `IsPaused`, `ApplyInputGate`, and the ordered `Update` tick.
7. Scene: `Managers` object with all managers + `SingletonManager` + `GameTuning`; fill
   `_singletonsToInitialize` in dependency order; `Canvas/Container(SafeArea)/{InGame, PopUps}`
   + `Dark Area`; wire `Manager_PopUp`.
8. Create `Resources/FileRef.asset` (just access `FileRef.Instance` once in the Editor and it
   auto-creates) and assign prefabs/sounds there.
9. Add an Editor builder for any scene structure you'll regenerate more than once.

### Packages

Versions are the ones Shift & Sort ships with (Unity 6000.3).

| Package | Install | Used for |
|---|---|---|
| **DOTween** (free) | [Asset Store](https://assetstore.unity.com/packages/tools/animation/dotween-hotween-v2-27676) or [dotween.demigiant.com](https://dotween.demigiant.com/download.php). Imports into `Assets/Plugins/Demigiant/`; then run *Tools > Demigiant > DOTween Utility Panel > Setup DOTween* (adds the `DOTWEEN` define and `Resources/DOTweenSettings.asset`). | All animation and timed delays. No coroutines. |
| **TextMesh Pro** | Comes with `com.unity.ugui` 2.0 in Unity 6 ([manual](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/TextMeshPro/index.html)). Import the essentials once: *Window > TextMeshPro > Import TMP Essential Resources* (→ `Assets/TextMesh Pro/`). | All UI text. Font assets are made from the TTFs in `Fonts/`. |
| **Input System** `com.unity.inputsystem` 1.19.0 | Package Manager → Unity Registry ([manual](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/manual/index.html)). Set *Player > Active Input Handling* to **Input System Package (New)**. | `Manager_Input` (`Pointer.current`), `InputSystemUIInputModule`. |
| **Haptic Feedback** `xyz.candycoded.hapticfeedback` 1.0.3 | Not on OpenUPM: *Package Manager > + > Install package from git URL* → `https://github.com/CandyCoded/HapticFeedback.git#v1.0.3` ([repo](https://github.com/CandyCoded/HapticFeedback), MIT). Its code compiles only for Android, iOS and the Editor, so wrap calls in `#if UNITY_ANDROID \|\| UNITY_IOS \|\| UNITY_EDITOR`. | Device vibration (`Haptics.Play`). No-op in the Editor. |
| **Test Framework** `com.unity.test-framework` 1.6.0 | Unity Registry ([manual](https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/manual/index.html)); usually preinstalled. | EditMode / PlayMode tests. |

Copied source, not a package:

- **Sortify `[Button]`** (`Scripts/Sortify/`: `ButtonAttribute.cs`, `SortifyHelper.cs`, `Editor/ButtonAttributeDrawer.cs`). It turns parameterless methods into Inspector buttons. `TestManager` uses it. Copy the folder from an existing project; there is no package to install.

### Rules to carry over

- Override `Awake2()`, never `Awake()`, in a singleton.
- Managers decide, views display. One manager owns the frame order.
- Events default to `delegate { }`; listeners subscribe *and* read.
- Save fields and serialized enums are append-only; each save field has exactly one writer.
- Gameplay numbers go in `Tuning`/`GameTuning`, assets go in `FileRef`, never literals or
  per-manager prefab fields.
- Runtime code never uses `UnityEditor` without `#if UNITY_EDITOR`.
- `Resources/` ships in full — keep only what's loaded by name there.
