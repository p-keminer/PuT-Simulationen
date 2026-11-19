"""Build native VIs through the installed LabVIEW MCP AIXML backend."""
from pathlib import Path
import json
import os
import shutil
import sys

ROOT = Path(__file__).resolve().parents[1]
import labview_client
from vi_graph import Graph, ERROR

labview_client.ROOT = ROOT / ".build"
VI_DIR = ROOT / "LabVIEW"
INTERNAL_DIR = VI_DIR / "Intern"
BLOCK_DIR = VI_DIR / "Bausteine"
GENERATE = "Virtual DAQ Signal erzeugen"
MEASURE = "Virtual DAQ Signal erfassen"
CONFIGURE = "Virtual DAQ Messauftrag einstellen"
EXECUTE = "Virtual DAQ Daten lesen und schreiben"
TEST_DIR = ROOT / ".build/native-test"
EXPRESS_BUILD = ROOT / ".build/express-build"
os.environ["AIXML2VI_TRY_WIRES"] = "Refnum.TCPNetConn"
SLOTS = {"inputs": [11, 10, 9], "outputs": [3, 2, 1], "error_in": 8, "error_out": 0}
ARRAY = "array{double}"
TYPES = ["Wahl treffen", "AI Spannung", "AO Spannung", "DI Lesen", "DO Schreiben",
         "CTR Frequenz", "DMM Widerstand", "Ausgaenge freigeben"]
MEASUREMENT_TYPE = "uint8{" + ",".join(TYPES) + "}"
CONFIG = "cluster{" + MEASUREMENT_TYPE + ".Messart,string.Kanal,string.PFI,double.Rate,int32.Samples,double.Bereich,bool.Kontinuierlich}"
CONFIG_DEFAULT = "[0,,,1000,100,10,false]"
WAVEFORM_TYPE = "uint8{Sinus,Rechteck,Dreieck,Gleichspannung}"
EXPRESS_NAME = "Virtual DAQ"
EXPRESS_MODES = "uint8{Wahl treffen,AI Signal erfassen,AO Werte ausgeben,DI Lesen,DO Schreiben,CTR Frequenz,DMM Widerstand,Ausgänge zurücksetzen,AO Signal erzeugen}"
EXPRESS_CHANNELS = ["", "myDAQ1/ai0", "myDAQ1/ai1", "myDAQ1/ao0", "myDAQ1/ao1", "myDAQ1/port0",
                    *[f"myDAQ1/port0/line{i}" for i in range(8)], "myDAQ1/ctr0", "myDAQ1/dmm"]
CHANNEL_TYPE = "uint8{Kanal waehlen," + ",".join(EXPRESS_CHANNELS[1:]) + "}"
PFI_TYPE = "uint8{PFI waehlen,PFI0,PFI1,PFI2,PFI3}"
EXPRESS_SETTINGS = [
    ("Messart", EXPRESS_MODES, 0), ("Kanal", CHANNEL_TYPE, 0),
    ("Abtastrate Hz", "double", 1000), ("Samples pro Block", "int32", 100),
    ("Spannungsbereich +/- V", "double", 10), ("Kontinuierlich", "bool", "false"),
    ("PFI Klemme", PFI_TYPE, 0), ("Signalform", WAVEFORM_TYPE, 0),
    ("Frequenz Hz", "double", 50), ("Amplitude Vss", "double", 2),
    ("Offset V", "double", 0), ("Punkte pro Periode", "int32", 100),
]
EXPRESS_OUTPUTS = ["Messwert", "Daten", "Signal", "Status", "error out"]
DIALOG_LABELS = {"Messart": "1. Messart", "Kanal": "2. Anschluss"}
DIALOG_HINT = "3. Einstellungen"
# Match the order of the public mode enum. Indexes refer to EXPRESS_CHANNELS.
AI_SETTINGS = ("Abtastrate Hz", "Samples pro Block", "Spannungsbereich +/- V", "Kontinuierlich")
AO_SETTINGS = ("Abtastrate Hz", "Spannungsbereich +/- V", "Kontinuierlich")
DC_SETTINGS = ("Signalform", "Offset V", "Spannungsbereich +/- V")
PERIODIC_SETTINGS = ("Frequenz Hz", "Amplitude Vss", "Punkte pro Periode")
EXPRESS_DIALOG_MODES = [
    ((), (), "Zuerst eine Messart auswählen."),
    ((1, 2), AI_SETTINGS, "Signal für den Graphen oder Daten für die einzelnen Spannungswerte verwenden."),
    ((3, 4), AO_SETTINGS, "Spannungswerte als Array mit Ausgabewerte verbinden."),
    (tuple(range(5, 14)), (), "Messwert liefert 0 oder 1 für eine Leitung, sonst den Portwert 0 bis 255."),
    (tuple(range(5, 14)), (), "Einen Wert an Ausgabewerte anschließen. Leitung 0 oder 1, Port 0 bis 255."),
    ((14,), (), "Signal an PFI1 / DIO1 (GATE) anschließen. Messwert liefert Hz."),
    ((15,), (), "Widerstand an die rote V/Ohm-Buchse und COM anschließen. Messwert liefert Ohm."),
    ((), (), "Stoppt AO0 und AO1 und schaltet alle DIO-Leitungen auf Eingang."),
    ((3, 4), DC_SETTINGS + PERIODIC_SETTINGS, "Der Analogausgang bleibt aktiv, bis er gestoppt oder neu eingestellt wird."),
]
EXPRESS_HELP_INPUT = ("cluster{array{ref{LV.GObject}.reference}.InstanceCtlRefs,"
    "array{ref{LV.Control}}.ConfigDlgCtlRefs,"
    "array{cluster{uint16.Ctl Type,string.Instance Ctl Label,string.Config Dialog Ctl Label}.  }.Control Data,"
    "ref{LV.VI}.viref Instance,array{int32}.Types,ref{LV.VI}.Config DialogVI}")


def transport(slots, port=48371):
    g = Graph("PuT3 TCP Austausch", "Eine Anfrage an die lokale Steckwerkbank. Antwort: Status und Messdaten als tabulatorgetrennte Zahlen.")
    p = g.doc
    command = g.control(p, "Anfrage", "string", "", con=slots["inputs"][0])
    error = g.control(p, "error in", ERROR, "[false,0,]", con=slots["error_in"])
    p[-1].set("connection", "recommended")
    timeout = g.constant(p, "int32", 2500)
    opened = g.node(p, "TCP Open Connection", [
        ("address", g.constant(p, "string", "127.0.0.1")),
        ("remote port or service name", g.constant(p, "uint16", port)),
        ("?parm9", timeout), ("error in (no error)", error)],
        ["connection ID", "error out"])
    data = g.one(p, "Concatenate Strings", [("string", command), ("string", g.constant(p, "string", "\n"))], "concatenated string")
    sent = g.node(p, "TCP Write", [("connection ID", opened["connection ID"]), ("data in", data),
        ("?parm9", timeout), ("error in (no error)", opened["error out"])],
        ["connection ID out", "error out"])
    head = g.node(p, "TCP Read", [("connection ID", sent["connection ID out"]),
        ("bytes to read", g.constant(p, "int32", 8)), ("?parm9", timeout),
        ("error in (no error)", sent["error out"])], ["connection ID out", "data out", "error out"])
    size = g.one(p, "Decimal String To Number", [("string", head["data out"])], "number")
    body = g.node(p, "TCP Read", [("connection ID", head["connection ID out"]),
        ("bytes to read", size), ("?parm9", timeout),
        ("error in (no error)", head["error out"])], ["connection ID out", "data out", "error out"])
    closed = g.one(p, "TCP Close Connection", [("connection ID", body["connection ID out"]),
        ("error in (no error)", body["error out"])], "error out")
    g.indicator(p, "Antwort", "string", "", body["data out"], con=slots["outputs"][0])
    g.indicator(p, "error out", ERROR, "[false,0,]", closed, con=slots["error_out"])
    return g


