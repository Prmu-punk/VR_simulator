# VR Simulator Webcam Motion Demo

Unity XR demo controlled by a Python webcam tracking sidecar. It is meant for first-person webcam motion games and simulator prototypes, not precise VR controller replacement.

## What It Does

- Tracks face, pose, and hands from a single webcam.
- Sends tracking data to Unity over UDP.
- Drives an XR Interaction Toolkit rig and a motion-game mode.
- Demo scene includes broad arm-swing slicing, motion values, and simple XR interactions.

## Requirements

- Unity 2022.3 LTS
- Python 3.11+ recommended
- A webcam
- Internet access on first Python run to install packages and download the MediaPipe model

## Run

1. Open this folder in Unity 2022.3 LTS.
2. Let Unity restore packages from `Packages/manifest.json`.
3. Open `Assets/Scenes/WebcamDemo.unity`.
4. Start the Python tracking service:

   ```bash
   cd tools/tracking_service
   ./run_service.sh
   ```

5. Enter Play mode in Unity.

The Python service opens the default webcam, downloads the MediaPipe holistic model if needed, then sends frames to Unity at `127.0.0.1:7777`.

## Python Controls

- `r`: recalibrate neutral pose
- `d`: toggle debug overlay
- `q` or `Esc`: quit

## Repository Notes

Unity-generated caches such as `Library/`, `Logs/`, `UserSettings/`, Python `.venv`, and downloaded models are ignored. Clone, open in Unity, and run the sidecar script to recreate local generated state.
