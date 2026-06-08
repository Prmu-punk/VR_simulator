#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import math
import socket
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Optional
from urllib.error import URLError
from urllib.request import urlretrieve

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks.python.components.containers.landmark import Landmark, NormalizedLandmark
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions


SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_MODEL_PATH = SCRIPT_DIR / "models" / "holistic_landmarker.task"
HOLISTIC_MODEL_URL = "https://storage.googleapis.com/mediapipe-models/holistic_landmarker/holistic_landmarker/float16/latest/holistic_landmarker.task"


FACE_INDICES = {
    "nose_tip": 1,
    "chin": 152,
    "left_eye_outer": 33,
    "right_eye_outer": 263,
    "left_mouth": 61,
    "right_mouth": 291,
}

POSE_LEFT_SHOULDER = 11
POSE_RIGHT_SHOULDER = 12
POSE_LEFT_ELBOW = 13
POSE_RIGHT_ELBOW = 14
POSE_LEFT_WRIST = 15
POSE_RIGHT_WRIST = 16
POSE_LEFT_HIP = 23
POSE_RIGHT_HIP = 24
POSE_NOSE = 0

HAND_WRIST = 0
HAND_THUMB_TIP = 4
HAND_INDEX_MCP = 5
HAND_MIDDLE_MCP = 9
HAND_PINKY_MCP = 17
HAND_INDEX_TIP = 8


@dataclass
class HandNeutral:
    center_x: float
    center_y: float
    palm_size: float


@dataclass
class Calibration:
    head_yaw: float
    head_pitch: float
    shoulder_center_x: float
    shoulder_center_z: float
    shoulder_center_y: float
    hip_center_y: float
    torso_height: float
    shoulder_span: float
    left_hand: HandNeutral
    right_hand: HandNeutral


@dataclass
class ScreenHands:
    left: Optional[list]
    right: Optional[list]


@dataclass
class FilteredResult:
    face_landmarks: Optional[list]
    pose_landmarks: Optional[list]
    pose_world_landmarks: Optional[list]


class PinchLatch:
    def __init__(self, on_threshold: float = 0.38, off_threshold: float = 0.52) -> None:
        self.on_threshold = on_threshold
        self.off_threshold = off_threshold
        self.active = False

    def update(self, ratio: float) -> tuple[bool, float]:
        if self.active:
            self.active = ratio < self.off_threshold
        else:
            self.active = ratio < self.on_threshold

        strength = 0.0
        if self.off_threshold > self.on_threshold:
            strength = float(np.clip((self.off_threshold - ratio) / (self.off_threshold - self.on_threshold), 0.0, 1.0))

        return self.active, strength


class LowPass:
    def __init__(self, alpha: float) -> None:
        self.alpha = alpha
        self.value = None

    def reset(self) -> None:
        self.value = None

    def update(self, values: list[float]) -> list[float]:
        vector = np.array(values, dtype=np.float32)
        if self.value is None:
            self.value = vector
        else:
            self.value = self.value + (vector - self.value) * self.alpha

        return [float(value) for value in self.value]


class OneEuroScalar:
    def __init__(self, min_cutoff: float, beta: float, derivative_cutoff: float = 1.0) -> None:
        self.min_cutoff = min_cutoff
        self.beta = beta
        self.derivative_cutoff = derivative_cutoff
        self.value = None
        self.derivative = 0.0

    def reset(self) -> None:
        self.value = None
        self.derivative = 0.0

    def update(self, value: float, dt: float) -> float:
        if self.value is None:
            self.value = value
            return value

        dt = max(dt, 1e-4)
        raw_derivative = (value - self.value) / dt
        derivative_alpha = self._alpha(self.derivative_cutoff, dt)
        self.derivative += derivative_alpha * (raw_derivative - self.derivative)

        cutoff = self.min_cutoff + self.beta * abs(self.derivative)
        value_alpha = self._alpha(cutoff, dt)
        self.value += value_alpha * (value - self.value)
        return self.value

    @staticmethod
    def _alpha(cutoff: float, dt: float) -> float:
        tau = 1.0 / (2.0 * math.pi * max(cutoff, 1e-4))
        return 1.0 / (1.0 + tau / dt)


