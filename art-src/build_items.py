"""Draws the 105 mm tank shell and the Tank Kit (our own art) and packs them as game item mods (.zedmod =
zip of mod.json + sprite.png + preview.jpg + locale) into workshop/content/Tank/items/Ammo/.
The plugin loads that file next to Tank.dll (src/Bundled.cs), so every install route works.
Run:  python art-src/build_items.py"""
import json, os, zipfile
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.join(HERE, "..")

# Never change the guid after release: saves and other mods refer to it.
GUID = "3f7c1d05-a105-4c2b-9e10-105a7a4c0105"
RID = 881105  # requested runtime id; the game's loader remaps it if it clashes
KIT_GUID = "9b2e4f1a-7a4c-4d1e-8c3b-7a4c0e7a4c01"
KIT_RID = 881106
KIT_NAME = "Tank Kit"
KIT_DESC = ("Everything needed to put a main battle tank together: armour plate, a diesel power pack, tracks and a turret, "
            "packed in one heavy crate. Built at a workbench. Carry it and press Page Up to have the tank assembled "
            "in front of you; the kit is used up.")
KIT3_GUID = "c3d0b7e1-5a4e-4f6b-9d2a-3d7a4c0e7a4c"
KIT3_RID = 881107
KIT3_NAME = "Battle Tank Kit"
KIT3_DESC = ("A heavier crate for the Battle Tank: a fully modelled main battle tank with a turret that turns in 3D, "
             "a 105 mm gun and a roof machine gun. Built at a workbench. Carry it and press Page Up to have it assembled "
             "in front of you; the kit is used up (a Battle Tank Kit is used before a Tank Kit).")
# Iron Plate, Metal Component, Copper Tube, Iron Wire, Copper Wire
KIT3_RECIPE = [(8, 80), (62, 30), (11, 15), (12, 25), (13, 15)]
NAME = "105 mm Tank Shell"
DESC = ("A 105 mm high-explosive anti-tank round for the tank's main gun. A brass case full of propellant "
        "and a shaped-charge warhead with a long stand-off probe on the nose. Load it into the tank's cargo or "
        "carry it in your backpack; each shot uses one. Heavy: you won't carry many on foot.")

# Iron Plate, Metal Component, Copper Tube, Iron Wire, Copper Wire
KIT_RECIPE = [(8, 60), (62, 20), (11, 10), (12, 20), (13, 10)]

W, H = 512, 128  # sprite, drawn pointing right (+x), nose at the right