def subvi(g, target, inputs, outputs):
    return g.call(g.doc, target + ".vi", inputs, outputs)


def linked_subvi(g, target, relative_path, inputs, outputs):
    result = subvi(g, target, inputs, outputs)
    uid = g.doc.findall("Call")[-1].get("uid")
    directive = f'<?lvmcp calllink uid="{uid}" tp="1" path="{relative_path}" ?>'
    g.suffix = "\n".join(filter(None, (getattr(g, "suffix", ""), directive)))
    return result


def cat(g, *nets):
    return g.one(g.doc, "Concatenate Strings", [("string", net) for net in nets], "concatenated string")


def format_value(g, net, pattern):
    return g.one(g.doc, "Format Into String",
        [("format string", g.constant(g.doc, "string", pattern)), ("input 1", net)], "resulting string")


def split_response(g, response):
    p = g.doc
    values = g.one(p, "Spreadsheet String To Array", [
        ("format string", g.constant(p, "string", "%.;%f")),
        ("spreadsheet string", response),
        ("array type (2D Dbl)", g.constant(p, ARRAY, "[]"))], "array")
    status = g.index(p, values, 0)
    rest = g.one(p, "Array Subset", [("array", values), ("index", g.constant(p, "int32", 1))], "subarray")
    return status, rest


def acquire():
    g = Graph("PuT3 DAQ", "Virtuelles myDAQ. Auftrag und Ausgabewerte bedrahten. Daten sind Volt, Ohm, Hz oder Portwert je nach Messart. Signal ist eine analoge LabVIEW-Wellenform. Statuscodes stehen in der Anleitung.")
    p = g.doc
    cfg = g.control(p, "Auftrag", CONFIG, CONFIG_DEFAULT, con=SLOTS["inputs"][0])
    output = g.control(p, "Ausgabewerte", ARRAY, "[]", con=SLOTS["inputs"][1])
    p[-1].set("connection", "recommended")
    error = g.control(p, "error in", ERROR, "[false,0,]", con=SLOTS["error_in"])
    p[-1].set("connection", "recommended")
    f = g.fields(p, cfg, ["Messart", "Kanal", "PFI", "Rate", "Samples", "Bereich", "Kontinuierlich"])
    operations = g.constant(p, "array{string}", "[,AI,AO,DI,DO,FREQ,OHM,RESET]")
    operation = g.one(p, "Index Array", [("array", operations), ("index", f["Messart"])], "element")
    delimiter = g.constant(p, "string", "|")
    rate = format_value(g, f["Rate"], "%.;%.17g")
    size = g.one(p, "Array Size", [("array", output)], "size(s)")
    is_output = g.one(p, "Equal?", [("x", f["Messart"]), ("y", g.constant(p, "uint8", 2))], "x = y?")
    count = format_value(g, g.select(p, is_output, size, f["Samples"]), "%d")
    span = format_value(g, f["Bereich"], "%.;%.17g")
    repeat = g.select(p, f["Kontinuierlich"], g.constant(p, "string", "1"), g.constant(p, "string", "0"))
    samples = g.one(p, "Array To Spreadsheet String", [
        ("format string", g.constant(p, "string", "%.;%.17g")), ("array", output)], "spreadsheet string")
    cmd = cat(g, operation, delimiter, f["Kanal"], delimiter, f["PFI"], delimiter, rate, delimiter,
              count, delimiter, span, delimiter, repeat, delimiter, samples)
    net = subvi(g, "PuT3 TCP Austausch", [("Anfrage", cmd), ("error in", error)], ["Antwort", "error out"])
    device_status, data = split_response(g, net["Antwort"])
    status_error = g.fields(p, net["error out"], ["status"])["status"]
    status = g.select(p, status_error, g.constant(p, "double", -9), device_status)
    dt = g.one(p, "Reciprocal", [("x", f["Rate"])], "1/x")
    start = g.one(p, "Get Date/Time In Seconds", [], "seconds since 1Jan1904")
    wave = g.one(p, "Build Waveform", [("t0", start), ("dt", dt), ("Y", data)], "output waveform", fields="t0,dt,Y")
    g.indicator(p, "Daten", ARRAY, "[]", data, con=SLOTS["outputs"][0])
    g.indicator(p, "Signal", "doublewaveform", "", wave, con=SLOTS["outputs"][1], style="graph21703")
    g.indicator(p, "Status", "double", 0, status, con=SLOTS["outputs"][2])
    g.indicator(p, "error out", ERROR, "[false,0,]", net["error out"], con=SLOTS["error_out"])
    return g


def configure(g):
    p = g.doc
    options = [
        ("Messart", MEASUREMENT_TYPE, 0),
        ("Physikalischer Kanal", "string", ""),
        ("PFI Klemme", "string", ""),
        ("Abtastrate Hz", "double", 1000),
        ("Samples pro Block", "int32", 100),
        ("Spannungsbereich ±V", "double", 10),
        ("Kontinuierlich", "bool", "false"),
    ]
    values = [g.control(p, name, dtype, value) for name, dtype, value in options]
    return g.bundle(p, CONFIG, CONFIG_DEFAULT,
        dict(zip(["Messart", "Kanal", "PFI", "Rate", "Samples", "Bereich", "Kontinuierlich"], values)))


def task():
    g = Graph(CONFIGURE, "Messart, Kanal, Bereich und Abtastung fuer das eigene VI einstellen. Auftrag mit Virtual DAQ Daten lesen und schreiben verbinden.")
    config = configure(g)
    g.pattern = 4835
    for slot, control in enumerate(list(g.doc.findall("Control"))):
        control.set("conIdx", str(slot))
        control.set("connection", "recommended")
    g.indicator(g.doc, "Auftrag", CONFIG, CONFIG_DEFAULT, config, con=20)
    return g


