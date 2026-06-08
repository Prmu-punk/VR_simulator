using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace WebcamXR
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class DemoButtonTarget : MonoBehaviour
    {
        [SerializeField]
        Renderer m_TargetRenderer;

        [SerializeField]
        Color m_OffColor = new Color(0.85f, 0.25f, 0.2f);

        [SerializeField]
        Color m_OnColor = new Color(0.2f, 0.85f, 0.35f);

        [SerializeField]
        Transform m_ButtonCap;

        [SerializeField]
        float m_PressDepth = 0.03f;

        XRSimpleInteractable m_Interactable;
        Vector3 m_ButtonCapStartLocalPosition;
        bool m_IsOn;

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);

            if (m_TargetRenderer != null)
                m_TargetRenderer.material = new Material(m_TargetRenderer.sharedMaterial);

            if (m_ButtonCap != null)
                m_ButtonCapStartLocalPosition = m_ButtonCap.localPosition;

            ApplyVisual();
        }

        void OnDestroy()
        {
            if (m_Interactable == null)
                return;

            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);
        }

        void OnSelectEntered(SelectEnterEventArgs _)
        {
            m_IsOn = !m_IsOn;

            if (m_ButtonCap != null)
                m_ButtonCap.localPosition = m_ButtonCapStartLocalPosition + Vector3.down * m_PressDepth;

            ApplyVisual();
        }

        void OnSelectExited(SelectExitEventArgs _)
        {
            if (m_ButtonCap != null)
                m_ButtonCap.localPosition = m_ButtonCapStartLocalPosition;
        }

        void ApplyVisual()
        {
            if (m_TargetRenderer == null)
                return;

            m_TargetRenderer.material.color = m_IsOn ? m_OnColor : m_OffColor;
        }
    }
}
