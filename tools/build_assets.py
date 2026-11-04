#!/usr/bin/env python3
"""Build all runtime assets for the BeamNG prank app."""
import os, glob, shutil
from PIL import Image

REF = os.path.dirname(os.path.abspath(__file__))
ROOT = "/data/data/com.termux/files/home/beamng-prank"
OUT = os.path.join(ROOT, "src", "Assets")

def clean(d):
    if os.path.isdir(d):
        shutil.rmtree(d)
    os.makedirs(d, exist_ok=True)

def save(img, name, q=88):
    p = os.path.join(OUT, name)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    img.convert("RGB").save(p, quality=q, optimize=True, subsampling=1)
    return os.path.getsize(p)

total = 0
def report(name, size, w, h):
    global total
    total += size
    print(f"{name:34s} {w}x{h}  {size//1024:5d} KB")

clean(os.path.join(OUT, "cards"))
clean(os.path.join(OUT, "load"))


# --- cosmetic repairs baked into the screenshots -----------------------
def clean_player_name(im, box=(64, 1031, 191, 1054), src_x=186):
    """Erase the streamer's nickname by stretching a clean column of the same bar."""
    x0, y0, x1, y1 = box
    strip = im.crop((src_x, y0, src_x + 3, y1))
    patch = strip.resize((x1 - x0, y1 - y0), Image.BILINEAR)
    im.paste(patch, (x0, y0))
    return im


def fix_quit_button(im, box=(1213, 719, 1451, 769)):
    """The screenshot caught Quit in its red hover state, with the mouse over it.
    Repaint it as a normal idle button and remove the baked cursor and red glow."""
    x0, y0, x1, y1 = box
    px = im.load()
    h = y1 - y0

    def grey(y):
        t = max(0.0, min(1.0, (y - y0) / float(h - 1)))
        return (int(76 - 12 * t), int(70 - 10 * t), int(58 - 8 * t))

    # panel body + whatever red glow bled outside of it
    for y in range(y0 - 6, y1 + 16):
        inside = y0 <= y < y1
        for x in range(x0 - 8, x1 + 8):
            r, g, b = px[x, y]
            if inside and min(r, g, b) > 120:           # white icon / label
                base = grey(y)
                k = min(1.0, (min(r, g, b) - 90) / 150.0)
                px[x, y] = (int(base[0] + (255 - base[0]) * k),
                            int(base[1] + (255 - base[1]) * k),
                            int(base[2] + (255 - base[2]) * k))
                continue
            red = max(0.0, min(1.0, (r - max(g, b) - 6) / 45.0))
            if red <= 0.02:
                continue
            if inside:
                target = grey(y)
            else:
                l = int((r + g + b) / 3.0 * 0.62)
                target = (l, l, l)
            px[x, y] = (int(r + (target[0] - r) * red),
                        int(g + (target[1] - g) * red),
                        int(b + (target[2] - b) * red))

    # the baked mouse cursor sits in the lower left corner of the button
    for y in range(750, y1 + 1):
        for x in range(1230, 1264):
            px[x, y] = grey(min(y, y1 - 1))
    # ...and its tip pokes out below the button - mirror clean pixels from underneath
    for i, y in enumerate(range(y1, y1 + 12)):
        for x in range(1232, 1262):
            px[x, y] = px[x, y1 + 12 + (11 - i)]

    # soft top highlight, like the other bottom buttons
    for x in range(x0 + 2, x1 - 2):
        r, g, b = px[x, y0]
        px[x, y0] = (min(255, r + 26), min(255, g + 25), min(255, b + 22))
    return im


# --- backgrounds -------------------------------------------------------
main = fix_quit_button(clean_player_name(Image.open(os.path.join(REF, "g_33.jpg")).convert("RGB")))
report("bg_main.jpg", save(main, "bg_main.jpg", 90), *main.size)

maps = clean_player_name(Image.open(os.path.join(REF, "g_36.jpg")).convert("RGB"), box=(64, 1048, 191, 1068))
report("bg_maps.jpg", save(maps, "bg_maps.jpg", 90), *maps.size)