def execute_task():
    g = Graph(EXECUTE, "Auftrag an die Steckwerkbank senden. Daten liefert die Messwerte. AO/DO verwenden Ausgabewerte. Der Widerstand steht in Daten[0] in Ohm.")
    p = g.doc
    cfg = g.control(p, "Auftrag", CONFIG, CONFIG_DEFAULT, con=SLOTS["inputs"][0])
    values = g.control(p, "Ausgabewerte", ARRAY, "[]", con=SLOTS["inputs"][1])
    p[-1].set("connection", "recommended")
    error = g.control(p, "error in", ERROR, "[false,0,]", con=SLOTS["error_in"])
    p[-1].set("connection", "recommended")
    result = linked_subvi(g, "PuT3 DAQ", "||Intern|PuT3 DAQ.vi",
        [("Auftrag", cfg), ("Ausgabewerte", values), ("error in", error)],
        ["Daten", "Signal", "Status", "error out"])
    g.indicator(p, "Daten", ARRAY, "[]", result["Daten"], con=SLOTS["outputs"][0])
    g.indicator(p, "Signal", "doublewaveform", "", result["Signal"], con=SLOTS["outputs"][1], style="graph21703")
    g.indicator(p, "Status", "double", 0, result["Status"], con=SLOTS["outputs"][2])
    g.indicator(p, "error out", ERROR, "[false,0,]", result["error out"], con=SLOTS["error_out"])
    return g


def assistant():
    g = Graph(MEASURE, "Bedien-VI fuer die Steckwerkbank. Messart, physikalischen Kanal und Timing waehlen. Ausgabewerte sind einzelne Portwerte oder ein analoger Datenblock.")
    p = g.doc
    config = configure(g)
    output = g.control(p, "Ausgabewerte", ARRAY, "[]")
    g.pattern = 4835
    for slot, control in enumerate(list(p.findall("Control"))):
        control.set("conIdx", str(slot))
        control.set("connection", "recommended")
    result = linked_subvi(g, "PuT3 DAQ", "|Intern|PuT3 DAQ.vi",
        [("Auftrag", config), ("Ausgabewerte", output)], ["Daten", "Status", "Signal", "error out"])
    g.indicator(p, "Signal", "doublewaveform", "", result["Signal"], style="graph21703", con=20)
    g.indicator(p, "Daten", ARRAY, "[]", result["Daten"], con=21)
    g.indicator(p, "Status", "double", 0, result["Status"], con=22)
    g.indicator(p, "error out", ERROR, "[false,0,]", result["error out"], con=27)
    return g


def signal_buffer(g, form, freq, peak_to_peak, offset, points):
    p = g.doc
    def calc(parent, node, out, a, b):
        return g.one(parent, node, [("x", a), ("y", b)], out)

    safe_points = g.one(p, "In Range and Coerce", [
        ("x", points), ("lower limit", g.constant(p, "int32", 4)), ("upper limit", g.constant(p, "int32", 4096))],
        "coerced(x)", includeLow="true", includeHigh="true")
    loop = g.add(p, "Structure", _name="For Loop", maxin=safe_points, maxout="")
    index = loop.attrib["uid"] + ".value"
    loop.set("count", index)
    def inside(name, net):
        tunnel = g.add(loop, "Tunnel", _id="In" + name, inputs="value:" + net)
        bound = tunnel.attrib["uid"] + ".value"
        tunnel.set("outputs", "value:" + bound)
        return bound

    phase = calc(loop, "Divide", "x/y", index, inside("Points", safe_points))
    shape = inside("Shape", form)
    amplitude = inside("Vss", peak_to_peak)
    dc = inside("Offset", offset)
    zero = g.constant(loop, "double", 0)
    one = g.constant(loop, "double", 1)
    two = g.constant(loop, "double", 2)
    neg_one = g.constant(loop, "double", -1)
    half = g.constant(loop, "double", .5)
    radians = calc(loop, "Multiply", "x*y", phase, g.constant(loop, "double", 6.283185307179586))
    sine = g.one(loop, "Sine", [("x", radians)], "sin(x)")
    upper_half = calc(loop, "Greater Or Equal?", "x >= y?", phase, half)
    square = g.select(loop, upper_half, neg_one, one)
    scaled = calc(loop, "Multiply", "x*y", phase, g.constant(loop, "double", 4))
    centered = calc(loop, "Subtract", "x-y", scaled, two)
    absolute = g.one(loop, "Absolute Value", [("x", centered)], "abs(x)")
    triangle = calc(loop, "Subtract", "x-y", one, absolute)
    shapes = g.array(loop, [sine, square, triangle, zero])
    sample = g.one(loop, "Index Array", [("array", shapes), ("index", shape)], "element")
    gain = calc(loop, "Multiply", "x*y", half, amplitude)
    volts = calc(loop, "Add", "x+y", calc(loop, "Multiply", "x*y", sample, gain), dc)
    out = g.add(loop, "Tunnel", _id="OutSamples", inputs="value:" + volts, mode="index")
    buffer = out.attrib["uid"] + ".value"
    out.set("outputs", "value:" + buffer)
    rate = calc(p, "Multiply", "x*y", safe_points, freq)
    return buffer, rate


def function_generator():
    g = Graph(GENERATE, "Virtuelles SFP. Frequenz, Signalform, Spitze-Spitze und Offset in LabVIEW waehlen. Ein Lauf startet den gewaehlten Analogausgang. Ein neuer Lauf ersetzt das Signal auf dem gewaehlten Kanal.")
    p = g.doc
    channel = g.control(p, "Physikalischer Kanal", "string", "")
    form = g.control(p, "Signalform", WAVEFORM_TYPE, 0)
    freq = g.control(p, "Frequenz Hz", "double", 50)
    peak_to_peak = g.control(p, "Amplitude Vss", "double", 2)
    offset = g.control(p, "Offset V", "double", 0)
    points = g.control(p, "Punkte pro Periode", "int32", 100)
    span = g.control(p, "Spannungsbereich ±V", "double", 10)
    g.pattern = 4835
    for slot, control in enumerate(p.findall("Control")):
        control.set("conIdx", str(slot))
        control.set("connection", "recommended")
    buffer, rate = signal_buffer(g, form, freq, peak_to_peak, offset, points)
    config = g.bundle(p, CONFIG, CONFIG_DEFAULT, {
        "Messart": g.constant(p, MEASUREMENT_TYPE, 2),
        "Kanal": channel, "Rate": rate, "Bereich": span,
        "Kontinuierlich": g.constant(p, "bool", "true")})
    output = linked_subvi(g, "PuT3 DAQ", "|Intern|PuT3 DAQ.vi",
        [("Auftrag", config), ("Ausgabewerte", buffer)], ["Status", "error out"])
    dt = g.one(p, "Reciprocal", [("x", rate)], "1/x")
    wave = g.one(p, "Build Waveform", [("dt", dt), ("Y", buffer)], "output waveform", fields="dt,Y")
    g.indicator(p, "Signalvorschau", "doublewaveform", "", wave, style="graph21703", con=20)
    g.indicator(p, "Status", "double", 0, output["Status"], con=21)
    g.indicator(p, "error out", ERROR, "[false,0,]", output["error out"], con=27)
    return g


