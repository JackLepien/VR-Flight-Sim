# Send browser controls to Unity

This adds an alternative to the existing calibrated VR hand controls. VR Controller remains the default; its input actions and calibration button are preserved.

## Start the bridge

Stop any existing control server first, then run from the repository root:

```powershell
& ".\Control Server\Start-UnityControls.ps1"
```

The launcher uses `..\.venv` outside the repository and enables UDP forwarding to `127.0.0.1:15050`. Open <http://127.0.0.1:8765> and keep the browser tab visible while testing. The browser publishes every 50 ms; background tabs may be throttled. The forwarding label indicates configuration, not an acknowledgement from Unity.

## Choose controls in Unity

1. Open `Assets/Scenes/OpenXR Setup.unity`.
2. Select **XR Origin (VR)** and find **Tilt Controller** in the Inspector.
3. Set **Control Source** to **Local Server**. Do this outside Play mode to retain the choice, or during Play mode for a temporary test.
4. Enter Play mode. A **Helicopter Control Receiver** is added automatically at runtime if needed. It listens on UDP port 15050 on this computer.
5. Use the browser controls. Return to **VR Controller** to use the existing hand controls, including the A-button calibration.

| Browser control | Unity movement |
| --- | --- |
| Cyclic X / A, D | Left/right translation and bank |
| Cyclic Y / W, S | Forward/back translation and pitch |
| Collective / Q, E | Descend/climb; zero holds altitude |
| Pedals / J, L | Turn left/right around the headset |
| Space / RESET ALL | Neutralize all axes |

**Max Speed** controls horizontal speed, **Climb Speed** controls vertical speed, and **Yaw Speed** is degrees per second. Existing tilt settings apply to cyclic input. Collective uses the browser's current signed -1 to +1 convention. These are direct movement controls for the current prototype, not a helicopter lift or inertia simulation.

The receiver rejects malformed, incomplete, and non-finite packets and clamps axes to -1 through +1. It returns neutral controls after 0.5 seconds without valid input, so closing the browser or stopping the server stops commanded movement. Tilt eases back to level. Disable/re-enable the receiver to retry after a port binding error; only one receiver may bind port 15050 at a time.

This setup targets Unity Editor or a desktop player on the server's computer. A standalone Quest build runs on a different device and needs a separate network configuration; its loopback address points to the headset itself.
