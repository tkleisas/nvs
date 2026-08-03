#!/usr/bin/env python3
"""CLI client for the NVS embedded UI-automation server.

Start NVS with the automation server enabled (loopback only):
    nvs --automation-port 5050
    # or: set NVS_AUTOMATION_PORT=5050

Then drive it with JSON-lines commands:
    python tools/automation_client.py ping
    python tools/automation_client.py state
    python tools/automation_client.py tree --max-depth 6 --max-nodes 2000
    python tools/automation_client.py screenshot --path shot.png [--control DatabaseTreeView]
    python tools/automation_client.py command --name ShowDatabaseExplorer
    python tools/automation_client.py menu --path "Database/Ask AI..."
    python tools/automation_client.py open-solution --path C:/src/MyApp/MyApp.slnx
    python tools/automation_client.py activate --id DatabaseExplorer

Protocol: one JSON request per line -> one JSON response per line.
    {"id":1,"cmd":"screenshot","args":{"path":"shot.png"}}
    <- {"id":1,"ok":true,"result":{"path":"...","width":1200,"height":800}}
"""

from __future__ import annotations

import argparse
import itertools
import json
import socket
import sys

_ids = itertools.count(1)


def send(port: int, cmd: str, args: dict | None = None, timeout: float = 120.0) -> dict:
    request = {"id": next(_ids), "cmd": cmd}
    if args:
        request["args"] = args
    payload = json.dumps(request) + "\n"

    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as sock:
        sock.sendall(payload.encode("utf-8"))
        response = b""
        while not response.endswith(b"\n"):
            chunk = sock.recv(65536)
            if not chunk:
                break
            response += chunk

    return json.loads(response.decode("utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("cmd", help="ping | state | tree | screenshot | command | menu | open-solution | activate")
    parser.add_argument("--port", type=int, default=5050, help="automation port (default 5050)")
    parser.add_argument("--path", help="file path (screenshot output, solution path, or menu path)")
    parser.add_argument("--control", help="automation id or control name for --cmd screenshot/tree")
    parser.add_argument("--window", help="window title (substring) to screenshot instead of the main window")
    parser.add_argument("--text", help="text for --cmd set-text")
    parser.add_argument("--name", help="command name for --cmd command")
    parser.add_argument("--id", help="panel id for --cmd activate")
    parser.add_argument("--max-depth", type=int, help="tree max depth")
    parser.add_argument("--max-nodes", type=int, help="tree max nodes")
    ns = parser.parse_args()

    args: dict = {}
    for key in ("path", "control", "window", "text", "name", "id"):
        value = getattr(ns, key)
        if value is not None:
            args[key] = value
    if ns.max_depth is not None:
        args["maxDepth"] = ns.max_depth
    if ns.max_nodes is not None:
        args["maxNodes"] = ns.max_nodes

    try:
        response = send(ns.port, ns.cmd, args or None)
    except (ConnectionRefusedError, socket.timeout, OSError) as exc:
        print(f"error: cannot reach automation server on 127.0.0.1:{ns.port} ({exc})", file=sys.stderr)
        print("hint: start NVS with --automation-port %d" % ns.port, file=sys.stderr)
        return 2

    print(json.dumps(response, indent=2))
    return 0 if response.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