class LandmarkFilter:
    def __init__(self, min_cutoff: float, beta: float) -> None:
        self.min_cutoff = min_cutoff
        self.beta = beta
        self.filters: list[tuple[OneEuroScalar, OneEuroScalar, OneEuroScalar]] = []

    def reset(self) -> None:
        self.filters = []

    def apply(self, landmarks, dt: float):
        if not landmarks:
            self.reset()
            return None

        if len(self.filters) != len(landmarks):
            self.filters = [
                (
                    OneEuroScalar(self.min_cutoff, self.beta),
                    OneEuroScalar(self.min_cutoff, self.beta),
                    OneEuroScalar(self.min_cutoff, self.beta),
                )
                for _ in landmarks
            ]

        filtered = []
        for landmark, coordinate_filters in zip(landmarks, self.filters):
            x_filter, y_filter, z_filter = coordinate_filters
            landmark_type = NormalizedLandmark if isinstance(landmark, NormalizedLandmark) else Landmark
            filtered.append(
                landmark_type(
                    x=x_filter.update(float(landmark.x or 0.0), dt),
                    y=y_filter.update(float(landmark.y or 0.0), dt),
                    z=z_filter.update(float(landmark.z or 0.0), dt),
                    visibility=landmark.visibility,
                    presence=landmark.presence,
                    name=landmark.name,
                )
            )

        return filtered


class HandPayloadFilter:
    def __init__(self, alpha: float = 0.42) -> None:
        self.position = LowPass(alpha)
        self.forward = LowPass(alpha)

    def reset(self) -> None:
        self.position.reset()
        self.forward.reset()

    def apply(self, payload: dict) -> dict:
        if not payload["tracked"]:
            self.reset()
            return payload

        payload["local_position"] = [round(value, 4) for value in self.position.update(payload["local_position"])]
        forward = np.array(self.forward.update(payload["forward"]), dtype=np.float32)
        norm = np.linalg.norm(forward)
        if norm > 1e-5:
            forward = forward / norm
        payload["forward"] = [round(float(value), 4) for value in forward]
        return payload


