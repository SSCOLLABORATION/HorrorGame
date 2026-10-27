using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ProBuilder;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace VectorParkour
{
    /// <summary>
    /// Editor & Assembly script for building the complete Vector 3D parkour level.
    /// Procedurally constructs a 200m+ rooftop track with VaultBlocks, SlideBarriers, and Gaps
    /// using ProBuilderMesh (ShapeGenerator.GenerateCube), applies materials and vertex colors,
    /// sets up tags/layers, and configures Player, Chaser, and Camera in a single batch.
    /// </summary>
    public static class ParkourLevelBuilder
    {
        private const string RootGameObjectName = "ParkourLevel";

#if UNITY_EDITOR
        [MenuItem("Tools/Parkour/Build Parkour Level", priority = 1)]
        public static void BuildLevelMenuItem()
        {
            BuildLevel();
        }
#endif

        public static void BuildLevel()
        {
#if UNITY_EDITOR
            // Step 1: Ensure required Tags and Layers exist
            EnsureTagExists("Obstacle_Vault");
            EnsureTagExists("Obstacle_Slide");
            EnsureLayerExists("Obstacle_Vault");
            EnsureLayerExists("Obstacle_Slide");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Parkour Level");
#endif

            // Step 2: Clean up previous level instance if present
            GameObject existingRoot = GameObject.Find(RootGameObjectName);
            if (existingRoot != null)
            {
#if UNITY_EDITOR
                Undo.DestroyObjectImmediate(existingRoot);
#else
                Object.DestroyImmediate(existingRoot);
#endif
            }

            GameObject levelRoot = new GameObject(RootGameObjectName);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(levelRoot, "Create Level Root");
#endif

            // Step 3: Setup materials
            Material floorMat = GetOrCreateMaterial("Mat_RooftopFloor", new Color(0.12f, 0.14f, 0.18f), 0.25f);
            Material railingMat = GetOrCreateMaterial("Mat_RooftopRailing", new Color(0.25f, 0.28f, 0.35f), 0.4f);
            Material vaultMat = GetOrCreateMaterial("Mat_VaultObstacle", new Color(1.0f, 0.25f, 0.1f), 0.6f, new Color(0.8f, 0.15f, 0.05f));
            Material slideMat = GetOrCreateMaterial("Mat_SlideObstacle", new Color(0.0f, 0.85f, 1.0f), 0.6f, new Color(0.0f, 0.6f, 0.8f));
            Material goalMat = GetOrCreateMaterial("Mat_GoalFinish", new Color(0.1f, 1.0f, 0.4f), 0.7f, new Color(0.1f, 0.9f, 0.35f));

            // Step 4: Build Rooftop Track Segments & Gaps
            // Track total length: > 200m along Z-axis. Width: 4.0m. Top surface at Y = 0.
            GameObject trackRoot = new GameObject("TrackSegments");
            trackRoot.transform.SetParent(levelRoot.transform);

            GameObject obstaclesRoot = new GameObject("Obstacles");
            obstaclesRoot.transform.SetParent(levelRoot.transform);

            // Segment Definitions: (Start Z, Length)
            var segments = new (float startZ, float length)[]
            {
                (-12.0f, 47.0f),     // Segment 1: Z -12 to 35 (Grounds Chaser at -6m and Player at 0m)
                (39.5f, 40.5f),      // Gap 1: 4.5m -> Segment 2: Z 39.5 to 80
                (85.0f, 45.0f),      // Gap 2: 5.0m -> Segment 3: Z 85 to 130
                (135.0f, 42.0f),     // Gap 3: 5.0m -> Segment 4: Z 135 to 177
                (182.0f, 40.0f)      // Gap 4: 5.0m -> Segment 5: Z 182 to 222 (Finish)
            };

            foreach (var seg in segments)
            {
                BuildPlatformSegment(trackRoot.transform, seg.startZ, seg.length, 4.0f, floorMat, railingMat);
            }

            // Step 5: Spawn Obstacles along the path
            // VaultBlock: 1.2m waist-high cube (Y = 0 to 1.2m, center Y = 0.6m)
            // SlideBarrier: 1.2m bottom clearance beam (Y = 1.2m to 2.2m, center Y = 1.7m)
            float[] vaultPositions = new float[] { 14.0f, 52.0f, 98.0f, 122.0f, 158.0f, 194.0f };
            float[] slidePositions = new float[] { 26.0f, 68.0f, 112.0f, 146.0f, 169.0f, 206.0f };

            foreach (float z in vaultPositions)
            {
                BuildVaultBlock(obstaclesRoot.transform, z, vaultMat);
            }

            foreach (float z in slidePositions)
            {
                BuildSlideBarrier(obstaclesRoot.transform, z, slideMat, railingMat);
            }

            // Finish Goal Pad at Z = 216m
            BuildFinishGate(obstaclesRoot.transform, 216.0f, goalMat, railingMat);

            // Step 6: Assemble Player
            GameObject player = AssemblePlayer(levelRoot.transform);

            // Step 7: Assemble Chaser
            GameObject chaser = AssembleChaser(levelRoot.transform, player.transform);

            // Step 8: Assemble Follow Camera
            AssembleCamera(player.transform, chaser.transform);

            // Step 9: Configure Lighting
            SetupLighting(levelRoot.transform);

#if UNITY_EDITOR
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("<color=#00FFAA><b>[Vector Parkour]</b> Level successfully assembled with ProBuilder track, Player, Chaser, and Camera!</color>");
#endif
        }

        #region ProBuilder Geometry Generation

        private static void BuildPlatformSegment(Transform parent, float startZ, float length, float width, Material floorMat, Material railingMat)
        {
            float thickness = 2.0f;
            float centerZ = startZ + (length / 2.0f);
            Vector3 size = new Vector3(width, thickness, length);
            Vector3 position = new Vector3(0f, -thickness / 2.0f, centerZ);

            // ProBuilder Floor Mesh
            ProBuilderMesh floorPB = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            floorPB.transform.position = position;
            floorPB.transform.SetParent(parent);
            floorPB.gameObject.name = $"Platform_{startZ:F0}-{startZ + length:F0}";

            MeshRenderer mr = floorPB.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = floorMat;

            ApplyProBuilderVertexColor(floorPB, new Color(0.12f, 0.14f, 0.18f));
            floorPB.ToMesh();
            floorPB.Refresh();

            BoxCollider bc = floorPB.gameObject.AddComponent<BoxCollider>();
            bc.size = size;

            // Side Curbs / Parapets along track edges (Width: 0.2m, Height: 0.35m)
            BuildCurb(parent, -width / 2.0f - 0.1f, centerZ, length, railingMat);
            BuildCurb(parent, width / 2.0f + 0.1f, centerZ, length, railingMat);
        }

        private static void BuildCurb(Transform parent, float posX, float centerZ, float length, Material mat)
        {
            Vector3 size = new Vector3(0.2f, 0.35f, length);
            Vector3 pos = new Vector3(posX, 0.175f, centerZ);

            ProBuilderMesh curbPB = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            curbPB.transform.position = pos;
            curbPB.transform.SetParent(parent);
            curbPB.gameObject.name = "RooftopEdgeCurb";

            MeshRenderer mr = curbPB.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;

            ApplyProBuilderVertexColor(curbPB, new Color(0.25f, 0.28f, 0.35f));
            curbPB.ToMesh();
            curbPB.Refresh();

            BoxCollider bc = curbPB.gameObject.AddComponent<BoxCollider>();
            bc.size = size;
        }

        private static void BuildVaultBlock(Transform parent, float z, Material vaultMat)
        {
            // Waist-high cube (Height: 1.2m, Width: 4.0m, Depth: 1.2m, Center Y: 0.6m)
            Vector3 size = new Vector3(4.0f, 1.2f, 1.2f);
            Vector3 pos = new Vector3(0f, 0.6f, z);

            ProBuilderMesh pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            pb.transform.position = pos;
            pb.transform.SetParent(parent);
            pb.gameObject.name = $"VaultBlock_{z:F0}";
            pb.gameObject.tag = "Obstacle_Vault";

            int layer = LayerMask.NameToLayer("Obstacle_Vault");
            if (layer != -1) pb.gameObject.layer = layer;

            MeshRenderer mr = pb.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = vaultMat;

            ApplyProBuilderVertexColor(pb, new Color(1.0f, 0.25f, 0.1f));
            pb.ToMesh();
            pb.Refresh();

            BoxCollider bc = pb.gameObject.AddComponent<BoxCollider>();
            bc.size = size;
        }

        private static void BuildSlideBarrier(Transform parent, float z, Material slideMat, Material postMat)
        {
            GameObject barrierRoot = new GameObject($"SlideBarrier_{z:F0}");
            barrierRoot.transform.SetParent(parent);
            barrierRoot.transform.position = new Vector3(0f, 0f, z);

            // Elevated crossbeam: Bottom clearance 1.2m, height 1.0m (spanning Y 1.2m to 2.2m, center Y 1.7m)
            Vector3 beamSize = new Vector3(4.0f, 1.0f, 0.8f);
            Vector3 beamPos = new Vector3(0f, 1.7f, z);

            ProBuilderMesh beamPB = ShapeGenerator.GenerateCube(PivotLocation.Center, beamSize);
            beamPB.transform.position = beamPos;
            beamPB.transform.SetParent(barrierRoot.transform);
            beamPB.gameObject.name = "Crossbeam";
            beamPB.gameObject.tag = "Obstacle_Slide";

            int layer = LayerMask.NameToLayer("Obstacle_Slide");
            if (layer != -1) beamPB.gameObject.layer = layer;

            MeshRenderer beamMR = beamPB.GetComponent<MeshRenderer>();
            if (beamMR != null) beamMR.sharedMaterial = slideMat;

            ApplyProBuilderVertexColor(beamPB, new Color(0.0f, 0.85f, 1.0f));
            beamPB.ToMesh();
            beamPB.Refresh();

            BoxCollider bc = beamPB.gameObject.AddComponent<BoxCollider>();
            bc.size = beamSize;

            // Side support pillars (Left and Right)
            BuildSupportPost(barrierRoot.transform, -1.95f, z, postMat);
            BuildSupportPost(barrierRoot.transform, 1.95f, z, postMat);
        }

        private static void BuildSupportPost(Transform parent, float posX, float z, Material postMat)
        {
            Vector3 postSize = new Vector3(0.2f, 2.2f, 0.8f);
            Vector3 postPos = new Vector3(posX, 1.1f, z);

            ProBuilderMesh postPB = ShapeGenerator.GenerateCube(PivotLocation.Center, postSize);
            postPB.transform.position = postPos;
            postPB.transform.SetParent(parent);
            postPB.gameObject.name = "SupportPost";

            MeshRenderer mr = postPB.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = postMat;

            ApplyProBuilderVertexColor(postPB, new Color(0.3f, 0.35f, 0.42f));
            postPB.ToMesh();
            postPB.Refresh();

            BoxCollider bc = postPB.gameObject.AddComponent<BoxCollider>();
            bc.size = postSize;
        }

        private static void BuildFinishGate(Transform parent, float z, Material goalMat, Material postMat)
        {
            GameObject goalRoot = new GameObject("FinishGoalGate");
            goalRoot.transform.SetParent(parent);

            // Overhead arch beam
            Vector3 beamSize = new Vector3(4.2f, 0.6f, 1.0f);
            Vector3 beamPos = new Vector3(0f, 3.2f, z);

            ProBuilderMesh beamPB = ShapeGenerator.GenerateCube(PivotLocation.Center, beamSize);
            beamPB.transform.position = beamPos;
            beamPB.transform.SetParent(goalRoot.transform);
            beamPB.gameObject.name = "GoalArch";

            MeshRenderer mr = beamPB.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = goalMat;

            ApplyProBuilderVertexColor(beamPB, new Color(0.1f, 1.0f, 0.4f));
            beamPB.ToMesh();
            beamPB.Refresh();

            // Finish Floor Line
            Vector3 padSize = new Vector3(4.0f, 0.05f, 2.0f);
            Vector3 padPos = new Vector3(0f, 0.025f, z);
            ProBuilderMesh padPB = ShapeGenerator.GenerateCube(PivotLocation.Center, padSize);
            padPB.transform.position = padPos;
            padPB.transform.SetParent(goalRoot.transform);
            padPB.gameObject.name = "GoalLine";

            MeshRenderer mrPad = padPB.GetComponent<MeshRenderer>();
            if (mrPad != null) mrPad.sharedMaterial = goalMat;
            ApplyProBuilderVertexColor(padPB, new Color(0.1f, 1.0f, 0.4f));
            padPB.ToMesh();
            padPB.Refresh();
        }

        private static void ApplyProBuilderVertexColor(ProBuilderMesh pb, Color color)
        {
            if (pb == null) return;
            Color[] colors = new Color[pb.vertexCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = color;
            }
            pb.colors = colors;
        }

        #endregion

        #region Character & Scene Assembly

        private static GameObject AssemblePlayer(Transform parent)
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(parent);
            player.transform.position = new Vector3(0f, 0.05f, 0f);
            player.transform.rotation = Quaternion.identity;

            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;

            // Vault Detector Forward Trigger
            GameObject detector = new GameObject("VaultDetector");
            detector.transform.SetParent(player.transform);
            detector.transform.localPosition = Vector3.zero;
            detector.transform.localRotation = Quaternion.identity;

            BoxCollider trigger = detector.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.9f, 1.3f);
            trigger.size = new Vector3(2.5f, 1.6f, 1.8f);
            detector.AddComponent<VaultDetector>();

            // Visual Avatar: Instantiate HumanDummy_M Blue prefab
            GameObject visual = InstantiateDummyPrefab("HumanDummy_M Blue", player.transform, new Color(0.2f, 0.6f, 1.0f));

            // Attach PlayerParkourController
            PlayerParkourController controller = player.AddComponent<PlayerParkourController>();

            return player;
        }

        private static GameObject AssembleChaser(Transform parent, Transform playerTransform)
        {
            GameObject chaser = new GameObject("Chaser");
            chaser.transform.SetParent(parent);
            chaser.transform.position = new Vector3(0f, 0.05f, -6.0f);
            chaser.transform.rotation = Quaternion.identity;

            CharacterController cc = chaser.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;

            // Visual Avatar: Instantiate HumanDummy_M Red prefab
            GameObject visual = InstantiateDummyPrefab("HumanDummy_M Red", chaser.transform, new Color(1.0f, 0.2f, 0.2f));

            // Attach ChaserAI
            ChaserAI ai = chaser.AddComponent<ChaserAI>();

            return chaser;
        }

        private static GameObject InstantiateDummyPrefab(string prefabName, Transform parent, Color fallbackColor)
        {
            GameObject visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(parent);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;

#if UNITY_EDITOR
            string[] guids = AssetDatabase.FindAssets($"{prefabName} t:Prefab");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, visualRoot.transform);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;

                    RuntimeAnimatorController animController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Settings/VectorCharacterAnimator.controller");
                    if (animController != null)
                    {
                        Animator anim = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                        anim.runtimeAnimatorController = animController;
                        anim.applyRootMotion = false;
                    }

                    return visualRoot;
                }
            }
