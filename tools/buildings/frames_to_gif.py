"""
Склеить кадры из PlanetSnapshots.RenderBuildingsAnimation в GIF.
    python3 tools/buildings/frames_to_gif.py папка_кадров выход.gif [ширина] [кадров_в_секунду]
"""
import glob
import os
import sys

from PIL import Image


def main():
    src, out = sys.argv[1], sys.argv[2]
    width = int(sys.argv[3]) if len(sys.argv) > 3 else 1032
    fps = float(sys.argv[4]) if len(sys.argv) > 4 else 15
    files = sorted(glob.glob(os.path.join(src, "frame_*.png")))
    frames = []
    for f in files:
        im = Image.open(f).convert("RGB")
        im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
        frames.append(im.quantize(colors=200, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE))
    frames[0].save(out, save_all=True, append_images=frames[1:], duration=round(1000 / fps), loop=0, optimize=True)
    print(out, len(frames), "кадров,", os.path.getsize(out) // 1024, "КБ")


if __name__ == "__main__":
    main()
