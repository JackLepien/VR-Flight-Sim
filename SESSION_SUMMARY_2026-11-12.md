# Flight simulator session summary — November 11–12, 2026

This session crossed midnight in America/Chicago. This document records the completed work and context for future sessions.

## Goal and important user preferences

The local browser control surface simulates a real helicopter simulator's cyclic joystick, collective stick, and foot pedals. We connected these controls to the existing Unity movement prototype.

- Keep the existing XR Origin VR controller controls and calibration. Browser controls are an alternative input source.
- Keep Python's `.venv` outside the Unity project and Git repository.
- The user explicitly requested that `Control Server/README.md`'s startup path remain unchanged. We left that README unchanged; use the separate launcher and Unity guide below for this machine.

## Python environment and local server

The README referenced a missing in-repository `.venv`. We initially created it to run the server, then relocated it at the user's request. Because virtual environments contain absolute paths, we recreated it at the external location and removed the in-repository copy.

Current environment:

```text
C:\Users\Jack\Documents\#College\Artmaking in VR\Flight Sim\.venv
```

This is `..\.venv` relative to the repository root. Python is version 3.12.10. The server uses only Python's standard library; no extra packages were required.

The server site is <http://127.0.0.1:8765>. We started it, verified successful HTTP responses, and opened it in the browser. We also opened the external environment folder in File Explorer at the user's request.

## Unity bridge implementation

Data flow:

```text
Browser controls
  -> POST /api/control every 50 ms
  -> Python server
  -> UDP JSON to 127.0.0.1:15050
  -> HelicopterControlReceiver
  -> TiltController on XR Origin (VR)
```

Packets carry `cyclicX`, `cyclicY`, `collective`, and `yaw`, each normalized to -1 through +1. The existing server also includes sequence and timestamp metadata.

### Files added or changed

- `Flight Sim/Assets/Scripts/HelicopterControlReceiver.cs` and its `.meta`: receives local UDP packets on Unity's main thread without blocking, validates all four axes, clamps values, and returns neutral controls after 0.5 seconds without a valid packet. Socket cleanup handles disable/re-enable and failed binds.
- `Flight Sim/Assets/Scripts/TiltController.cs`: adds a selectable **Control Source** with **VR Controller** and **Local Server**. VR Controller remains the code default; existing input actions, hand calibration, and response settings are preserved. Local Server adds collective and pedal control. The receiver is added at runtime if needed.
- `Control Server/server.py`: includes UDP forwarding configuration in control/reset HTTP responses so the browser can display it. The default UDP destination port is now 15050; forwarding still requires `UNITY_UDP_HOST` to be configured.
- `Control Server/web/app.js`: labels configured forwarding as **UDP FORWARDING**. This indicates configuration, not acknowledgement that Unity is receiving.
- `Control Server/Start-UnityControls.ps1`: uses the external environment and sets `UNITY_UDP_HOST=127.0.0.1` and `UNITY_UDP_PORT=15050` before starting the server.
- `Control Server/UNITY_INPUTS.md`: setup instructions, axis mapping, troubleshooting, and current limitations.
- `Control Server/test_unity_bridge.py`: isolated HTTP-to-UDP integration test, including control values, forwarding metadata, and reset packets.
- `Flight Sim/Assets/Editor/HelicopterControlChecks.cs` and its `.meta`: Unity editor validation of the default port, real UDP delivery, axis mapping, clamping, malformed/incomplete/non-finite input rejection, timeout, recovery, and socket close/reopen.

### Axis mapping

| Input | Prototype behavior |
| --- | --- |
| Cyclic X / A, D | Left/right movement and bank |
| Cyclic Y / W, S | Forward/back movement and pitch |
| Collective / Q, E | Descend/climb; zero holds altitude |
| Pedals / J, L | Left/right yaw around the headset |
| Space / RESET ALL | Neutralize all axes |

Horizontal speed uses **Max Speed**. Local Server defaults are **Climb Speed = 3 m/s** and **Yaw Speed = 45 degrees/s**. Existing tilt settings apply to cyclic input. This remains direct prototype movement; we did not implement helicopter lift, inertia, or rotor physics.

## Runtime port error and fix

Unity initially failed to bind `127.0.0.1:5050` with a socket access-permissions error. Diagnostics reproduced the failure and showed `0.0.0.0:5050` already occupied by `svchost.exe` (PID 9712 at the time). Windows reported no UDP exclusion ranges. We left the Windows service alone.

We verified port 15050 was available, changed the receiver/server/launcher defaults and Unity guide to 15050, and restarted the server with that destination. The original README still contains its old 5050 example because the user requested leaving it unchanged.

Unity's validation passed on the actual new default port. After the change, exit and re-enter Play mode to recreate a runtime receiver. If a receiver has been manually saved with an older **Port** value, set it to 15050 as well.

## Resume in a future session

From the repository root, stop any already running control server before launching another:

```powershell
& ".\Control Server\Start-UnityControls.ps1"
```

1. Open <http://127.0.0.1:8765> and keep the browser tab visible; background tabs can throttle updates.
2. In Unity, open `Assets/Scenes/OpenXR Setup.unity`.
3. Select **XR Origin (VR)**, then **Tilt Controller → Control Source → Local Server**.
4. Enter Play mode and use the browser controls.
5. Select **VR Controller** to return to the existing calibrated hand controls. The right controller's A button recalibrates neutral.

The scene was observed with **Local Server** selected during testing. The class default remains VR Controller. Inspect the current scene rather than assuming its saved selection.

The server was last confirmed healthy and forwarding to `127.0.0.1:15050`. Process state does not persist reliably between sessions; check `/health` and `/api/state` instead of assuming it is still running. Windows GUI launch and access to the installed Python executable required sandbox escalation in this session.

## Verification and scope

- Python HTTP-to-UDP integration test passed. Rerun from the repository root with `& "..\.venv\Scripts\python.exe" -B ".\Control Server\test_unity_bridge.py"`.
- Unity scripts compiled successfully. An existing warning about the Inspector-assigned `m_ForwardSource` field appeared in the standalone compilation check.
- Unity UDP validation passed, including binding the actual new default port. Rerun outside Play mode with **Tools → Flight Sim → Validate Local Controls**. Its automated request/result files are under Unity's ignored `Library` directory.
- `Control Server/README.md` remained unchanged.
- The user confirmed the setup worked after the port fix. We did not record a quantitative flight-motion or latency test.
- Existing scene changes and colored cube grid editor files were already present before this work. We did not create the grid or overwrite the scene during the bridge implementation.
- Git status was clean immediately before creating this summary. No commit was made by the assistant during this session.

This connection targets Unity Editor or a desktop player on the server's computer. Standalone Quest networking is not configured: the headset's loopback address points to itself. Future work can address a networked headset, physical hardware input, or flight physics while retaining the selectable VR control mode.
