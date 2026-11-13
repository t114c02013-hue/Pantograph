"""程式產生的 256×256 色盤 Palette.png：8×8 格，每格 32 px 一個顏色；UV 指到色格中心。沒有任何商標、字樣、車號。"""
import bpy

CELL = 32
GRID = 8


def _hex(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


class Palette:
    def __init__(self, params):
        self.colors = {
            "white": _hex(params["liveryWhite"]), "orange": _hex(params["liveryOrange"]), "dark": _hex(params["liveryDark"]),
            "roof": (0.62, 0.64, 0.66), "walkway": (0.28, 0.30, 0.32), "glass": (0.10, 0.14, 0.20), "metal": (0.55, 0.57, 0.60),
            "insulator": (0.55, 0.32, 0.20), "yellow": (0.98, 0.80, 0.05), "red": (0.80, 0.12, 0.10), "green": (0.20, 0.75, 0.30),
            "craneyellow": (0.95, 0.70, 0.08), "rail": (0.25, 0.24, 0.22), "concrete": (0.60, 0.62, 0.64), "blue": (0.15, 0.35, 0.75),
            "steel": (0.40, 0.42, 0.45), "rubber": (0.08, 0.08, 0.08), "copper": (0.72, 0.45, 0.20), "orangesafety": (1.0, 0.45, 0.05),
            "strapyellow": _hex("#F2C200"), "rubberdarkgrey": _hex("#3A3A3A"),   # a6 增補 2：Strap_Yellow、Rubber_DarkGrey
            "aqua": _hex(params.get("liveryAqua", "#2EB0C8")),   # terry 2026-11-13 輕軌水藍（只往後加，舊模型的色格位置不變）
        }
        self.index = {name: i for i, name in enumerate(self.colors)}
        assert len(self.colors) <= GRID * GRID

    def uv(self, name):
        i = self.index[name]
        cx, cy = i % GRID, i // GRID
        return ((cx + 0.5) / GRID, (cy + 0.5) / GRID)

    def image(self, path):
        size = CELL * GRID
        img = bpy.data.images.new("Palette", width=size, height=size, alpha=False)
        px = [0.0] * (size * size * 4)
        for name, (r, g, b) in self.colors.items():
            i = self.index[name]
            cx, cy = i % GRID, i // GRID
            for y in range(cy * CELL, (cy + 1) * CELL):
                for x in range(cx * CELL, (cx + 1) * CELL):
                    k = (y * size + x) * 4
                    px[k:k + 4] = [r, g, b, 1.0]
        img.pixels[:] = px
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        return img
