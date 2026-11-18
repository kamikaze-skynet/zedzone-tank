# The Battle Tank's 3D model, Blender and Claude

The Battle Tank is a real 3D model. The game draws its cars by filming each car's 3D model with
its own camera and showing that picture in the world; the mod swaps the base car's body for our
hull and turret meshes, so the game's camera and lighting draw it, and the turret turns in 3D.

| File | What it is |
|---|---|
| `blender/build_abrams.py` | Builds the Battle Tank (M1A1 Abrams-style) in Blender from code and exports it |
| `blender/build_tank.py` | The first, generic tank design (kept for reference; its turret sits 2.0 m up, so it needs `TurretPivot` changed back in `src/Model3D.cs`) |
| `blender/out/tank_model.blend` | The finished scene (open it in Blender to look around) |
| `blender/out/preview_*.png` | Renders |
| `src/model3d/tank_hull.obj`, `tank_turret.obj` (+ `.mtl`) | What the plugin loads (embedded in Tank.dll) |
| `blender/build.ps1` | Runs Blender, rebuilds the items and the plugin |

Model rules the plugin relies on: metres, +X is the front, +Z is up. The hull's origin is the
ground point under the turret ring; the turret's origin is the ring centre at deck height (the
plugin puts it 1.72 m up, `TurretPivot` in `src/Model3D.cs`). The barrel tip is about 7.7 m in front of the ring. Colours come from
the material colours (Base Color), one material per colour.

## 1. Set up Blender on your PC

1. Install Blender 4.2 or newer from https://www.blender.org/download/ (the normal installer).
2. Rebuild everything from the repo folder:

```powershell
cd F:\zed-zone-mods\zedzone-tank
.\blender\build.ps1
```

That runs Blender without opening a window, writes `blender\out`, copies the model into
`src\model3d`, and rebuilds Tank.dll.

## 2. Edit the model by hand

Open `blender\out\tank_model.blend`. Two objects: `TankHull` and `TankTurret`.
When you're done, export each one on its own:

1. Click the object, then File > Export > Wavefront (.obj).
2. Tick **Selected Only**, **Triangulated Mesh**, **Materials: Export**; set **Forward Axis = X**, **Up Axis = Z**.
3. Save as `src\model3d\tank_hull.obj` (or `tank_turret.obj`).
4. Build: `dotnet build src\Tank.csproj -c Release`.

Running `build.ps1` again rebuilds from `build_abrams.py` and overwrites hand edits, so either keep
editing by hand, or change `build_abrams.py` (the better way when Claude does the work).

## 3. Let Claude drive Blender (Blender MCP)

Blender MCP is a free open-source add-on plus a small server that lets Claude look at and change
your open Blender scene (it runs Python inside Blender).

1. Install **uv** (runs the server). In PowerShell:
   ```powershell
   powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
   ```
2. Download `addon.py` from https://github.com/ahujasid/blender-mcp (the green Code button, or open
   the file and click "Download raw file").
3. In Blender: Edit > Preferences > Add-ons > the arrow menu > **Install from Disk**, pick `addon.py`,
   and tick **Blender MCP**.
4. In Blender's 3D view press **N**, open the **BlenderMCP** tab, and click **Connect to Claude**.
5. Connect Claude to it:
   - **Claude Desktop:** Settings > Developer > Edit Config, and add:
     ```json
     {
       "mcpServers": {
         "blender": { "command": "uvx", "args": ["blender-mcp"] }
       }
     }
     ```
     then restart Claude Desktop. A hammer icon shows the Blender tools.
   - **Claude Code:** `claude mcp add blender -- uvx blender-mcp`
6. Ask Claude things like "open blender\out\tank_model.blend, add spare track links to the turret
   sides, then export TankTurret to src\model3d\tank_turret.obj with forward X, up Z, triangulated".

Only run one Claude app connected to Blender at a time. The add-on can run any Python inside
Blender, so only connect it to Claude.

## 4. How Claude builds it in the cloud

Cloud sessions use Blender as a Python module (`pip install bpy`) and run
`python blender/build_abrams.py`, which is the same script as `build.ps1` runs on your PC. Renders use
Cycles on the CPU because there's no graphics card there.

## Desert Sentinel (the 1.4.0 Battle Tank)

The in-game Battle Tank is now made from the hand-refined Desert Sentinel scene (kept on the modding PC, not in this repo).
`blender/sentinel/export_game.py` turns that scene into the game files:

1. joins everything under the `Turret` empty into one turret mesh and everything else into one hull mesh, cutting the polygon count (tracks and running gear the most);
2. unwraps the painted parts and bakes their colour (camo, decals, procedural paint) into `tank_hull.png` / `tank_turret.png`; plain-coloured parts keep their colour in the `.mtl`;
3. exports `tank_hull.obj` (origin on the ground under the turret ring) and `tank_turret.obj` (origin at the ring, 1.86 m up).

Run it inside Blender with `exec(open(r"F:\zed-zone-mods\tank-blender\export_game.py", encoding="utf-8").read())`, then copy the files from `F:\zed-zone-mods\tank-blender\game` into `src/model3d` (rotate to Blender axes and gzip the `.obj` files as the 1.4.0 commit did). `sentinel_helpers.py` holds the mesh-building helpers the scene's parts were made with.
