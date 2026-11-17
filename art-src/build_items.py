"""Draws the 105 mm tank shell (our own art) and packs it as a game item mod (.zedmod =
zip of mod.json + sprite.png + preview.jpg + locale) into workshop/content/Tank/items/Ammo/.
The plugin loads that file next to Tank.dll (src/Bundled.cs), so every install route works.
Run:  python art-src/build_items.py"""
import json, os, zipfile
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.join(HERE, "..")
ART = os.path.join(REPO, "items", "Ammo", "shell105")
OUT = os.path.join(REPO, "workshop", "content", "Tank", "items", "Ammo")

# Never change the guid after release: saves and other mods refer to it.
GUID = "3f7c1d05-a105-4c2b-9e10-105a7a4c0105"
RID = 881105  # requested runtime id; the game's loader remaps it if it clashes
NAME = "105 mm Tank Shell"
DESC = ("A 105 mm high-explosive anti-tank round for the tank's main gun. A brass case full of propellant "
        "and a shaped-charge warhead with a long stand-off probe on the nose. Load it into the tank's cargo or "
        "carry it in your backpack; each shot uses one. Heavy: you won't carry many on foot.")

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

def main():
    os.makedirs(ART, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    sprite = draw_shell(1)
    sprite.save(os.path.join(ART, "sprite.png"))
    big = draw_shell(1).resize((480, 120), Image.LANCZOS)
    prev = Image.new("RGB", (512, 512), (34, 38, 30))
    pd = ImageDraw.Draw(prev)
    for y in range(0, 512, 32):
        pd.line([(0, y), (512, y)], fill=(40, 45, 36))
    prev.paste(big, (16, 196), big)
    pd.text((20, 360), "105 mm", fill=(220, 200, 120))
    prev.save(os.path.join(ART, "preview.jpg"), quality=92)

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
    locale = {"langCode": "en_US", "modGuid": GUID, "translatorNote": "", "itemName": NAME,
              "itemDescription": DESC, "extraFields": []}
    path = os.path.join(OUT, GUID + ".zedmod")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("mod.json", json.dumps(d, indent=4, ensure_ascii=False))
        z.write(os.path.join(ART, "sprite.png"), "sprite.png")
        z.write(os.path.join(ART, "preview.jpg"), "preview.jpg")
        z.writestr("locale/en_US.json", json.dumps(locale, indent=4, ensure_ascii=False))
    print("built", os.path.relpath(path, REPO))

if __name__ == "__main__":
    main()
