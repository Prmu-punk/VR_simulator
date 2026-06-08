using UnityEngine;

namespace WebcamXR
{
    public class WebcamSaber : MonoBehaviour
    {
        [SerializeField]
        float m_MinSliceSpeed = 0.35f;

        [SerializeField]
        Vector3 m_BladeLocalOffset = new Vector3(0f, 0f, 0.34f);

        [SerializeField]
        Vector3 m_BladeSize = new Vector3(0.08f, 0.08f, 0.72f);

        Vector3 m_LastPosition;
        Vector3 m_Velocity;
        float m_ExternalSwingSpeed;
        bool m_ExternalAttack;
        bool m_HasLastPosition;

        void Awake()
        {
            EnsureBlade();
        }

        void Update()
        {
            if (m_HasLastPosition && Time.deltaTime > 0f)
                m_Velocity = (transform.position - m_LastPosition) / Time.deltaTime;

            m_LastPosition = transform.position;
            m_HasLastPosition = true;
        }

        void OnTriggerEnter(Collider other)
        {
            TrySlice(other);
        }

        void OnTriggerStay(Collider other)
        {
            TrySlice(other);
        }

        void TrySlice(Collider other)
        {
            if (m_Velocity.magnitude < m_MinSliceSpeed && m_ExternalSwingSpeed < m_MinSliceSpeed && !m_ExternalAttack)
                return;

            var target = other.GetComponentInParent<DemoSliceTarget>();
            if (target != null)
                target.Slice(EffectiveVelocity());
        }

        public void SetMotionInput(float swingSpeed, bool attack)
        {
            m_ExternalSwingSpeed = swingSpeed;
            m_ExternalAttack = attack;
        }

        Vector3 EffectiveVelocity()
        {
            if (m_Velocity.magnitude >= m_ExternalSwingSpeed)
                return m_Velocity;

            return transform.right * Mathf.Max(m_ExternalSwingSpeed, m_MinSliceSpeed);
        }

        void EnsureBlade()
        {
            if (GetComponent<Rigidbody>() == null)
            {
                var rigidbody = gameObject.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
            }

            if (GetComponent<BoxCollider>() == null)
            {
                var collider = gameObject.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.center = m_BladeLocalOffset;
                collider.size = m_BladeSize;
            }

            if (transform.Find("Webcam Saber Blade") != null)
                return;

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Webcam Saber Blade";
            blade.transform.SetParent(transform, false);
            blade.transform.localPosition = m_BladeLocalOffset;
            blade.transform.localScale = m_BladeSize;

            var bladeCollider = blade.GetComponent<Collider>();
            if (bladeCollider != null)
                Destroy(bladeCollider);

            var renderer = blade.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = new Color(0.2f, 0.85f, 1f, 0.65f);
                renderer.material = material;
            }
        }
    }
}
