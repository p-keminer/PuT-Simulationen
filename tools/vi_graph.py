"""AIXML graph builder reused from the local measurement-station tooling."""
import xml.etree.ElementTree as E

ERROR = "cluster{bool.status,int32.code,string.source}"

def escape(value):
    return str(value).replace("\\", "\\5C").replace(",", "\\2C").replace(":", "\\3A").replace("\r", "\\0D").replace("\n", "\\0A")

def literal(dtype, value):
    # Structural commas in arrays/clusters are syntax, not escaped string data.
    if dtype in ("string", "path"):
        return escape(value)
    return str(value).lower() if dtype == "bool" else str(value)


class Graph:
    def __init__(self, name, description):
        self.name = name
        self.doc = E.Element("VI", _name=name + ".vi", description=description)
        self.next_uid = 1000
        self.controls = []

    def uid(self):
        self.next_uid += 1
        return str(self.next_uid)

    def add(self, parent, tag, **attrs):
        uid = self.uid()
        parent_uid = parent.attrib.get("uid", "root")
        e = E.SubElement(parent, tag, uid=uid, uid_parent=parent_uid, **attrs)
        return e

    def constant(self, parent, dtype, value):
        e = self.add(parent, "Constant", type=dtype, value=literal(dtype, value))
        e.set("outputs", "value:" + e.attrib["uid"] + ".value")
        return e.attrib["uid"] + ".value"

    def control(self, parent, name, dtype, value, position=None, con=None, style=None):
        e = self.add(parent, "Control", _name=name, type=dtype, value=literal(dtype, value))
        if con is not None:
            e.set("conIdx", str(con)); e.set("connection", "required")
        if style:
            e.set("style", style)
        net = e.attrib["uid"] + ".value"
        e.set("outputs", "value:" + net)
        self.controls.append({"name": name, "position": position, "uid": e.attrib["uid"]})
        return net

    def indicator(self, parent, name, dtype, value, net, position=None, con=None, style=None):
        e = self.add(parent, "Indicator", _name=name, type=dtype, value=literal(dtype, value), inputs="value:" + net)
        if con is not None:
            e.set("conIdx", str(con)); e.set("connection", "recommended")
        if style:
            e.set("style", style)
        self.controls.append({"name": name, "position": position, "uid": e.attrib["uid"]})
        return e

    def node(self, parent, name, inputs, outputs, **attrs):
        e = self.add(parent, "Node", _name=escape(name), **attrs)
        e.set("inputs", ",".join(escape(k) + ":" + v for k, v in inputs))
        nets = {k: e.attrib["uid"] + ".out" + str(i) for i, k in enumerate(outputs)}
        e.set("outputs", ",".join(escape(k) + ":" + v for k, v in nets.items()))
        return nets

    def one(self, parent, name, inputs, output, **attrs):
        return self.node(parent, name, inputs, [output], **attrs)[output]

    def select(self, parent, test, yes, no):
        return self.one(parent, "Select", [("t", yes), ("s", test), ("f", no)], "s? t:f")

    def fields(self, parent, source, names):
        return self.node(parent, "Unbundle By Name", [("input cluster", source)], names, fields=",".join(names))

    def array(self, parent, values):
        return self.one(parent, "Build Array", [("element", v) for v in values], "appended array")

    def index(self, parent, source, index):
        value = self.constant(parent, "int32", index)
        return self.one(parent, "Index Array", [("array", source), ("index", value)], "element")

    def greater_zero(self, parent, value):
        zero = self.constant(parent, "double", 0)
        return self.one(parent, "Greater?", [("x", value), ("y", zero)], "x > y?")

    def bundle(self, parent, dtype, defaults, fields):
        seed = self.constant(parent, dtype, defaults)
        return self.one(parent, "Bundle By Name", [("input cluster", seed)] + list(fields.items()),
                        "output cluster", fields=",".join(fields))

    def call(self, parent, target, inputs, outputs):
        e = self.add(parent, "Call", target=target)
        e.set("inputs", ",".join(escape(k) + ":" + v for k, v in inputs))
        nets = {k: e.attrib["uid"] + ".out" + str(i) for i, k in enumerate(outputs)}
        e.set("outputs", ",".join(escape(k) + ":" + v for k, v in nets.items()))
        return nets

    def write(self, folder):
        folder.mkdir(parents=True, exist_ok=True)
        E.indent(self.doc, "  ")
        path = folder / (self.name + ".xml")
        E.ElementTree(self.doc).write(path, encoding="utf-8")
        return path
