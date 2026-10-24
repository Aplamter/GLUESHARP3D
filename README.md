# Glue3D in C# (raylib)

Glue3D converted from Scratch (`Glue3D.sb3`) into a native C# desktop app. It uses
[raylib](https://www.raylib.com) through [Raylib-cs](https://github.com/ChrisDill/Raylib-cs)
for graphics, input and sound. It doesn't need a browser or Scratch.

## Run it

The prebuilt Windows version is `Glue3D.exe` and needs nothing else installed. Keep the
`assets` folder next to the exe.

- **F11** (or **Alt+Enter**) toggles fullscreen. The window can also be resized.
- **3D view camera (Unity-style):**
  - Hold the **right mouse button** and drag to look around. The cursor hides and comes back where it was.
    You can look straight up and down (±90°, the original stopped at 45°).
  - **Mouse wheel** moves forward/back along the view, and **middle-button drag** slides the camera.
  - W/A/S/D move as before. While the right button is held, W/S also follow the view up/down, and **Q/E** move down/up.
  - Settings (sensitivity, invert-Y, pitch limit, zoom and pan speed) are at the top of `Custom/CameraControls.cs`.
- **Scene toolbar** (top-left of the 3D view): **Select**, **Move**, **Rotate**, **Scale**, and **Show gizmo**.
  - Move/Rotate/Scale: click an object to select it, then drag its gizmo. Move has an arrow per axis, plus a
    center square that slides the object on the ground. Rotate has a ring per axis (green ring = yaw).
    Scale has a handle per axis, plus a center square for uniform scale.
  - Hold **Ctrl** while dragging to snap (0.25 units, 15°, 0.1 scale). A readout shows the values.
  - With the gizmo turned off, drag the object itself: Move slides it, Rotate turns it, Scale resizes it.
  - **Select** works like the original Glue3D: clicking an object opens its menu (delete, copy, assign camera…).
  - The toolbar hides while a dialog or menu is open, while a Glue3D scene tool is active, and while the game runs.

## Build it yourself

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download), then:

```bash
dotnet run -c Release                                   # build and run
dotnet publish -c Release -r win-x64 -o publish-win     # single self-contained Glue3D.exe
dotnet publish -c Release -r linux-x64 -o publish-linux # also works on Linux/macOS (osx-arm64)
```

## Where things are

| Path | What it is |
| --- | --- |
| `Generated/Glue3DSprite.g.cs` | **All of Glue3D's scripts**, about 15,000 lines of C#. Each custom block is a method, and each "when ..." script is an iterator. |
| `Generated/Stage.g.cs` | The project's 531 global variables and 44 lists. |
| `Generated/Project.g.cs` | Creates the stage and sprite with their starting position, costume and so on. |
| `Runtime/` | A small Scratch engine written for this project (see below). |
| `Custom/` | Hand-written editor additions: `CameraControls.cs` (Unity-style camera) and `SceneGizmo.cs` (toolbar + gizmos). The converter never overwrites these. |
| `assets/costumes`, `assets/sounds` | 278 costumes (SVGs pre-rendered to high-resolution PNGs) and 120 sounds. |
| `assets/initial-lists.json` | Starting contents of the lists (kept out of the code to keep it readable). |
| `tools/` | The converter that produced `Generated/` and `assets/`. Run it again on any .sb3. |

### How the Scratch blocks map to C#

```csharp
// Scratch: when green flag clicked / forever / broadcast [Tick]
IEnumerator WhenGreenFlagClicked()
{
    StartEngine(0);
    while (true)
    {
        Broadcast("Tick");
        yield return null; // next frame
    }
}
```

- **Custom blocks** become methods, such as `TextOnScaleCenterGbcCursorLimitScroll(text, x, y, ...)`.
  The original Scratch block text is in the `<summary>` above each one.
- **Variables** are `Value` fields: `stage.renderDrawX` for global ones, plain names for this sprite's own.
  A `Value` is a number, text or true/false, and it converts between them using Scratch's rules.
  `.Num`, `.Str` and `.Bool` read it as each type.
