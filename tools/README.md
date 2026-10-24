# Scratch → C# converter

These are the two tools that produced `Generated/` and `assets/` from `Glue3D.sb3`.

```bash
# 1. unzip the project
mkdir sb3 && cd sb3 && unzip ../MyProject.sb3 && cd ..

# 2. costumes/sounds -> assets/ (renders SVGs in Chromium with Scratch's fonts)
npm install
npx playwright install chromium
node tools/rasterize.js sb3 assets

# 3. blocks -> C#
python3 tools/sb3_to_cs.py sb3 Generated assets --namespace Glue3D --title "My Project" --rename "OldSpriteName=NiceName"
```

Then copy `scratch-render-fonts/src/NotoSans-Medium.ttf` to `assets/fonts/`, which list monitors use,
and build with `dotnet run -c Release`.

- `--rename` gives a sprite a friendlier class name. Scratch sprite names can be any characters,
  e.g. Glue3D's sprite is just a backtick.
- The generator prints any blocks it doesn't support. They're left in the code as comments.
- All three tools need: Python 3.8+, Node.js 18+ and the .NET 8 SDK.
