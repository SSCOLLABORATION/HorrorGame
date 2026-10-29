using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.ProBuilder;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Vector3D.Core;
using Vector3D.UI;
using VectorParkour;

namespace Vector3D.EditorScripts
{
    /// <summary>
    /// Master Editor generator for Vector3D multi-level rooftop parkour world.
    /// Builds both Level 1 (X = 0f) and Level 2 (X = 100f) in the same scene.
    /// Features:
    /// - Strictly uses ProBuilder ShapeGenerator.GenerateCube with BoxColliders.
    /// - Procedurally generates platforms, curbs, vault obstacles, slide obstacles, and finish gates.
    /// - Level 2 wide chasms (12-16m) with cyan & orange wall-run surfaces tagged WallRunObstacle.
    /// - Fully idempotent: re-running replaces generated geometry without duplicating core scene actors.
    /// - Wires EventSystem, GameManager, VectorFollowCamera, and UI Canvas with Next Level button.
    /// </summary>
    public static class ParkourSceneGenerator
    {
        private const string RootGameObjectName = "ParkourLevel";

        [MenuItem("Tools/Vector3D/Build Complete Scene", priority = 1)]
        public static void BuildCompleteScene()
        {
            // 1. Ensure required Tags and Layers exist
            EnsureTagExists("Obstacle_Vault");
            EnsureTagExists("Obstacle_Slide");
            EnsureTagExists("WallRunObstacle");
            EnsureTagExists("Finish");
            EnsureLayerExists("Obstacle_Vault");
            EnsureLayerExists("Obstacle_Slide");

            // 2. Ensure EventSystem with InputSystemUIInputModule
            SetupEventSystem();


            // 4. Setup Materials
            Material floorMat = GetOrCreateMaterial("Mat_RooftopFloor", new Color(0.12f, 0.14f, 0.18f), 0.25f);
            Material railingMat = GetOrCreateMaterial("Mat_RooftopRailing", new Color(0.25f, 0.28f, 0.35f), 0.4f);
            Material vaultMat = GetOrCreateMaterial("Mat_VaultObstacle", new Color(1.0f, 0.25f, 0.1f), 0.6f, new Color(0.8f, 0.15f, 0.05f));
            Material slideMat = GetOrCreateMaterial("Mat_SlideObstacle", new Color(0.0f, 0.85f, 1.0f), 0.6f, new Color(0.0f, 0.6f, 0.8f));
            Material goalMat = GetOrCreateMaterial("Mat_GoalFinish", new Color(0.1f, 1.0f, 0.4f), 0.7f, new Color(0.1f, 0.9f, 0.35f));
            Material wallCyanMat = GetOrCreateMaterial("Mat_WallRun_Cyan", new Color(0.0f, 0.95f, 1.0f), 0.8f, new Color(0.0f, 0.8f, 1.0f));
            Material wallOrangeMat = GetOrCreateMaterial("Mat_WallRun_Orange", new Color(1.0f, 0.55f, 0.05f), 0.8f, new Color(0.9f, 0.45f, 0.0f));

            // 5. Setup Level Root
            GameObject levelRoot = GameObject.Find(RootGameObjectName);
            if (levelRoot == null)
            {
                levelRoot = new GameObject(RootGameObjectName);
                Undo.RegisterCreatedObjectUndo(levelRoot, "Create Level Root");
            }

            // Clean up previous generated level branches under root for idempotency
            Transform oldL1 = levelRoot.transform.Find("Level 1");
            if (oldL1 != null) Undo.DestroyObjectImmediate(oldL1.gameObject);
            Transform oldL2 = levelRoot.transform.Find("Level 2");
            if (oldL2 != null) Undo.DestroyObjectImmediate(oldL2.gameObject);
            Transform oldL3 = levelRoot.transform.Find("Level 3");
            if (oldL3 != null) Undo.DestroyObjectImmediate(oldL3.gameObject);
            Transform oldL4 = levelRoot.transform.Find("Level 4");
            if (oldL4 != null) Undo.DestroyObjectImmediate(oldL4.gameObject);
            Transform oldTrack = levelRoot.transform.Find("TrackSegments");
            if (oldTrack != null) Undo.DestroyObjectImmediate(oldTrack.gameObject);
            Transform oldObs = levelRoot.transform.Find("Obstacles");
            if (oldObs != null) Undo.DestroyObjectImmediate(oldObs.gameObject);

            // 6. Build Level 1 (X = 0f, Z = 0..252m)
            GameObject level1Root = new GameObject("Level 1");
            level1Root.transform.SetParent(levelRoot.transform, false);
            BuildLevel1(level1Root.transform, floorMat, railingMat, vaultMat, slideMat, goalMat);

            // 7. Build Level 2 (X = 100f, Z = 0..252m)
            GameObject level2Root = new GameObject("Level 2");
            level2Root.transform.SetParent(levelRoot.transform, false);
            BuildLevel2(level2Root.transform, floorMat, railingMat, vaultMat, slideMat, goalMat, wallCyanMat, wallOrangeMat);

            // 8. Build Level 3 (X = 200f, Z = 0..252m)
            GameObject level3Root = new GameObject("Level 3");
            level3Root.transform.SetParent(levelRoot.transform, false);
            BuildLevel3(level3Root.transform, floorMat, railingMat, vaultMat, slideMat, goalMat, wallCyanMat, wallOrangeMat);

            // 9. Build Level 4 (X = 300f, Z = 0..252m)
            GameObject level4Root = new GameObject("Level 4");
            level4Root.transform.SetParent(levelRoot.transform, false);
            BuildLevel4(level4Root.transform, floorMat, railingMat, vaultMat, slideMat, goalMat, wallCyanMat, wallOrangeMat);

            // 10. Configure GameManager with Level Isolation References
            SetupGameManager(level1Root, level2Root, level3Root, level4Root);

            // 11. Setup Lighting
            SetupLighting(levelRoot.transform);

            // 12. Setup Player (Reuse existing or assemble)
            GameObject player = EnsurePlayer(levelRoot.transform);

            // 13. Setup Chaser (Reuse existing or assemble)
            GameObject chaser = EnsureChaser(levelRoot.transform, player.transform);

            // 14. Setup Follow Camera
            EnsureCamera(player.transform, chaser.transform);

            // 15. Setup Audio Manager (BGM and SFX)
            SetupAudioManager();

            // 16. Build Full Single-Scene UI Canvas Overlay System (with Level Select & NextLevelButton)
            BuildUISystem();

            // 17. Mark Dirty and Save Scene
            var activeScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);

            Debug.Log("<color=green>[Vector3D]</color> Multi-Level Scene (Level 1, Level 2, Level 3 & Level 4) with Audio System generated and saved successfully!");
        }

        #region Level 1 Construction (X = 0f)