class MotionTracker:
    def __init__(self) -> None:
        self.previous_left_wrist = None
        self.previous_right_wrist = None

    def reset(self) -> None:
        self.previous_left_wrist = None
        self.previous_right_wrist = None

    def build_payload(
        self,
        result,
        calibration: Optional[Calibration],
        dt: float,
        sensitivity: float,
        swing_threshold: float,
        dodge_threshold: float,
        crouch_threshold: float,
    ) -> dict:
        if not calibration or not has_landmarks(result.pose_landmarks, POSE_RIGHT_HIP + 1) or not has_landmarks(result.pose_world_landmarks, POSE_RIGHT_WRIST + 1):
            self.reset()
            return default_motion_payload()

        pose = result.pose_landmarks
        pose_world = result.pose_world_landmarks

        left_shoulder = pose_world[POSE_LEFT_SHOULDER]
        right_shoulder = pose_world[POSE_RIGHT_SHOULDER]
        left_hip = pose_world[POSE_LEFT_HIP]
        right_hip = pose_world[POSE_RIGHT_HIP]
        left_wrist = pose_world[POSE_LEFT_WRIST]
        right_wrist = pose_world[POSE_RIGHT_WRIST]

        shoulder_center_x = (left_shoulder.x + right_shoulder.x) * 0.5
        shoulder_center_z = (left_shoulder.z + right_shoulder.z) * 0.5
        shoulder_center_y = (left_shoulder.y + right_shoulder.y) * 0.5
        hip_center_y = (left_hip.y + right_hip.y) * 0.5
        torso_height = max(abs(shoulder_center_y - hip_center_y), 0.12)

        lean_left_right = (shoulder_center_x - calibration.shoulder_center_x) / max(calibration.shoulder_span * 0.75, 0.1)
        lean_forward_back = (calibration.shoulder_center_z - shoulder_center_z) / max(calibration.shoulder_span * 0.5, 0.08)
        crouch = (calibration.torso_height - torso_height + (calibration.shoulder_center_y - shoulder_center_y) * 0.35) / max(calibration.torso_height * 0.45, 0.08)

        left_swing_speed = self._speed(left_wrist, "left", dt)
        right_swing_speed = self._speed(right_wrist, "right", dt)

        left_block = is_blocking(pose, POSE_LEFT_WRIST, POSE_LEFT_ELBOW, POSE_LEFT_SHOULDER)
        right_block = is_blocking(pose, POSE_RIGHT_WRIST, POSE_RIGHT_ELBOW, POSE_RIGHT_SHOULDER)

        return {
            "tracked": True,
            "lean_left_right": round(clamp(apply_deadzone(lean_left_right, dodge_threshold) * sensitivity, -1.0, 1.0), 4),
            "lean_forward_back": round(clamp(apply_deadzone(lean_forward_back, 0.12) * sensitivity, -1.0, 1.0), 4),
            "crouch": round(clamp(apply_deadzone(crouch, crouch_threshold), 0.0, 1.0), 4),
            "right_swing_speed": round(min(right_swing_speed, 6.0), 4),
            "left_swing_speed": round(min(left_swing_speed, 6.0), 4),
            "right_block": right_block,
            "left_block": left_block,
            "right_attack": right_swing_speed >= swing_threshold,
            "left_attack": left_swing_speed >= swing_threshold,
        }

    def _speed(self, wrist, side: str, dt: float) -> float:
        current = np.array([wrist.x, wrist.y, wrist.z], dtype=np.float32)
        previous = self.previous_left_wrist if side == "left" else self.previous_right_wrist
        if side == "left":
            self.previous_left_wrist = current
        else:
            self.previous_right_wrist = current

        if previous is None:
            return 0.0

        return float(np.linalg.norm(current - previous) / max(dt, 1e-4))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Webcam tracking sidecar for the Unity XRI demo.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7777)
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=480)
    parser.add_argument("--send-hz", type=float, default=15.0)
    parser.add_argument("--calibration-seconds", type=float, default=2.0)
    parser.add_argument("--model-path", default=str(DEFAULT_MODEL_PATH))
    parser.add_argument("--hand-sensitivity", type=float, default=0.75)
    parser.add_argument("--aim-sensitivity", type=float, default=0.85)
    parser.add_argument("--move-sensitivity", type=float, default=0.85)
    parser.add_argument("--head-sensitivity", type=float, default=0.35)
    parser.add_argument("--ray-sensitivity", type=float, default=1.65)
    parser.add_argument("--landmark-min-cutoff", type=float, default=1.2)
    parser.add_argument("--landmark-beta", type=float, default=0.03)
    parser.add_argument("--motion-sensitivity", type=float, default=1.25)
    parser.add_argument("--swing-threshold", type=float, default=0.75)
    parser.add_argument("--dodge-threshold", type=float, default=0.12)
    parser.add_argument("--crouch-threshold", type=float, default=0.12)
    return parser.parse_args()


def clamp(value: float, low: float, high: float) -> float:
    return max(low, min(high, value))


def average_points(*points: tuple[float, float]) -> tuple[float, float]:
    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    return sum(xs) / len(xs), sum(ys) / len(ys)


def normalized_distance(point_a: tuple[float, float], point_b: tuple[float, float]) -> float:
    return math.dist(point_a, point_b)


def landmark_xy(landmarks, index: int) -> tuple[float, float]:
    landmark = landmarks[index]
    return landmark.x, landmark.y


def has_landmarks(landmarks, minimum_count: int = 1) -> bool:
    return landmarks is not None and len(landmarks) >= minimum_count


def apply_deadzone(value: float, deadzone: float) -> float:
    if abs(value) <= deadzone:
        return 0.0

    sign = 1.0 if value > 0.0 else -1.0
    return sign * (abs(value) - deadzone) / max(1.0 - deadzone, 1e-5)


def default_motion_payload() -> dict:
    return {
        "tracked": False,
        "lean_left_right": 0.0,
        "lean_forward_back": 0.0,
        "crouch": 0.0,
        "right_swing_speed": 0.0,
        "left_swing_speed": 0.0,
        "right_block": False,
        "left_block": False,
        "right_attack": False,
        "left_attack": False,
    }


