using Unity.XR.CoreUtils;
using UnityEngine;

namespace WebcamXR
{
    public class WebcamLocomotionDriver : MonoBehaviour
    {
        [SerializeField]
        XROrigin m_XROrigin;

        [SerializeField]
        Transform m_ForwardReference;

        [SerializeField]
        float m_MoveSpeedMetersPerSecond = 0.9f;

        [SerializeField]
        float m_Deadzone = 0.18f;

        Vector2 m_MoveInput;

        public void SetMoveInput(float strafe, float forward)
        {
            m_MoveInput = new Vector2(strafe, forward);
        }

        void Reset()
        {
            m_XROrigin = GetComponent<XROrigin>();
        }

        void Update()
        {
            if (m_XROrigin == null)
                return;

            var input = Vector2.ClampMagnitude(m_MoveInput, 1f);
            if (input.magnitude < m_Deadzone)
                return;

            Transform reference;
            if (m_ForwardReference != null)
                reference = m_ForwardReference;
            else if (m_XROrigin.Camera != null)
                reference = m_XROrigin.Camera.transform;
            else
                reference = transform;
            var forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
            var delta = (right * input.x + forward * input.y) * (m_MoveSpeedMetersPerSecond * Time.deltaTime);

            m_XROrigin.transform.position += delta;
        }
    }
}
