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
TEST_DIR = ROOT / ".build/native-test"
os.environ["AIXML2VI_TRY_WIRES"] = "Refnum.TCPNetConn"
SLOTS = {"inputs": [11, 10, 9], "outputs": [3, 2, 1], "error_in": 8, "error_out": 0}
ARRAY = "array{double}"
TYPES = ["Wahl treffen", "AI Spannung", "AO Spannung", "DI Lesen", "DO Schreiben",
         "CTR Frequenz", "DMM Widerstand", "Ausgaenge freigeben"]
CONFIG = "cluster{uint8{" + ",".join(TYPES) + "}.Messart,string.Kanal,string.PFI,double.Rate,int32.Samples,double.Bereich,bool.Kontinuierlich}"
CONFIG_DEFAULT = "[0,,,1000,100,10,false]"


def transport(slots):
    g = Graph("PuT3 TCP Austausch", "Eine Anfrage an die lokale Steckwerkbank. Antwort: Status und Messdaten als tabulatorgetrennte Zahlen.")
    p = g.doc
    command = g.control(p, "Anfrage", "string", "", con=slots["inputs"][0])
    error = g.control(p, "error in", ERROR, "[false,0,]", con=slots["error_in"])
    p[-1].set("connection", "recommended")
    timeout = g.constant(p, "int32", 2500)
    opened = g.node(p, "TCP Open Connection", [
        ("address", g.constant(p, "string", "127.0.0.1")),
        ("remote port or service name", g.constant(p, "uint16", 48371)),
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
        ("Messart", "uint8{" + ",".join(TYPES) + "}", 0),
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
    g = Graph("PuT3 Auftrag", "Erstellt den Typ fuer PuT3 DAQ.vi. Hier Messart, Kanal, Bereich und Abtastung einstellen. Die Steckwerkbank wird erst beim Aufruf von PuT3 DAQ angesprochen.")
    config = configure(g)
    g.pattern = 4835
    for slot, control in enumerate(list(g.doc.findall("Control"))):
        control.set("conIdx", str(slot))
        control.set("connection", "recommended")
    g.indicator(g.doc, "Auftrag", CONFIG, CONFIG_DEFAULT, config, con=20)
    return g


def assistant():
    g = Graph("PuT3 Messassistent", "Bedien-VI fuer die Steckwerkbank. Messart, physikalischen Kanal und Timing waehlen. Ausgabewerte sind einzelne Portwerte oder ein analoger Datenblock. Bedien-VI einmal ausfuehren oder PuT3 DAQ.vi in eigener Schleife aufrufen.")
    p = g.doc
    config = configure(g)
    output = g.control(p, "Ausgabewerte", ARRAY, "[]")
    g.pattern = 4835
    for slot, control in enumerate(list(p.findall("Control"))):
        control.set("conIdx", str(slot))
        control.set("connection", "recommended")
    result = subvi(g, "PuT3 DAQ", [("Auftrag", config), ("Ausgabewerte", output)],
                   ["Daten", "Status", "Signal", "error out"])
    g.indicator(p, "Signal", "doublewaveform", "", result["Signal"], style="graph21703", con=20)
    g.indicator(p, "Daten", ARRAY, "[]", result["Daten"], con=21)
    g.indicator(p, "Status", "double", 0, result["Status"], con=22)
    g.indicator(p, "error out", ERROR, "[false,0,]", result["error out"], con=27)
    return g


def function_generator():
    g = Graph("PuT3 Funktionsgenerator", "Virtuelles SFP. Frequenz, Signalform, Spitze-Spitze und Offset in LabVIEW waehlen. Ein Lauf startet den gewaehlten Analogausgang. Ein neuer Lauf ersetzt das Signal auf dem gewaehlten Kanal.")
    p = g.doc
    form_type = "uint8{Sinus,Rechteck,Dreieck,Gleichspannung}"
    channel = g.control(p, "Physikalischer Kanal", "string", "")
    form = g.control(p, "Signalform", form_type, 0)
    freq = g.control(p, "Frequenz Hz", "double", 50)
    peak_to_peak = g.control(p, "Amplitude Vss", "double", 2)
    offset = g.control(p, "Offset V", "double", 0)
    points = g.control(p, "Punkte pro Periode", "int32", 100)
    span = g.control(p, "Spannungsbereich ±V", "double", 10)
    g.pattern = 4835
    for slot, control in enumerate(list(p.findall("Control"))):
        control.set("conIdx", str(slot))
        control.set("connection", "recommended")

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
    config = g.bundle(p, CONFIG, CONFIG_DEFAULT, {
        "Messart": g.constant(p, "uint8{" + ",".join(TYPES) + "}", 2),
        "Kanal": channel, "Rate": rate, "Bereich": span,
        "Kontinuierlich": g.constant(p, "bool", "true")})
    output = subvi(g, "PuT3 DAQ", [("Auftrag", config), ("Ausgabewerte", buffer)], ["Status", "error out"])
    dt = g.one(p, "Reciprocal", [("x", rate)], "1/x")
    wave = g.one(p, "Build Waveform", [("dt", dt), ("Y", buffer)], "output waveform", fields="dt,Y")
    g.indicator(p, "Signalvorschau", "doublewaveform", "", wave, style="graph21703", con=20)
    g.indicator(p, "Status", "double", 0, output["Status"], con=21)
    g.indicator(p, "error out", ERROR, "[false,0,]", output["error out"], con=27)
    return g


def cli_probe():
    g = Graph("PuT3 CLI Probe", "Fuehrt eine skalierbare native Test-Anfrage an die Steckwerkbank aus.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    result = subvi(g, "PuT3 TCP Austausch", [("Anfrage", g.index(p, args, 0))], ["Antwort", "error out"])
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
    cfg = g.bundle(p, CONFIG, CONFIG_DEFAULT,
        dict(zip(["Messart", "Kanal", "PFI", "Rate", "Samples", "Bereich", "Kontinuierlich"],
                 [operation, values[1], values[2], integer(3), integer(4), integer(5), continuous])))
    data_in = g.one(p, "Spreadsheet String To Array", [
        ("format string", g.constant(p, "string", "%.;%f")), ("spreadsheet string", values[7]),
        ("array type (2D Dbl)", g.constant(p, ARRAY, "[]")),
        ("delimiter (Tab)", g.constant(p, "string", ","))], "array")
    result = subvi(g, "PuT3 DAQ", [("Auftrag", cfg), ("Ausgabewerte", data_in)],
                   ["Daten", "Status", "Signal", "error out"])
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
    generator = subvi(g, "PuT3 Funktionsgenerator", [
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
    g = Graph("PuT3 Experiment Express Creator", "Entwicklungsprobe mit dem installierten NI Express VI Creator.")
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
    name = g.constant(p, "string", "PuT3 Virtuelle DAQ")
    result = wizard("byob_Convert VI to Block.vi", [
        ("selected VI", paths[0]), ("parent directory", paths[1]),
        ("block name", name)], ["block path", "sub-block comment", "error out"])
    names = ["Messart", "Physikalischer Kanal", "PFI Klemme", "Abtastrate Hz", "Samples pro Block",
             "Spannungsbereich ±V", "Kontinuierlich", "Ausgabewerte", "Signal", "Daten", "Status", "error out"]
    rule = "uint16{Invalid Wire Rule,Required,Recommended,Optional}"
    terminal_info = ("cluster{array{bool}.Expandable,array{uint16}.Expand Type,array{bool}.Configurable,"
                     "array{uint16}.Config Type,array{" + rule + "}.connection type}")
    info = g.constant(p, terminal_info,
        "[[true,true,true,true,true,true,true,true,true,true,true,true],"
        "[0,0,0,0,0,0,0,0,0,0,0,0],[true,true,true,true,true,true,true,false,false,false,false,false],"
        "[0,0,0,0,0,0,0,0,0,0,0,0],[2,2,2,2,2,2,2,2,2,2,2,2]]")
    config = wizard("byob_Create Config Page.vi", [
        ("parent directory", paths[1]), ("subVI info", result["sub-block comment"]),
        ("block name", name), ("block path", result["block path"]),
        ("terminal names", g.constant(p, "array{string}", "[" + ",".join(names) + "]")),
        ("terminal info", info), ("error in (no error)", result["error out"])],
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
    g = Graph("PuT3 CLI Express Lauf", "Fuehrt die vom NI-Assistenten erzeugte Express-Quelle mit einem DMM-Auftrag aus.")
    p = g.doc
    args = g.control(p, "Inputs", "array{string.String}", "[]", con=11)
    p[-1].set("connection", "recommended")
    target = "subPuT3 Virtuelle DAQ.vi"
    result = g.call(p, target, [
        ("Messart", g.constant(p, "uint8{" + ",".join(TYPES) + "}", 6)),
        ("Physikalischer Kanal", g.index(p, args, 0))],
        ["Daten", "Status", "error out"])
    uid = p.findall("Call")[-1].get("uid")
    g.suffix = f'<?lvmcp calllink uid="{uid}" tp="1" path="|PuT3 Virtuelle DAQSource.llb|{target}" ?>'
    code = g.fields(p, result["error out"], ["code"])["code"]
    g.indicator(p, "Exit code", "int32", 0, code, con=3)
    g.indicator(p, "Output", "string", "", cat(g, g.constant(p, "string", "RESULT|"),
        format_value(g, result["Status"], "%.;%g"), g.constant(p, "string", "|"),
        format_value(g, g.index(p, result["Daten"], 0), "%.;%.8g")), con=2)
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
            build(client, assistant())
            build(client, cli_express(), TEST_DIR)
        elif "--express-run" in sys.argv:
            TEST_DIR.mkdir(parents=True, exist_ok=True)
            for path in VI_DIR.glob("*.llb"):
                shutil.copy2(path, TEST_DIR / path.name)
            for name in ("PuT3 DAQ.vi", "PuT3 TCP Austausch.vi"):
                shutil.copy2(VI_DIR / name, TEST_DIR / name)
            build(client, cli_express_runtime(), TEST_DIR)
        elif "--panel" in sys.argv:
            build(client, cli_panel(), TEST_DIR)
            g = Graph("PuT3 Private Express Steckprobe", "Leeres Blockdiagramm fuer die Express-Konfigurationspruefung.")
            g.indicator(g.doc, "Steckprobe", "bool", "false", g.constant(g.doc, "bool", "true"))
            build(client, g, TEST_DIR)
        else:
            if "--cli" not in sys.argv:
                for factory in (lambda: transport(SLOTS), acquire, task, assistant, function_generator):
                    build(client, factory())
            TEST_DIR.mkdir(parents=True, exist_ok=True)
            for name in ("PuT3 DAQ.vi", "PuT3 TCP Austausch.vi", "PuT3 Funktionsgenerator.vi"):
                shutil.copy2(VI_DIR / name, TEST_DIR / name)
            for factory in (cli_probe, cli_errors, cli_daq, cli_generator):
                build(client, factory(), TEST_DIR)
