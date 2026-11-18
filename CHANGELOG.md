# Changelog

## 1.4.6

- Battle Tank see-through fix: the 3D tank was on the map's Vehicle layer, so the main camera drew it straight onto the map (unsorted, see-through, pale) instead of the car camera filming it into the car's picture like every other car. It now goes on the car camera's own layer (VehicleModel).
- The car camera may now clip up to 12 m behind itself (BattleTank.CameraNearClip), so the turret and gun are no longer cut off when they swing toward it.
- The car's picture is 768 px instead of 256 (BattleTank.PictureSize) so the zoomed-out tank stays sharp.

## 1.4.5

- Draw diagnostics for the Battle Tank (config Debug.DrawDiagnostics, on by default). A few times after the tank appears, Tank.log gets a 'Draw check' block: the car camera (clear mode, background, layers, clip range, render target format), whether the tank sits inside the camera's clip range, the quad that shows the car on the map with all its material settings, every renderer on the car, and all cameras.

## 1.4.4

- Battle Tank: materials are forced to draw solid (opaque queue, depth writes, no alpha blending) and the mesh gets solid white vertex colours, so parts no longer show through each other.

## 1.4.3

- Battle Tank: the car shader's spray-paint layer now gets a fully transparent texture. Left empty, it read as solid white and washed the whole tank out.

## 1.4.2

- Battle Tank: parts no longer look pale or see-through. The camo was also going into the car shader's spray-paint layer, which is drawn on top of the paint; that layer is now left empty, and Unity's lightmap slots are left alone.

## 1.4.1

- Battle Tank paint fix: the car shader was still drawing the base car's own colour sheet over the Desert Sentinel, which made it look patchy red, white and blue. Every texture slot of the shader now gets the tank's baked camo (or a solid colour for plain parts).

## 1.4.0
- The Battle Tank is now the **Desert Sentinel**, a far more detailed M1-style tank modelled in Blender: angled wedge turret with a gun slot and round mantlet, commander's cupola with six vision blocks and a detailed .50 cal on an armoured cradle, loader's M240 on a swing arm, gunner's sight with open ballistic doors, thermal viewer on a pedestal, flat deck with a short glacis, headlight clusters, tow clevises and shackles, stowed tow cables, full-height side skirts with armour tiles, bustle rack with jerry cans and a spare road wheel.
- Its camouflage is now painted on: the model carries baked paint textures instead of flat colours (the mod loads UVs and `map_Kd` textures from the model files).
- The roof machine gun now fires from the .50 cal's muzzle, and the main gun from the new barrel's tip.
- The model files are stored compressed, so the DLL stays around 9 MB.

## 1.3.1
- Battle Tank model rebuilt with far more detail (about 48,000 triangles, was 6,500): individual track links with guide horns, paired road wheels with hub bolts, toothed sprockets, spoked idlers, bolted skirts, louvred engine deck, tow cables, periscopes, commander's cupola with vision blocks, gun bellows and sleeve bands, smoke launchers, loaded stowage basket. Smooth shading now carries into the game.

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
