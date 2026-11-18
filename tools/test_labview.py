"""End-to-end checks against the isolated NI LabVIEW CLI instance."""
from pathlib import Path
import re
import socket
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
PORT = "48372"
WORKBENCH_PORT = 48371
VI = ROOT / ".build/native-test"
HIDDEN = subprocess.CREATE_NO_WINDOW


def cli(test_name, *args):
    executable = VI / f"PuT3 CLI {test_name}.vi"
    result = subprocess.run(["LabVIEWCLI", "-OperationName", "RunVI", "-PortNumber", PORT,
                             "-VIPath", str(executable), *map(str, args)],
                            capture_output=True, encoding="mbcs", errors="replace", timeout=15,
                            creationflags=HIDDEN)
    if result.returncode:
        raise RuntimeError(f"{test_name} {args}\n{result.stdout}\n{result.stderr}")
    return result.stdout


def measure(mode, channel, *, pfi="_", rate=1000, count=1, value="_", continuous=False, span=10):
    text = cli("Messung", mode, "myDAQ1/" + channel, pfi, rate, count, span, int(continuous), value)
    status, dt, rest = re.search(r"^RESULT\|([^|]+)\|([^|]+)\|([^\r\n]*)", text, re.M).groups()
    return float(status), float(dt), [float(x) for x in rest.split()]


def wait_for_server():
    for attempt in range(70):
        try:
            with socket.create_connection(("127.0.0.1", WORKBENCH_PORT), timeout=.1):
                return
        except OSError:
            time.sleep(.1)
    raise TimeoutError("Steckwerkbank startet nicht")


def start_fixture(name):
    with socket.socket() as probe:
        if probe.connect_ex(("127.0.0.1", WORKBENCH_PORT)) == 0:
            raise RuntimeError("Testport bereits belegt")
    process = subprocess.Popen([str(ROOT / "Start/PuT3.exe"), "--bench-test-server", name],
                               creationflags=HIDDEN)
    try:
        wait_for_server()
    except Exception:
        process.terminate()
        process.wait(timeout=5)
        raise
    return process


def main():
    def check(label, condition):
        if not condition:
            raise AssertionError(label)
        print("PASS", label, flush=True)

    def near(actual, expected, tolerance=.01):
        return abs(actual - expected) <= tolerance

    process = start_fixture("intact")
    try:
        check("Native TCP loopback", "\n0\t1\n" in cli("Probe", "PING"))
        check("Native AO0 constant", measure(2, "ao0", value=3.25)[0] == 0)
        status, dt, data = measure(1, "ai0", count=4)
        check("5.1 native AI0 loopback through BNC", status == 0 and near(dt, 0.001) and
              len(data) == 4 and all(near(x, 3.25) for x in data))
        check("5.2 native DMM resistance", near(measure(6, "dmm")[2][0], 3000))
        express = cli("Express Lauf", "myDAQ1/dmm")
        express_status, express_resistance = re.search(r"RESULT\|([^|]+)\|([^\r\n]+)", express).groups()
        check("5.2 NI-generated Express execution subVI", float(express_status) == 0 and near(float(express_resistance), 3000))
        check("5.3 LabVIEW rectangular generator at AO1", "RESULT|0.000000" in cli("Generator", "myDAQ1/ao1"))
        check("5.3 native frequency via PFI1", near(measure(5, "ctr0", pfi="PFI1")[2][0], 50, .2))
        check("5.3 wrong PFI0 does not see the waveform", measure(5, "ctr0", pfi="PFI0")[0] == -5)
        check("5.4 LabVIEW rectangular generator at AO0", "RESULT|0.000000" in cli("Generator", "myDAQ1/ao0"))
        status, dt, data = measure(1, "ai0", count=400, rate=2000, continuous=True)
        edges = [i for i in range(1, len(data)) if data[i - 1] < 2.5 <= data[i]]
        check("5.4 continuous sampled rectangle with LabVIEW waveform dt", status == 0 and len(data) == 400 and
              near(dt, .0005, .0000001) and min(data) < .002 and max(data) > 4.99 and
              len(edges) >= 8 and all(abs(right - left - 40) <= 1 for left, right in zip(edges, edges[1:])))
        check("5.5 native AI1 potentiometer", near(measure(1, "ai1")[2][0], 5.0067))
        check("Release channels before digital exercise", measure(7, "port0")[0] == 0)
        check("5.5 native 8-bit port output and readback", measure(4, "port0", value=131)[0] == 0 and
              measure(3, "port0")[2] == [131])
        check("Invalid physical channel from LabVIEW", measure(1, "ai99")[0] == -1)
    finally:
        process.terminate()
        process.wait(timeout=5)

    process = start_fixture("open-bnc")
    try:
        check("Native cable break changes result", measure(2, "ao0", value=3.25)[0] == 0 and
              measure(1, "ai0")[0] == -3)
    finally:
        process.terminate()
        process.wait(timeout=5)


if __name__ == "__main__":
    main()
