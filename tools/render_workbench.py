"""Render original myDAQ and miniAdap-1 geometry and export the projected connection points."""
from pathlib import Path
import json
import math
import sys

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector

WIDTH, HEIGHT = 1120, 720
TILT = math.radians(18)
ASSETS = Path(__file__).resolve().parents[1] / "Workbench" / "Assets"
ASSETS.mkdir(parents=True, exist_ok=True)
anchors = {}

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def color(hex_code):
    srgb = [int(hex_code[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb)


def material(name, hex_code, roughness=.46, metal=0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*color(hex_code), 1)
    surface = mat.node_tree.nodes.get("Principled BSDF")
    surface.inputs["Base Color"].default_value = (*color(hex_code), 1)
    surface.inputs["Roughness"].default_value = roughness
    surface.inputs["Metallic"].default_value = metal
    return mat


mat_blue = material("Blue work mat", "263C4E", .83)
mat_grid = material("Printed mat grid", "385062", .85)
mat_shadow = material("Graphite rubber base", "222A31", .72)
mat_shell = material("Gray instrument enclosure", "CDD3D5", .35)
mat_cap = material("Light-gray faceplate", "E0E5E4", .36)
mat_face = material("Recessed faceplate", "92A7AB", .54)
mat_pin = material("Brass and copper plating", "C7A568", .28, .60)
mat_steel = material("Machined nickel", "9AA9AF", .25, .73)
mat_hole = material("Recess", "10191C", .78)
mat_pcb = material("Green solder mask", "16775F", .42)
mat_pcb_edge = material("PCB substrate", "55734D", .65)
mat_trace = material("Copper under solder mask", "368B6A", .46)
mat_assembly = material("Components and connector dielectric", "232D32", .46)
mat_terminal = material("Green connector body", "469078", .55)
mat_resistor = material("Resistor ceramic body", "D1A979", .44)
mat_red = material("Red socket insulation and LED lens", "B4443F", .28)
mat_white = material("Silkscreen", "9AC6AE", .69)


def position(x, y, z):
    return Vector(((x - WIDTH / 2) / 100, (HEIGHT / 2 - y) / (100 * math.cos(TILT)), z))


def finish(obj, name, mat, bevel=0):
    obj.name = name
    obj.data.materials.append(mat)
    if bevel:
        mod = obj.modifiers.new("Milled edges", 'BEVEL')
        mod.width = bevel
        mod.segments = 3
        normals = obj.modifiers.new("Face normals", 'WEIGHTED_NORMAL')
        normals.keep_sharp = True
    return obj


def box(name, x, y, z, width, height, depth, mat, bevel=.018):
    bpy.ops.mesh.primitive_cube_add(size=1, location=position(x, y, z))
    obj = bpy.context.object
    obj.dimensions = (width / 100, height / (100 * math.cos(TILT)), depth)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, name, mat, bevel)


def disk(name, x, y, z, radius, depth, mat, horizontal=False, bevel=.006):
    rotation = (0, math.pi / 2, 0) if horizontal else (0, 0, 0)
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=radius, depth=depth,
                                        location=position(x, y, z), rotation=rotation)
    return finish(bpy.context.object, name, mat, bevel)


def mark(name, x, y, z):
    anchors[name] = position(x, y, z)


def contact(name, x, y, z, scale=1):
    disk(name + " · plated recess", x, y, z, .109 * scale, .041, mat_pin)
    disk(name + " · opening", x, y, z + .024, .061 * scale, .013, mat_hole)
    mark(name, x, y, z + .031)


def screw(x, y, z):
    disk("Fastener", x, y, z, .092, .025, mat_steel)
    obj = box("Screw slot", x, y, z + .017, 12, 2, .012, mat_hole, .002)
    obj.rotation_euler.z = math.pi / 5


