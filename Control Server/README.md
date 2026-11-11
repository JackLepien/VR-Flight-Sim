# Local Helicopter Control Server

This is a browser-based control surface for testing the helicopter simulator before the physical controls are available.

## Run it

From the project root:

```powershell
& ".\\.venv\\Scripts\\python.exe" ".\\Control Server\\server.py"
```

Open <http://127.0.0.1:8765> in a browser.

## Controls

- Drag the cyclic pad, or use `WASD`
- Move collective with the slider, or use `Q` / `E`
- Move yaw pedals with the slider, or use `J` / `L`
- Press `Space` or `RESET ALL` to return every axis to neutral

All values are normalized from `-1.0` to `+1.0`.

## Control packet

The browser sends `POST /api/control` every 50 milliseconds:

```json
{
  "cyclicX": 0.0,
  "cyclicY": 0.0,
  "collective": 0.0,
  "yaw": 0.0
}
```

The server also exposes `GET /api/state` and `GET /health`.

Unity forwarding is intentionally disabled until the Unity receiver is implemented. To enable the future UDP bridge:

```powershell
$env:UNITY_UDP_HOST = "127.0.0.1"
$env:UNITY_UDP_PORT = "5050"
& ".\\.venv\\Scripts\\python.exe" ".\\Control Server\\server.py"
```
