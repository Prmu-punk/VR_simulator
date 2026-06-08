using UnityEngine;

namespace WebcamXR
{
    public class DemoStatusOverlay : MonoBehaviour
    {
        [SerializeField]
        WebcamRigDriver m_RigDriver;

        [SerializeField]
        ModeSwitcher m_ModeSwitcher;

        [SerializeField]
        bool m_ShowHelp = true;

        void OnGUI()
        {
            var area = new Rect(16f, 16f, 520f, 300f);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Webcam XR Demo");

            if (m_ModeSwitcher != null)
                GUILayout.Label($"Mode: {m_ModeSwitcher.CurrentSource}");

            if (m_RigDriver == null || m_RigDriver.Receiver == null)
            {
                GUILayout.Label("Tracking receiver not configured.");
                GUILayout.EndArea();
                return;
            }

            var receiver = m_RigDriver.Receiver;
            GUILayout.Label(receiver.HasFreshFrame
                ? $"UDP: connected ({receiver.SecondsSinceLastPacket * 1000f:0} ms)"
                : "UDP: waiting for packets");
            GUILayout.Label(m_RigDriver.IsCalibrated ? "Calibration: ready" : "Calibration: waiting");

            if (!string.IsNullOrEmpty(receiver.LastError))
                GUILayout.Label($"Last error: {receiver.LastError}");

            var motion = receiver.LatestFrame != null ? receiver.LatestFrame.motion : null;
            if (motion != null && motion.tracked)
            {
                GUILayout.Label($"Motion: F/B {motion.lean_forward_back:+0.00;-0.00;0.00}  L/R {motion.lean_left_right:+0.00;-0.00;0.00}  Crouch {motion.crouch:0.00}");
                GUILayout.Label($"Swing: R {motion.right_swing_speed:0.00}  L {motion.left_swing_speed:0.00}  Block R/L {motion.right_block}/{motion.left_block}");
            }
            else
            {
                GUILayout.Label("Motion: waiting for body pose");
            }

            if (m_ShowHelp)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Python window: r = recalibrate, d = overlay, q = quit");
                GUILayout.Label("Move: lean torso. Swing: broad arm motion. Select: pinch.");
            }

            GUILayout.EndArea();
        }
    }
}
