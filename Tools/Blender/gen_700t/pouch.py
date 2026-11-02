"""ENV_ToolPouch（工具袋，靜態）：Pocket_5..10（10 給 R1.5b 的降弓固定帶）、SocketCase、BoltBag、Lanyard_Anchor_5..9。本輪放在工作檯上。"""
from common import box, empty_mesh


def build(ctx, p):
    bag = box(ctx, "ENV_ToolPouch", (0.36, 0.10, 0.24), (0, 0, 0.12), "Dark", "dark")
    for k, n in enumerate(range(5, 11)):
        box(ctx, f"Pocket_{n}", (0.05, 0.03, 0.12), (-0.15 + k * 0.06, -0.065, 0.02), "Dark", "rubber", parent=bag)
    box(ctx, "SocketCase", (0.10, 0.04, 0.05), (0.12, 0.07, 0.08), "Metal", "steel", parent=bag)
    box(ctx, "BoltBag", (0.08, 0.05, 0.07), (-0.12, 0.075, 0.06), "Paint", "orangesafety", parent=bag)
    for k, n in enumerate(range(5, 10)):
        empty_mesh(ctx, f"Lanyard_Anchor_{n}", (-0.15 + k * 0.06, -0.08, 0.09), parent=bag, size=0.006)
    return [("ENV_ToolPouch", bag)]