def is_blocking(pose_landmarks, wrist_index: int, elbow_index: int, shoulder_index: int) -> bool:
    if not has_landmarks(pose_landmarks, max(wrist_index, elbow_index, shoulder_index) + 1):
        return False

    wrist = pose_landmarks[wrist_index]
    elbow = pose_landmarks[elbow_index]
    shoulder = pose_landmarks[shoulder_index]
    return wrist.y < shoulder.y + 0.08 and elbow.y < shoulder.y + 0.16


def hand_center_and_size(hand_landmarks) -> tuple[tuple[float, float], float]:
    wrist = landmark_xy(hand_landmarks, HAND_WRIST)
    index_mcp = landmark_xy(hand_landmarks, HAND_INDEX_MCP)
    middle_mcp = landmark_xy(hand_landmarks, HAND_MIDDLE_MCP)
    pinky_mcp = landmark_xy(hand_landmarks, HAND_PINKY_MCP)

    center = average_points(wrist, index_mcp, middle_mcp, pinky_mcp)
    size = (
        normalized_distance(wrist, index_mcp)
        + normalized_distance(wrist, pinky_mcp)
        + normalized_distance(index_mcp, pinky_mcp)
    ) / 3.0
    return center, max(size, 1e-5)


def pinch_ratio(hand_landmarks) -> float:
    thumb_tip = landmark_xy(hand_landmarks, HAND_THUMB_TIP)
    index_tip = landmark_xy(hand_landmarks, HAND_INDEX_TIP)
    _, palm_size = hand_center_and_size(hand_landmarks)
    return normalized_distance(thumb_tip, index_tip) / palm_size


def screen_order_hands(result) -> ScreenHands:
    hands = []
    for hand_landmarks in (result.left_hand_landmarks, result.right_hand_landmarks):
        if has_landmarks(hand_landmarks, HAND_PINKY_MCP + 1):
            center, _ = hand_center_and_size(hand_landmarks)
            hands.append((center[0], hand_landmarks))

    if len(hands) == 0:
        return ScreenHands(None, None)

    hands.sort(key=lambda item: item[0])
    if len(hands) == 1:
        center_x, hand_landmarks = hands[0]
        if center_x < 0.5:
            return ScreenHands(hand_landmarks, None)
        return ScreenHands(None, hand_landmarks)

    return ScreenHands(hands[0][1], hands[-1][1])


def estimate_head_pose(face_landmarks, frame_width: int, frame_height: int) -> tuple[bool, float, float]:
    image_points = np.array(
        [
            to_pixel(face_landmarks[FACE_INDICES["nose_tip"]], frame_width, frame_height),
            to_pixel(face_landmarks[FACE_INDICES["chin"]], frame_width, frame_height),
            to_pixel(face_landmarks[FACE_INDICES["left_eye_outer"]], frame_width, frame_height),
            to_pixel(face_landmarks[FACE_INDICES["right_eye_outer"]], frame_width, frame_height),
            to_pixel(face_landmarks[FACE_INDICES["left_mouth"]], frame_width, frame_height),
            to_pixel(face_landmarks[FACE_INDICES["right_mouth"]], frame_width, frame_height),
        ],
        dtype=np.float64,
    )

    model_points = np.array(
        [
            (0.0, 0.0, 0.0),
            (0.0, -63.6, -12.5),
            (-43.3, 32.7, -26.0),
            (43.3, 32.7, -26.0),
            (-28.9, -28.9, -24.1),
            (28.9, -28.9, -24.1),
        ],
        dtype=np.float64,
    )

    focal_length = float(frame_width)
    center = (frame_width / 2.0, frame_height / 2.0)
    camera_matrix = np.array(
        [
            [focal_length, 0.0, center[0]],
            [0.0, focal_length, center[1]],
            [0.0, 0.0, 1.0],
        ],
        dtype=np.float64,
    )
    dist_coeffs = np.zeros((4, 1), dtype=np.float64)

    success, rotation_vector, _ = cv2.solvePnP(
        model_points,
        image_points,
        camera_matrix,
        dist_coeffs,
        flags=cv2.SOLVEPNP_ITERATIVE,
    )
    if not success:
        return False, 0.0, 0.0

    rotation_matrix, _ = cv2.Rodrigues(rotation_vector)
    sy = math.sqrt(rotation_matrix[0, 0] ** 2 + rotation_matrix[1, 0] ** 2)
    singular = sy < 1e-6

    if not singular:
        pitch = math.degrees(math.atan2(rotation_matrix[2, 1], rotation_matrix[2, 2]))
        yaw = math.degrees(math.atan2(-rotation_matrix[2, 0], sy))
    else:
        pitch = math.degrees(math.atan2(-rotation_matrix[1, 2], rotation_matrix[1, 1]))
        yaw = math.degrees(math.atan2(-rotation_matrix[2, 0], sy))

    return True, -yaw, pitch