veh = Image.open(os.path.join(REF, "V_5.jpg"))
report("bg_vehicles.jpg", save(veh, "bg_vehicles.jpg", 90), *veh.size)

load_last = Image.open(os.path.join(REF, "hi", "088.jpg"))
report("bg_loading.jpg", save(load_last, "bg_loading.jpg", 90), *load_last.size)


def save_label_mask(card_img, name, label_h):
    """White-on-transparent bitmap of just the card's white label text/icon."""
    w, h = card_img.size
    src = card_img.crop((0, h - label_h, w, h)).convert("RGB")
    out = Image.new("RGBA", (w, label_h), (255, 255, 255, 0))
    sp = src.load()
    op = out.load()
    for y in range(label_h):
        for x in range(w):
            r, g, b = sp[x, y]
            lum = (r + g + b) / 3.0
            a = int(max(0.0, min(1.0, (lum - 125.0) / 95.0)) * 255)
            if a:
                op[x, y] = (255, 255, 255, a)
    p = os.path.join(OUT, name)
    out.save(p, optimize=True)
    return os.path.getsize(p)

# --- vehicle cards ------------------------------------------------------
# rows in the source screenshot: Recent, then three rows of Cars and Trucks
VX, VW, VH, VPX = 16, 290, 183, 294
VEH_ROWS = [150, 378, 561, 744]
n = 0
for rowy in VEH_ROWS:
    for c in range(5):
        x, y = VX + c * VPX, rowy
        card = veh.crop((x, y, x + VW, y + VH))
        s = save(card, f"cards/v{n:02d}.jpg", 86)
        report(f"cards/v{n:02d}.jpg", s, VW, VH)
        save_label_mask(card, f"cards/v{n:02d}l.png", 32)
        n += 1


def clean_selected_band(card, label_h):
    """The source frame had this card selected, so its label strip is solid orange.
    Rebuild the strip from the row of art right above it, then darken it the same
    way the other cards' strips are darkened."""
    w, h = card.size
    bh = label_h + 6
    strip = card.crop((0, h - bh - label_h - 4, w, h - label_h - 4))
    band = strip.point(lambda v: int(v * 0.48))
    card.paste(band, (0, h - bh))
    return card

# --- map cards (4 cols, 3 usable rows) ---------------------------------
MX, MW, MH, MPX = 6, 364, 210, 378
MAP_ROWS = [111, 342, 554, 769]
n = 0
for r, rowy in enumerate(MAP_ROWS):
    for c in range(4):
        if r == 0 and c == 3:
            continue
        x, y = MX + c * MPX, rowy
        card = maps.crop((x, y, x + MW, y + MH))
        save_label_mask(card, f"cards/m{n:02d}l.png", 48)
        if n == 8:
            card = clean_selected_band(card, 48)
        s = save(card, f"cards/m{n:02d}.jpg", 86)
        report(f"cards/m{n:02d}.jpg", s, MW, MH)
        n += 1

# --- loading animation strip (frames 10..88 of the 30fps grab) ---------
STRIP = (25, 398, 725, 692)
frames = sorted(glob.glob(os.path.join(REF, "hi", "*.jpg")))[9:88]
sw = sh = 0
for i, f in enumerate(frames):
    im = Image.open(f).crop(STRIP)
    sw, sh = im.size
    s = save(im, f"load/f{i:03d}.jpg", 82)
    total += s
print(f"load/f000..f{len(frames)-1:03d}.jpg  {sw}x{sh}  {total//1024:5d} KB total")
print("TOTAL", total // 1024, "KB")

# --- icon --------------------------------------------------------------
shutil.copy(os.path.join(REF, "steam_client.ico"), os.path.join(OUT, "app.ico"))
ico = Image.open(os.path.join(REF, "steam_client.ico"))
ico.size = (32, 32); ico.load()
ico.convert("RGBA").save(os.path.join(OUT, "app32.png"))
print("app.ico + app32.png copied")
