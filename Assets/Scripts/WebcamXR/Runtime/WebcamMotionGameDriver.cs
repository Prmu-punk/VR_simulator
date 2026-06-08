using UnityEngine;

namespace WebcamXR
{
    [DefaultExecutionOrder(100)]
    public class WebcamMotionGameDriver : MonoBehaviour
    {
        [SerializeField]
        WebcamTrackingReceiver m_Receiver;

        [SerializeField]
        WebcamLocomotionDriver m_LocomotionDriver;

        [SerializeField]
        WebcamSaber m_RightSaber;

        [SerializeField]
        WebcamSaber m_LeftSaber;

        [SerializeField]
        bool m_OverrideLocomotion = true;

        [SerializeField]
        float m_ForwardScale = 1f;

        [SerializeField]
        float m_DodgeScale = 0.75f;

        public MotionTrackingData CurrentMotion { get; private set; }

        void Reset()
        {
            m_Receiver = GetComponent<WebcamTrackingReceiver>();
            m_LocomotionDriver = GetComponent<WebcamLocomotionDriver>();
        }

        void Awake()
        {
            ResolveReferences();
        }

        void LateUpdate()
        {
            ResolveReferences();

            var frame = m_Receiver != null ? m_Receiver.LatestFrame : null;
            CurrentMotion = frame != null ? frame.motion : null;
            if (CurrentMotion == null || !CurrentMotion.tracked)
            {
                ApplySaberMotion(0f, 0f, false, false);
                return;
            }

            if (m_OverrideLocomotion && m_LocomotionDriver != null)
                m_LocomotionDriver.SetMoveInput(CurrentMotion.lean_left_right * m_DodgeScale, CurrentMotion.lean_forward_back * m_ForwardScale);

            ApplySaberMotion(CurrentMotion.right_swing_speed, CurrentMotion.left_swing_speed, CurrentMotion.right_attack, CurrentMotion.left_attack);
        }

        void ApplySaberMotion(float rightSwingSpeed, float leftSwingSpeed, bool rightAttack, bool leftAttack)
        {
            if (m_RightSaber != null)
                m_RightSaber.SetMotionInput(rightSwingSpeed, rightAttack);

            if (m_LeftSaber != null)
                m_LeftSaber.SetMotionInput(leftSwingSpeed, leftAttack);
        }

        void ResolveReferences()
        {
            if (m_Receiver == null)
                m_Receiver = GetComponent<WebcamTrackingReceiver>();

            if (m_LocomotionDriver == null)
                m_LocomotionDriver = GetComponent<WebcamLocomotionDriver>();

            if (m_RightSaber == null)
            {
                var rightController = GameObject.Find("Right Controller");
                if (rightController != null)
                    m_RightSaber = rightController.GetComponent<WebcamSaber>();
            }

            if (m_LeftSaber == null)
            {
                var leftController = GameObject.Find("Left Controller");
                if (leftController != null)
                    m_LeftSaber = leftController.GetComponent<WebcamSaber>();
            }
        }
    }
}
