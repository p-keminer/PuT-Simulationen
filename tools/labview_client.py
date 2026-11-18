"""Small bounded MCP stdio client for reproducible local engineering and tests."""
from datetime import datetime
from pathlib import Path
import json
import os
import queue
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parents[1]


class LabviewClient:
    def __init__(self, label="AT1B"):
        executable = Path(os.environ.get("LABVIEW_MCP_EXE", str(Path.home() / ".codex/tools/labview-mcp-v2.3.2/server/bin/LabVIEWMCP.exe")))
        if not executable.is_file():
            raise FileNotFoundError("Set LABVIEW_MCP_EXE to the installed LabVIEW MCP executable")
        self.log = ROOT / "Nachweise" / (label + "_" + datetime.now().strftime("%Y%m%d_%H%M%S_%f"))
        self.log.mkdir(parents=True)
        self.err = (self.log / "server.stderr.log").open("w", encoding="utf-8")
        self.proc = subprocess.Popen([str(executable)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                     stderr=self.err, text=True, encoding="utf-8", bufsize=1,
                                     env=dict(os.environ, LABVIEWMCP_AIXML_BACKEND="pylabview"),
                                     creationflags=subprocess.CREATE_NO_WINDOW)
        self.messages = queue.Queue()
        self.seq = 0
        threading.Thread(target=self._receive, daemon=True).start()
        self.request("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                    "clientInfo": {"name": label, "version": "1"}})
        self.send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        result = self.request("tools/list", {})
        self.tools = {x["name"]: x for x in result["result"]["tools"]}

    def _receive(self):
        for line in self.proc.stdout:
            try:
                self.messages.put(json.loads(line))
            except json.JSONDecodeError:
                self.messages.put({"non_protocol_stdout": line})
        self.messages.put({"server_closed": True})

    def send(self, message):
        self.proc.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
        self.proc.stdin.flush()

    def request(self, method, params, timeout=45):
        self.seq += 1
        message = {"jsonrpc": "2.0", "id": self.seq, "method": method, "params": params}
        stem = self.log / (f"{self.seq:04d}-" + method.replace("/", "-"))
        stem.with_suffix(".request.json").write_text(json.dumps(message, indent=2, ensure_ascii=False), encoding="utf-8")
        self.send(message)
        deadline = time.monotonic() + timeout
        while True:
            left = deadline - time.monotonic()
            if left <= 0:
                raise TimeoutError(f"MCP timeout in {method}, id {self.seq}; inspect preserved process/evidence before retry")
            try:
                answer = self.messages.get(timeout=left)
            except queue.Empty:
                raise TimeoutError(f"MCP timeout in {method}, id {self.seq}")
            if answer.get("server_closed"):
                raise RuntimeError("MCP server closed its output")
            if answer.get("id") != self.seq:
                continue
            stem.with_suffix(".response.json").write_text(json.dumps(answer, indent=2, ensure_ascii=False), encoding="utf-8")
            if "error" in answer:
                raise RuntimeError(answer["error"])
            return answer

    def call(self, name, **arguments):
        schema = self.tools[name]["inputSchema"]
        if "backend" in schema.get("properties", {}):
            arguments.setdefault("backend", "pylabview")
        timeout = min(120, arguments.get("timeoutSeconds", 30) + 10)
        answer = self.request("tools/call", {"name": name, "arguments": arguments}, timeout=timeout)
        if answer["result"].get("isError"):
            raise RuntimeError(answer["result"])
        text = "\n".join(x["text"] for x in answer["result"].get("content", []) if x["type"] == "text")
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return {"text": text}

    def close(self):
        self.proc.stdin.close()
        try:
            self.proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.proc.terminate()
            self.proc.wait(timeout=5)
        self.err.close()

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()
