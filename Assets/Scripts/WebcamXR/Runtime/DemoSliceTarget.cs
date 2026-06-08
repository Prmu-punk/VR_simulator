using UnityEngine;

namespace WebcamXR
{
    [RequireComponent(typeof(Collider))]
    public class DemoSliceTarget : MonoBehaviour
    {
        [SerializeField]
        Color m_SlicedColor = new Color(0.95f, 0.35f, 0.25f);

        [SerializeField]
        float m_HalfLifetimeSeconds = 4f;

        bool m_Sliced;

        void Awake()
        {
            var collider = GetComponent<Collider>();
            collider.isTrigger = true;
        }

        public void Slice(Vector3 bladeVelocity)
        {
            if (m_Sliced)
                return;

            m_Sliced = true;
            var baseScale = transform.localScale;
            var basePosition = transform.position;
            var push = bladeVelocity.sqrMagnitude > 0.001f ? bladeVelocity.normalized : transform.right;

            CreateHalf(basePosition - transform.right * baseScale.x * 0.18f, baseScale, -push);
            CreateHalf(basePosition + transform.right * baseScale.x * 0.18f, baseScale, push);
            Destroy(gameObject);
        }

        void CreateHalf(Vector3 position, Vector3 sourceScale, Vector3 impulse)
        {
            var half = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            half.name = "Sliced Fruit Half";
            half.transform.position = position;
            half.transform.localScale = new Vector3(sourceScale.x * 0.45f, sourceScale.y, sourceScale.z);

            var renderer = half.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = m_SlicedColor;
                renderer.material = material;
            }

            var rigidbody = half.AddComponent<Rigidbody>();
            rigidbody.mass = 0.15f;
            rigidbody.AddForce((impulse + Vector3.up * 0.6f) * 2.0f, ForceMode.Impulse);
            rigidbody.AddTorque(Random.insideUnitSphere * 1.5f, ForceMode.Impulse);

            Destroy(half, m_HalfLifetimeSeconds);
        }
    }
}
