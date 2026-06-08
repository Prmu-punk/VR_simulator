using Unity.XR.CoreUtils;
using UnityEngine;

namespace WebcamXR
{
    public class WebcamRigDriver : MonoBehaviour
    {
        [System.Serializable]
        class HandRuntimeState
        {
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            public float lastTrackedTime = -999f;
            public bool hasPose;
        }

        [SerializeField]
        WebcamTrackingReceiver m_Receiver;

        [SerializeField]
        XROrigin m_XROrigin;

        [SerializeField]
        Transform m_HeadTransform;

        [SerializeField]
        WebcamXRController m_LeftController;

        [SerializeField]
        WebcamXRController m_RightController;

        [SerializeField]
        WebcamLocomotionDriver m_LocomotionDriver;

        [SerializeField]
        float m_HeadSmoothing = 5f;

        [SerializeField]
        float m_MaxHeadDegreesPerSecond = 45f;

        [SerializeField]
        float m_HandPositionSmoothing = 8f;

        [SerializeField]
        float m_HandRotationSmoothing = 18f;

        [SerializeField]
        float m_HandLostGraceSeconds = 0.3f;

        [SerializeField]
        bool m_BlockLocomotionWhilePinching = true;

        [SerializeField]
        bool m_FixControllerPositions = false;

        [SerializeField]
        Vector3 m_LeftControllerPosition = new Vector3(-0.34f, 1.25f, 0.72f);

        [SerializeField]
        Vector3 m_RightControllerPosition = new Vector3(0.34f, 1.25f, 0.72f);

        float m_CurrentYaw;
        float m_CurrentPitch;
        HandRuntimeState m_LeftState = new HandRuntimeState();
        HandRuntimeState m_RightState = new HandRuntimeState();

        public WebcamTrackingReceiver Receiver => m_Receiver;
        public bool HasFreshFrame => m_Receiver != null && m_Receiver.HasFreshFrame;
        public bool IsCalibrated => m_Receiver != null && m_Receiver.LatestFrame != null && m_Receiver.LatestFrame.calibrated;

        void Reset()
        {
            m_Receiver = GetComponent<WebcamTrackingReceiver>();
            m_XROrigin = GetComponent<XROrigin>();
            m_LocomotionDriver = GetComponent<WebcamLocomotionDriver>();

            if (m_XROrigin != null && m_XROrigin.Camera != null)
                m_HeadTransform = m_XROrigin.Camera.transform;
        }

        void Update()
        {
            if (m_Receiver == null || m_XROrigin == null || m_HeadTransform == null || m_LeftController == null || m_RightController == null)
                return;

            if (!m_Receiver.HasFreshFrame)
            {
                ClearRigState();
                return;
            }

            var frame = m_Receiver.LatestFrame;
            if (frame == null || !frame.calibrated)
            {
                ClearRigState();
                return;
            }

            UpdateHead(frame.head);

            var leftPinch = UpdateHand(frame.left, m_LeftState, m_LeftController, m_LeftControllerPosition);
            var rightPinch = UpdateHand(frame.right, m_RightState, m_RightController, m_RightControllerPosition);

            var allowMovement = frame.move != null && frame.move.tracked;
            if (m_BlockLocomotionWhilePinching && (leftPinch || rightPinch))
                allowMovement = false;

            if (m_LocomotionDriver != null)
            {
                if (allowMovement)
                    m_LocomotionDriver.SetMoveInput(frame.move.strafe, frame.move.forward);
                else
                    m_LocomotionDriver.SetMoveInput(0f, 0f);
            }
        }

        void UpdateHead(HeadTrackingData head)
        {
            if (head == null || !head.tracked)
                return;

            var factor = 1f - Mathf.Exp(-m_HeadSmoothing * Time.deltaTime);
            var targetYaw = Mathf.LerpAngle(m_CurrentYaw, head.yaw_deg, factor);
            var targetPitch = Mathf.LerpAngle(m_CurrentPitch, head.pitch_deg, factor);
            var maxStep = m_MaxHeadDegreesPerSecond * Time.deltaTime;
            m_CurrentYaw = Mathf.MoveTowardsAngle(m_CurrentYaw, targetYaw, maxStep);
            m_CurrentPitch = Mathf.MoveTowardsAngle(m_CurrentPitch, targetPitch, maxStep);
            m_HeadTransform.localRotation = Quaternion.Euler(-m_CurrentPitch, m_CurrentYaw, 0f);
        }

        bool UpdateHand(HandTrackingData hand, HandRuntimeState runtimeState, WebcamXRController controller, Vector3 fallbackPosition)
        {
            var now = Time.unscaledTime;

            if (hand != null && hand.tracked)
            {
                runtimeState.lastTrackedTime = now;

                var targetPosition = m_FixControllerPositions ? fallbackPosition : hand.PositionVector(fallbackPosition);
                var targetForward = hand.ForwardVector(Vector3.forward);
                var targetRotation = SafeLookRotation(targetForward, Vector3.up);

                if (!runtimeState.hasPose)
                {
                    runtimeState.position = targetPosition;
                    runtimeState.rotation = targetRotation;
                    runtimeState.hasPose = true;
                }

                var positionFactor = 1f - Mathf.Exp(-m_HandPositionSmoothing * Time.deltaTime);
                var rotationFactor = 1f - Mathf.Exp(-m_HandRotationSmoothing * Time.deltaTime);

                runtimeState.position = Vector3.Lerp(runtimeState.position, targetPosition, positionFactor);
                runtimeState.rotation = Quaternion.Slerp(runtimeState.rotation, targetRotation, rotationFactor);

                controller.ApplyPose(true, runtimeState.position, runtimeState.rotation);
                controller.ApplySelect(hand.pinch, hand.pinch_strength);
                controller.ApplyActivate(false, 0f);
                return hand.pinch;
            }

            var withinGrace = runtimeState.hasPose && now - runtimeState.lastTrackedTime <= m_HandLostGraceSeconds;
            if (withinGrace)
            {
                controller.ApplyPose(true, runtimeState.position, runtimeState.rotation);
                controller.ApplySelect(false, 0f);
                controller.ApplyActivate(false, 0f);
                return false;
            }

            runtimeState.hasPose = false;
            controller.ApplyPose(false, fallbackPosition, Quaternion.identity);
            controller.ClearInput();
            return false;
        }

        void ClearRigState()
        {
            if (m_LeftController != null)
            {
                m_LeftController.ApplyPose(false, Vector3.zero, Quaternion.identity);
                m_LeftController.ClearInput();
            }

            if (m_RightController != null)
            {
                m_RightController.ApplyPose(false, Vector3.zero, Quaternion.identity);
                m_RightController.ClearInput();
            }

            if (m_LocomotionDriver != null)
                m_LocomotionDriver.SetMoveInput(0f, 0f);
        }

        static Quaternion SafeLookRotation(Vector3 forward, Vector3 up)
        {
            if (forward.sqrMagnitude < 0.0001f)
                return Quaternion.identity;

            var adjustedUp = Vector3.ProjectOnPlane(up, forward);
            if (adjustedUp.sqrMagnitude < 0.0001f)
                adjustedUp = Vector3.up;

            return Quaternion.LookRotation(forward.normalized, adjustedUp.normalized);
        }
    }
}