def to_pixel(landmark, frame_width: int, frame_height: int) -> tuple[float, float]:
    return landmark.x * frame_width, landmark.y * frame_height


def compute_calibration(result, hands: ScreenHands, frame_width: int, frame_height: int) -> Optional[Calibration]:
    if (
        not has_landmarks(result.face_landmarks, max(FACE_INDICES.values()) + 1)
        or not has_landmarks(result.pose_world_landmarks, POSE_RIGHT_HIP + 1)
        or not has_landmarks(hands.left, HAND_PINKY_MCP + 1)
        or not has_landmarks(hands.right, HAND_PINKY_MCP + 1)
    ):
        return None

    tracked, yaw, pitch = estimate_head_pose(result.face_landmarks, frame_width, frame_height)
    if not tracked:
        return None

    pose_world = result.pose_world_landmarks
    left_shoulder = pose_world[POSE_LEFT_SHOULDER]
    right_shoulder = pose_world[POSE_RIGHT_SHOULDER]
    shoulder_center_x = (left_shoulder.x + right_shoulder.x) * 0.5
    shoulder_center_z = (left_shoulder.z + right_shoulder.z) * 0.5
    shoulder_center_y = (left_shoulder.y + right_shoulder.y) * 0.5
    hip_center_y = (pose_world[POSE_LEFT_HIP].y + pose_world[POSE_RIGHT_HIP].y) * 0.5
    torso_height = max(abs(shoulder_center_y - hip_center_y), 0.12)
    shoulder_span = abs(left_shoulder.x - right_shoulder.x)

    left_center, left_size = hand_center_and_size(hands.left)
    right_center, right_size = hand_center_and_size(hands.right)

    return Calibration(
        head_yaw=yaw,
        head_pitch=pitch,
        shoulder_center_x=shoulder_center_x,
        shoulder_center_z=shoulder_center_z,
        shoulder_center_y=shoulder_center_y,
        hip_center_y=hip_center_y,
        torso_height=torso_height,
        shoulder_span=max(shoulder_span, 0.12),
        left_hand=HandNeutral(left_center[0], left_center[1], left_size),
        right_hand=HandNeutral(right_center[0], right_center[1], right_size),
    )


def build_move_payload(result, calibration: Optional[Calibration], sensitivity: float) -> dict:
    if not calibration or not has_landmarks(result.pose_world_landmarks, POSE_RIGHT_SHOULDER + 1):
        return {"tracked": False, "strafe": 0.0, "forward": 0.0}

    pose_world = result.pose_world_landmarks
    left_shoulder = pose_world[POSE_LEFT_SHOULDER]
    right_shoulder = pose_world[POSE_RIGHT_SHOULDER]
    shoulder_center_x = (left_shoulder.x + right_shoulder.x) * 0.5
    shoulder_center_z = (left_shoulder.z + right_shoulder.z) * 0.5

    strafe = (shoulder_center_x - calibration.shoulder_center_x) / max(calibration.shoulder_span * 0.9, 0.14)
    forward = (calibration.shoulder_center_z - shoulder_center_z) / max(calibration.shoulder_span * 0.55, 0.09)
    strafe = apply_deadzone(strafe, 0.18) * sensitivity
    forward = apply_deadzone(forward, 0.14) * sensitivity

    return {
        "tracked": True,
        "strafe": round(clamp(strafe, -1.0, 1.0), 4),
        "forward": round(clamp(forward, -1.0, 1.0), 4),
    }


