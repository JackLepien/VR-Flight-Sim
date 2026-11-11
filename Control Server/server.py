"""Local helicopter simulator control server.

Run with:
    .venv\\Scripts\\python.exe "Control Server\\server.py"

The browser control panel posts normalized controls to /api/control. If
UNITY_UDP_HOST is set, each control packet is also forwarded over UDP so a
Unity receiver can consume the same data later.
"""

from __future__ import annotations

import json
import math
import os
import socket
import threading
import time
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import urlparse


ROOT = Path(__file__).resolve().parent
WEB_ROOT = ROOT / "web"
HOST = "127.0.0.1"
PORT = int(os.environ.get("CONTROL_SERVER_PORT", "8765"))
UNITY_UDP_HOST = os.environ.get("UNITY_UDP_HOST", "")
UNITY_UDP_PORT = int(os.environ.get("UNITY_UDP_PORT", "5050"))

CONTROL_KEYS = ("cyclicX", "cyclicY", "collective", "yaw")
state_lock = threading.Lock()
control_state: dict[str, Any] = {
    "cyclicX": 0.0,
    "cyclicY": 0.0,
    "collective": 0.0,
    "yaw": 0.0,
    "sequence": 0,
    "updatedAt": time.time(),
}


def clamp(value: float) -> float:
    return max(-1.0, min(1.0, value))


def finite_number(value: Any) -> float:
    number = float(value)
    if not math.isfinite(number):
        raise ValueError("control values must be finite")
    return number


def snapshot() -> dict[str, Any]:
    with state_lock:
        return dict(control_state)


def send_udp(packet: dict[str, Any]) -> None:
    if not UNITY_UDP_HOST:
        return

    payload = (json.dumps(packet, separators=(",", ":")) + "\n").encode("utf-8")
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp_socket:
        udp_socket.sendto(payload, (UNITY_UDP_HOST, UNITY_UDP_PORT))


def update_controls(incoming: dict[str, Any]) -> dict[str, Any]:
    with state_lock:
        for key in CONTROL_KEYS:
            if key in incoming:
                control_state[key] = clamp(finite_number(incoming[key]))

        control_state["sequence"] += 1
        control_state["updatedAt"] = time.time()
        packet = dict(control_state)

    send_udp(packet)
    return packet


def reset_controls() -> dict[str, Any]:
    return update_controls({key: 0.0 for key in CONTROL_KEYS})


class ControlRequestHandler(BaseHTTPRequestHandler):
    """Serve the control panel and its small JSON API."""

    server_version = "HelicopterControlServer/0.1"

    def do_GET(self) -> None:  # noqa: N802 - required by BaseHTTPRequestHandler
        path = urlparse(self.path).path

        if path == "/" or path == "/index.html":
            self.serve_file("index.html", "text/html; charset=utf-8")
        elif path == "/styles.css":
            self.serve_file("styles.css", "text/css; charset=utf-8")
        elif path == "/app.js":
            self.serve_file("app.js", "text/javascript; charset=utf-8")
        elif path == "/api/state":
            self.send_json(
                {
                    **snapshot(),
                    "udpEnabled": bool(UNITY_UDP_HOST),
                    "udpTarget": f"{UNITY_UDP_HOST}:{UNITY_UDP_PORT}" if UNITY_UDP_HOST else None,
                }
            )
        elif path == "/health":
            self.send_json({"ok": True})
        else:
            self.send_error(HTTPStatus.NOT_FOUND, "Not found")

    def do_POST(self) -> None:  # noqa: N802 - required by BaseHTTPRequestHandler
        path = urlparse(self.path).path

        if path not in ("/api/control", "/api/reset"):
            self.send_error(HTTPStatus.NOT_FOUND, "Not found")
            return

        try:
            if path == "/api/reset":
                packet = reset_controls()
            else:
                body = self.read_json()
                packet = update_controls(body)
        except (ValueError, TypeError, json.JSONDecodeError) as error:
            self.send_json({"error": str(error)}, HTTPStatus.BAD_REQUEST)
            return
        except OSError as error:
            self.send_json({"error": f"UDP forwarding failed: {error}"}, HTTPStatus.BAD_GATEWAY)
            return

        self.send_json(packet)

    def do_OPTIONS(self) -> None:  # noqa: N802 - required by BaseHTTPRequestHandler
        self.send_response(HTTPStatus.NO_CONTENT)
        self.send_common_headers()
        self.end_headers()

    def read_json(self) -> dict[str, Any]:
        length = int(self.headers.get("Content-Length", "0"))
        if length > 16_384:
            raise ValueError("request body is too large")

        body = self.rfile.read(length)
        decoded = json.loads(body.decode("utf-8"))
        if not isinstance(decoded, dict):
            raise ValueError("request body must be a JSON object")
        return decoded

    def serve_file(self, filename: str, content_type: str) -> None:
        file_path = WEB_ROOT / filename
        try:
            content = file_path.read_bytes()
        except FileNotFoundError:
            self.send_error(HTTPStatus.NOT_FOUND, "Asset not found")
            return

        self.send_response(HTTPStatus.OK)
        self.send_common_headers()
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(content)))
        self.end_headers()
        self.wfile.write(content)

    def send_json(self, payload: dict[str, Any], status: HTTPStatus = HTTPStatus.OK) -> None:
        content = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_common_headers()
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(content)))
        self.end_headers()
        self.wfile.write(content)

    def send_common_headers(self) -> None:
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Cache-Control", "no-store")

    def log_message(self, format: str, *args: Any) -> None:
        # Keep the terminal readable while the control panel sends updates.
        if self.path not in ("/api/control", "/api/state"):
            super().log_message(format, *args)


def main() -> None:
    server = ThreadingHTTPServer((HOST, PORT), ControlRequestHandler)
    print(f"Helicopter control server: http://{HOST}:{PORT}")
    if UNITY_UDP_HOST:
        print(f"UDP forwarding enabled: {UNITY_UDP_HOST}:{UNITY_UDP_PORT}")
    else:
        print("UDP forwarding disabled. Set UNITY_UDP_HOST later when Unity is ready.")
    print("Press Ctrl+C to stop.")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopping server.")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