def express_engine():
    g = Graph("Virtual DAQ Express Ausfuehrung", "Signal erzeugen oder erfassen. Pro Knoten eine Messart und einen Kanal konfigurieren. Messwert gibt z. B. den Widerstand in Ohm aus.")
    p = g.doc
    g.pattern = 4835
    c = {name: g.control(p, name, dtype, value, con=slot)
         for slot, (name, dtype, value) in enumerate(EXPRESS_SETTINGS)}
    external = g.control(p, "Ausgabewerte", ARRAY, "[]", con=12)
    for control in p.findall("Control"):
        control.set("connection", "recommended")
    mode = c["Messart"]
    generate = g.one(p, "Equal?", [("x", mode), ("y", g.constant(p, "uint8", 8))], "x = y?")
    operations = g.array(p, [g.constant(p, MEASUREMENT_TYPE, i) for i in range(len(TYPES))])
    task_mode = g.select(p, generate, g.constant(p, MEASUREMENT_TYPE, 2),
        g.one(p, "Index Array", [("array", operations), ("index", mode)], "element"))
    channels = g.constant(p, "array{string}", "[" + ",".join(EXPRESS_CHANNELS) + "]")
    counter = g.one(p, "Equal?", [("x", mode), ("y", g.constant(p, "uint8", 5))], "x = y?")
    buffer, rate = signal_buffer(g, c["Signalform"], c["Frequenz Hz"], c["Amplitude Vss"],
        c["Offset V"], c["Punkte pro Periode"])
    dc = g.one(p, "Equal?", [("x", c["Signalform"]), ("y", g.constant(p, "uint8", 3))], "x = y?")
    reset = g.one(p, "Equal?", [("x", mode), ("y", g.constant(p, "uint8", 7))], "x = y?")
    channel = g.one(p, "Index Array", [("array", channels), ("index", c["Kanal"])], "element")
    buffer = g.select(p, dc, g.array(p, [c["Offset V"]]), buffer)
    rate = g.select(p, dc, g.constant(p, "double", 1000), rate)
    configuration = g.bundle(p, CONFIG, CONFIG_DEFAULT, {
        "Messart": task_mode,
        "Kanal": g.select(p, reset, g.constant(p, "string", "myDAQ1/port0"), channel),
        "PFI": g.select(p, counter, g.constant(p, "string", "PFI1"), g.constant(p, "string", "")),
        "Rate": g.select(p, generate, rate, c["Abtastrate Hz"]),
        "Bereich": c["Spannungsbereich +/- V"], "Samples": c["Samples pro Block"],
        "Kontinuierlich": g.select(p, generate, g.constant(p, "bool", "true"), c["Kontinuierlich"])})
    result = linked_subvi(g, "PuT3 DAQ", "|PuT3 DAQ.vi",
        [("Auftrag", configuration), ("Ausgabewerte", g.select(p, generate, buffer, external))],
        ["Daten", "Signal", "Status", "error out"])
    size = g.one(p, "Array Size", [("array", result["Daten"])], "size(s)")
    measured = g.select(p, g.greater_zero(p, size), g.index(p, result["Daten"], 0),
        g.constant(p, "double", "NaN"))
    g.indicator(p, "Messwert", "double", 0, measured, con=20)
    g.indicator(p, "Daten", ARRAY, "[]", result["Daten"], con=21)
    g.indicator(p, "Signal", "doublewaveform", "", result["Signal"], style="graph21703", con=22)
    g.indicator(p, "Status", "double", 0, result["Status"], con=23)
    g.indicator(p, "error out", ERROR, "[false,0,]", result["error out"], con=27)
    return g


def cli_probe():
    g = Graph("PuT3 CLI Probe", "Fuehrt eine skalierbare native Test-Anfrage an die Steckwerkbank aus.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    result = linked_subvi(g, "PuT3 TCP Austausch", "|Intern|PuT3 TCP Austausch.vi",
        [("Anfrage", g.index(p, args, 0))], ["Antwort", "error out"])
    code = g.fields(p, result["error out"], ["code"])["code"]
    g.indicator(p, "Exit code", "int32", 0, code, con=3)
    g.indicator(p, "Output", "string", "", result["Antwort"], con=2)
    return g


