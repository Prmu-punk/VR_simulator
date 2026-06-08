using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace WebcamXR.Editor
{
    public static class WebcamXRSceneBootstrapper
    {
        const string InteractableLayerName = "WebcamXRInteractable";
        const string TeleportLayerName = "WebcamXRTeleport";
        const int InteractableLayerIndex = 24;
        const int TeleportLayerIndex = 25;
        const string ScenePath = "Assets/Scenes/WebcamDemo.unity";
        const string MaterialFolder = "Assets/Scenes/GeneratedMaterials";

        [MenuItem("WebcamXR/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            EnsureLayer(InteractableLayerIndex, InteractableLayerName);
            EnsureLayer(TeleportLayerIndex, TeleportLayerName);
            EnsureFolder("Assets/Scenes");
            EnsureFolder(MaterialFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.5f);

            var interactionManager = new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

            var xrOriginRoot = new GameObject("Webcam XROrigin");
            var xrOrigin = xrOriginRoot.AddComponent<XROrigin>();
#pragma warning disable CS0618
            var locomotionSystem = xrOriginRoot.AddComponent<LocomotionSystem>();
#pragma warning restore CS0618
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            xrOrigin.CameraYOffset = 1.65f;

            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOriginRoot.transform, false);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();

            xrOrigin.Origin = xrOriginRoot;
            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            xrOrigin.Camera = cameraObject.GetComponent<Camera>();
#pragma warning disable CS0618
            locomotionSystem.xrOrigin = xrOrigin;
#pragma warning restore CS0618

            var teleportationProvider = xrOriginRoot.AddComponent<TeleportationProvider>();
            teleportationProvider.delayTime = 0f;

            var receiver = xrOriginRoot.AddComponent<WebcamTrackingReceiver>();
            var locomotionDriver = xrOriginRoot.AddComponent<WebcamLocomotionDriver>();
            var rigDriver = xrOriginRoot.AddComponent<WebcamRigDriver>();
            var overlay = xrOriginRoot.AddComponent<DemoStatusOverlay>();
            var modeSwitcher = xrOriginRoot.AddComponent<ModeSwitcher>();

            var leftControllerRoot = CreateController("Left Controller", cameraOffset.transform, TeleportLayerIndex, InteractionLayerMask.GetMask(TeleportLayerName), false);
            var rightControllerRoot = CreateController("Right Controller", cameraOffset.transform, InteractableLayerIndex, InteractionLayerMask.GetMask(InteractableLayerName), true);

            var leftController = leftControllerRoot.GetComponent<WebcamXRController>();
            var rightController = rightControllerRoot.GetComponent<WebcamXRController>();

            AssignRigReferences(locomotionDriver, xrOrigin, cameraObject.transform);
            AssignRigReferences(rigDriver, receiver, xrOrigin, cameraObject.transform, leftController, rightController, locomotionDriver);
            AssignOverlayReferences(overlay, rigDriver, modeSwitcher);
            AssignModeReferences(modeSwitcher, xrOriginRoot);

            CreateLighting();
            CreateFloor(interactionManager, TeleportLayerIndex);
            CreateGrabCube(interactionManager, InteractableLayerIndex);
            CreateButton(interactionManager, InteractableLayerIndex);
            CreateLever(interactionManager, InteractableLayerIndex);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("WebcamXR", $"Created demo scene at {ScenePath}", "OK");
        }

        static GameObject CreateController(string name, Transform parent, int physicsLayer, InteractionLayerMask interactionLayer, bool useForceGrab)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.layer = physicsLayer;

            var controller = root.AddComponent<WebcamXRController>();
            controller.enableInputActions = true;
            controller.enableInputTracking = true;

            var rayInteractor = root.AddComponent<XRRayInteractor>();
            rayInteractor.interactionLayers = interactionLayer;
            rayInteractor.raycastMask = 1 << physicsLayer;
            rayInteractor.lineType = XRRayInteractor.LineType.StraightLine;
            rayInteractor.enableUIInteraction = false;
            rayInteractor.useForceGrab = useForceGrab;

            var lineRenderer = root.AddComponent<LineRenderer>();
            lineRenderer.material = GetOrCreateMaterial("Line.mat", new Color(0.3f, 0.85f, 1f));
            lineRenderer.widthMultiplier = 0.01f;
            lineRenderer.positionCount = 0;

            root.AddComponent<XRInteractorLineVisual>();
            return root;
        }

        static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        static void CreateFloor(XRInteractionManager _, int teleportLayer)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Teleport Floor";
            floor.layer = teleportLayer;
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(3f, 1f, 3f);
            floor.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Floor.mat", new Color(0.25f, 0.3f, 0.34f));

            var teleportArea = floor.AddComponent<TeleportationArea>();
            teleportArea.interactionLayers = InteractionLayerMask.GetMask(TeleportLayerName);
        }

        static void CreateGrabCube(XRInteractionManager _, int interactableLayer)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Grab Cube";
            cube.layer = interactableLayer;
            cube.transform.position = new Vector3(0f, 1.15f, 1.4f);
            cube.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            cube.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Cube.mat", new Color(0.96f, 0.71f, 0.18f));

            var rigidbody = cube.AddComponent<Rigidbody>();
            rigidbody.mass = 0.4f;
            rigidbody.angularDrag = 4f;

            var grabInteractable = cube.AddComponent<XRGrabInteractable>();
            grabInteractable.interactionLayers = InteractionLayerMask.GetMask(InteractableLayerName);
        }

        static void CreateButton(XRInteractionManager _, int interactableLayer)
        {
            var root = new GameObject("Demo Button");
            root.transform.position = new Vector3(-0.75f, 1.05f, 1.35f);

            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Button Base";
            baseObject.transform.SetParent(root.transform, false);
            baseObject.transform.localScale = new Vector3(0.16f, 0.06f, 0.16f);
            baseObject.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("ButtonBase.mat", new Color(0.15f, 0.15f, 0.18f));

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "Button Cap";
            cap.layer = interactableLayer;
            cap.transform.SetParent(root.transform, false);
            cap.transform.localPosition = new Vector3(0f, 0.11f, 0f);
            cap.transform.localScale = new Vector3(0.22f, 0.05f, 0.22f);
            cap.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("ButtonCap.mat", new Color(0.82f, 0.22f, 0.2f));

            var interactable = cap.AddComponent<XRSimpleInteractable>();
            interactable.interactionLayers = InteractionLayerMask.GetMask(InteractableLayerName);

            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            targetObject.name = "Button Target";
            targetObject.transform.position = new Vector3(-1.2f, 1.4f, 1.75f);
            targetObject.transform.localScale = Vector3.one * 0.18f;
            targetObject.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("ButtonTarget.mat", new Color(0.85f, 0.25f, 0.2f));

            var script = cap.AddComponent<DemoButtonTarget>();
            SetField(script, "m_TargetRenderer", targetObject.GetComponent<Renderer>());
            SetField(script, "m_ButtonCap", cap.transform);
        }

        static void CreateLever(XRInteractionManager _, int interactableLayer)
        {
            var root = new GameObject("Demo Lever");
            root.transform.position = new Vector3(0.75f, 1.0f, 1.35f);
            root.layer = interactableLayer;

            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseObject.name = "Lever Base";
            baseObject.transform.SetParent(root.transform, false);
            baseObject.transform.localScale = new Vector3(0.18f, 0.08f, 0.18f);
            baseObject.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("LeverBase.mat", new Color(0.16f, 0.16f, 0.18f));

            var handleVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handleVisual.name = "Lever Handle";
            handleVisual.layer = interactableLayer;
            handleVisual.transform.SetParent(root.transform, false);
            handleVisual.transform.localPosition = new Vector3(0f, 0.16f, 0.18f);
            handleVisual.transform.localScale = new Vector3(0.06f, 0.3f, 0.06f);
            handleVisual.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("LeverHandle.mat", new Color(0.3f, 0.75f, 0.95f));

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "Lever Indicator";
            indicator.transform.SetParent(root.transform, false);
            indicator.transform.localPosition = new Vector3(0f, 0.06f, -0.22f);
            indicator.transform.localScale = Vector3.one * 0.08f;
            indicator.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("LeverIndicator.mat", new Color(0.8f, 0.3f, 0.25f));

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            grabInteractable.interactionLayers = InteractionLayerMask.GetMask(InteractableLayerName);
            grabInteractable.trackPosition = false;
            grabInteractable.trackRotation = false;
            grabInteractable.throwOnDetach = false;

            var collider = root.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            var boxCollider = root.AddComponent<BoxCollider>();
            boxCollider.center = new Vector3(0f, 0.18f, 0.12f);
            boxCollider.size = new Vector3(0.18f, 0.4f, 0.18f);

            var script = root.AddComponent<DemoLever>();
            SetField(script, "m_HandleVisual", handleVisual.transform);
            SetField(script, "m_IndicatorRenderer", indicator.GetComponent<Renderer>());
        }

        static void AssignRigReferences(WebcamLocomotionDriver locomotionDriver, XROrigin xrOrigin, Transform headTransform)
        {
            SetField(locomotionDriver, "m_XROrigin", xrOrigin);
            SetField(locomotionDriver, "m_ForwardReference", headTransform);
        }

        static void AssignRigReferences(
            WebcamRigDriver rigDriver,
            WebcamTrackingReceiver receiver,
            XROrigin xrOrigin,
            Transform headTransform,
            WebcamXRController leftController,
            WebcamXRController rightController,
            WebcamLocomotionDriver locomotionDriver)
        {
            SetField(rigDriver, "m_Receiver", receiver);
            SetField(rigDriver, "m_XROrigin", xrOrigin);
            SetField(rigDriver, "m_HeadTransform", headTransform);
            SetField(rigDriver, "m_LeftController", leftController);
            SetField(rigDriver, "m_RightController", rightController);
            SetField(rigDriver, "m_LocomotionDriver", locomotionDriver);
        }

        static void AssignOverlayReferences(DemoStatusOverlay overlay, WebcamRigDriver rigDriver, ModeSwitcher modeSwitcher)
        {
            SetField(overlay, "m_RigDriver", rigDriver);
            SetField(overlay, "m_ModeSwitcher", modeSwitcher);
        }

        static void AssignModeReferences(ModeSwitcher modeSwitcher, GameObject webcamRoot)
        {
            SetField(modeSwitcher, "m_WebcamRoot", webcamRoot);
        }

        static Material GetOrCreateMaterial(string fileName, Color color)
        {
            var path = $"{MaterialFolder}/{fileName}";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string folderPath)
        {
            var parts = folderPath.Split('/');
            var current = parts[0];

            for (var i = 1; i < parts.Length; ++i)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static void EnsureLayer(int index, string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layersProperty = tagManager.FindProperty("layers");
            var layerProperty = layersProperty.GetArrayElementAtIndex(index);

            if (string.IsNullOrEmpty(layerProperty.stringValue))
            {
                layerProperty.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return;
            }

            if (layerProperty.stringValue == name)
                return;

            throw new UnityException($"Layer slot {index} is already used by '{layerProperty.stringValue}'. Free it or update {nameof(WebcamXRSceneBootstrapper)}.");
        }

        static void SetField(Object target, string fieldName, Object value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
