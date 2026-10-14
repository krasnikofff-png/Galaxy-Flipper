"""
Портреты магнатов в 2,5D: картинка автора + карта глубины.

Берёт портреты из tools/portraits/source/<магнат>.webp, срезает нарисованную рамку, считает карту глубины
нейросетью Depth Anything V2 Small (ONNX, локально, без интернета) и кладёт в Unity:
    unity/Assets/Resources/Magnates/Portraits/<магнат>.bytes        — картинка (PNG);
    unity/Assets/Resources/Magnates/Portraits/<магнат>_depth.bytes  — глубина (PNG): белое — ближе к зрителю.
Расширение .bytes — чтобы Unity не сжимал и не растягивал картинки при импорте: игра распаковывает PNG сама.
В игре портрет — рельефная сетка: при покачивании ближнее смещается сильнее дальнего, и картинка оживает.

Один раз — окружение и модель (около 135 МБ, в tools/portraits/.cache, в git не попадают):
    python3 -m venv tools/portraits/.cache/venv
    tools/portraits/.cache/venv/bin/pip install onnxruntime numpy pillow
    curl -L -o tools/portraits/.cache/models/depth-anything-v2-small.onnx \\
        https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/main/onnx/model.onnx
Запуск:
    tools/portraits/.cache/venv/bin/python tools/portraits/make_depth.py
"""
import os
import sys

import numpy as np
import onnxruntime as ort
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SOURCE = os.path.join(HERE, "source")
MODEL = os.path.join(HERE, ".cache", "models", "depth-anything-v2-small.onnx")
OUT = os.path.join(REPO, "unity", "Assets", "Resources", "Magnates", "Portraits")

# Нарисованная рамка: отступы от краёв картинки 1122×1402 (слева, сверху, справа, снизу).
FRAME = (32, 30, 32, 48)
# Размер картинки и карты глубины для игры: портрет на экране — до 150 точек в ширину.
SIZE = (720, 900)
DEPTH_SIZE = (360, 450)
# Сторона входа сети — кратна 14 (так устроена Depth Anything).
NET = 518


def crop_frame(im):
    w, h = im.size
    l, t, r, b = FRAME
    return im.crop((l * w // 1122, t * h // 1402, w - r * w // 1122, h - b * h // 1402))


def depth(session, im):
    """Относительная глубина 0…1 (1 — ближе всего) того же размера, что и картинка."""
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
    img = Image.fromarray((d * 255).astype(np.uint8)).resize(DEPTH_SIZE, Image.BICUBIC)
    # Чуть размыть: сетка в игре гладкая, ступеньки на краях волос не нужны.
    return img.filter(ImageFilter.GaussianBlur(1.6))


def main():
    os.makedirs(OUT, exist_ok=True)
    session = ort.InferenceSession(MODEL, providers=["CPUExecutionProvider"])
    names = sys.argv[1:] or sorted(os.path.splitext(f)[0] for f in os.listdir(SOURCE) if f.endswith((".webp", ".png", ".jpg")))
    for name in names:
        src = next(os.path.join(SOURCE, name + ext) for ext in (".webp", ".png", ".jpg") if os.path.exists(os.path.join(SOURCE, name + ext)))
        im = crop_frame(Image.open(src).convert("RGB"))
        im.resize(SIZE, Image.LANCZOS).save(os.path.join(OUT, name + ".bytes"), format="PNG", optimize=True)
        depth(session, im).save(os.path.join(OUT, name + "_depth.bytes"), format="PNG", optimize=True)
        print("Портрет:", name)


if __name__ == "__main__":
    main()
