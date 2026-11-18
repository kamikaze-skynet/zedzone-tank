# Changelog

## 1.5.11

- Battle Tank picture is lifted toward the camera (BattleTank.PictureLift, 4) so tall grass can't hide it by depth. The grass was already drawn before the tank in 1.5.10, so the cover wasn't a draw-order problem.
- Draw check now logs how solid the tank picture is (alpha), the main camera and quad depth, and the grass shader settings.

## 1.5.10

- Battle Tank picture is placed above the tall grass meshes themselves: it reads the grass cells' sorting layer, order and render queue and draws after them (the render queue wins over sorting layers, which is why 1.5.9 didn't help). Logs "Grass draw order:" once.

## 1.5.9

- Battle Tank draws on top of tall grass and bushes like the classic tank: its car picture goes on the player's sprite layer at Look.SortingOrder (BattleTank.DrawOnTop). The game sorts car pictures in with the grass, which buried this big tank.
- The extra grass pushing from 1.5.5-1.5.8 is off by default now (BattleTankBase.GrassPushArea 0); the game's own car grass pushing stays.

## 1.5.8

- Grass push uses many small circles instead of one huge one (a huge circle only bent the grass a little near the tank). Circles of GrassPushCircle metres cover GrassPushArea around the tank plus GrassPushBelow under it, closest to the tank first, as many as the game's free slots allow. New config keys, so earlier values don't carry over.

## 1.5.7

- Grass push actually covers the Battle Tank: the game refused one big grass circle, so the area (GrassPushRadius around the middle, stretched GrassPushDown below it) is now tiled with the biggest circle the game accepts, using the free grass-presser slots. Tank.log reports the circle size and count.

## 1.5.6

- Bigger grass push around the Battle Tank: radius 2x half the hull (BattleTankBase.GrassPushRadius, replaces GrassPushSize), plus a second circle below the tank (GrassPushDown 0.6), since grass below it grows up the screen over the hull.

## 1.5.5

- The Battle Tank pushes grass down over a bigger area: an extra grass presser over the tank's middle, covering the whole tank and a margin around it (BattleTankBase.GrassPushSize, default 1.4). Grass is pushed down, not cut, like any other vehicle. Tank.log prints the first few 'Tank grass press' lines.

## 1.5.4

- Machine gun sound keeps up with a held trigger: every round now plays its own overlapping copy of the clip through a game audio source (the game's PlayAt skipped a clip that was still sounding, so only the odd shot was heard).
- The automatic sound pick prefers a real machine gun (M249 and the like) over submachine guns.

## 1.5.3

- Grass works like it does for any other vehicle: the Battle Tank no longer crushes grass by default (new setting BattleTankBase.TankCrushesGrass, off by default; the old CrushGrass setting is no longer read). Instead the tank's vehicle size is set to the tank's footprint, which the game uses for a car's grass pressing, path footprint and body checks.

## 1.5.2

- Grass the Battle Tank crushes grows back after a reload instead of being removed from the world save. Set BattleTankBase.CrushGrassPermanent = true to keep permanent tank trails.

## 1.5.1

- The Battle Tank crushes the grass it drives over: every grass tile under the hull is removed for good, so tall grass no longer covers the tank. Pressing the grass (1.5.0) didn't work because the game only allows a few grass pressers at once. Config BattleTankBase.CrushGrass.

## 1.5.0

- Battle Tank gets its own base instead of a plain pickup: the pickup under it now has a hit box the size of the tank (it used to be pickup-sized, so things clipped into the tank), 4x the weight, 5x the bodywork armour and 80% of the top speed. All of it is in the new BattleTankBase config section (Enabled, Weight, Armour, TopSpeed, TurnRate). Tank.log prints a 'Tank base:' line with the before/after values.
- Tall grass no longer stands over the Battle Tank: grass is flattened under the whole hull every frame (the game only flattens it around a car's middle). Config BattleTankBase.FlattenGrass.
- The game's own vehicle picture field now points at the tank's sharper picture too.

## 1.4.9

- Battle Tank side skirts (and other big flat panels) now show the full camo. The bake read edge wear from the simplified game mesh, where it smeared across whole panels and turned them pale grey; the bake now uses the same flat wear value as the Blender viewport. Turret paint is also baked at its real height, so it gets the same dust as in Blender.

## 1.4.8

- Battle Tank no longer leans: when the tank was called in while its base car was still settling, the tyres gave a tilted frame and the 3D model stayed tilted about 35 degrees inside the car picture. The model now always sits upright and faces straight along the car.

## 1.4.7

- Battle Tank paint is bolder: more contrast and colour in the baked camo and the plain parts, and slightly darker, so the tank no longer looks faint on the map.

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
