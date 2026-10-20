"""
Конвейер картинок автора: папка с картинками → картинка нужного размера + карта глубины → Unity.

Картинки лежат по категориям в tools/pictures/source/<категория>/<имя>.(png|webp|jpg), в игру уходят в
unity/Assets/Resources/Pictures/<категория>/<имя>.png и <имя>_depth.png. Импорт настраивает
unity/Assets/Editor/PictureImporter.cs: цвет сжимается (BC7 на компьютере, ASTC на телефоне), глубина — без сжатия.
В игре картинка с глубиной — в 2,5D: лежит на сетке, выдавленной по карте глубины (белое — ближе), и покачивается.

Категории и размеры (см. docs/art/prompts.md, там же — что рисовать):
    magnates    портреты магнатов и ведущего (host), 4:5
    incidents   карточки случаев (имя — Id случая из Balance.Incidents), 2:1
    legends     легендарные планеты «Лота недели» (Id из Balance.Legends), 2:1
    scenes      start, season_win, season_lose — экраны, 16:9, без глубины
    backgrounds map, sector_1, sector_2 — фон карты, 16:10, без глубины

Картинка подгоняется под пропорцию категории: обрезается по центру (широкое — с боков, высокое — снизу и сверху).
У портретов с нарисованной рамкой рамку срезает --crop слева,сверху,справа,снизу в долях (0.03,0.02,0.03,0.035).

Один раз — окружение и модель (около 135 МБ, в tools/pictures/.cache, в git не попадают):
    python3 -m venv tools/pictures/.cache/venv
    tools/pictures/.cache/venv/bin/pip install onnxruntime numpy pillow
    curl -L -o tools/pictures/.cache/models/depth-anything-v2-small.onnx \\
        https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/main/onnx/model.onnx
Запуск (все категории, только новые и изменившиеся файлы; --all — всё заново; --crop — для названных):
    tools/pictures/.cache/venv/bin/python tools/pictures/make_pictures.py [категория[/имя] …] [--all] [--crop l,t,r,b]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SOURCE = os.path.join(HERE, "source")
MODEL = os.path.join(HERE, ".cache", "models", "depth-anything-v2-small.onnx")
OUT = os.path.join(REPO, "unity", "Assets", "Resources", "Pictures")
EXTS = (".png", ".webp", ".jpg", ".jpeg")

# Категория: (ширина, высота) картинки в игре и нужна ли карта глубины. Стороны кратны 4 — так сжимает BC7.
CATEGORIES = {
    "magnates": ((720, 900), True),
    "incidents": ((800, 400), True),
    "legends": ((800, 400), True),
    "scenes": ((1280, 720), False),
    "backgrounds": ((2048, 1280), False),
}
# Сторона входа сети — кратна 14 (так устроена Depth Anything).
NET = 518


def fit(im, size):
    """Обрезать по центру до пропорции size и уменьшить до size."""
    w, h = im.size
    tw, th = size
    if w * th > h * tw:
        nw = h * tw // th
        im = im.crop(((w - nw) // 2, 0, (w - nw) // 2 + nw, h))
    else:
        nh = w * th // tw
        im = im.crop((0, (h - nh) // 2, w, (h - nh) // 2 + nh))
    return im.resize(size, Image.LANCZOS)


def crop(im, shares):
    w, h = im.size
    l, t, r, b = shares
    return im.crop((round(w * l), round(h * t), w - round(w * r), h - round(h * b)))


def depth(session, im, size):
    """Относительная глубина 0…1 (1 — ближе всего), размером size."""
    w, h = im.size
    nh = NET
    nw = max(14, round(w * NET / h / 14) * 14)
    x = np.asarray(im.convert("RGB").resize((nw, nh), Image.BICUBIC), dtype=np.float32) / 255.0
    x = (x - np.array([0.485, 0.456, 0.406], np.float32)) / np.array([0.229, 0.224, 0.225], np.float32)
    x = x.transpose(2, 0, 1)[None]
    d = session.run(None, {"pixel_values": x})[0][0]
    # Отбросить крайние значения, чтобы одна яркая точка не сплющила всё остальное.
    lo, hi = np.percentile(d, 1), np.percentile(d, 99.5)
    d = np.clip((d - lo) / max(hi - lo, 1e-6), 0, 1)
    img = Image.fromarray((d * 255).astype(np.uint8)).resize(size, Image.BICUBIC)
    # Чуть размыть: сетка в игре гладкая, ступеньки на краях не нужны.
    return img.filter(ImageFilter.GaussianBlur(1.6))


def sources(wanted):
    for category in sorted(os.listdir(SOURCE)):
        folder = os.path.join(SOURCE, category)
        if not os.path.isdir(folder) or category.startswith("."):
            continue
        if category not in CATEGORIES:
            print("Неизвестная категория:", category, "— добавьте в CATEGORIES")
            continue
        for f in sorted(os.listdir(folder)):
            name, ext = os.path.splitext(f)
            if ext.lower() not in EXTS:
                continue
            if wanted and category not in wanted and category + "/" + name not in wanted:
                continue
            yield category, name, os.path.join(folder, f)


def main():
    args = sys.argv[1:]
    force = "--all" in args
    crop_shares = None
    if "--crop" in args:
        crop_shares = tuple(float(x) for x in args[args.index("--crop") + 1].split(","))
        del args[args.index("--crop"):args.index("--crop") + 2]
    wanted = [a for a in args if not a.startswith("--")]
    session = None
    done = 0
    for category, name, src in sources(wanted):
        size, with_depth = CATEGORIES[category]
        folder = os.path.join(OUT, category)
        os.makedirs(folder, exist_ok=True)
        out = os.path.join(folder, name + ".png")
        if not force and os.path.exists(out) and os.path.getmtime(out) >= os.path.getmtime(src):
            continue
        im = Image.open(src).convert("RGB")
        if crop_shares:
            im = crop(im, crop_shares)
        im = fit(im, size)
        im.save(out, format="PNG", optimize=True)
        if with_depth:
            if session is None:
                import onnxruntime as ort
                session = ort.InferenceSession(MODEL, providers=["CPUExecutionProvider"])
            depth(session, im, (size[0] // 2, size[1] // 2)).save(os.path.join(folder, name + "_depth.png"), format="PNG", optimize=True)
        print(f"{category}/{name}: {size[0]}×{size[1]}" + (" + глубина" if with_depth else ""))
        done += 1
    print("Готово:", done, "картинок" if done != 1 else "картинка")


if __name__ == "__main__":
    main()
