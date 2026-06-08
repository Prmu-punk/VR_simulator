using UnityEngine;

namespace WebcamXR
{
    public static class DemoSliceRuntimeInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var rightController = GameObject.Find("Right Controller");
            if (rightController != null && rightController.GetComponent<WebcamSaber>() == null)
                rightController.AddComponent<WebcamSaber>();

            var xrOrigin = GameObject.Find("Webcam XROrigin");
            if (xrOrigin != null && xrOrigin.GetComponent<WebcamMotionGameDriver>() == null)
                xrOrigin.AddComponent<WebcamMotionGameDriver>();

            if (Object.FindObjectOfType<DemoSliceTarget>() != null)
                return;

            CreateFruit(new Vector3(-0.45f, 1.25f, 1.65f), new Color(0.95f, 0.25f, 0.18f));
            CreateFruit(new Vector3(0f, 1.45f, 1.85f), new Color(0.95f, 0.75f, 0.18f));
            CreateFruit(new Vector3(0.45f, 1.15f, 1.65f), new Color(0.25f, 0.75f, 0.35f));
        }

        static void CreateFruit(Vector3 position, Color color)
        {
            var fruit = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fruit.name = "Slice Fruit";
            fruit.transform.position = position;
            fruit.transform.localScale = Vector3.one * 0.22f;

            var renderer = fruit.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = color;
                renderer.material = material;
            }

            var collider = fruit.GetComponent<Collider>();
            if (collider != null)
                collider.isTrigger = true;

            fruit.AddComponent<DemoSliceTarget>();
        }
    }
}