        private static void BuildLevel1(Transform levelRoot, Material floorMat, Material railingMat, Material vaultMat, Material slideMat, Material goalMat)
        {
            float trackX = 0f;
            ParkourSector sector = levelRoot.gameObject.GetComponent<ParkourSector>() ?? levelRoot.gameObject.AddComponent<ParkourSector>();
            sector.SectorNumber = 1;
            sector.TrackCenterX = trackX;

            GameObject trackRoot = new GameObject("TrackSegments");
            trackRoot.transform.SetParent(levelRoot, false);

            GameObject obstaclesRoot = new GameObject("Obstacles");
            obstaclesRoot.transform.SetParent(levelRoot, false);

            // Platform segments: (startZ, length)
            var segments = new (float startZ, float length)[]
            {
                (-12.0f, 47.0f), // Segment 1: Z -12 to 35
                (40.0f, 40.0f),  // Gap 1: 5.0m -> Segment 2: Z 40 to 80
                (85.0f, 45.0f),  // Gap 2: 5.0m -> Segment 3: Z 85 to 130
                (135.0f, 43.0f), // Gap 3: 5.0m -> Segment 4: Z 135 to 178
                (183.0f, 69.0f)  // Gap 4: 5.0m -> Segment 5: Z 183 to 252 (Finish)
            };

            foreach (var seg in segments)
            {
                BuildPlatformSegment(trackRoot.transform, trackX, seg.startZ, seg.length, 4.0f, floorMat, railingMat);
            }

            // Vault Obstacles
            float[] vaultPositions = new float[] { 14.0f, 52.0f, 98.0f, 122.0f, 158.0f, 194.0f };
            foreach (float z in vaultPositions)
            {
                BuildVaultBlock(obstaclesRoot.transform, trackX, z, vaultMat);
            }

            // Slide Obstacles
            float[] slidePositions = new float[] { 26.0f, 68.0f, 112.0f, 146.0f, 169.0f, 206.0f };
            foreach (float z in slidePositions)
            {
                BuildSlideBarrier(obstaclesRoot.transform, trackX, z, slideMat, railingMat);
            }

            // Finish Goal Gate at Z = 246m
            BuildFinishGate(obstaclesRoot.transform, trackX, 246.0f, goalMat, railingMat, 1);
        }

        #endregion

        #region Level 2 Construction (X = 100f)

        private static void BuildLevel2(Transform levelRoot, Material floorMat, Material railingMat, Material vaultMat, Material slideMat, Material goalMat, Material cyanWallMat, Material orangeWallMat)
        {
            float trackX = 100.0f;
            ParkourSector sector = levelRoot.gameObject.GetComponent<ParkourSector>() ?? levelRoot.gameObject.AddComponent<ParkourSector>();
            sector.SectorNumber = 2;
            sector.TrackCenterX = trackX;
            GameObject trackRoot = new GameObject("TrackSegments");
            trackRoot.transform.SetParent(levelRoot, false);

            GameObject obstaclesRoot = new GameObject("Obstacles");
            obstaclesRoot.transform.SetParent(levelRoot, false);

            GameObject wallsRoot = new GameObject("WallRunSurfaces");
            wallsRoot.transform.SetParent(levelRoot, false);

            // Segments and chasms:
            // Segment 1: Z -12 to 35
            BuildPlatformSegment(trackRoot.transform, trackX, -12.0f, 47.0f, 4.0f, floorMat, railingMat);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 16.0f, vaultMat);

