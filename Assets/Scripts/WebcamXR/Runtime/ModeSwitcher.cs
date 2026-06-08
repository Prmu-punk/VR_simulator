using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WebcamXR
{
    public class ModeSwitcher : MonoBehaviour
    {
        public enum ControlSource
        {
            Webcam,
            Simulator,
        }

        [SerializeField]
        ControlSource m_CurrentSource = ControlSource.Webcam;

        [SerializeField]
        GameObject m_WebcamRoot;

        [SerializeField]
        GameObject m_SimulatorRoot;

#if ENABLE_INPUT_SYSTEM
        [SerializeField]
        Key m_ToggleKey = Key.Tab;
#endif

        public ControlSource CurrentSource => m_CurrentSource;

        void Start()
        {
            ApplyMode();
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (m_SimulatorRoot != null && Keyboard.current != null && Keyboard.current[m_ToggleKey].wasPressedThisFrame)
            {
                m_CurrentSource = m_CurrentSource == ControlSource.Webcam ? ControlSource.Simulator : ControlSource.Webcam;
                ApplyMode();
            }
#endif
        }

        public void ApplyMode()
        {
            if (m_SimulatorRoot == null)
            {
                m_CurrentSource = ControlSource.Webcam;
                return;
            }

            if (m_WebcamRoot != null && m_SimulatorRoot != null)
                m_WebcamRoot.SetActive(m_CurrentSource == ControlSource.Webcam);

            if (m_SimulatorRoot != null)
                m_SimulatorRoot.SetActive(m_CurrentSource == ControlSource.Simulator);
        }
    }
}
