using System;
using UnityEngine;

namespace WebcamXR
{
    [Serializable]
    public class TrackingFrameV1
    {
        public int version = 1;
        public long timestamp_ms;
        public bool calibrated;
        public HeadTrackingData head = new HeadTrackingData();
        public MoveTrackingData move = new MoveTrackingData();
        public MotionTrackingData motion = new MotionTrackingData();
        public HandTrackingData left = new HandTrackingData();
        public HandTrackingData right = new HandTrackingData();
    }

    [Serializable]
    public class HeadTrackingData
    {
        public bool tracked;
        public float yaw_deg;
        public float pitch_deg;
    }

    [Serializable]
    public class MoveTrackingData
    {
        public bool tracked;
        public float strafe;
        public float forward;
    }

    [Serializable]
    public class MotionTrackingData
    {
        public bool tracked;
        public float lean_left_right;
        public float lean_forward_back;
        public float crouch;
        public float right_swing_speed;
        public float left_swing_speed;
        public bool right_block;
        public bool left_block;
        public bool right_attack;
        public bool left_attack;
    }

    [Serializable]
    public class HandTrackingData
    {
        public bool tracked;
        public bool pinch;
        public float pinch_strength;
        public float[] local_position = Array.Empty<float>();
        public float[] forward = Array.Empty<float>();

        public Vector3 PositionVector(Vector3 fallback)
        {
            if (local_position == null || local_position.Length < 3)
                return fallback;

            return new Vector3(local_position[0], local_position[1], local_position[2]);
        }

        public Vector3 ForwardVector(Vector3 fallback)
        {
            if (forward == null || forward.Length < 3)
                return fallback;

            var vector = new Vector3(forward[0], forward[1], forward[2]);
            return vector.sqrMagnitude > 0.0001f ? vector.normalized : fallback;
        }
    }
}