def build_hand_payload(
    hand_landmarks,
    calibration: Optional[HandNeutral],
    latch: PinchLatch,
    base_position: tuple[float, float, float],
    hand_sensitivity: float,
    aim_sensitivity: float,
    ray_sensitivity: float,
) -> dict:
    if not has_landmarks(hand_landmarks, HAND_PINKY_MCP + 1) or calibration is None:
        latch.active = False
        return {
            "tracked": False,
            "pinch": False,
            "pinch_strength": 0.0,
            "local_position": list(base_position),
            "forward": [0.0, 0.0, 1.0],
        }

    center, palm_size = hand_center_and_size(hand_landmarks)
    raw_dx = apply_deadzone((center[0] - calibration.center_x) * 3.0, 0.08)
    raw_dy = apply_deadzone((calibration.center_y - center[1]) * 2.5, 0.08)
    palm_ratio = palm_size / max(calibration.palm_size, 1e-5)
    raw_dz = apply_deadzone(palm_ratio - 1.0, 0.08)

    dx = raw_dx * 0.42 * hand_sensitivity
    dy = raw_dy * 0.36 * hand_sensitivity
    dz = -raw_dz * 0.36 * hand_sensitivity

    position = [
        round(clamp(base_position[0] + dx, -0.8, 0.8), 4),
        round(clamp(base_position[1] + dy, 0.85, 1.75), 4),
        round(clamp(base_position[2] + dz, 0.35, 1.2), 4),
    ]

    index_tip = hand_landmarks[HAND_INDEX_TIP]
    index_mcp = hand_landmarks[HAND_INDEX_MCP]
    finger_forward = np.array(
        [
            (index_tip.x - index_mcp.x) * 6.5 * ray_sensitivity,
            -(index_tip.y - index_mcp.y) * 5.5 * ray_sensitivity,
            1.0,
        ],
        dtype=np.float32,
    )
    screen_aim = np.array(
        [
            (center[0] - 0.5) * 1.4 * aim_sensitivity,
            (0.46 - center[1]) * 1.0 * aim_sensitivity,
            1.0,
        ],
        dtype=np.float32,
    )
    forward = finger_forward * 0.88 + screen_aim * 0.12
    norm = np.linalg.norm(forward)
    if norm < 1e-5:
        forward = np.array([0.0, 0.0, 1.0], dtype=np.float32)
    else:
        forward = forward / norm

    pinch, pinch_strength = latch.update(pinch_ratio(hand_landmarks))

    return {
        "tracked": True,
        "pinch": pinch,
        "pinch_strength": round(pinch_strength, 4),
        "local_position": position,
        "forward": [round(float(forward[0]), 4), round(float(forward[1]), 4), round(float(forward[2]), 4)],
    }


def draw_status(frame, payload: dict, calibrated: bool, stable_seconds: float, show_debug: bool) -> None:
    cv2.putText(frame, "Webcam XR Tracking", (16, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (240, 240, 240), 2)
    cv2.putText(frame, f"Calibrated: {'yes' if calibrated else 'no'}", (16, 56), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (120, 255, 140), 2)
    cv2.putText(frame, f"Stable timer: {stable_seconds:0.1f}s", (16, 82), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (120, 200, 255), 2)
    cv2.putText(frame, f"Move fwd/strafe: {payload['move']['forward']:+0.2f} / {payload['move']['strafe']:+0.2f}", (16, 108), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 120), 2)

    if not show_debug:
        return

    cv2.putText(frame, f"Head yaw/pitch: {payload['head']['yaw_deg']:+0.1f} / {payload['head']['pitch_deg']:+0.1f}", (16, 136), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (220, 220, 220), 1)
    cv2.putText(frame, f"L pinch: {payload['left']['pinch']} ({payload['left']['pinch_strength']:.2f})", (16, 160), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (220, 220, 220), 1)
    cv2.putText(frame, f"R pinch: {payload['right']['pinch']} ({payload['right']['pinch_strength']:.2f})", (16, 184), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (220, 220, 220), 1)
    motion = payload.get("motion", default_motion_payload())
    cv2.putText(frame, f"Motion lean FB/LR: {motion['lean_forward_back']:+0.2f} / {motion['lean_left_right']:+0.2f}", (16, 208), cv2.FONT_HERSHEY_SIMPLEX, 0.52, (220, 220, 220), 1)
    cv2.putText(frame, f"Swing R/L: {motion['right_swing_speed']:.2f} / {motion['left_swing_speed']:.2f}  Block R/L: {motion['right_block']} / {motion['left_block']}", (16, 232), cv2.FONT_HERSHEY_SIMPLEX, 0.52, (220, 220, 220), 1)