#endif

            // Fallback mannequin geometry if prefab not loaded
            GameObject fallbackBody = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallbackBody.transform.SetParent(visualRoot.transform);
            fallbackBody.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            fallbackBody.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Object.DestroyImmediate(fallbackBody.GetComponent<Collider>());

            Material mat = GetOrCreateMaterial($"Mat_Fallback_{prefabName}", fallbackColor, 0.5f);
            fallbackBody.GetComponent<MeshRenderer>().sharedMaterial = mat;

            return visualRoot;
        }

        private static void AssembleCamera(Transform player, Transform chaser)
        {
            Camera mainCam = Camera.main;
            GameObject camObj = mainCam != null ? mainCam.gameObject : new GameObject("Main Camera");

            if (mainCam == null)
            {
                mainCam = camObj.AddComponent<Camera>();
                camObj.tag = "MainCamera";
                camObj.AddComponent<AudioListener>();
            }

            camObj.transform.position = new Vector3(-7.2f, 3.8f, -8.5f);
            camObj.transform.rotation = Quaternion.Euler(13.0f, 28.0f, 0.0f);

            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.07f, 0.09f, 0.14f); // Sleek dusk dystopian skyline
            mainCam.fieldOfView = 60.0f;

            VectorFollowCamera followCam = camObj.GetComponent<VectorFollowCamera>();
            if (followCam == null) followCam = camObj.AddComponent<VectorFollowCamera>();
            followCam.SetTargets(player, chaser);
            followCam.SetOffset(new Vector3(-7.2f, 3.8f, -8.5f), new Vector3(13.0f, 28.0f, 0.0f));
            followCam.SnapToTarget();
        }

        private static void SetupLighting(Transform parent)
        {
            Light dirLight = Object.FindFirstObjectByType<Light>();
            GameObject lightObj;

            if (dirLight != null && dirLight.type == LightType.Directional)
            {
                lightObj = dirLight.gameObject;
            }
            else
            {
                lightObj = new GameObject("Directional Light");
                dirLight = lightObj.AddComponent<Light>();
                dirLight.type = LightType.Directional;
            }

            lightObj.transform.SetParent(parent);
            lightObj.transform.rotation = Quaternion.Euler(35.0f, 45.0f, 0.0f);
            dirLight.color = new Color(1.0f, 0.96f, 0.90f);
            dirLight.intensity = 1.15f;
            dirLight.shadows = LightShadows.Soft;
        }

        #endregion

        #region Helpers

        private static Material GetOrCreateMaterial(string name, Color color, float smoothness, Color emissionColor = default)
        {
#if UNITY_EDITOR
            string dir = "Assets/Settings";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            string path = $"{dir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
                mat = new Material(s);
                mat.name = name;

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

                if (emissionColor != default)
                {
                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
                }

                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

                if (emissionColor != default)
                {
                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
                }
            }
            return mat;
#else
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            Material mat = new Material(s);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
#endif
        }

#if UNITY_EDITOR
        private static void EnsureTagExists(string tag)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty tagsProp = tagManager.FindProperty("tags");

            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue.Equals(tag))
                    return;
            }

            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedProperties();
        }

        private static void EnsureLayerExists(string layer)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layersProp = tagManager.FindProperty("layers");

            for (int i = 8; i < layersProp.arraySize; i++)
            {
                SerializedProperty sp = layersProp.GetArrayElementAtIndex(i);
                if (sp.stringValue.Equals(layer)) return;
            }

            for (int i = 8; i < layersProp.arraySize; i++)
            {
                SerializedProperty sp = layersProp.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue))
                {
                    sp.stringValue = layer;
                    tagManager.ApplyModifiedProperties();
                    return;
                }
            }
        }
#endif
        #endregion
    }
}