def cli_errors():
    g = Graph("PuT3 CLI Fehlerliste", "Liest den Compile-Zustand anhand eines Dateipfads in der Testinstanz.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    path = g.one(p, "String To Path", [("string", g.index(p, args, 0))], "path")
    opened = g.node(p, "Open VI Reference", [("vi path", path)], ["vi reference", "error out"])
    diag = g.node(p, "Invoke Node", [("reference", opened["vi reference"]),
        ("error in (no error)", opened["error out"])], ["Errors", "Details", "error out"],
        target="Get Errors", type="{LV.VI}")
    flatten = [g.one(p, "Flatten To XML", [("anything", diag[n])], "xml string") for n in ("Errors", "Details", "error out")]
    code = g.fields(p, opened["error out"], ["code"])["code"]
    g.indicator(p, "Exit code", "int32", 0, code, con=3)
    g.indicator(p, "Output", "string", "", cat(g, *flatten), con=2)
    return g


def cli_daq():
    g = Graph("PuT3 CLI Messung", "Prueft den produktiven LabVIEW-Auftrag mit Zahlenwerten vom Terminal.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    values = [g.index(p, args, i) for i in range(8)]
    def integer(i):
        return g.one(p, "Decimal String To Number", [("string", values[i])], "number")
    enum_type = "uint8{" + ",".join(TYPES) + "}"
    choice = g.array(p, [g.constant(p, enum_type, i) for i in range(len(TYPES))])
    operation = g.one(p, "Index Array", [("array", choice), ("index", integer(0))], "element")
    continuous = g.greater_zero(p, integer(6))
    cfg = linked_subvi(g, CONFIGURE, f"|Bausteine|{CONFIGURE}.vi",
        [("Messart", operation), ("Physikalischer Kanal", values[1]), ("PFI Klemme", values[2]),
         ("Abtastrate Hz", integer(3)), ("Samples pro Block", integer(4)),
         ("Spannungsbereich ±V", integer(5)), ("Kontinuierlich", continuous)], ["Auftrag"])
    data_in = g.one(p, "Spreadsheet String To Array", [
        ("format string", g.constant(p, "string", "%.;%f")), ("spreadsheet string", values[7]),
        ("array type (2D Dbl)", g.constant(p, ARRAY, "[]")),
        ("delimiter (Tab)", g.constant(p, "string", ","))], "array")
    result = linked_subvi(g, EXECUTE, f"|Bausteine|{EXECUTE}.vi",
        [("Auftrag", cfg["Auftrag"]), ("Ausgabewerte", data_in)], ["Daten", "Status", "Signal", "error out"])
    code = g.fields(p, result["error out"], ["code"])["code"]
    dt = g.one(p, "Get Waveform Components", [("waveform", result["Signal"])], "dt", fields="dt")
    data = g.one(p, "Array To Spreadsheet String", [
        ("format string", g.constant(p, "string", "%.;%.17g")), ("array", result["Daten"])], "spreadsheet string")
    bar = g.constant(p, "string", "|")
    g.indicator(p, "Exit code", "int32", 0, code, con=3)
    g.indicator(p, "Output", "string", "", cat(g, g.constant(p, "string", "RESULT|"),
        format_value(g, result["Status"], "%.;%g"), bar, format_value(g, dt, "%.;%g"), bar, data), con=2)
    return g


def cli_generator():
    g = Graph("PuT3 CLI Generator", "Fuehrt den nativen LabVIEW-Funktionsgenerator mit einer bekannten Parametrierung aus.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    generator = subvi(g, GENERATE, [
        ("Physikalischer Kanal", g.index(p, args, 0)),
        ("Signalform", g.constant(p, "uint8{Sinus,Rechteck,Dreieck,Gleichspannung}", 1)),
        ("Frequenz Hz", g.constant(p, "double", 50)),
        ("Amplitude Vss", g.constant(p, "double", 5)),
        ("Offset V", g.constant(p, "double", 2.5)),
        ("Punkte pro Periode", g.constant(p, "int32", 100))],
        ["Status", "error out"])
    code = g.fields(p, generator["error out"], ["code"])["code"]
    g.indicator(p, "Exit code", "int32", 0, code, con=3)
    g.indicator(p, "Output", "string", "", cat(g, g.constant(p, "string", "RESULT|"),
        format_value(g, generator["Status"], "%.;%g")), con=2)
    return g


def cli_express():
    g = Graph("PuT3 Express Creator", "Baut die Virtual-DAQ-Quelle und den Konfigurationsdialog mit dem NI Express VI Creator.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    paths = [g.one(p, "String To Path", [("string", g.index(p, args, i))], "path") for i in (0, 1)]
    links = []
    def wizard(target, inputs, outputs):
        result = g.call(p, target, inputs, outputs)
        uid = p.findall("Call")[-1].get("uid")
        links.append(f'<?lvmcp calllink uid="{uid}" tp="0" path="<project>|ExpressDev.llb|{target}" ?>')
        return result
    name = g.constant(p, "string", EXPRESS_NAME)
    result = wizard("byob_Convert VI to Block.vi", [
        ("selected VI", paths[0]), ("parent directory", paths[1]),
        ("block name", name)], ["block path", "sub-block comment", "error out"])
    names = [name for name, _, _ in EXPRESS_SETTINGS] + ["Ausgabewerte"] + EXPRESS_OUTPUTS
    rule = "uint16{Invalid Wire Rule,Required,Recommended,Optional}"
    terminal_info = ("cluster{array{bool}.Expandable,array{uint16}.Expand Type,array{bool}.Configurable,"
                     "array{uint16}.Config Type,array{" + rule + "}.connection type}")
    count = len(names)
    configured = len(EXPRESS_SETTINGS)
    # Express VI Creator's terminal kinds: input 0, indicator 6, error indicator 16.
    kinds = [0] * (configured + 1) + [6] * (len(EXPRESS_OUTPUTS) - 1) + [16]
    # Kind 1 creates a named configuration constant without an external connector.
    expandable_kinds = [1] * configured + kinds[configured:]
    info = g.constant(p, terminal_info, json.dumps(
        [[True] * count, expandable_kinds, [True] * configured + [False] * (count - configured),
         kinds, [2] * count], separators=(",", ":")).lower())
    terminal_names = g.constant(p, "array{string}", "[" + ",".join(names) + "]")
    controls = wizard("byob_Create Controls & Inds.vi", [
        ("VI path", result["block path"]), ("subVI info", result["sub-block comment"]),
        ("terminal names", terminal_names), ("terminal info", info),
        ("error in (no error)", result["error out"])],
        ["VI Refnum", "terminal info out", "error out"])
    source_closed = g.one(p, "Close Reference", [("reference", controls["VI Refnum"]),
        ("error in (no error)", controls["error out"])], "error out")
    config = wizard("byob_Create Config Page.vi", [
        ("parent directory", paths[1]), ("subVI info", result["sub-block comment"]),
        ("block name", name), ("block path", result["block path"]),
        ("terminal names", terminal_names), ("terminal info", controls["terminal info out"]),
        ("error in (no error)", source_closed)],
        ["config page path", "error out"])
    linked = wizard("byob_Link Block to ConfigPg.vi", [
        ("Block Path", result["block path"]), ("Config Page Path", config["config page path"]),
        ("error in (no error)", config["error out"])], ["link message", "error out"])
    inspected = wizard("byob_Get Config Page Info.vi", [
        ("block path", result["block path"]), ("block name", name),
        ("get minimal? (F)", g.constant(p, "bool", "true")),
        ("error in (no error)", linked["error out"])], ["config page path", "error out"])
    g.suffix = "\n".join(links)
    errors = g.fields(p, inspected["error out"], ["code", "source"])
    code = errors["code"]
    path_text = cat(g, g.one(p, "Path To String", [("path", inspected["config page path"])], "string"),
                    g.constant(p, "string", "\n"), linked["link message"], errors["source"])
    g.indicator(p, "Exit code", "int32", 0, code, con=3)
    g.indicator(p, "Output", "string", "", path_text, con=2)
    return g


def cli_express_runtime():
    g = Graph("PuT3 CLI Express Lauf", "Prueft Messung und Signalerzeugung mit der nativen Express-Quelle.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    def enum_arg(arg, dtype, count):
        index = g.one(p, "Decimal String To Number", [("string", g.index(p, args, arg))], "number")
        values = g.array(p, [g.constant(p, dtype, i) for i in range(count)])
        return g.one(p, "Index Array", [("array", values), ("index", index)], "element")

    target = f"sub{EXPRESS_NAME}.vi"
    result = g.call(p, target, [
        ("Messart", enum_arg(0, EXPRESS_MODES, 9)),
        ("Kanal", enum_arg(1, CHANNEL_TYPE, len(EXPRESS_CHANNELS))),
        ("Signalform", g.constant(p, WAVEFORM_TYPE, 1)),
        ("Frequenz Hz", g.constant(p, "double", 50)),
        ("Amplitude Vss", g.constant(p, "double", 4)),
        ("Offset V", g.constant(p, "double", 2))],
        ["Messwert", "Status", "error out"])
    uid = p.findall("Call")[-1].get("uid")
    g.suffix = f'<?lvmcp calllink uid="{uid}" tp="1" path="|Intern|{EXPRESS_NAME}Source.llb|{target}" ?>'
    code = g.fields(p, result["error out"], ["code"])["code"]
    g.indicator(p, "Exit code", "int32", 0, code, con=3)
    g.indicator(p, "Output", "string", "", cat(g, g.constant(p, "string", "RESULT|"),
        format_value(g, result["Status"], "%.;%g"), g.constant(p, "string", "|"),
        format_value(g, result["Messwert"], "%.;%.8g")), con=2)
    return g


def express_help():
    g = Graph("genHelpVirtual DAQ", "Kontexthilfe fuer den Virtual-DAQ-Knoten.")
    p = g.doc
    g.pattern = 4824
    g.control(p, "Output Cluster", EXPRESS_HELP_INPUT, "[[],[],[],,[],]", con=0)
    p[-1].set("connection", "recommended")
    help_text = g.constant(p, "string", "Virtual DAQ\n\nMessart und Kanal im Dialog einstellen. "
        "Messwert gibt z. B. den Widerstand in Ohm aus. Signal und Daten enthalten den erfassten Block. "
        "Ausgabewerte nimmt eigene AO- oder DO-Werte an. Status 0 bedeutet Erfolg.")
    g.indicator(p, "Help Description", "string", "", help_text, con=5)
    return g


def express_dialog():
    g = Graph(f"Configure {EXPRESS_NAME}", "Messart, Anschluss und die zugehörigen Einstellungen des Express-VI.")
    g.pattern = 4806
    p = g.doc

    def indexed(parent, array, index):
        return g.one(parent, "Index Array", [("array", array), ("index", index)], "element")

    def equal(parent, a, b):
        return g.one(parent, "Equal?", [("x", a), ("y", b)], "x = y?")

    def enter(loop, name, value, indexed_input=False):
        tunnel = g.add(loop, "Tunnel", _id="In" + name, inputs="value:" + value)
        if indexed_input:
            tunnel.set("mode", "index")
        result = tunnel.get("uid") + ".value"
        tunnel.set("outputs", "value:" + result)
        return result

    def leave(loop, name, value):
        tunnel = g.add(loop, "Tunnel", _id="Out" + name, inputs="value:" + value)
        result = tunnel.get("uid") + ".value"
        tunnel.set("outputs", "value:" + result)
        return result

    def ni(target, inputs, outputs):
        name = target + ".vi"
        result = g.call(p, name, inputs, outputs)
        uid = p.findall("Call")[-1].get("uid")
        link = f'<?lvmcp calllink uid="{uid}" tp="0" path="<vilib>|express|express shared|{name}" ?>'
        g.suffix = "\n".join(filter(None, (getattr(g, "suffix", ""), link)))
        return result

    host = g.control(p, "subVI", "ref{LV.SubVI}", "", con=0)
    instance = g.control(p, "VI", "ref{LV.VI}", "", con=2)
    parent = g.control(p, "ParentVI", "ref{LV.VI}", "", con=3)
    for c in p.findall("Control"):
        c.set("connection", "recommended")
    chain = g.one(p, "Call Chain", [], "call chain")
    dialog = g.one(p, "Open VI Reference", [("vi path", g.index(p, chain, 0))], "vi reference")
    control_names = [name for name, _, _ in EXPRESS_SETTINGS]
    settings = g.constant(p, "array{cluster{uint16.Ctl Type,string.SourceVI Ctl Label,string.ConfigDiag Ctl Label}}",
        "[" + ",".join(f"[1,{name},{DIALOG_LABELS.get(name, '')}]" for name in control_names) + "]")
    existing = ni("ex_Read Properties", [("Control Data", settings), ("viref Configuration Dialog", dialog),
        ("viref Instance", instance)], ["viref Configuration Dialog out", "Output Cluster"])
    current = existing["viref Configuration Dialog out"]
    refs = g.fields(p, existing["Output Cluster"], ["ConfigDlgCtlRefs"])["ConfigDlgCtlRefs"]
    opened = g.one(p, "Invoke Node", [("reference", current), ("Activate", g.constant(p, "bool", "true"))],
        "error out", target="FP.Open", type="{LV.VI}")

    loop = g.add(p, "Structure", _name="While Loop")
    vi = enter(loop, "Dialog", current)
    ready = enter(loop, "Opened", opened)
    fields = enter(loop, "Controls", refs)
    live = {"Messart", "Kanal", "Signalform"}
    choices = {name: g.control(loop if name in live else p, DIALOG_LABELS.get(name, name), dtype, value)
               for name, dtype, value in EXPRESS_SETTINGS}
    ok = g.control(loop, "OK", "bool", "false", style="latched")
    cancel = g.control(loop, "Cancel", "bool", "false", style="latched")
    mode = choices["Messart"]
    channel = choices["Kanal"]
    dc = equal(loop, choices["Signalform"], g.constant(loop, "uint8", 3))
    generator = equal(loop, mode, g.constant(loop, "uint8", 8))
    dc_selected = g.select(loop, generator, dc, g.constant(loop, "bool", "false"))
    row = g.select(loop, dc_selected, g.constant(loop, "uint8", 9), mode)
    states = EXPRESS_DIALOG_MODES + [(EXPRESS_DIALOG_MODES[8][0], DC_SETTINGS, EXPRESS_DIALOG_MODES[8][2])]

    enabled = lambda v: "true" if v else "false"
    def vector(values):
        return "[" + ",".join(values) + "]"

    rules = []
    for channels, params, _ in states:
        visible = vector(enabled(name == "Messart" or (name == "Kanal" and channels) or name in params) for name in control_names)
        allowed = vector(enabled(index in channels) for index in range(len(EXPRESS_CHANNELS)))
        blocked = vector(str(index) for index in range(1, len(EXPRESS_CHANNELS)) if index not in channels)
        rules.append(vector((visible, allowed, blocked)))
    table = g.constant(loop, "array{cluster{array{bool}.Visible,array{bool}.Allowed,array{uint32}.Blocked}}", vector(rules))
    rule = g.fields(loop, indexed(loop, table, row), ["Visible", "Allowed", "Blocked"])
    valid = g.select(loop, equal(loop, mode, g.constant(loop, "uint8", 7)),
        g.constant(loop, "bool", "true"), indexed(loop, rule["Allowed"], channel))

    options = g.add(loop, "Structure", _name="For Loop", maxin="", maxout="")
    options.set("count", options.get("uid") + ".value")
    ref = enter(options, "Control", fields, True)
    visible = enter(options, "Visible", rule["Visible"], True)
    g.one(options, "Property Node", [("reference", ref), ("Visible", visible)], "error out", type="{LV.Control}", fields="write+Visible")
    enum = g.one(loop, "To More Specific Class", [("reference", g.index(loop, fields, 1)),
        ("target class", g.constant(loop, "ref{LV.Enum}", ""))], "specific class reference")
    g.one(loop, "Property Node", [("reference", enum), ("Disabled Items []", rule["Blocked"])],
        "error out", type="{LV.Enum}", fields="write+Disabled Items []")

    # The native dialog controls remain addressable by label for ex_Read/ex_Reconfigure.
    panel = g.one(loop, "Property Node", [("reference", vi), ("error in (no error)", ready)],
        "Front Panel", type="{LV.VI}", fields="read+Front Panel")
    controls = g.one(loop, "Property Node", [("reference", panel)], "Controls[]", type="{LV.Panel}", fields="read+Controls[]")
    # Find OK along with the other UI objects by name rather than relying on z-order.
    buttons = g.add(loop, "Structure", _name="For Loop", maxin="", maxout="")
    buttons.set("count", buttons.get("uid") + ".value")
    button = enter(buttons, "Control", controls, True)
    text_label = g.one(buttons, "Property Node", [("reference", button)], "Label.Text", type="{LV.Control}", fields="read+Label.Text")
    is_ok = equal(buttons, text_label, g.constant(buttons, "string", "OK"))
    case = g.add(buttons, "Structure", _name="Case Structure", selectin=is_ok)
    for n, value in (("In1", button), ("In2", enter(buttons, "Valid", valid))):
        g.add(case, "Tunnel", _id=n, inputs="value:" + value)
    g.add(case, "CaseFrame", selector="False", selectout="")
    selected = g.add(case, "CaseFrame", selector="True", selectout="")
    params = []
    for n in ("In1", "In2"):
        tunnel = g.add(selected, "Tunnel", _id=n)
        params.append(tunnel.get("uid") + ".value")
        tunnel.set("outputs", "value:" + params[-1])
    disabled = g.select(selected, params[1], g.constant(selected, "uint8", 0), g.constant(selected, "uint8", 2))
    g.one(selected, "Property Node", [("reference", params[0]), ("Disabled", disabled)], "error out", type="{LV.Control}", fields="write+Disabled")

    hints = g.array(loop, [g.constant(loop, "string", hint) for _, _, hint in states])
    g.indicator(loop, DIALOG_HINT, "string", "", indexed(loop, hints, row))
    accepted = g.select(loop, valid, ok, g.constant(loop, "bool", "false"))
    panel_open = g.one(loop, "Property Node", [("reference", vi), ("error in (no error)", ready)],
        "Front Panel Window:Open", type="{LV.VI}", fields="read+Front Panel Window\\3AOpen")
    stop = g.select(loop, panel_open, g.select(loop, cancel, g.constant(loop, "bool", "true"), accepted), g.constant(loop, "bool", "true"))
    g.node(loop, "Wait (ms)", [("milliseconds to wait", g.constant(loop, "uint32", 100))], [])
    g.add(loop, "Condition", value="stop", inputs="value:" + stop)
    finished = leave(loop, "Accepted", accepted)
    terminals = g.constant(p, "array{string}", "[Ausgabewerte," + ",".join(EXPRESS_OUTPUTS) + "]")
    ni("ex_Reconfigure", [("VI refnum", instance), ("Parent VI refnum", parent), ("subVI refnum", host),
        ("Data Valid", finished), ("Inputs/Outputs to Grow", terminals), ("Props Cluster", existing["Output Cluster"])], [])
    g.indicator(p, "Data Valid", "bool", "false", finished, con=1)
    return g


def cli_copy_express_member():
    g = Graph("PuT3 Express Bibliotheksdatei speichern", "Speichert ein Express-Hilfs-VI in seiner LabVIEW-Bibliothek.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    source, destination = [g.one(p, "String To Path", [("string", g.index(p, args, index))], "path") for index in (0, 1)]
    opened = g.node(p, "Open VI Reference", [("vi path", source)], ["vi reference", "error out"])
    saved = g.one(p, "Invoke Node", [("reference", opened["vi reference"]),
        ("error in (no error)", opened["error out"]), ("Path to saved file", destination),
        ("Save a Copy", g.constant(p, "bool", "true"))], "error out", target="Save.Instrument", type="{LV.VI}")
    closed = g.one(p, "Close Reference", [("reference", opened["vi reference"]), ("error in (no error)", saved)], "error out")
    result = g.fields(p, closed, ["code", "source"])
    g.indicator(p, "Exit code", "int32", 0, result["code"], con=3)
    g.indicator(p, "Output", "string", "", result["source"], con=2)
    return g


def cli_express_layout():
    g = Graph("PuT3 Express Dialog anordnen", "Ordnet die von LabVIEW erzeugten Dialogfelder in zwei Spalten an.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    path = g.one(p, "String To Path", [("string", g.index(p, args, 0))], "path")
    opened = g.node(p, "Open VI Reference", [("vi path", path)], ["vi reference", "error out"])
    vi = opened["vi reference"]
    panel = g.node(p, "Property Node", [("reference", vi), ("error in (no error)", opened["error out"])],
        ["Front Panel", "error out"], type="{LV.VI}", fields="read+Front Panel")
    objects = g.node(p, "Property Node", [("reference", panel["Front Panel"]),
        ("error in (no error)", panel["error out"])], ["Controls[]", "Decorations[]", "error out"],
        type="{LV.Panel}", fields="read+Controls[],read+Decorations[]")
    names = [DIALOG_LABELS.get(name, name) for name, _, _ in EXPRESS_SETTINGS] + ["OK", "Cancel", DIALOG_HINT]
    coords = [(36, 42), (325, 42), (36, 171), (260, 171), (36, 227), (260, 227),
              (36, 171), (36, 171), (260, 171), (425, 171), (260, 227), (425, 227),
              (385, 336), (487, 336), (36, 101)]
    labels = g.constant(p, "array{string}", "[" + ",".join(names) + "]")
    positions = g.constant(p, "array{cluster{int32.Left,int32.Top}}",
        "[" + ",".join(f"[{x},{y}]" for x, y in coords) + "]")
    enum_indices = g.constant(p, "array{int32}", "[0,1,6,7]")

    def edit_objects(items, error, edit):
        loop = g.add(p, "Structure", _name="For Loop", maxin="", maxout="")
        loop.set("count", loop.get("uid") + ".value")

        def enter(net, indexed=False):
            tunnel = g.add(loop, "Tunnel", _id="In" + str(len(loop)), inputs="value:" + net)
            if indexed:
                tunnel.set("mode", "index")
            inside = tunnel.get("uid") + ".value"
            tunnel.set("outputs", "value:" + inside)
            return inside

        item = enter(items, indexed=True)
        state = g.add(loop, "ShiftReg")
        left = g.add(state, "Left", inputs="value:" + error)
        error_in = left.get("uid") + ".value"
        left.set("outputs", "value:" + error_in)
        last = edit(loop, item, error_in, enter)
        right = g.add(state, "Right", inputs="value:" + last)
        error_out = right.get("uid") + ".value"
        right.set("outputs", "value:" + error_out)
        return error_out

    def place_control(loop, ref, error, enter):
        label = g.node(loop, "Property Node", [("reference", ref), ("error in (no error)", error)],
            ["Label.Text", "error out"], type="{LV.Control}", fields="read+Label.Text")
        index = g.one(loop, "Search 1D Array", [("1D array", enter(labels)),
            ("element", label["Label.Text"])], "index of element")
        visible = g.one(loop, "Greater Or Equal?", [("x", index), ("y", g.constant(loop, "int32", 0))], "x >= y?")
        position = g.one(loop, "Index Array", [("array", enter(positions)), ("index", index)], "element")
        placed = g.one(loop, "Property Node", [("reference", ref), ("error in (no error)", label["error out"]),
            ("Position", position), ("Visible", visible)], "error out",
            type="{LV.Control}", fields="write+Position,write+Visible")
        enum_index = g.one(loop, "Search 1D Array", [("1D array", enter(enum_indices)), ("element", index)], "index of element")
        is_enum = g.one(loop, "Greater Or Equal?", [("x", enum_index), ("y", g.constant(loop, "int32", 0))], "x >= y?")
        hint_index = g.constant(loop, "int32", len(names) - 1)
        type_index = g.select(loop, is_enum, g.constant(loop, "int32", 1),
            g.select(loop, g.one(loop, "Equal?", [("x", index), ("y", hint_index)], "x = y?"),
                g.constant(loop, "int32", 2), g.constant(loop, "int32", 0)))
        case = g.add(loop, "Structure", _name="Case Structure", selectin=type_index)
        for key, net in [("In1", ref), ("In2", placed)]:
            g.add(case, "Tunnel", _id=key, inputs="value:" + net)
        for selector in ("0, Default", "1", "2"):
            frame = g.add(case, "CaseFrame", selector=selector, selectout="")
            ref_tunnel = g.add(frame, "Tunnel", _id="In1")
            err_tunnel = g.add(frame, "Tunnel", _id="In2")
            err_in = err_tunnel.get("uid") + ".value"
            err_tunnel.set("outputs", "value:" + err_in)
            err_out = err_in
            if selector in ("1", "2"):
                control_ref = ref_tunnel.get("uid") + ".value"
                ref_tunnel.set("outputs", "value:" + control_ref)
                cls, prop, width = ("LV.Enum", "Named Numeric Size", 205) if selector == "1" else ("LV.String", "Size", 525)
                converted = g.node(frame, "To More Specific Class", [("reference", control_ref), ("error in", err_in),
                    ("target class", g.constant(frame, f"ref{{{cls}}}", ""))], ["specific class reference", "error out"])
                specific = converted["specific class reference"]
                size = g.node(frame, "Property Node", [("reference", specific), ("error in (no error)", converted["error out"])],
                    [prop, "error out"], type=f"{{{cls}}}", fields=f"read+{prop}")
                wider = g.one(frame, "Bundle By Name", [("input cluster", size[prop]),
                    ("Width", g.constant(frame, "int32", width))], "output cluster", fields="Width")
                err_out = g.one(frame, "Property Node", [("reference", specific), ("error in (no error)", size["error out"]),
                    (prop, wider)], "error out", type=f"{{{cls}}}", fields=f"write+{prop}")
            g.add(frame, "Tunnel", _id="Out1", inputs="value:" + err_out)
        result = g.add(case, "Tunnel", _id="Out1")
        net = result.get("uid") + ".value"
        result.set("outputs", "value:" + net)
        return net

    def hide_decoration(loop, ref, error, enter):
        return g.one(loop, "Property Node", [("reference", ref), ("error in (no error)", error),
            ("Visible", g.constant(loop, "bool", "false"))], "error out", type="{LV.Decoration}", fields="write+Visible")

    controls = edit_objects(objects["Controls[]"], objects["error out"], place_control)
    decorations = edit_objects(objects["Decorations[]"], controls, hide_decoration)
    bounds = g.constant(p, "cluster{int32.Left,int32.Top,int32.Right,int32.Bottom}", "[130,130,721,516]")
    origin = g.constant(p, "cluster{int32.X,int32.Y}", "[0,0]")
    sized = g.one(p, "Property Node", [("reference", vi), ("error in (no error)", decorations),
        ("Front Panel Window:Panel Bounds", bounds), ("Front Panel Window:Origin", origin)],
        "error out", type="{LV.VI}", fields="write+Front Panel Window\\3APanel Bounds,write+Front Panel Window\\3AOrigin")
    saved = g.one(p, "Invoke Node", [("reference", vi), ("error in (no error)", sized)], "error out",
        target="Save.Instrument", type="{LV.VI}")
    closed = g.one(p, "Close Reference", [("reference", vi), ("error in (no error)", saved)], "error out")
    result = g.fields(p, closed, ["code", "source"])
    g.indicator(p, "Exit code", "int32", 0, result["code"], con=3)
    g.indicator(p, "Output", "string", "", result["source"], con=2)
    return g


def cli_panel():
    g = Graph("PuT3 CLI Panel", "Oeffnet eine Pruefdatei in der separaten LabVIEW-Instanz.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    path = g.one(p, "String To Path", [("string", g.index(p, args, 0))], "path")
    opened = g.node(p, "Open VI Reference", [("vi path", path)], ["vi reference", "error out"])
    panel = g.one(p, "Invoke Node", [("reference", opened["vi reference"]),
        ("error in (no error)", opened["error out"]), ("Activate", g.constant(p, "bool", "true"))],
        "error out", target="FP.Open", type="{LV.VI}")
    info = g.fields(p, panel, ["code", "source"])
    g.indicator(p, "Exit code", "int32", 0, info["code"], con=3)
    g.indicator(p, "Output", "string", "", info["source"], con=2)
    return g


def build(c, graph, folder=VI_DIR):
    folder.mkdir(parents=True, exist_ok=True)
    xml = graph.write(ROOT / ".build/aixml")
    with xml.open("a", encoding="utf-8") as stream:
        stream.write(f'\n<?lvmcp pane="{getattr(graph, "pattern", 4815)}" ?>\n{getattr(graph, "suffix", "")}\n')
    name = "lvai_convert_aixml_to_vi"
    schema = c.tools[name]["inputSchema"]["properties"]
    result = c.call(name, **{k: v for k, v in {
        "aiXmlFilePath": str(xml), "viPath": str(folder / (graph.name + ".vi")),
        "checkExecutable": False, "timeoutSeconds": 50}.items() if k in schema})
    print(graph.name, result.get("errorMessage", result.get("text", "")), flush=True)
    if result.get("errorCode", 0) or not (folder / (graph.name + ".vi")).is_file():
        raise RuntimeError(graph.name)


if __name__ == "__main__":
    with labview_client.LabviewClient("PuT3") as client:
        if "--probe" in sys.argv:
            print(json.dumps(client.call("lvai_connector_pane"), ensure_ascii=True))
            for name, tool in client.tools.items():
                if any(s in name for s in ("run_vi", "errors", "express", "close_vi")):
                    print(name, json.dumps(tool["inputSchema"], ensure_ascii=True))
        elif "--express" in sys.argv:
            EXPRESS_BUILD.mkdir(parents=True, exist_ok=True)
            for name in ("PuT3 DAQ.vi", "PuT3 TCP Austausch.vi"):
                shutil.copy2(INTERNAL_DIR / name, EXPRESS_BUILD / name)
            build(client, express_engine(), EXPRESS_BUILD)
            build(client, cli_express(), EXPRESS_BUILD)
            build(client, cli_express_layout(), EXPRESS_BUILD)
            build(client, express_dialog(), EXPRESS_BUILD / "members")
            build(client, express_help(), EXPRESS_BUILD / "members")
            build(client, cli_copy_express_member(), EXPRESS_BUILD)
        elif "--express-run" in sys.argv:
            shutil.copytree(VI_DIR, TEST_DIR, dirs_exist_ok=True)
            build(client, cli_express_runtime(), TEST_DIR)
        elif "--panel" in sys.argv:
            build(client, cli_panel(), TEST_DIR)
            g = Graph("PuT3 Private Express Steckprobe", "Leeres Blockdiagramm fuer die Express-Konfigurationspruefung.")
            g.indicator(g.doc, "Steckprobe", "bool", "false", g.constant(g.doc, "bool", "true"))
            build(client, g, TEST_DIR)
        else:
            if "--cli" not in sys.argv:
                for factory in (lambda: transport(SLOTS), acquire):
                    build(client, factory(), INTERNAL_DIR)
                for factory in (task, execute_task):
                    build(client, factory(), BLOCK_DIR)
                for factory in (assistant, function_generator):
                    build(client, factory())
            shutil.copytree(VI_DIR, TEST_DIR, dirs_exist_ok=True)
            for factory in (cli_probe, cli_errors, cli_daq, cli_generator):
                build(client, factory(), TEST_DIR)