            // Gap 1: Z 35 to 40 (5m jumpable gap)
            // Segment 2: Z 40 to 68 (28m platform)
            BuildPlatformSegment(trackRoot.transform, trackX, 40.0f, 28.0f, 4.0f, floorMat, railingMat);
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 54.0f, slideMat, railingMat);

            // Gap 2: Z 68 to 82 (14m wide chasm!)
            // Left Wall-Run Surface: centered at X = 98.0f, spanning Z = 65 to 85 (20m length, center Z = 75)
            BuildWallRunSurface(wallsRoot.transform, 98.0f, 75.0f, 20.0f, 4.0f, 0.5f, cyanWallMat, "WallRun_Left_1");

            // Segment 3: Z 82 to 124 (42m platform)
            BuildPlatformSegment(trackRoot.transform, trackX, 82.0f, 42.0f, 4.0f, floorMat, railingMat);
            // Generous recovery run after wall-run: Wall ends at Z = 85m, Slide obstacle placed at Z = 108m (23m distance)
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 108.0f, slideMat, railingMat);

            // Gap 3: Z 124 to 139 (15m wide chasm!)
            // Right Wall-Run Surface: centered at X = 102.0f, spanning Z = 121 to 142 (21m length, center Z = 131.5)
            BuildWallRunSurface(wallsRoot.transform, 102.0f, 131.5f, 21.0f, 4.0f, 0.5f, orangeWallMat, "WallRun_Right_2");

            // Segment 4: Z 139 to 180 (41m platform)
            BuildPlatformSegment(trackRoot.transform, trackX, 139.0f, 41.0f, 4.0f, floorMat, railingMat);
            // Generous recovery run after wall-run: Wall ends at Z = 142m, Vault obstacle placed at Z = 166m (24m distance)
            BuildVaultBlock(obstaclesRoot.transform, trackX, 166.0f, vaultMat);

            // Gap 4: Z 180 to 196 (16m wide chasm!)
            // Left Wall-Run Surface: centered at X = 98.0f, spanning Z = 177 to 199 (22m length, center Z = 188)
            BuildWallRunSurface(wallsRoot.transform, 98.0f, 188.0f, 22.0f, 4.0f, 0.5f, cyanWallMat, "WallRun_Left_3");

            // Segment 5: Z 196 to 252 (56m platform to finish)
            BuildPlatformSegment(trackRoot.transform, trackX, 196.0f, 56.0f, 4.0f, floorMat, railingMat);
            // Generous recovery run after wall-run: Wall ends at Z = 199m, Slide obstacle placed at Z = 222m (23m distance)
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 222.0f, slideMat, railingMat);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 236.0f, vaultMat);

            // Finish Goal Gate at Z = 246m
            BuildFinishGate(obstaclesRoot.transform, trackX, 246.0f, goalMat, railingMat, 2);
        }

        #endregion

        #region Level 3 Construction (X = 200f)

        private static void BuildLevel3(Transform levelRoot, Material floorMat, Material railingMat, Material vaultMat, Material slideMat, Material goalMat, Material cyanWallMat, Material orangeWallMat)
        {
            float trackX = 200.0f;
            ParkourSector sector = levelRoot.gameObject.GetComponent<ParkourSector>() ?? levelRoot.gameObject.AddComponent<ParkourSector>();
            sector.SectorNumber = 3;
            sector.TrackCenterX = trackX;

            // Traversal Zone for Vertical Shaft
            GameObject shaftZoneGo = new GameObject("TraversalZone_VerticalShaft");
            shaftZoneGo.transform.SetParent(levelRoot, false);
            shaftZoneGo.transform.position = new Vector3(trackX, 5.0f, 85.0f);
            BoxCollider shaftCol = shaftZoneGo.AddComponent<BoxCollider>();
            shaftCol.isTrigger = true;
            shaftCol.size = new Vector3(6.0f, 12.0f, 48.0f);
            TraversalZone shaftZone = shaftZoneGo.AddComponent<TraversalZone>();
            shaftZone.ZoneType = TraversalZoneType.VerticalShaft;
            shaftZone.SectorNumber = 3;
            shaftZone.TargetElevation = 6.0f;

            // Traversal Zone for Rooftop Drop
            GameObject dropZoneGo = new GameObject("TraversalZone_RooftopDrop");
            dropZoneGo.transform.SetParent(levelRoot, false);
            dropZoneGo.transform.position = new Vector3(trackX, 3.0f, 175.0f);
            BoxCollider dropCol = dropZoneGo.AddComponent<BoxCollider>();
            dropCol.isTrigger = true;
            dropCol.size = new Vector3(6.0f, 8.0f, 16.0f);
            TraversalZone dropZone = dropZoneGo.AddComponent<TraversalZone>();
            dropZone.ZoneType = TraversalZoneType.RooftopDrop;
            dropZone.SectorNumber = 3;
            dropZone.TargetElevation = 0.0f;

            GameObject trackRoot = new GameObject("TrackSegments");
            trackRoot.transform.SetParent(levelRoot, false);

            GameObject obstaclesRoot = new GameObject("Obstacles");
            obstaclesRoot.transform.SetParent(levelRoot, false);

            GameObject wallsRoot = new GameObject("WallRunSurfaces");
            wallsRoot.transform.SetParent(levelRoot, false);

            // =========================================================================
            // Section A: Introduction (Conventional running, vaulting, and sliding)
            // =========================================================================
            // Segment 1: Z -12 to 35 (Ground level Y = 0)
            BuildPlatformSegment(trackRoot.transform, trackX, -12.0f, 47.0f, 4.0f, floorMat, railingMat);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 16.0f, vaultMat);

            // Gap 1: Z 35 to 40 (5m jumpable gap)
            // Segment 2: Z 40 to 65 (25m platform, ground level Y = 0)
            BuildPlatformSegment(trackRoot.transform, trackX, 40.0f, 25.0f, 4.0f, floorMat, railingMat);
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 52.0f, slideMat, railingMat);

            // =========================================================================
            // Section B & C: Wall-Run Initiation & Vertical Alternating Wall-Jump Structure
            // =========================================================================
            // Abyss Pit below vertical climbing shaft: Z = 65 to 102 (37m open pit)
            //
            // Left Wall-Run Surface (Cyan):
            // Center X = 198.0f (thickness 0.5m -> inner face at X = 198.25f, player attaches at X ≈ 198.72f)
            // Spanning Z = 62.0f to 106.0f (Length = 44m, Center Z = 84.0f, Height = 10.0f)
            // Starts before the gap (at Z = 62m) so player seamlessly initiates wall run from Segment 2
            BuildWallRunSurface(wallsRoot.transform, 198.0f, 84.0f, 44.0f, 10.0f, 0.5f, cyanWallMat, "WallRun_Left_V1");

            // Right Wall-Run Surface (Orange):
            // Center X = 202.0f (thickness 0.5m -> inner face at X = 201.75f, player attaches at X ≈ 201.28f)
            // Spanning Z = 62.0f to 108.0f (Length = 46m, Center Z = 85.0f, Height = 10.0f)
            // Opposing wall flanking the vertical climbing shaft creating an overhead canyon corridor
            BuildWallRunSurface(wallsRoot.transform, 202.0f, 85.0f, 46.0f, 10.0f, 0.5f, orangeWallMat, "WallRun_Right_V1");

            // =========================================================================
            // Section D: Upper Platform & Rooftop Drop (Elevated at Y = 6.0m)
            // =========================================================================
            // Segment 3: Upper Platform Z = 92 to 170 (Length = 78m, Elevated at Y = 6.0m)
            BuildPlatformSegment(trackRoot.transform, trackX, 92.0f, 78.0f, 4.0f, floorMat, railingMat, 6.0f);
            // Generous recovery run after wall-run: Walls end at Z = 108m, Slide barrier placed at Z = 134m (26m distance)
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 134.0f, slideMat, railingMat, 6.0f);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 154.0f, vaultMat, 6.0f);

            // Rooftop Drop Gap: Z = 170 to 176 (6m gap, dropping from Y = 6.0m to ground Y = 0.0m)

            // =========================================================================
            // Section E: Landing Platform & Finish Line (Ground level Y = 0)
            // =========================================================================
            // Segment 4: Landing Platform Z = 176 to 252 (Length = 76m, Ground level Y = 0)
            BuildPlatformSegment(trackRoot.transform, trackX, 176.0f, 76.0f, 4.0f, floorMat, railingMat, 0.0f);
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 202.0f, slideMat, railingMat, 0.0f);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 222.0f, vaultMat, 0.0f);

            // Finish Goal Gate at Z = 246m (TargetLevelIndex = 3)
            BuildFinishGate(obstaclesRoot.transform, trackX, 246.0f, goalMat, railingMat, 3);
        }

        #endregion

        #region Level 4 Construction (X = 300f - The Final Parkour Challenge)

        private static void BuildLevel4(Transform levelRoot, Material floorMat, Material railingMat, Material vaultMat, Material slideMat, Material goalMat, Material cyanWallMat, Material orangeWallMat)
        {
            float trackX = 300.0f;
            ParkourSector sector = levelRoot.gameObject.GetComponent<ParkourSector>() ?? levelRoot.gameObject.AddComponent<ParkourSector>();
            sector.SectorNumber = 4;
            sector.TrackCenterX = trackX;

            // Traversal Zone for Vertical Shaft Gauntlet
            GameObject shaftZoneGo = new GameObject("TraversalZone_VerticalShaft");
            shaftZoneGo.transform.SetParent(levelRoot, false);
            shaftZoneGo.transform.position = new Vector3(trackX, 6.0f, 105.5f);
            BoxCollider shaftCol = shaftZoneGo.AddComponent<BoxCollider>();
            shaftCol.isTrigger = true;
            shaftCol.size = new Vector3(6.0f, 14.0f, 42.0f);
            TraversalZone shaftZone = shaftZoneGo.AddComponent<TraversalZone>();
            shaftZone.ZoneType = TraversalZoneType.VerticalShaft;
            shaftZone.SectorNumber = 4;
            shaftZone.TargetElevation = 6.0f;

            // Traversal Zone for Rooftop Drop
            GameObject dropZoneGo = new GameObject("TraversalZone_RooftopDrop");
            dropZoneGo.transform.SetParent(levelRoot, false);
            dropZoneGo.transform.position = new Vector3(trackX, 3.0f, 183.0f);
            BoxCollider dropCol = dropZoneGo.AddComponent<BoxCollider>();
            dropCol.isTrigger = true;
            dropCol.size = new Vector3(6.0f, 8.0f, 16.0f);
            TraversalZone dropZone = dropZoneGo.AddComponent<TraversalZone>();
            dropZone.ZoneType = TraversalZoneType.RooftopDrop;
            dropZone.SectorNumber = 4;
            dropZone.TargetElevation = 0.0f;

            GameObject trackRoot = new GameObject("TrackSegments");
            trackRoot.transform.SetParent(levelRoot, false);

            GameObject obstaclesRoot = new GameObject("Obstacles");
            obstaclesRoot.transform.SetParent(levelRoot, false);

            GameObject wallsRoot = new GameObject("WallRunSurfaces");
            wallsRoot.transform.SetParent(levelRoot, false);

            // =========================================================================
            // Section A: The Last Run (Z = -12m to 58m, Ground Y = 0)
            // =========================================================================
            // Platform 1: Z = -12 to 32 (Length = 44m, Ground Y = 0)
            BuildPlatformSegment(trackRoot.transform, trackX, -12.0f, 44.0f, 4.0f, floorMat, railingMat, 0.0f);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 14.0f, vaultMat, 0.0f);
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 24.0f, slideMat, railingMat, 0.0f);

            // Jumpable Gap 1: Z = 32 to 37 (5m gap)
            // Platform 2: Z = 37 to 58 (Length = 21m, Ground Y = 0)
            BuildPlatformSegment(trackRoot.transform, trackX, 37.0f, 21.0f, 4.0f, floorMat, railingMat, 0.0f);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 46.0f, vaultMat, 0.0f);

            // =========================================================================
            // Section B: Combined Obstacle Sequence (Z = 58m to 88m, Ground Y = 0)
            // =========================================================================
            // Jumpable Gap 2: Z = 58 to 63 (5m gap)
            // Platform 3: Z = 63 to 88 (Length = 25m, Ground Y = 0)
            BuildPlatformSegment(trackRoot.transform, trackX, 63.0f, 25.0f, 4.0f, floorMat, railingMat, 0.0f);
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 68.0f, slideMat, railingMat, 0.0f);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 74.0f, vaultMat, 0.0f);

            // Wall-Run Surface Right (Orange): Z = 78 to 88 (Length = 10m, Center Z = 83.0f, Height = 5.0f, thickness = 0.5m)
            BuildWallRunSurface(wallsRoot.transform, 302.0f, 83.0f, 10.0f, 5.0f, 0.5f, orangeWallMat, "WallRun_Right_SectionB");

            // =========================================================================
            // Section C: Vertical Wall-Jump Gauntlet (Z = 88m to 145m)
            // =========================================================================
            // Abyss Pit below vertical climbing shaft: Z = 88 to 120 (32m open abyss pit)
            //
            // Left Wall-Run Surface (Cyan):
            // Center X = 298.0f (thickness 0.5m -> inner face at X = 298.25f, player attaches at X ≈ 298.72f)
            // Spanning Z = 86.0f to 125.0f (Length = 39m, Center Z = 105.5f, Height = 12.0f)
            // Starts before the abyss (at Z = 86m) so player seamlessly initiates wall run from Platform 3
            BuildWallRunSurface(wallsRoot.transform, 298.0f, 105.5f, 39.0f, 12.0f, 0.5f, cyanWallMat, "WallRun_Left_FinalGauntlet");

            // Right Wall-Run Surface (Orange):
            // Center X = 302.0f (thickness 0.5m -> inner face at X = 301.75f, player attaches at X ≈ 301.28f)
            // Spanning Z = 86.0f to 125.0f (Length = 39m, Center Z = 105.5f, Height = 12.0f)
            // Opposing wall flanking the vertical gauntlet; engaging Shift + A/D alternating traversal
            BuildWallRunSurface(wallsRoot.transform, 302.0f, 105.5f, 39.0f, 12.0f, 0.5f, orangeWallMat, "WallRun_Right_FinalGauntlet");

            // =========================================================================
            // Section D: The Chaser's Final Pursuit (Z = 120m to 186m, Elevated Y = 6.0m)
            // =========================================================================
            // Platform 4: Upper Platform Z = 120 to 180 (Length = 60m, Elevated at Y = 6.0m)
            BuildPlatformSegment(trackRoot.transform, trackX, 120.0f, 60.0f, 4.0f, floorMat, railingMat, 6.0f);
            // Generous recovery run after wall-run: Walls end at Z = 125m, Slide barrier placed at Z = 146m (21m distance)
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 146.0f, slideMat, railingMat, 6.0f);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 165.0f, vaultMat, 6.0f);

            // Rooftop Drop Gap: Z = 180 to 186 (6m gap, dropping from Y = 6.0m down to ground Y = 0.0m)

            // =========================================================================
            // Section E: The Final Approach & Finish (Z = 186m to 252m, Ground Y = 0)
            // =========================================================================
            // Platform 5: Landing Platform Z = 186 to 252 (Length = 66m, Ground Y = 0.0m)
            BuildPlatformSegment(trackRoot.transform, trackX, 186.0f, 66.0f, 4.0f, floorMat, railingMat, 0.0f);
            BuildSlideBarrier(obstaclesRoot.transform, trackX, 205.0f, slideMat, railingMat, 0.0f);
            BuildVaultBlock(obstaclesRoot.transform, trackX, 224.0f, vaultMat, 0.0f);

            // Finish Goal Gate at Z = 246m (TargetLevelIndex = 4)
            BuildFinishGate(obstaclesRoot.transform, trackX, 246.0f, goalMat, railingMat, 4);
        }

        #endregion

        #region ProBuilder Geometry Generation

        private static void BuildPlatformSegment(Transform parent, float trackX, float startZ, float length, float width, Material floorMat, Material railingMat, float topY = 0.0f)
        {
            float thickness = 2.0f;
            float centerZ = startZ + (length / 2.0f);
            Vector3 size = new Vector3(width, thickness, length);
            Vector3 position = new Vector3(trackX, topY - (thickness / 2.0f), centerZ);

            ProBuilderMesh floorPB = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            floorPB.transform.position = position;
            floorPB.transform.SetParent(parent);
            floorPB.gameObject.name = $"Platform_{trackX:F0}_{startZ:F0}-{startZ + length:F0}";

            MeshRenderer mr = floorPB.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = floorMat;

            ApplyProBuilderVertexColor(floorPB, new Color(0.12f, 0.14f, 0.18f));
            floorPB.ToMesh();
            floorPB.Refresh();

            BoxCollider bc = floorPB.gameObject.AddComponent<BoxCollider>();
            bc.size = size;

            // Side Curbs along track edges (Width: 0.2m, Height: 0.35m)
            BuildCurb(parent, trackX - (width / 2.0f) - 0.1f, centerZ, length, railingMat, topY);
            BuildCurb(parent, trackX + (width / 2.0f) + 0.1f, centerZ, length, railingMat, topY);
        }

        private static void BuildCurb(Transform parent, float posX, float centerZ, float length, Material mat, float topY = 0.0f)
        {
            Vector3 size = new Vector3(0.2f, 0.35f, length);
            Vector3 pos = new Vector3(posX, topY + 0.175f, centerZ);

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

        private static void BuildVaultBlock(Transform parent, float trackX, float z, Material vaultMat, float topY = 0.0f)
        {
            Vector3 size = new Vector3(4.0f, 1.2f, 1.2f);
            Vector3 pos = new Vector3(trackX, topY + 0.6f, z);

            ProBuilderMesh pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            pb.transform.position = pos;
            pb.transform.SetParent(parent);
            pb.gameObject.name = $"VaultBlock_{trackX:F0}_{z:F0}";
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

        private static void BuildSlideBarrier(Transform parent, float trackX, float z, Material slideMat, Material postMat, float topY = 0.0f)
        {
            GameObject barrierRoot = new GameObject($"SlideBarrier_{trackX:F0}_{z:F0}");
            barrierRoot.transform.SetParent(parent);
            barrierRoot.transform.position = new Vector3(trackX, topY, z);

            Vector3 beamSize = new Vector3(4.0f, 1.0f, 0.8f);
            Vector3 beamPos = new Vector3(trackX, topY + 1.7f, z);

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

            // Side support pillars
            BuildSupportPost(barrierRoot.transform, trackX - 1.95f, z, postMat, topY);
            BuildSupportPost(barrierRoot.transform, trackX + 1.95f, z, postMat, topY);
        }

        private static void BuildSupportPost(Transform parent, float posX, float z, Material postMat, float topY = 0.0f)
        {
            Vector3 postSize = new Vector3(0.2f, 2.2f, 0.8f);
            Vector3 postPos = new Vector3(posX, topY + 1.1f, z);

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

        private static void BuildWallRunSurface(Transform parent, float posX, float centerZ, float length, float height, float thickness, Material wallMat, string name)
        {
            Vector3 size = new Vector3(thickness, height, length);
            Vector3 pos = new Vector3(posX, height / 2.0f, centerZ);

            ProBuilderMesh wallPB = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            wallPB.transform.position = pos;
            wallPB.transform.SetParent(parent);
            wallPB.gameObject.name = name;
            wallPB.gameObject.tag = "WallRunObstacle";

            MeshRenderer mr = wallPB.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = wallMat;

            ApplyProBuilderVertexColor(wallPB, wallMat.color);
            wallPB.ToMesh();
            wallPB.Refresh();

            BoxCollider bc = wallPB.gameObject.AddComponent<BoxCollider>();
            bc.size = size;
        }

        private static void BuildFinishGate(Transform parent, float trackX, float z, Material goalMat, Material postMat, int targetLevel = 1)
        {
            GameObject goalRoot = new GameObject($"FinishGoalGate_{trackX:F0}");
            goalRoot.transform.SetParent(parent);
            goalRoot.transform.position = new Vector3(trackX, 0f, z);

            // Overhead arch beam
            Vector3 beamSize = new Vector3(4.2f, 0.6f, 1.0f);
            Vector3 beamPos = new Vector3(trackX, 3.2f, z);

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
            Vector3 padPos = new Vector3(trackX, 0.025f, z);
            ProBuilderMesh padPB = ShapeGenerator.GenerateCube(PivotLocation.Center, padSize);
            padPB.transform.position = padPos;
            padPB.transform.SetParent(goalRoot.transform);
            padPB.gameObject.name = "GoalLine";

            MeshRenderer mrPad = padPB.GetComponent<MeshRenderer>();
            if (mrPad != null) mrPad.sharedMaterial = goalMat;
            ApplyProBuilderVertexColor(padPB, new Color(0.1f, 1.0f, 0.4f));
            padPB.ToMesh();
            padPB.Refresh();

            // Finish trigger collider (spatially constrained to this track)
            BoxCollider trigger = goalRoot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 2.0f, 0f);
            trigger.size = new Vector3(6.0f, 5.0f, 2.0f);
            goalRoot.tag = "Finish";

            Rigidbody rb = goalRoot.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            FinishLine fl = goalRoot.AddComponent<FinishLine>();
            fl.TargetLevelIndex = targetLevel;
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

        #region Actor & Camera Setup

        private static GameObject EnsurePlayer(Transform parent)
        {
            PlayerParkourController existing = Object.FindFirstObjectByType<PlayerParkourController>();
            GameObject playerGo;

            if (existing != null)
            {
                playerGo = existing.gameObject;
                playerGo.transform.SetParent(parent);
                existing.TeleportToStart(new Vector3(0f, 1.1f, 0f));
            }
            else
            {
                playerGo = new GameObject("Player");
                playerGo.transform.SetParent(parent);
                playerGo.transform.position = new Vector3(0f, 1.1f, 0f);
                playerGo.transform.rotation = Quaternion.identity;

                CharacterController cc = playerGo.AddComponent<CharacterController>();
                cc.height = 1.8f;
                cc.radius = 0.35f;
                cc.center = new Vector3(0f, 0.9f, 0f);
                cc.stepOffset = 0.3f;
                cc.slopeLimit = 45f;

                GameObject detector = new GameObject("VaultDetector");
                detector.transform.SetParent(playerGo.transform);
                detector.transform.localPosition = Vector3.zero;
                detector.transform.localRotation = Quaternion.identity;

                BoxCollider trigger = detector.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 0.9f, 1.3f);
                trigger.size = new Vector3(2.5f, 1.6f, 1.8f);
                detector.AddComponent<VaultDetector>();

                InstantiateDummyPrefab("HumanDummy_M Blue", playerGo.transform, new Color(0.2f, 0.6f, 1.0f));
                playerGo.AddComponent<PlayerParkourController>();
            }

            PlayerParkourController controller = playerGo.GetComponent<PlayerParkourController>();
            if (controller != null)
            {
                SerializedObject pSo = new SerializedObject(controller);
                var impProp = pSo.FindProperty("wallClimbImpulse");
                if (impProp != null) impProp.floatValue = 9.2f;
                var gravProp = pSo.FindProperty("wallClimbGravity");
                if (gravProp != null) gravProp.floatValue = -14.0f;
                var rangeProp = pSo.FindProperty("wallDetectionRange");
                if (rangeProp != null) rangeProp.floatValue = 2.85f;
                var finishZProp = pSo.FindProperty("finishZ");
                if (finishZProp != null) finishZProp.floatValue = 246.0f;
                var vrProp = pSo.FindProperty("visualRoot");
                Transform visual = playerGo.transform.Find("Visual");
                if (vrProp != null && visual != null) vrProp.objectReferenceValue = visual;
                pSo.ApplyModifiedProperties();
            }

            return playerGo;
        }

        private static GameObject EnsureChaser(Transform parent, Transform playerTransform)
        {
            ChaserAI existing = Object.FindFirstObjectByType<ChaserAI>();
            GameObject chaserGo;

            if (existing != null)
            {
                chaserGo = existing.gameObject;
                chaserGo.transform.SetParent(parent);
                existing.TeleportToStart(new Vector3(0f, 1.1f, -6.0f));
            }
            else
            {
                chaserGo = new GameObject("Chaser");
                chaserGo.transform.SetParent(parent);
                chaserGo.transform.position = new Vector3(0f, 1.1f, -6.0f);
                chaserGo.transform.rotation = Quaternion.identity;

                CharacterController cc = chaserGo.AddComponent<CharacterController>();
                cc.height = 1.8f;
                cc.radius = 0.35f;
                cc.center = new Vector3(0f, 0.9f, 0f);
                cc.stepOffset = 0.3f;
                cc.slopeLimit = 45f;

                InstantiateDummyPrefab("HumanDummy_M Red", chaserGo.transform, new Color(1.0f, 0.2f, 0.2f));
                chaserGo.AddComponent<ChaserAI>();
            }

            ChaserAI ai = chaserGo.GetComponent<ChaserAI>();
            if (ai != null)
            {
                SerializedObject cSo = new SerializedObject(ai);
                var targetProp = cSo.FindProperty("targetPlayer");
                if (targetProp != null) targetProp.objectReferenceValue = playerTransform;
                var vrProp = cSo.FindProperty("visualRoot");
                Transform visual = chaserGo.transform.Find("Visual");
                if (vrProp != null && visual != null) vrProp.objectReferenceValue = visual;
                cSo.ApplyModifiedProperties();
            }

            return chaserGo;
        }

        private static void EnsureCamera(Transform playerTransform, Transform chaserTransform)
        {
            Camera mainCam = Camera.main;
            GameObject camGo = mainCam != null ? mainCam.gameObject : GameObject.Find("Main Camera");

            if (camGo == null)
            {
                camGo = new GameObject("Main Camera");
                mainCam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
            }

            VectorFollowCamera followCam = camGo.GetComponent<VectorFollowCamera>() ?? camGo.AddComponent<VectorFollowCamera>();
            followCam.SetTargets(playerTransform, chaserTransform);
            followCam.SnapToTarget();
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
            return visualRoot;
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

        #region Core Systems & UI Canvas

        private static void SetupEventSystem()
        {
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
            GameObject esGo;
            if (eventSystem == null)
            {
                esGo = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
                esGo.AddComponent<EventSystem>();
            }
            else
            {
                esGo = eventSystem.gameObject;
            }

            StandaloneInputModule standalone = esGo.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                Object.DestroyImmediate(standalone);
            }

            InputSystemUIInputModule inputModule = esGo.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                esGo.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static void SetupGameManager(GameObject level1Root, GameObject level2Root, GameObject level3Root, GameObject level4Root)
        {
            GameManager gm = Object.FindFirstObjectByType<GameManager>();
            if (gm == null)
            {
                GameObject gmGo = new GameObject("GameManager");
                Undo.RegisterCreatedObjectUndo(gmGo, "Create GameManager");
                gm = gmGo.AddComponent<GameManager>();
            }

            SerializedObject so = new SerializedObject(gm);
            SerializedProperty p1 = so.FindProperty("_level1Root");
            if (p1 != null) p1.objectReferenceValue = level1Root;
            SerializedProperty p2 = so.FindProperty("_level2Root");
            if (p2 != null) p2.objectReferenceValue = level2Root;
            SerializedProperty p3 = so.FindProperty("_level3Root");
            if (p3 != null) p3.objectReferenceValue = level3Root;
            SerializedProperty p4 = so.FindProperty("_level4Root");
            if (p4 != null) p4.objectReferenceValue = level4Root;
            so.ApplyModifiedProperties();

            // Enforce default level isolation: Level 1 active, Level 2, 3, 4 inactive
            if (level1Root != null) level1Root.SetActive(true);
            if (level2Root != null) level2Root.SetActive(false);
            if (level3Root != null) level3Root.SetActive(false);
            if (level4Root != null) level4Root.SetActive(false);
        }

        private static void BuildUISystem()
        {
            Font builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtinFont == null)
            {
                builtinFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            GameObject canvasGo = GameObject.Find("UI_Canvas");
            if (canvasGo != null)
            {
                Undo.DestroyObjectImmediate(canvasGo);
            }

            canvasGo = new GameObject("UI_Canvas");
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create UI_Canvas");

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            UIManager uiManager = canvasGo.AddComponent<UIManager>();

            // 1. START SCREEN PANEL
            GameObject startPanel = new GameObject("StartScreenPanel", typeof(RectTransform), typeof(Image));
            startPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform startRect = startPanel.GetComponent<RectTransform>();
            startRect.anchorMin = Vector2.zero;
            startRect.anchorMax = Vector2.one;
            startRect.offsetMin = Vector2.zero;
            startRect.offsetMax = Vector2.zero;

            Image startImage = startPanel.GetComponent<Image>();
            Sprite bgSprite = GetOrCreateBackgroundSprite();
            if (bgSprite != null)
            {
                startImage.sprite = bgSprite;
                startImage.color = Color.white;
            }
            else
            {
                startImage.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);
            }

            // Left Menu Container Card (positioned over the left atmospheric region, leaving track artwork visible)
            GameObject leftCard = new GameObject("LeftMenuCard", typeof(RectTransform), typeof(Image));
            leftCard.transform.SetParent(startPanel.transform, false);
            RectTransform cardRect = leftCard.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0f, 0.5f);
            cardRect.anchorMax = new Vector2(0f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = new Vector2(360f, 0f);
            cardRect.sizeDelta = new Vector2(540f, 780f);

            Image cardImg = leftCard.GetComponent<Image>();
            cardImg.color = new Color(0.03f, 0.05f, 0.09f, 0.82f);

            // Glowing top accent bar
            CreateAccentBar("TopAccentBar", leftCard.transform, new Color(0.2f, 0.85f, 1f, 0.9f), new Vector2(0f, 370f), new Vector2(480f, 4f));

            // Title & Subtitle inside Left Card
            CreateText("TitleText", leftCard.transform, "VECTOR 3D", 64, FontStyle.Bold,
                Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 305f), new Vector2(480f, 80f), builtinFont);

            CreateText("SubtitleText", leftCard.transform, "ROOFTOP PURSUIT // OUTRUN THE ENFORCER", 17, FontStyle.Normal,
                new Color(0.5f, 0.82f, 1f, 0.95f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 255f), new Vector2(480f, 32f), builtinFont);

            // Active Sector indicator
            Text currentLevelLabel = CreateText("CurrentLevelLabel", leftCard.transform, "ACTIVE SECTOR: 01", 17, FontStyle.Bold,
                new Color(0.2f, 0.95f, 1f, 1f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 205f), new Vector2(380f, 32f), builtinFont);

            CreateAccentBar("Divider1", leftCard.transform, new Color(0.3f, 0.45f, 0.6f, 0.35f), new Vector2(0f, 170f), new Vector2(440f, 2f));

            // Main Menu Action Buttons inside Left Card
            Button playButton = CreateButton("PlayButton", leftCard.transform, "PLAY RUN", 26,
                new Color(0.12f, 0.55f, 0.95f, 1f), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), new Vector2(400f, 62f), builtinFont);

            Button levelSelectButton = CreateButton("LevelSelectButton", leftCard.transform, "SELECT LEVEL", 22,
                new Color(0.15f, 0.38f, 0.55f, 0.95f), new Color(0.9f, 0.95f, 1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 35f), new Vector2(400f, 56f), builtinFont);

            Button startQuitButton = CreateButton("QuitButton", leftCard.transform, "QUIT", 20,
                new Color(0.22f, 0.22f, 0.28f, 0.95f), new Color(0.85f, 0.85f, 0.85f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -35f), new Vector2(400f, 50f), builtinFont);

            CreateAccentBar("Divider2", leftCard.transform, new Color(0.3f, 0.45f, 0.6f, 0.35f), new Vector2(0f, -90f), new Vector2(440f, 2f));

            // Tactical Controls guide inside Left Card
            CreateText("ControlsHeader", leftCard.transform, "TACTICAL CONTROLS", 16, FontStyle.Bold,
                new Color(0.4f, 0.85f, 1f, 0.95f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(460f, 26f), builtinFont);

            CreateText("ControlsLine1", leftCard.transform, "[W / Space] Jump / Vault    |    [S] Slide / Dive", 15, FontStyle.Normal,
                new Color(0.88f, 0.92f, 0.98f, 0.95f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -155f), new Vector2(460f, 26f), builtinFont);

            CreateText("ControlsLine2", leftCard.transform, "[A] Left Wall-Run    |    [D] Right Wall-Run", 15, FontStyle.Normal,
                new Color(0.88f, 0.92f, 0.98f, 0.95f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(460f, 26f), builtinFont);

            CreateText("ControlsLine3", leftCard.transform, "[Shift / E] Speed Burst (1.4x Sprint)", 15, FontStyle.Normal,
                new Color(0.88f, 0.92f, 0.98f, 0.95f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -225f), new Vector2(460f, 26f), builtinFont);

            CreateText("ControlsLine4", leftCard.transform, "Auto-Run Active // Press A/D to lock onto glowing walls", 13, FontStyle.Italic,
                new Color(0.35f, 0.95f, 0.85f, 0.85f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -260f), new Vector2(460f, 24f), builtinFont);

            // Bottom accent bar
            CreateAccentBar("BottomAccentBar", leftCard.transform, new Color(0.2f, 0.85f, 1f, 0.5f), new Vector2(0f, -370f), new Vector2(480f, 3f));

            startPanel.SetActive(true);

            // 2. LEVEL SELECT PANEL (Popup Card 780x750)
            GameObject levelSelectPanel = new GameObject("LevelSelectPanel", typeof(RectTransform), typeof(Image));
            levelSelectPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform levelSelectRect = levelSelectPanel.GetComponent<RectTransform>();
            levelSelectRect.anchorMin = new Vector2(0.5f, 0.5f);
            levelSelectRect.anchorMax = new Vector2(0.5f, 0.5f);
            levelSelectRect.pivot = new Vector2(0.5f, 0.5f);
            levelSelectRect.anchoredPosition = Vector2.zero;
            levelSelectRect.sizeDelta = new Vector2(780f, 750f);

            Image levelSelectImage = levelSelectPanel.GetComponent<Image>();
            levelSelectImage.color = new Color(0.06f, 0.07f, 0.10f, 0.96f);

            CreateText("Header", levelSelectPanel.transform, "SELECT LEVEL", 38, FontStyle.Bold,
                Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 310f), new Vector2(600f, 50f), builtinFont);

            CreateText("SubHeader", levelSelectPanel.transform, "CHOOSE A ROOFTOP SECTOR TO DEPLOY", 16, FontStyle.Normal,
                new Color(0.4f, 0.8f, 1f, 0.85f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 275f), new Vector2(600f, 28f), builtinFont);

            // Level 1 Card Button
            Button level1Button = CreateLevelCard("Level1Button", levelSelectPanel.transform,
                "SECTOR 01: ROOFTOPS", "Classic Chase Circuit // Low/High Vaults & Slide Barriers",
                new Color(0.10f, 0.18f, 0.28f, 0.95f), new Color(0.25f, 0.9f, 1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(680f, 80f), builtinFont);

            // Level 2 Card Button
            Button level2Button = CreateLevelCard("Level2Button", levelSelectPanel.transform,
                "SECTOR 02: HIGHRISE", "Vertical Hazards // Gap Chasms & Cyan/Orange Wall-Runs",
                new Color(0.20f, 0.14f, 0.28f, 0.95f), new Color(1f, 0.65f, 0.25f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(680f, 80f), builtinFont);

            // Level 3 Card Button
            Button level3Button = CreateLevelCard("Level3Button", levelSelectPanel.transform,
                "SECTOR 03: VERTICAL ASCENT", "Alternate Wall-Jumps // Shift to Engage & A/D Climb",
                new Color(0.12f, 0.24f, 0.20f, 0.95f), new Color(0.2f, 1f, 0.6f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(680f, 80f), builtinFont);

            // Level 4 Card Button
            Button level4Button = CreateLevelCard("Level4Button", levelSelectPanel.transform,
                "SECTOR 04: THE FINAL RUN", "Full Gauntlet // Abyss Chasm, Opposing Walls & Final Pursuit",
                new Color(0.28f, 0.12f, 0.18f, 0.95f), new Color(1f, 0.35f, 0.45f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(680f, 80f), builtinFont);

            // Back Button
            Button levelSelectBackButton = CreateButton("BackButton", levelSelectPanel.transform, "< BACK TO MENU", 20,
                new Color(0.20f, 0.20f, 0.25f, 0.95f), new Color(0.85f, 0.85f, 0.85f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -180f), new Vector2(280f, 50f), builtinFont);

            levelSelectPanel.SetActive(false);

            // 3. END GAME PANEL (Popup Card 620x490)
            GameObject endPanel = new GameObject("EndScreenPanel", typeof(RectTransform), typeof(Image));
            endPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform endRect = endPanel.GetComponent<RectTransform>();
            endRect.anchorMin = new Vector2(0.5f, 0.5f);
            endRect.anchorMax = new Vector2(0.5f, 0.5f);
            endRect.pivot = new Vector2(0.5f, 0.5f);
            endRect.anchoredPosition = Vector2.zero;
            endRect.sizeDelta = new Vector2(620f, 490f);

            Image endImage = endPanel.GetComponent<Image>();
            endImage.color = new Color(0.08f, 0.08f, 0.12f, 0.96f);

            Text statusHeaderText = CreateText("StatusHeader", endPanel.transform, "CAUGHT BY HUNTER", 36, FontStyle.Bold,
                new Color(1f, 0.25f, 0.25f, 1f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 175f), new Vector2(560f, 65f), builtinFont);

            Text statusSubText = CreateText("StatusSub", endPanel.transform, "The enforcer terminated your rooftop run.", 17, FontStyle.Normal,
                new Color(0.7f, 0.72f, 0.78f, 0.85f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 125f), new Vector2(520f, 35f), builtinFont);

            // Button Container with VerticalLayoutGroup for dynamic stacking
            GameObject buttonContainer = new GameObject("ButtonsContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            buttonContainer.transform.SetParent(endPanel.transform, false);
            RectTransform containerRect = buttonContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(0f, -45f);
            containerRect.sizeDelta = new Vector2(340f, 270f);

            VerticalLayoutGroup vlg = buttonContainer.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = 10f;
            vlg.childControlWidth = false;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            // Next Level Button (shown only on Level 1 Win)
            Button nextLevelButton = CreateButton("NextLevelButton", buttonContainer.transform, "NEXT LEVEL", 24,
                new Color(0.1f, 0.8f, 0.4f, 1f), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 50f), builtinFont);
            nextLevelButton.gameObject.SetActive(false);

            // Select Level Button on End Game Screen (Available on finish line win and game over)
            Button endLevelSelectButton = CreateButton("EndLevelSelectButton", buttonContainer.transform, "SELECT LEVEL", 22,
                new Color(0.15f, 0.38f, 0.55f, 0.95f), new Color(0.9f, 0.95f, 1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 50f), builtinFont);

            // Restart Button
            Button restartButton = CreateButton("RestartButton", buttonContainer.transform, "RESTART RUN", 22,
                new Color(0.12f, 0.55f, 0.95f, 1f), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 50f), builtinFont);

            // End Quit Button
            Button endQuitButton = CreateButton("QuitButton", buttonContainer.transform, "QUIT", 20,
                new Color(0.2f, 0.2f, 0.25f, 0.95f), new Color(0.85f, 0.85f, 0.85f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 44f), builtinFont);

            endPanel.SetActive(false);

            // 4. WIRE UP UIMANAGER REFERENCES
            SerializedObject so = new SerializedObject(uiManager);
            so.FindProperty("_startScreenPanel").objectReferenceValue = startPanel;
            so.FindProperty("_endScreenPanel").objectReferenceValue = endPanel;
            so.FindProperty("_levelSelectPanel").objectReferenceValue = levelSelectPanel;
            so.FindProperty("_playButton").objectReferenceValue = playButton;
            so.FindProperty("_levelSelectButton").objectReferenceValue = levelSelectButton;
            so.FindProperty("_startQuitButton").objectReferenceValue = startQuitButton;
            so.FindProperty("_level1Button").objectReferenceValue = level1Button;
            so.FindProperty("_level2Button").objectReferenceValue = level2Button;
            so.FindProperty("_level3Button").objectReferenceValue = level3Button;
            so.FindProperty("_level4Button").objectReferenceValue = level4Button;
            so.FindProperty("_levelSelectBackButton").objectReferenceValue = levelSelectBackButton;
            so.FindProperty("_restartButton").objectReferenceValue = restartButton;
            so.FindProperty("_endQuitButton").objectReferenceValue = endQuitButton;

            SerializedProperty nextProp = so.FindProperty("_nextLevelButton");
            if (nextProp != null) nextProp.objectReferenceValue = nextLevelButton;

            SerializedProperty endLevelProp = so.FindProperty("_endLevelSelectButton");
            if (endLevelProp != null) endLevelProp.objectReferenceValue = endLevelSelectButton;

            SerializedProperty statusProp = so.FindProperty("_statusHeaderText");
            if (statusProp != null) statusProp.objectReferenceValue = statusHeaderText;

            SerializedProperty subProp = so.FindProperty("_statusSubText");
            if (subProp != null) subProp.objectReferenceValue = statusSubText;

            SerializedProperty levelLabelProp = so.FindProperty("_currentLevelLabel");
            if (levelLabelProp != null) levelLabelProp.objectReferenceValue = currentLevelLabel;

            AudioClip clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/sOUNDS/menu button click sound.mp3");
            if (clickClip == null) clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/menu button click sound.mp3");
            if (clickClip != null)
            {
                SerializedProperty clickProp = so.FindProperty("_buttonClickClip");
                if (clickProp != null) clickProp.objectReferenceValue = clickClip;
            }

            so.ApplyModifiedProperties();
        }

        private static void SetupAudioManager()
        {
            GameObject audioManagerGo = GameObject.Find("AudioManager");
            if (audioManagerGo == null)
            {
                audioManagerGo = new GameObject("AudioManager");
                Undo.RegisterCreatedObjectUndo(audioManagerGo, "Create AudioManager");
            }

            AudioManager audioManager = audioManagerGo.GetComponent<AudioManager>();
            if (audioManager == null)
            {
                audioManager = audioManagerGo.AddComponent<AudioManager>();
            }

            AudioSource[] sources = audioManagerGo.GetComponents<AudioSource>();
            AudioSource musicSource = sources.Length > 0 ? sources[0] : audioManagerGo.AddComponent<AudioSource>();
            AudioSource sfxSource = sources.Length > 1 ? sources[1] : audioManagerGo.AddComponent<AudioSource>();

            // Find Audio Clips
            AudioClip bgmClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/sOUNDS/Background music.mp3");
            if (bgmClip == null) bgmClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/Background music.mp3");
            if (bgmClip == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:AudioClip Background music");
                if (guids.Length > 0) bgmClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            AudioClip clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/sOUNDS/menu button click sound.mp3");
            if (clickClip == null) clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/menu button click sound.mp3");
            if (clickClip == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:AudioClip menu button click");
                if (guids.Length > 0) clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            AudioClip deathClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/sOUNDS/Death sound.mp3");
            if (deathClip == null) deathClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/Death sound.mp3");
            if (deathClip == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:AudioClip Death sound");
                if (guids.Length > 0) deathClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            AudioClip climbClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/sOUNDS/khoamthanh-kick-bright-medium-504170.mp3");
            if (climbClip == null) climbClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/khoamthanh-kick-bright-medium-504170.mp3");
            if (climbClip == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:AudioClip khoamthanh-kick");
                if (guids.Length > 0) climbClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            musicSource.playOnAwake = true;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f; // Pure 2D stereo
            musicSource.ignoreListenerPause = true;
            musicSource.volume = 0.5f;
            if (bgmClip != null)
            {
                musicSource.clip = bgmClip;
            }

            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f; // Pure 2D stereo
            sfxSource.ignoreListenerPause = true;
            sfxSource.volume = 0.85f;

            SerializedObject so = new SerializedObject(audioManager);
            so.FindProperty("_musicSource").objectReferenceValue = musicSource;
            so.FindProperty("_sfxSource").objectReferenceValue = sfxSource;
            if (bgmClip != null) so.FindProperty("_bgmClip").objectReferenceValue = bgmClip;
            if (deathClip != null) so.FindProperty("_deathClip").objectReferenceValue = deathClip;
            if (clickClip != null) so.FindProperty("_buttonClickClip").objectReferenceValue = clickClip;
            if (climbClip != null) so.FindProperty("_wallClimbClip").objectReferenceValue = climbClip;
            so.ApplyModifiedProperties();
        }

        private static Button CreateLevelCard(string name, Transform parent, string title, string subtitle,
            Color normalColor, Color titleColor, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Font font)
        {
            GameObject btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);

            RectTransform rect = btnGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            Image img = btnGo.GetComponent<Image>();
            img.color = normalColor;

            Button btn = btnGo.GetComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = normalColor * 1.35f;
            colors.pressedColor = normalColor * 0.75f;
            colors.selectedColor = normalColor;
            btn.colors = colors;

            // Title text (top left of card)
            CreateText("Title", btnGo.transform, title, 24, FontStyle.Bold, titleColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(25f, 16f), new Vector2(-160f, 36f), font);

            // Subtitle text (bottom left of card)
            CreateText("Subtitle", btnGo.transform, subtitle, 15, FontStyle.Normal, new Color(0.75f, 0.8f, 0.88f, 0.9f), TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(25f, -18f), new Vector2(-160f, 30f), font);

            // Deploy badge (right side of card)
            CreateText("DeployBadge", btnGo.transform, "[ PLAY ]", 18, FontStyle.Bold, titleColor, TextAnchor.MiddleRight,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(100f, 40f), font);

            return btn;
        }

        private static Text CreateText(string name, Transform parent, string content, int fontSize, FontStyle fontStyle,
            Color color, TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Font font)
        {
            GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(parent, false);

            RectTransform rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;

            if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
            {
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchoredPosition = anchoredPos;
                rect.sizeDelta = sizeDelta;
            }

            Text text = textGo.GetComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;

            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, int fontSize,
            Color normalColor, Color textColor, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Font font)
        {
            GameObject btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);

            RectTransform rect = btnGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            Image img = btnGo.GetComponent<Image>();
            img.color = normalColor;

            Button btn = btnGo.GetComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = normalColor * 1.25f;
            colors.pressedColor = normalColor * 0.75f;
            colors.selectedColor = normalColor;
            btn.colors = colors;

            LayoutElement le = btnGo.AddComponent<LayoutElement>();
            le.preferredWidth = sizeDelta.x;
            le.preferredHeight = sizeDelta.y;

            CreateText("Label", btnGo.transform, label, fontSize, FontStyle.Bold, textColor, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, font);

            return btn;
        }

        private static GameObject CreateAccentBar(string name, Transform parent, Color color, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject barGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            barGo.transform.SetParent(parent, false);

            RectTransform rect = barGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            Image img = barGo.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            return barGo;
        }

        private static Sprite GetOrCreateBackgroundSprite()
        {
            string path = "Assets/_Project/Textures/MainMenuBackground.png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                bool reimport = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    reimport = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    reimport = true;
                }
                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    reimport = true;
                }
                if (reimport)
                {
                    importer.SaveAndReimport();
                }
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        #endregion

        #region Helpers & Materials

        private static Material GetOrCreateMaterial(string name, Color color, float smoothness, Color emissionColor = default)
        {
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
        }

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
        #endregion
    }
}