def draw_shell(scale=1):
    s = scale
    im = Image.new("RGBA", (W * s, H * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    cy = H * s // 2
    def box(x0, x1, half, fill, outline=(30, 26, 18, 255)):
        d.rectangle([x0 * s, cy - half * s, x1 * s, cy + half * s], fill=fill, outline=outline, width=2 * s)
    # base rim and brass case
    box(8, 22, 34, (150, 112, 40, 255))
    box(20, 250, 30, (196, 152, 62, 255))
    d.rectangle([24 * s, cy - 26 * s, 246 * s, cy - 18 * s], fill=(232, 196, 110, 255))  # shine
    d.rectangle([24 * s, cy + 18 * s, 246 * s, cy + 26 * s], fill=(160, 120, 46, 255))   # shade
    d.ellipse([10 * s, cy - 8 * s, 20 * s, cy + 8 * s], fill=(110, 80, 30, 255))          # primer
    # case neck and driving band
    d.polygon([(250 * s, cy - 30 * s), (270 * s, cy - 24 * s), (270 * s, cy + 24 * s), (250 * s, cy + 30 * s)],
              fill=(186, 142, 56, 255), outline=(30, 26, 18, 255))
    box(270, 282, 25, (170, 92, 52, 255))  # copper band
    # warhead body (olive drab) tapering to the ogive
    d.polygon([(282 * s, cy - 24 * s), (380 * s, cy - 24 * s), (430 * s, cy - 9 * s), (430 * s, cy + 9 * s),
               (380 * s, cy + 24 * s), (282 * s, cy + 24 * s)], fill=(86, 92, 52, 255), outline=(26, 28, 16, 255))
    d.polygon([(286 * s, cy - 20 * s), (378 * s, cy - 20 * s), (410 * s, cy - 11 * s), (286 * s, cy - 11 * s)],
              fill=(120, 128, 76, 255))
    # yellow HE marking band and black stencil stripe
    box(300, 312, 24, (214, 186, 40, 255))
    box(330, 336, 24, (24, 24, 20, 255))
    # stand-off probe and fuze tip
    box(430, 488, 5, (70, 74, 70, 255))
    d.polygon([(488 * s, cy - 8 * s), (504 * s, cy), (488 * s, cy + 8 * s)], fill=(60, 62, 58, 255), outline=(20, 20, 20, 255))
    return im

def draw_kit(body=(92, 100, 56, 255), title="TANK KIT"):
    im = Image.new("RGBA", (256, 192), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    ol = (24, 26, 16, 255)
    d.rectangle([8, 24, 248, 184], fill=body, outline=ol, width=4)      # crate
    for x in (8, 244):
        d.rectangle([x, 24, x + 4, 184], fill=(70, 76, 42, 255))
    for y in (60, 148):
        d.rectangle([8, y, 248, y + 8], fill=(70, 76, 42, 255), outline=ol, width=1)  # slats
    d.rectangle([8, 8, 248, 30], fill=(104, 112, 64, 255), outline=ol, width=4)       # lid
    for x in (40, 216):
        d.rectangle([x - 10, 4, x + 10, 34], fill=(60, 60, 56, 255), outline=ol, width=2)  # latches
    # stencilled tank silhouette (side view) and text
    st = (210, 200, 150, 255)
    d.rectangle([62, 104, 194, 128], fill=st)          # hull
    d.polygon([(54, 110), (62, 104), (62, 128), (54, 122)], fill=st)
    d.rectangle([96, 88, 154, 104], fill=st)           # turret
    d.rectangle([154, 93, 214, 98], fill=st)           # barrel
    for cx in range(74, 190, 20):
        d.ellipse([cx - 8, 124, cx + 8, 140], fill=st)  # road wheels
    d.text((128 - len(title) * 3, 40), title, fill=st)
    d.text((100, 156), "105 MM", fill=st)
    return im

def pack(key, kind, data, sprite, preview):
    art = os.path.join(REPO, "items", kind, key)
    os.makedirs(art, exist_ok=True)
    out = os.path.join(REPO, "workshop", "content", "Tank", "items", kind)
    os.makedirs(out, exist_ok=True)
    sprite.save(os.path.join(art, "sprite.png"))
    preview.save(os.path.join(art, "preview.jpg"), quality=92)
    guid = data["modGuid"]
    locale = {"langCode": "en_US", "modGuid": guid, "translatorNote": "", "itemName": data["itemName"],
              "itemDescription": data["itemDescription"], "extraFields": []}
    path = os.path.join(out, guid + ".zedmod")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("mod.json", json.dumps(data, indent=4, ensure_ascii=False))
        z.write(os.path.join(art, "sprite.png"), "sprite.png")
        z.write(os.path.join(art, "preview.jpg"), "preview.jpg")
        z.writestr("locale/en_US.json", json.dumps(locale, indent=4, ensure_ascii=False))
    print("built", os.path.relpath(path, REPO), data["itemName"])

def preview_of(img, size, label):
    prev = Image.new("RGB", (512, 512), (34, 38, 30))
    pd = ImageDraw.Draw(prev)
    for y in range(0, 512, 32):
        pd.line([(0, y), (512, y)], fill=(40, 45, 36))
    big = img.resize(size, Image.LANCZOS)
    prev.paste(big, ((512 - size[0]) // 2, (512 - size[1]) // 2), big)
    pd.text((20, 470), label, fill=(220, 200, 120))
    return prev

def main():
    shell = draw_shell(1)
    d = {
        "modType": "Ammo", "modGuid": GUID, "runtimeId": RID, "publishMode": 0, "targetNativeItemId": 0,
        "primaryLangCode": "en_US", "spritePath": "sprite.png", "ppu": 640,
        "spriteRegions": [{"slotName": "Main", "region": {"x": 6, "y": 26, "w": 500, "h": 76}}],
        "itemName": NAME, "itemName_EN": NAME, "itemDescription": DESC, "itemDescription_EN": DESC,
        "itemRarity": 0.9, "hiddenItem": False, "itemInventorySizeX": 4, "itemInventorySizeY": 1,
        "generateItemInRoom": True, "generateItemInField": False, "generateItemInCoastFloat": 0.0,
        "weight": 2.5, "itemPrice": 400.0, "stackNumber": 4,
        # Same explosive ingredients as the Boltgun round, plus iron plate (8) for the shell body.
        "craftRecipeItems": [{"itemId": 7, "itemNumber": 10.0}, {"itemId": 75, "itemNumber": 5.0}, {"itemId": 8, "itemNumber": 3.0}],
        "craftPlatform": 3, "craftAudioType": 0, "craftOutputNumber": 2,
        "repairRecipeItems": [{"itemId": 56, "itemNumber": 1.0}],
        "ammoNumber": 2, "ammoBaseDamage": 3000.0,
    }
    pack("shell105", "Ammo", d, shell, preview_of(shell, (480, 120), "105 mm"))

    kit = draw_kit()
    # The kit is an Ammo-type item only because that type is a plain stackable item; no gun uses it.
    # Recipe (game ids confirmed in game): Iron Plate 8, Copper Tube 11, Iron Wire 12, Copper Wire 13,
    # Metal Component 62. Built at the workbench (craftPlatform 2, same as the Boltgun guns).
    k = {
        "modType": "Ammo", "modGuid": KIT_GUID, "runtimeId": KIT_RID, "publishMode": 0, "targetNativeItemId": 0,
        "primaryLangCode": "en_US", "spritePath": "sprite.png", "ppu": 256,
        "spriteRegions": [{"slotName": "Main", "region": {"x": 6, "y": 2, "w": 244, "h": 184}}],
        "itemName": KIT_NAME, "itemName_EN": KIT_NAME, "itemDescription": KIT_DESC, "itemDescription_EN": KIT_DESC,
        "itemRarity": 0.99, "hiddenItem": False, "itemInventorySizeX": 4, "itemInventorySizeY": 3,
        "generateItemInRoom": False, "generateItemInField": False, "generateItemInCoastFloat": 0.0,
        "weight": 40.0, "itemPrice": 20000.0, "stackNumber": 1,
        "craftRecipeItems": [{"itemId": i, "itemNumber": float(n)} for i, n in KIT_RECIPE],
        "craftPlatform": 2, "craftAudioType": 0, "craftOutputNumber": 1,
        "repairRecipeItems": [{"itemId": 8, "itemNumber": 1.0}],
        "ammoNumber": 1, "ammoBaseDamage": 0.0,
    }
    pack("tankkit", "Ammo", k, kit, preview_of(kit, (384, 288), "Tank Kit"))

    # Battle Tank Kit: same kind of item, darker crate; its preview is a render of the 3D model
    kit3 = draw_kit((70, 74, 46, 255), "BATTLE TANK")
    k3 = dict(k)
    k3.update({"modGuid": KIT3_GUID, "runtimeId": KIT3_RID, "itemName": KIT3_NAME, "itemName_EN": KIT3_NAME,
               "itemDescription": KIT3_DESC, "itemDescription_EN": KIT3_DESC, "weight": 50.0, "itemPrice": 30000.0,
               "craftRecipeItems": [{"itemId": i, "itemNumber": float(n)} for i, n in KIT3_RECIPE]})
    render = os.path.join(REPO, "blender", "out", "preview_iso.png")
    if os.path.exists(render):
        r = Image.open(render).convert("RGB")
        w, h = r.size
        prev3 = r.crop(((w - h) // 2, 0, (w - h) // 2 + h, h)).resize((512, 512), Image.LANCZOS)
    else:
        prev3 = preview_of(kit3, (384, 288), "Battle Tank Kit")
    pack("battletankkit", "Ammo", k3, kit3, prev3)

if __name__ == "__main__":
    main()
