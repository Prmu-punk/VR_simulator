using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace WebcamXR
{
    [AddComponentMenu("XR/Webcam XR Controller")]
    public class WebcamXRController : XRBaseController
    {
        [SerializeField]
        bool m_MapUIPressToSelect = true;

        [SerializeField]
        bool m_ShowDebugVisual = true;

        [SerializeField]
        float m_DebugVisualRadius = 0.045f;

        [SerializeField]
        Color m_DebugVisualColor = new Color(0.25f, 0.85f, 1f);

        bool m_IsTracked;
        Vector3 m_LocalPosition;
        Quaternion m_LocalRotation = Quaternion.identity;

        bool m_SelectActive;
        float m_SelectValue;
        bool m_ActivateActive;
        float m_ActivateValue;
        bool m_UIPressActive;
        float m_UIPressValue;

        void Awake()
        {
            CreateDebugVisual();
        }

        public void ApplyPose(bool tracked, Vector3 localPosition, Quaternion localRotation)
        {
            m_IsTracked = tracked;
            m_LocalPosition = localPosition;
            m_LocalRotation = localRotation;
        }

        public void ApplySelect(bool active, float value)
        {
            m_SelectActive = active;
            m_SelectValue = Mathf.Clamp01(value);

            if (m_MapUIPressToSelect)
            {
                m_UIPressActive = active;
                m_UIPressValue = m_SelectValue;
            }
        }

        public void ApplyActivate(bool active, float value)
        {
            m_ActivateActive = active;
            m_ActivateValue = Mathf.Clamp01(value);
        }

        public void ApplyUIPress(bool active, float value)
        {
            m_UIPressActive = active;
            m_UIPressValue = Mathf.Clamp01(value);
        }

        public void ClearInput()
        {
            ApplySelect(false, 0f);
            ApplyActivate(false, 0f);
            ApplyUIPress(false, 0f);
        }

        protected override void UpdateTrackingInput(XRControllerState controllerState)
        {
            controllerState.time = Time.timeAsDouble;
            controllerState.isTracked = m_IsTracked;

            if (!m_IsTracked)
            {
                controllerState.inputTrackingState = InputTrackingState.None;
                return;
            }

            controllerState.inputTrackingState = InputTrackingState.Position | InputTrackingState.Rotation;
            controllerState.position = m_LocalPosition;
            controllerState.rotation = m_LocalRotation;
        }

        protected override void UpdateInput(XRControllerState controllerState)
        {
            controllerState.selectInteractionState.SetFrameState(m_SelectActive, m_SelectValue);
            controllerState.activateInteractionState.SetFrameState(m_ActivateActive, m_ActivateValue);
            controllerState.uiPressInteractionState.SetFrameState(m_UIPressActive, m_UIPressValue);
        }

        void CreateDebugVisual()
        {
            if (!m_ShowDebugVisual || transform.Find("Webcam Controller Visual") != null)
                return;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Webcam Controller Visual";
            visual.transform.SetParent(transform, false);
            visual.transform.localScale = Vector3.one * (m_DebugVisualRadius * 2f);

            var collider = visual.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = m_DebugVisualColor;
                renderer.material = material;
            }
        }
    }
}
