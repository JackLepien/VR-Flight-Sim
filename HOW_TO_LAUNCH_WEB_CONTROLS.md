# How to start the flight controls server

The local server lets you fly the Unity scene from a browser page (cyclic, collective, and pedals) instead of the VR controllers.

## Start it

1. Open PowerShell in the project folder (`VR Flight Sim`).
2. Run:

   ```powershell
   & ".\Control Server\Start-UnityControls.ps1"
   ```

3. Leave that window open. The server runs for as long as it stays open.
4. Open <http://127.0.0.1:8765> in your browser and keep the tab visible. Background tabs slow down updates.

To stop the server, press `Ctrl+C` in the PowerShell window or close it.

## Connect Unity

1. Open `Assets/Scenes/OpenXR Setup.unity`.
2. Select **XR Origin (VR)** in the Hierarchy.
3. On **Tilt Controller**, set **Control Source** to **Local Server**.
4. Press Play, then use the browser controls.

To go back to the VR controllers, set **Control Source** to **VR Controller**.

## Controls

| Input | Effect |
| --- | --- |
| A / D | Move and bank left / right |
| W / S | Move and pitch forward / back |
| Q / E | Descend / climb |
| J / L | Yaw left / right |
| Space | Reset all controls to neutral |

## If something goes wrong

- **"Python environment not found"**: the launcher expects a Python virtual environment in the folder above the project, at `Project 3\.venv`. Create it once with:

  ```powershell
  py -3.12 -m venv "..\.venv"
  ```

  The server only uses Python's standard library, so nothing else needs to be installed.
- **Unity can't bind the port**: the server sends to port 15050. Select the receiver in Unity and set **Port** to 15050. Exit and re-enter Play mode afterward.
- **Nothing moves in Unity**: check <http://127.0.0.1:8765/api/state>. It should show `"udpTarget": "127.0.0.1:15050"`. If it doesn't, stop the server and start it again with the command above.
- **Server won't start because the port is in use**: another copy is probably still running. Close it and try again.

This setup works for the Unity Editor or a desktop build on the same computer. A standalone Quest headset can't reach it.