def trace(points, thickness=.013):
    curve = bpy.data.curves.new("PCB trace", 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = thickness
    line = curve.splines.new('POLY')
    line.points.add(len(points) - 1)
    for point, (x, y) in zip(line.points, points):
        point.co = (*position(x, y, .168), 1)
    obj = bpy.data.objects.new("PCB trace", curve)
    bpy.context.collection.objects.link(obj)
    curve.materials.append(mat_trace)


box("Work mat", 560, 360, -.17, 1250, 810, .3, mat_blue, 0)
for x in range(20, WIDTH, 40):
    box("Mat grid", x, 360, -.018, .55, 740, .002, mat_grid, 0)
for y in range(20, HEIGHT, 40):
    box("Mat grid", 560, y, -.017, 1140, .55, .002, mat_grid, 0)

# Screen-oriented enclosure. The screw rail and DMM face are turned toward the learner.
box("myDAQ rubber surround", 560, 130, .195, 874, 200, .375, mat_shadow, .118)
box("myDAQ housing", 560, 130, .437, 852, 192, .365, mat_shell, .124)
box("myDAQ top panel", 560, 122, .641, 818, 161, .082, mat_cap, .066)
box("myDAQ screw terminal rail", 560, 188, .709, 807, 47, .176, mat_assembly, .024)
box("myDAQ DMM inlet panel", 835, 108, .716, 192, 65, .135, mat_face, .033)
for x in (154, 965):
    screw(x, 72, .651)
    screw(x, 156, .651)
for i, signal in enumerate([
    "+15", "-15", "AGND", "AO0", "AO1", "AGND", "AI0+", "AI0-", "AI1+", "AI1-",
    "DIO0", "DIO1", "DIO2", "DIO3", "DIO4", "DIO5", "DIO6", "DIO7", "DGND", "+5",
]):
    x = 180 + 40 * i
    box("Screw terminal cell", x, 185, .801, 36, 39, .074,
        mat_terminal if i not in (0, 1, 19) else mat_face, .013)
    contact(f"myDAQ · {i + 1:02} {signal}", x, 190, .863, .79)
    screw(x, 171, .85)

for name, x, insulation in [("VΩ+", 773, mat_red), ("COM", 835, mat_shadow), ("A", 897, mat_red)]:
    disk("DMM socket collar", x, 106, .838, .203, .117, insulation, bevel=.018)
    disk("DMM brass ring", x, 106, .902, .130, .029, mat_pin)
    disk("DMM opening", x, 106, .918, .090, .015, mat_hole)
    mark("DMM " + name, x, 106, .925)
disk("USB indicator lens", 648, 104, .707, .070, .026, mat_assembly)
mark("USB indicator", 648, 104, .725)

# The adapter follows the component arrangement in the miniAdap-1 drawing.
box("miniAdap-1 PCB substrate", 560, 438, .101, 825, 315, .14, mat_pcb_edge, .044)
box("miniAdap-1 solder mask", 560, 438, .159, 817, 307, .022, mat_pcb, .041)
for x, y in [(169, 310), (951, 310), (169, 574), (951, 574)]:
    disk("Mounting washer", x, y, .178, .095, .025, mat_steel)
    disk("Mounting hole", x, y, .191, .052, .010, mat_hole)

box("X1 connector", 559, 313, .269, 575, 26, .207, mat_assembly, .017)
for i in range(20):
    x = 292 + i * 28
    for y in (305, 320):
        disk("X1 plated contact", x, y, .387, .040, .023, mat_pin)

for name, x, y, outward in [
    ("X2 AO0", 185, 375, -1), ("X3 AO1", 185, 456, -1),
    ("X5 AI1", 935, 375, 1), ("X4 AI0", 935, 456, 1),
]:
    box(name + " · mounting body", x, y, .365, 61, 61, .38, mat_steel, .053)
    disk(name + " · cable barrel", x + outward * 37, y + 10, .395, .215, .44, mat_steel, horizontal=True)
    disk(name + " · knurled sleeve", x + outward * 54, y + 10, .395, .235, .12, mat_steel, horizontal=True)
    for dx in (-19, 19):
        for dy in (-20, 20):
            screw(x + dx, y + dy, .565)
    contact(name, x, y, .588, 1.41)

for i in range(8):
    x = 361 + i * 56
    trace([(x, 320), (x, 340), (x + 12, 352), (x + 12, 375)])
    disk(f"D{i} collar", x, 380, .229, .122, .078, mat_assembly)
    disk(f"D{i} red lens", x, 380, .317, .094, .134, mat_red, bevel=.028)
    mark(f"D{i}", x, 380, .396)
    trace([(x, 381), (x, 400), (x - 10, 410), (x - 10, 425)])

box("RN1 resistor network", 523, 422, .268, 202, 15, .185, mat_assembly, .013)
for i in range(9):
    disk("RN1 pad", 429 + i * 23.5, 434, .183, .039, .02, mat_pin)

for i in range(4):
    x = 334 + i * 104
    box(f"T{i} ground plate", x, 466, .252, 47, 37, .16, mat_steel, .025)
    disk(f"T{i} plunger", x, 465, .388, .102, .16, mat_assembly, bevel=.024)
    mark(f"T{i}", x, 465, .467)

groups = (
    ("AO1", "AO0", "AGND", "AGND"),
    ("DIO0", "DIO1", "DIO3", "+15", "-15", "DIO4"),
    ("DIO5", "DGND", "+5", "DIO6", "DIO7", "DIO2"),
    ("AGND", "AI1-", "EXT1", "AGND", "EXT0", "AI0-"),
)
for block, signals in enumerate(groups):
    columns = len(signals) // 2
    x_start = (316, 414, 540, 666)[block]
    for row, y in enumerate((516, 559)):
        base_z = .300 - .057 * row
        box(f"X{block+6} terminal block", x_start + (columns-1)*18, y + 1,
            base_z, columns * 36 + 4, 40, .225, mat_terminal, .024)
        for column in range(columns):
            i = row * columns + column
            x = x_start + column * 36
            disk("Terminal screw recess", x, y - 9, base_z + .12, .085, .035, mat_assembly)
            screw(x, y - 9, base_z + .148)
            contact(f"X{block+6}.{i+1} {signals[i]}", x, y + 6, base_z + .126, .67)
            trace([(x, y - 18), (x, y - 31), (x+11, y-42)])

for i, (x, y) in enumerate([(884, 321), (297, 353), (350, 353), (895, 506),
                             (806, 506), (203, 540), (651, 422)], 1):
    count = 3 if i in (4, 5) else 2
    box(f"J{i} pin header", x, y, .247, count*12+8, 18, .144, mat_assembly, .014)
    for j in range(count):
        pin_x = x + (j - (count-1)/2) * 12
        box("Jumper pin", pin_x, y, .391, 4, 4, .151, mat_pin, .004)
        mark(f"J{i}.{count-j if i in (4, 5) else j+1}", pin_x, y, .467)
    mark(f"J{i}", x, y, .46)

for i, x in enumerate((895, 806), 6):
    box(f"R{i} potentiometer housing", x, 554, .302, 59, 45, .228, mat_assembly, .028)
    disk(f"R{i} shaft collar", x, 554, .459, .206, .13, mat_steel, bevel=.014)
    disk(f"R{i} shaft", x, 554, .561, .164, .14, mat_face, bevel=.016)
    mark(f"R{i}", x, 554, .638)

disk("S1 piezo buzzer", 237, 525, .359, .249, .334, mat_assembly, bevel=.023)
disk("S1 sound port", 237, 525, .528, .054, .013, mat_hole)
for j in range(3):
    x, y = 756, 418 + 13*j
    disk("SMD resistor", x, y, .191, .042, .045, mat_resistor, horizontal=True)
    for dx in (-13, 13):
        disk("SMD resistor end", x+dx, y, .184, .041, .028, mat_steel)

for name, x in (("Widerstand A", 462), ("Widerstand B", 658)):
    box("Insulated measurement post", x, 663, .14, 43, 39, .267, mat_shadow, .064)
    contact(name, x, 663, .299, 1.2)
disk("Resistor lead", 560, 663, .188, .017, 1.85, mat_steel, horizontal=True)
disk("Resistor body", 560, 663, .188, .105, .75, mat_resistor, horizontal=True, bevel=.033)
for i, dx in enumerate((-23, -12, 0, 23)):
    mark(f"Resistor band {i}", 560 + dx, 663, .188 + .107)


def light(name, power, xyz, size):
    bpy.ops.object.light_add(type='AREA', location=xyz)
    lamp = bpy.context.object
    lamp.name = name
    lamp.rotation_euler = (-lamp.location).to_track_quat('-Z', 'Y').to_euler()
    lamp.data.energy = power
    lamp.data.shape = 'DISK'
    lamp.data.size = size


light("Large softbox", 1500, (-3, 1, 9), 7)
light("Front fill", 420, (4, -6, 5), 6)
light("Edge light", 620, (3, 5, 5), 4)
bpy.ops.object.camera_add(location=(0, -24 * math.sin(TILT), 24 * math.cos(TILT)))
camera = bpy.context.object
camera.rotation_euler = (-camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = WIDTH / 100
scene.camera = camera

scene.render.engine = 'CYCLES'
scene.cycles.samples = 20
scene.cycles.use_denoising = True
scene.world = bpy.data.worlds.new("Studio ambient light")
scene.world.use_nodes = True
scene.world.node_tree.nodes.get("Background").inputs["Strength"].default_value = .31
scene.view_settings.view_transform = 'AgX'
scene.render.resolution_x = 1680
scene.render.resolution_y = 1080
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGB'
scene.render.filepath = str(ASSETS / "steckwerkbank.png")
bpy.context.view_layer.update()
coords = {}
for name, pos in anchors.items():
    projected = world_to_camera_view(scene, camera, pos)
    coords[name] = [round(projected.x * WIDTH, 2), round((1 - projected.y) * HEIGHT, 2)]
(ASSETS / "anschluesse.json").write_text(json.dumps(coords, ensure_ascii=False, indent=2), encoding="utf-8")
if "--anchors-only" not in sys.argv:
    bpy.ops.render.render(write_still=True)
if "--save-blend" in sys.argv:
    models = ASSETS.parents[1] / "Modelle"
    models.mkdir(exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    scene.render.filepath = "//steckwerkbank.png"
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                view = area.spaces.active.region_3d
                view.view_location = (0, 0, 0)
                view.view_rotation = camera.rotation_euler.to_quaternion()
                view.view_distance = 13
                view.view_perspective = 'ORTHO'
                area.spaces.active.shading.color_type = 'MATERIAL'
    bpy.ops.wm.save_as_mainfile(filepath=str(models / "Steckwerkbank.blend"), compress=True)