def ensure_model(model_path: Path) -> Path:
    if model_path.exists():
        return model_path

    model_path.parent.mkdir(parents=True, exist_ok=True)
    print(f"Downloading MediaPipe holistic model to {model_path}...")
    try:
        urlretrieve(HOLISTIC_MODEL_URL, model_path)
    except (OSError, URLError) as exception:
        raise RuntimeError(
            f"Failed to download holistic model from {HOLISTIC_MODEL_URL}. "
            f"Download it manually to {model_path} or pass --model-path. Error: {exception}"
        ) from exception

    return model_path


def draw_landmark_points(frame, landmarks, color: tuple[int, int, int], stride: int = 1) -> None:
    if not landmarks:
        return

    height, width = frame.shape[:2]
    for index, landmark in enumerate(landmarks):
        if stride > 1 and index % stride != 0:
            continue

        x = int(clamp(landmark.x, 0.0, 1.0) * width)
        y = int(clamp(landmark.y, 0.0, 1.0) * height)
        cv2.circle(frame, (x, y), 2, color, -1)


def main() -> None:
    args = parse_args()
    model_path = ensure_model(Path(args.model_path).expanduser())
    cap = cv2.VideoCapture(args.camera)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)

    if not cap.isOpened():
        raise RuntimeError("Failed to open webcam.")

    socket_client = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    holistic = vision.HolisticLandmarker.create_from_options(
        vision.HolisticLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=str(model_path)),
            running_mode=vision.RunningMode.VIDEO,
            min_face_detection_confidence=0.5,
            min_face_landmarks_confidence=0.5,
            min_pose_detection_confidence=0.5,
            min_pose_landmarks_confidence=0.5,
            min_hand_landmarks_confidence=0.5,
        )
    )

    stable_started_at: Optional[float] = None
    calibration: Optional[Calibration] = None
    show_debug = True
    last_send_time = 0.0
    last_video_timestamp_ms = 0
    last_filter_time = None

    left_latch = PinchLatch()
    right_latch = PinchLatch()
    left_filter = HandPayloadFilter()
    right_filter = HandPayloadFilter()
    motion_tracker = MotionTracker()
    face_landmark_filter = LandmarkFilter(args.landmark_min_cutoff, args.landmark_beta)
    pose_landmark_filter = LandmarkFilter(args.landmark_min_cutoff, args.landmark_beta)
    pose_world_landmark_filter = LandmarkFilter(args.landmark_min_cutoff, args.landmark_beta)
    left_hand_landmark_filter = LandmarkFilter(args.landmark_min_cutoff, args.landmark_beta)
    right_hand_landmark_filter = LandmarkFilter(args.landmark_min_cutoff, args.landmark_beta)

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break

            frame = cv2.flip(frame, 1)
            frame_height, frame_width = frame.shape[:2]

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            now = time.time()
            video_timestamp_ms = max(int(now * 1000.0), last_video_timestamp_ms + 1)
            last_video_timestamp_ms = video_timestamp_ms
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            results = holistic.detect_for_video(mp_image, video_timestamp_ms)
            filter_dt = 1.0 / 30.0 if last_filter_time is None else now - last_filter_time
            last_filter_time = now

            raw_hands = screen_order_hands(results)
            hands = ScreenHands(
                left=left_hand_landmark_filter.apply(raw_hands.left, filter_dt),
                right=right_hand_landmark_filter.apply(raw_hands.right, filter_dt),
            )
            filtered_results = FilteredResult(
                face_landmarks=face_landmark_filter.apply(results.face_landmarks, filter_dt),
                pose_landmarks=pose_landmark_filter.apply(results.pose_landmarks, filter_dt),
                pose_world_landmarks=pose_world_landmark_filter.apply(results.pose_world_landmarks, filter_dt),
            )

            required_for_calibration = (
                has_landmarks(filtered_results.face_landmarks, max(FACE_INDICES.values()) + 1)
                and has_landmarks(filtered_results.pose_world_landmarks, POSE_RIGHT_HIP + 1)
                and has_landmarks(hands.left, HAND_PINKY_MCP + 1)
                and has_landmarks(hands.right, HAND_PINKY_MCP + 1)
            )

            if required_for_calibration:
                if stable_started_at is None:
                    stable_started_at = now
                elif calibration is None and now - stable_started_at >= args.calibration_seconds:
                    calibration = compute_calibration(filtered_results, hands, frame_width, frame_height)
            else:
                stable_started_at = None

            head_tracked = False
            yaw = 0.0
            pitch = 0.0
            if has_landmarks(filtered_results.face_landmarks, max(FACE_INDICES.values()) + 1):
                head_tracked, yaw, pitch = estimate_head_pose(filtered_results.face_landmarks, frame_width, frame_height)

            if calibration is not None:
                yaw = (yaw - calibration.head_yaw) * args.head_sensitivity
                pitch = (pitch - calibration.head_pitch) * args.head_sensitivity

            payload = {
                "version": 1,
                "timestamp_ms": video_timestamp_ms,
                "calibrated": calibration is not None,
                "head": {
                    "tracked": head_tracked,
                    "yaw_deg": round(clamp(yaw, -60.0, 60.0), 3),
                    "pitch_deg": round(clamp(pitch, -45.0, 45.0), 3),
                },
                "move": build_move_payload(filtered_results, calibration, args.move_sensitivity),
                "motion": motion_tracker.build_payload(
                    filtered_results,
                    calibration,
                    filter_dt,
                    args.motion_sensitivity,
                    args.swing_threshold,
                    args.dodge_threshold,
                    args.crouch_threshold,
                ),
                "left": left_filter.apply(build_hand_payload(
                    hands.left,
                    calibration.left_hand if calibration else None,
                    left_latch,
                    base_position=(-0.34, 1.25, 0.72),
                    hand_sensitivity=args.hand_sensitivity,
                    aim_sensitivity=args.aim_sensitivity,
                    ray_sensitivity=args.ray_sensitivity,
                )),
                "right": right_filter.apply(build_hand_payload(
                    hands.right,
                    calibration.right_hand if calibration else None,
                    right_latch,
                    base_position=(0.34, 1.25, 0.72),
                    hand_sensitivity=args.hand_sensitivity,
                    aim_sensitivity=args.aim_sensitivity,
                    ray_sensitivity=args.ray_sensitivity,
                )),
            }

            if now - last_send_time >= 1.0 / args.send_hz:
                socket_client.sendto(json.dumps(payload).encode("utf-8"), (args.host, args.port))
                last_send_time = now

            draw_landmark_points(frame, filtered_results.face_landmarks, (180, 180, 180), stride=8)
            draw_landmark_points(frame, filtered_results.pose_landmarks, (120, 220, 255))
            draw_landmark_points(frame, hands.left, (120, 255, 140))
            draw_landmark_points(frame, hands.right, (255, 180, 120))

            stable_seconds = 0.0 if stable_started_at is None else now - stable_started_at
            draw_status(frame, payload, calibration is not None, stable_seconds, show_debug)
            cv2.imshow("Webcam XR Tracking", frame)

            key = cv2.waitKey(1) & 0xFF
            if key in (27, ord("q")):
                break
            if key == ord("r"):
                calibration = None
                stable_started_at = None
                last_filter_time = None
                left_latch.active = False
                right_latch.active = False
                left_filter.reset()
                right_filter.reset()
                motion_tracker.reset()
                face_landmark_filter.reset()
                pose_landmark_filter.reset()
                pose_world_landmark_filter.reset()
                left_hand_landmark_filter.reset()
                right_hand_landmark_filter.reset()
            if key == ord("d"):
                show_debug = not show_debug
    finally:
        holistic.close()
        cap.release()
        socket_client.close()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
