# Webcam XR Demo

This repository contains a minimal Unity/XR Interaction Toolkit demo driven by a Python webcam tracking service.

## What it does

- Uses webcam body motion to drive an XRI rig:
  - face yaw/pitch -> headset look
  - left/right hand -> controller pose
  - pinch -> select / grab / teleport
  - upper-body lean -> locomotion
- Demo interactions:
  - grab cube
  - teleport
  - press button
  - pull lever

## Project layout

- `Assets/Scripts/WebcamXR/Runtime/`: Unity runtime scripts
- `Assets/Scripts/WebcamXR/Editor/`: Unity editor bootstrap script
- `tools/tracking_service/`: Python webcam tracking service

## Unity setup

1. Open the folder in **Unity 2022.3 LTS**.
2. Let Unity restore packages from `Packages/manifest.json`.
3. If Unity prompts you to enable the new Input System, accept and restart the editor.
4. Use the menu item `WebcamXR/Create Demo Scene`.
5. Open `Assets/Scenes/WebcamDemo.unity`.
6. Enter Play mode.

The generated scene uses XRI core components directly. It does not require a physical HMD.

## Python tracking service

From the repository root:

```bash
cd tools/tracking_service
./run_service.sh
```

The service opens the default webcam, performs a short neutral-pose calibration, and sends tracking frames to Unity over UDP `127.0.0.1:7777`.

Controls in the Python window:

- `r`: recalibrate
- `d`: toggle debug overlay
- `q` or `esc`: quit

## Notes

- This repo does not include Unity-generated `.meta` files. Unity will generate them on first import.
- The Unity side includes a keyboard-free webcam mode by default. It also includes a simple mode switch hook so you can extend it with a simulator fallback later if desired.
- The current environment used to build this repo did not have the Unity Editor available, so the scene is generated via editor code instead of being pre-authored as a binary `.unity` asset.
