# Tank (ZED ZONE mod)

A drivable main battle tank for ZED ZONE. BepInEx IL2CPP plugin, GUID `girthy_mods.zedzone.tank`.

## How to use

- **Page Up** puts a tank a few tiles in front of you (one per save by default, see `MaxTanks`).
- Get in like any car, start the engine and drive with the usual car keys. It is one of the game's own cars underneath, so it **burns gasoline from its fuel tank exactly like the game's cars** and refuels the same way. **Shift + Page Up** next to it pours the gas cans you carry into it.
- While you drive, the **turret turns toward your mouse** (at a set traverse speed).
  - **Left click** fires the main gun: a **105 mm Tank Shell** (a new ammo item this mod adds; it spawns in rooms and can be crafted). Each shot uses one shell from the tank's cargo hold or your backpack (setting `MainGun.UseAmmo`), reload 2.5 s.
  - **Right click** fires the **roof machine gun**, which works like the Helicopter mod's door gun: it locks on to the hostile nearest the mouse. Uses 5.56mm or 7.62x39mm rounds from the cargo or your backpack.
- Crew inside take only 10% of the damage they would take in a car (`Armor.CrewDamageTaken`).

Spawn Menu (1.3.0+) has a **Tank kit**: a tank, 2 gas cans, 20 105 mm shells and 500 machine gun rounds.

Settings are in `BepInEx\config\girthy_mods.zedzone.tank.cfg` (or the F11 Mod Menu). Logs go to `BepInEx\Tank.log`.

## Planned

- Crew seats for up to 4 players (driver, main gunner, machine gunner, commander), synced in multiplayer.

## Building

```
dotnet build src\Tank.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\ZED ZONE" -p:CopyToMods=true
```

The art works like the game's own cars: the tank is a 3D block model (`art-src/render_angles.py`, Python + NumPy + Pillow) pre-rendered from a tilted overhead view at 32 headings for the hull and 32 for the turret. In game the mod shows the hull frame that matches the tank's heading and the turret frame that matches where it aims. `python art-src/render_angles.py src` rewrites both sprite sheets.

## Credits

The car-tagging approach, gun lock-on, tracer effects and fuel-can code come from Vanish's Industrial Age helicopter, used with permission. Art is original.