- **Lists** are `ScratchList` objects with Scratch's 1-based indexes: `stage.ramEngineData.Item(79)`.
- **Operators** come from `Runtime/Ops.cs`. `Eq`, `Lt` and `Gt` compare like Scratch: case-insensitive,
  and `"10" = 10`. `Mod` keeps the divisor's sign, and `Sin`/`Cos` work in degrees and are rounded like Scratch's.
- `yield return null;` marks where Scratch waits for the next frame. Code that ran "without screen refresh"
  is ordinary C# methods with no pauses.

### The runtime (`Runtime/`)

| File | Responsibility |
| --- | --- |
| `Value.cs` | Scratch values, plus JavaScript-exact number parsing and printing (`0.1 + 0.2` prints `0.30000000000000004`, as in Scratch) |
| `Ops.cs` | Operator blocks and comparisons |
| `ScratchList.cs` | Lists (`last`, `random`, out-of-range rules) |
| `Targets.cs` | Stage and sprite blocks: motion, looks, pen, sound, clones, color conversion |
| `Engine.cs` | Window and main loop. Runs scripts frame by frame like Scratch's sequencer, which keeps running scripts until something is drawn or 75% of the frame time is used. |
| `Renderer.cs`, `Shaders.cs` | Pen layer and stamps. The GLSL is ported from Scratch's own renderer: round-capped anti-aliased pen lines, plus the color, brightness, ghost and pixelate effects. |
| `Input.cs` | Mouse in stage coordinates, and keyboard using Scratch's key names |
| `Audio.cs` | Sounds, with per-sprite volume, pitch and pan |
| `Monitors.cs` | On-stage list displays ("show list") |
| `Automation.cs` | Scripted input for testing only. It's off unless `SCRATCH_AUTOMATION` is set. |

## Changes from the original Scratch project

| Change | Where |
| --- | --- |
| Camera turns by right-mouse dragging instead of the arrow keys | `EngineFreeCamera` in `Generated/Glue3DSprite.g.cs` (marked `// EDITED`), `Custom/CameraControls.cs`, `Runtime/Input.cs` |
| Look up/down limit 90° instead of 45°, plus wheel zoom, middle-drag pan and Q/E | `EngineFreeCamera` (`// EDITED`), `Custom/CameraControls.cs` |
| Sky image stops sliding past 45° so its edge never shows | the 2D skybox in `EngineScreenRender` (`// EDITED`) |
| Scene toolbar and move/rotate/scale gizmos | `Custom/SceneGizmo.cs`, hooked up in `Program.cs` |
| Mouse wheel sends Scratch's up/down-arrow "key pressed" events (Glue3D scrolls lists this way) | `Runtime/Input.cs` |

**About looking straight down:** Glue3D's renderer is a 2.5D raycaster (like Doom), which fakes looking up and
down by sliding the picture. You can now look a full 90°, but walls stay vertical instead of tilting the way they
would in Unity. True tilting would need a new 3D renderer.

If you re-run the converter on the .sb3, `Generated/` is rebuilt and the `// EDITED` lines need re-applying.

## How it was checked

The C# app, TurboWarp and the JavaScript version were driven through the same clicks: the welcome
screen, the File menu, Settings, setting up the 2D raycasting engine, and looking around in the 3D
view. All three produced the same screens. Sound was checked by recording the app's output.

## Known differences from Scratch/TurboWarp

- SVG costumes are pre-rendered to PNG at 4–8× resolution with Scratch's fonts. Text set in Windows fonts
  like Gadugi uses Scratch's Sans Serif fallback, as in TurboWarp on a computer without those fonts.
- `Infinity - Infinity` and `0 × Infinity` give NaN in the C# code, where Scratch would first turn the
  NaN into 0. This only happens with infinite values.
- Only blocks this project uses were converted. Others (speech bubbles, "ask", other extensions) appear
  as `// (unsupported block: ...)` comments if you convert another project.
