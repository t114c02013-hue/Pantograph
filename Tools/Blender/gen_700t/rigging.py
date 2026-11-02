"""ENV_Sling（吊帶，單位長 1 m，Unity 縮放）與吊具架。"""
from common import box, cylinder


def build(ctx, p):
    sling = box(ctx, "ENV_Sling", (0.05, 0.004, 1.0), (0, 0, -0.5), "Paint", "orangesafety")
    for name, z in (("Sling_EyeTop", 0.5), ("Sling_EyeBottom", -0.5)):
        cylinder(ctx, name, 0.03, 0.01, (0, 0, z), axis="Y", segments=8, mat="Paint", color="orangesafety", parent=sling)
    rack = box(ctx, "ENV_SlingRack", (0.6, 0.3, 0.05), (0, 0, 1.2), "Metal", "steel")
    for sx in (-0.28, 0.28):
        box(ctx, "Leg", (0.04, 0.04, 1.2), (sx, 0, -0.6), "Metal", "steel", parent=rack)
    return [("ENV_Sling", sling), ("ENV_SlingRack", rack)]
