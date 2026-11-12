"""Run with Python's unittest; uses isolated HTTP and UDP ports."""
import json
import socket
import threading
import unittest
import urllib.request
from unittest.mock import patch

import server


class UnityBridgeTests(unittest.TestCase):
    def test_browser_posts_are_forwarded_with_status_and_reset(self):
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as receiver:
            receiver.bind(("127.0.0.1", 0))
            receiver.settimeout(2)
            with patch.object(server, "UNITY_UDP_HOST", "127.0.0.1"), patch.object(
                server, "UNITY_UDP_PORT", receiver.getsockname()[1]
            ), patch.dict(server.control_state, server.snapshot()):
                http = server.ThreadingHTTPServer(("127.0.0.1", 0), server.ControlRequestHandler)
                thread = threading.Thread(target=http.serve_forever, daemon=True)
                thread.start()
                try:
                    base = f"http://127.0.0.1:{http.server_port}"
                    axes = {"cyclicX": 0.25, "cyclicY": -0.5, "collective": 0.75, "yaw": -1}
                    request = urllib.request.Request(
                        base + "/api/control", json.dumps(axes).encode(),
                        {"Content-Type": "application/json"}, method="POST"
                    )
                    with urllib.request.urlopen(request, timeout=2) as response:
                        status = json.load(response)
                    forwarded = json.loads(receiver.recv(16384))
                    for key, value in axes.items():
                        self.assertEqual(status[key], value)
                        self.assertEqual(forwarded[key], value)
                    self.assertTrue(status["udpEnabled"])
                    self.assertEqual(status["udpTarget"], f"127.0.0.1:{receiver.getsockname()[1]}")
                    self.assertEqual(status["sequence"], forwarded["sequence"])

                    with urllib.request.urlopen(
                        urllib.request.Request(base + "/api/reset", b"", method="POST"), timeout=2
                    ) as response:
                        reset = json.load(response)
                    forwarded_reset = json.loads(receiver.recv(16384))
                    for key in axes:
                        self.assertEqual(reset[key], 0)
                        self.assertEqual(forwarded_reset[key], 0)
                finally:
                    http.shutdown()
                    http.server_close()
                    thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main()
