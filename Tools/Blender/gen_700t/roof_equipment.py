"""ENV_RoofEquipment：車頂設備零件（Unity 依 RoofSceneDef 擺放）：絕緣子、母線段、避雷器、車體接地座、高壓電纜端、控制線接頭。"""
from common import box, cylinder, empty_mesh


def insulator(ctx, name, h, parent=None):
    core = cylinder(ctx, name, 0.045, h, (0, 0, h / 2), segments=12, mat="Insulator", color="insulator", parent=parent)
    for k in range(4):   # 傘裙
        cylinder(ctx, f"{name}_Shed_{k + 1}", 0.085, 0.012, (0, 0, -h / 2 + h * (k + 1) / 5), segments=12, mat="Insulator", color="insulator", parent=core)
    empty_mesh(ctx, f"{name}_Top", (0, 0, h / 2), parent=core)
    return core


def build(ctx, p):
    root = box(ctx, "ENV_RoofEquipment", (0.02, 0.02, 0.02), (0, 0, 0), "Dark", "dark")
    insulator(ctx, "Insulator", p["insulatorHeight"], parent=root)
    bus = box(ctx, "Busbar", (0.08, 1.0, 0.06), (0, 0, 0.12), "Metal", "copper", parent=root)   # 單位長 1 m，Unity 依長度縮放
    for k, y in enumerate((-0.4, 0.4)):
        insulator(ctx, f"Busbar_Support_{k + 1}", 0.09, parent=bus).location.y = y
    arr = cylinder(ctx, "Arrester", 0.07, 0.55, (0, 0, 0.275), segments=12, mat="Insulator", color="insulator", parent=root)
    cylinder(ctx, "Arrester_Cap", 0.05, 0.05, (0, 0, 0.3), segments=12, mat="Metal", color="metal", parent=arr)
    seat = box(ctx, "Roof_CarBodyGround", (0.16, 0.16, 0.05), (0, 0, 0.025), "Metal", "steel", parent=root)
    box(ctx, "Clamp_Point", (0.04, 0.04, 0.06), (0, 0, 0.055), "Metal", "yellow", parent=seat)
    hv = box(ctx, "HV_Cable_End", (0.14, 0.14, 0.18), (0, 0, 0.09), "Dark", "dark", parent=root)
    cylinder(ctx, "HV_Cable_Connected", 0.025, 0.8, (0, 0.4, 0.0), axis="Y", segments=8, mat="Dark", color="rubber", parent=hv)
    cylinder(ctx, "HV_Cable_Parked", 0.025, 0.8, (0.3, 0.2, -0.05), axis="Y", segments=8, mat="Dark", color="rubber", parent=hv)
    ctrl = box(ctx, "CtrlCable_End", (0.08, 0.10, 0.08), (0, 0, 0.04), "Dark", "dark", parent=root)
    cylinder(ctx, "CtrlCable_Connected", 0.012, 0.6, (0, 0.3, 0.0), axis="Y", segments=8, mat="Dark", color="rubber", parent=ctrl)
    cylinder(ctx, "CtrlCable_Parked", 0.012, 0.6, (0.15, 0.2, -0.03), axis="Y", segments=8, mat="Dark", color="rubber", parent=ctrl)
    return [("ENV_RoofEquipment", root)]
