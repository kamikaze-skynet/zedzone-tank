# Changelog

## 1.3.0
- New **Battle Tank**: a real 3D model (built in Blender, `blender/build_tank.py`) drawn by the game's own car camera, with a turret that turns in 3D. Build a **Battle Tank Kit** at a workbench (80 Iron Plate, 30 Metal Component, 15 Copper Tube, 25 Iron Wire, 15 Copper Wire), carry it and press Page Up. Settings under `[BattleTank]`. See BLENDER.md for editing the model and connecting Claude to Blender.

## 1.2.3
- The roof machine gun had no sound: it picked the flamethrower's looping sound. It now picks a real machine gun's fire sound (setting `Effects.MachineGunSoundFrom` picks a specific gun).

## 1.2.2
- Fixed the tank being drawn backwards (W looked like reverse). The nose direction is now learned from how the tank actually moves when you hold W or S, instead of from the base car's headlights.

## 1.2.1
- The tank's own shells can't hurt anyone inside a tank, driver or riders (`Armor.CrewSafeFromOwnShells`, on). People standing nearby outside the tank can still be hit.

## 1.2.0
- Tanks are now crafted: build a **Tank Kit** at a workbench (60 Iron Plate, 20 Metal Component, 10 Copper Tube, 20 Iron Wire, 10 Copper Wire), carry it and press Page Up. The kit is used up. Setting `General.RequireTankKit` (on) can turn this off for free call-ins. Spawn Menu's Tank kit still gives a tank without a kit.

## 1.1.0
- The main gun now fires **105 mm Tank Shells**, a new ammo item (own art, stacks to 4, found in rooms, craftable). The item ships in `items\Ammo` next to Tank.dll and the plugin loads it. Setting `MainGun.RocketsIfNoShells` falls back to the game's recoilless rockets if the item fails to load.
- Gun sounds now come through the game's audio system. The art is rendered at the game's 45 degree car camera angle, and the tank no longer starts out facing backwards.

## 1.0.0
- First version: a drivable tank built on the game's own car (gasoline, refuel, cargo, damage) with original art in the game's car style: a 3D model pre-rendered at 32 angles for the hull and 32 for the turret. Turret aims at the mouse; left click fires the game's recoilless rockets, right click fires a roof machine gun. Page Up calls one in, Shift+Page Up refuels from carried gas cans.
