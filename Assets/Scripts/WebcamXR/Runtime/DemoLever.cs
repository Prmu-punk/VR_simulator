using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace WebcamXR
{
    [RequireComponent(typeof(XRGrabInteractable))]
    public class DemoLever : MonoBehaviour
    {
        [SerializeField]
        Transform m_HandleVisual;

        [SerializeField]
        Renderer m_IndicatorRenderer;

        [SerializeField]
        Color m_OffColor = new Color(0.8f, 0.3f, 0.25f);

        [SerializeField]
        Color m_OnColor = new Color(0.25f, 0.8f, 0.35f);

        [SerializeField]
        float m_MinAngle = -45f;

        [SerializeField]
        float m_MaxAngle = 45f;

        [SerializeField]
        float m_OnThreshold = 20f;

        XRGrabInteractable m_GrabInteractable;
        float m_CurrentAngle;
        bool m_IsOn;

        void Awake()
        {
            m_GrabInteractable = GetComponent<XRGrabInteractable>();

            if (m_IndicatorRenderer != null)
                m_IndicatorRenderer.material = new Material(m_IndicatorRenderer.sharedMaterial);

            ApplyAngle(0f);
            ApplyIndicator();
        }

        void Update()
        {
            if (m_GrabInteractable != null && m_GrabInteractable.isSelected && m_GrabInteractable.firstInteractorSelecting != null)
            {
                var interactorTransform = m_GrabInteractable.firstInteractorSelecting.transform;
                var localInteractorPosition = transform.InverseTransformPoint(interactorTransform.position);
                var desiredAngle = Mathf.Atan2(localInteractorPosition.x, localInteractorPosition.z) * Mathf.Rad2Deg;
                ApplyAngle(Mathf.Clamp(desiredAngle, m_MinAngle, m_MaxAngle));
            }

            var shouldBeOn = m_CurrentAngle >= m_OnThreshold;
            if (shouldBeOn != m_IsOn)
            {
                m_IsOn = shouldBeOn;
                ApplyIndicator();
            }
        }

        void ApplyAngle(float angle)
        {
            m_CurrentAngle = angle;

            if (m_HandleVisual != null)
                m_HandleVisual.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        void ApplyIndicator()
        {
            if (m_IndicatorRenderer != null)
                m_IndicatorRenderer.material.color = m_IsOn ? m_OnColor : m_OffColor;
        }
    }
}
