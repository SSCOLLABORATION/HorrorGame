using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using VectorParkour;
using Vector3D.Core;

namespace VectorParkour.EditorScripts
{
    public static class LevelPlaythroughTester
    {
        public class AttemptReport
        {
            public int attemptNumber;
            public int levelNumber;
            public string strategy;
            public bool completed;
            public float finalZ;
            public float minChaserDistance;
            public int stumbleCount;
            public List<string> stumbleLocations = new List<string>();
            public List<string> observations = new List<string>();
            public float runDuration;
        }

        public enum TestMode
        {
            FiveAttemptsLevel1,
            AllFourLevels
        }

        public static TestMode currentMode = TestMode.FiveAttemptsLevel1;
        public static int currentAttempt = 0;
        public static int currentTestingLevel = 1;
        private static float attemptStartTime = 0f;
        private static float minChaserDistInRun = 999f;
        private static int stumblesInRun = 0;
        private static List<string> stumbleLocsInRun = new List<string>();
        private static List<string> runObservations = new List<string>();
        public static List<AttemptReport> allReports = new List<AttemptReport>();
        public static bool isTestingActive = false;

        private static HashSet<int> handledObstacles = new HashSet<int>();

        [MenuItem("Tools/Vector3D/Run 5x Autonomous Playthrough Evaluation")]
        public static void Start5xPlaythroughs()
        {
            currentMode = TestMode.FiveAttemptsLevel1;
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.EnterPlaymode();
                EditorApplication.playModeStateChanged += OnPlayState;
                return;
            }

            BeginEvaluation();
        }

        [MenuItem("Tools/Vector3D/Run All Levels Playthrough (Levels 1 to 4)")]
        public static void StartAllLevelsPlaythrough()
        {
            currentMode = TestMode.AllFourLevels;
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.EnterPlaymode();
                EditorApplication.playModeStateChanged += OnPlayState;
                return;
            }

            BeginEvaluation();
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayState;
                EditorApplication.delayCall += BeginEvaluation;
            }
        }

        public static void BeginEvaluation()
        {
            allReports.Clear();
            currentAttempt = 0;
            currentTestingLevel = 1;
            isTestingActive = true;
            StartNextRun();
        }

        private static void StartNextRun()
        {
            currentAttempt++;

            if (currentMode == TestMode.FiveAttemptsLevel1)
            {
                if (currentAttempt > 5)
                {
                    FinishAllAttempts();
                    return;
                }
                currentTestingLevel = 1;
            }
            else // AllFourLevels
            {
                if (currentTestingLevel > 4)
                {
                    FinishAllAttempts();
                    return;
                }
            }

            attemptStartTime = Time.time;
            minChaserDistInRun = 999f;
            stumblesInRun = 0;
            stumbleLocsInRun.Clear();
            runObservations.Clear();
            handledObstacles.Clear();

            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.SelectLevel(currentTestingLevel, startPlaying: true);
            }

            EditorApplication.update -= OnTick;
            EditorApplication.update += OnTick;
            Debug.Log($"[PlaythroughTester] Started Level {currentTestingLevel} (Run #{currentAttempt})...");
        }

        private static void OnTick()
        {
            if (!isTestingActive || !EditorApplication.isPlaying)
            {
                EditorApplication.update -= OnTick;
                return;
            }

            var player = Object.FindFirstObjectByType<PlayerParkourController>();
            var chaser = Object.FindFirstObjectByType<ChaserAI>();
            var gm = GameManager.Instance;

            if (player == null || gm == null) return;

            float pZ = player.transform.position.z;

            if (chaser != null)
            {
                float dist = pZ - chaser.transform.position.z;
                if (dist < minChaserDistInRun) minChaserDistInRun = dist;
            }

            if (player.IsStumbling)
            {
                string loc = $"Z={pZ:F1}m";
                if (!stumbleLocsInRun.Contains(loc))
                {
                    stumblesInRun++;
                    stumbleLocsInRun.Add(loc);
                }
            }

            // Route actions based on active level
            if (currentMode == TestMode.FiveAttemptsLevel1)
            {
                ExecuteLevel1Actions(currentAttempt, player, pZ);
            }
            else
            {
                switch (currentTestingLevel)
                {
                    case 1: ExecuteLevel1Actions(5, player, pZ); break;
                    case 2: ExecuteLevel2Actions(player, pZ); break;
                    case 3: ExecuteLevel3Actions(player, pZ); break;
                    case 4: ExecuteLevel4Actions(player, pZ); break;
                }
            }

            // Check completion or failure
            if (player.HasCompletedLevel || gm.IsGameWon || pZ >= 246.5f)
            {
                RecordAttemptResult(true, pZ, $"Successfully completed Level {currentTestingLevel}!");

                if (currentMode == TestMode.AllFourLevels)
                {
                    currentTestingLevel++;
                    if (currentTestingLevel <= 4)
                    {
                        gm.StartNextLevel();
                    }
                }
                StartNextRun();
            }
            else if (gm.CurrentState == GameState.GameOver || player.transform.position.y < -4f)
            {
                RecordAttemptResult(false, pZ, $"Terminated at Z={pZ:F1}m: {(player.transform.position.y < -4f ? "Fell into pit" : "Caught by Hunter")}");
                
                if (currentMode == TestMode.AllFourLevels)
                {
                    // Retry this level
                    gm.RestartGame();
                }
                StartNextRun();
            }
        }

        #region Level 1 Actions
        private static void ExecuteLevel1Actions(int attempt, PlayerParkourController player, float pZ)
        {
            float jumpLead = 1.6f;
            float slideLead = 1.8f;
            float gapLead = 1.0f;

            if (attempt == 1) { jumpLead = 1.4f; slideLead = 1.6f; gapLead = 1.0f; }
            else if (attempt == 2) { jumpLead = 2.0f; slideLead = 2.0f; gapLead = 1.0f; }
            else if (attempt == 3) { jumpLead = 1.0f; slideLead = 1.2f; gapLead = 1.0f; }
            else if (attempt == 4)
            {
                jumpLead = 1.8f; slideLead = 1.8f; gapLead = 1.0f;
                if (!player.IsSprinting && pZ > 5f && pZ < 220f) player.InjectShiftInput();
            }
            else if (attempt == 5)
            {
                jumpLead = 1.5f; slideLead = 1.6f; gapLead = 1.0f;
                if (!player.IsSprinting && pZ > 210f) player.InjectShiftInput();
            }

            CheckTrigger(1, pZ, 14.0f - jumpLead, () => player.InjectJumpInput());
            CheckTrigger(2, pZ, 26.0f - slideLead, () => player.InjectSlideInput());
            CheckTrigger(3, pZ, 35.0f - gapLead, () => player.InjectJumpInput());
            CheckTrigger(4, pZ, 52.0f - jumpLead, () => player.InjectJumpInput());
            CheckTrigger(5, pZ, 68.0f - slideLead, () => player.InjectSlideInput());
            CheckTrigger(6, pZ, 80.0f - gapLead, () => player.InjectJumpInput());
            CheckTrigger(7, pZ, 98.0f - jumpLead, () => player.InjectJumpInput());
            CheckTrigger(8, pZ, 112.0f - slideLead, () => player.InjectSlideInput());
            CheckTrigger(9, pZ, 122.0f - jumpLead, () => player.InjectJumpInput());
            CheckTrigger(10, pZ, 130.0f - gapLead, () => player.InjectJumpInput());
            CheckTrigger(11, pZ, 146.0f - slideLead, () => player.InjectSlideInput());
            CheckTrigger(12, pZ, 158.0f - jumpLead, () => player.InjectJumpInput());
            CheckTrigger(13, pZ, 169.0f - slideLead, () => player.InjectSlideInput());
            CheckTrigger(14, pZ, 178.0f - gapLead, () => player.InjectJumpInput());
            CheckTrigger(15, pZ, 194.0f - jumpLead, () => player.InjectJumpInput());
            CheckTrigger(16, pZ, 206.0f - slideLead, () => player.InjectSlideInput());
        }
        #endregion

        #region Level 2 Actions
        private static void ExecuteLevel2Actions(PlayerParkourController player, float pZ)
        {
            // Vault Z=16
            CheckTrigger(201, pZ, 14.2f, () => player.InjectJumpInput());
            // Gap 1 Z=35
            CheckTrigger(202, pZ, 33.5f, () => player.InjectJumpInput());
            // Slide Z=54
            CheckTrigger(203, pZ, 52.2f, () => player.InjectSlideInput());

            // Wall Run Left 1 across gap 68..82 (Wall at X=98, Z=75)
            CheckTrigger(204, pZ, 66.8f, () => {
                player.InjectJumpInput();
                player.SetWallInput(true, false);
            });
            CheckTrigger(205, pZ, 81.2f, () => player.ClearWallInputOverride());

            // Slide Z=108
            CheckTrigger(206, pZ, 106.2f, () => player.InjectSlideInput());

            // Wall Run Right 2 across gap 124..139 (Wall at X=102, Z=131.5)
            CheckTrigger(207, pZ, 122.8f, () => {
                player.InjectJumpInput();
                player.SetWallInput(false, true);
            });
            CheckTrigger(208, pZ, 138.2f, () => player.ClearWallInputOverride());

            // Vault Z=166
            CheckTrigger(209, pZ, 164.2f, () => player.InjectJumpInput());

            // Wall Run Left 3 across gap 180..196 (Wall at X=98, Z=188)
            CheckTrigger(210, pZ, 178.8f, () => {
                player.InjectJumpInput();
                player.SetWallInput(true, false);
            });
            CheckTrigger(211, pZ, 195.2f, () => player.ClearWallInputOverride());

            // Slide Z=222
            CheckTrigger(212, pZ, 220.2f, () => player.InjectSlideInput());
            // Vault Z=236
            CheckTrigger(213, pZ, 234.2f, () => player.InjectJumpInput());
        }
        #endregion

        #region Level 3 Actions
        private static void ExecuteLevel3Actions(PlayerParkourController player, float pZ)
        {
            // Vault Z=16
            CheckTrigger(301, pZ, 14.2f, () => player.InjectJumpInput());
            // Gap 1 Z=35
            CheckTrigger(302, pZ, 33.5f, () => player.InjectJumpInput());
            // Slide Z=52
            CheckTrigger(303, pZ, 50.2f, () => player.InjectSlideInput());

            // Shaft Entry at Z=64 (Left wall X=198)
            CheckTrigger(304, pZ, 64.0f, () => {
                player.InjectJumpInput();
                player.SetWallInput(true, false);
            });
            // Alternating wall jump transfers inside shaft
            CheckTrigger(305, pZ, 69.0f, () => player.InjectDirectionalJump(1));
            CheckTrigger(306, pZ, 74.0f, () => player.InjectDirectionalJump(-1));
            CheckTrigger(307, pZ, 79.0f, () => player.InjectDirectionalJump(1));
            CheckTrigger(308, pZ, 84.0f, () => player.InjectDirectionalJump(-1));
            CheckTrigger(309, pZ, 89.0f, () => {
                player.InjectJumpInput();
                player.ClearWallInputOverride();
            });

            // On elevated platform Y=6
            CheckTrigger(310, pZ, 132.2f, () => player.InjectSlideInput());
            CheckTrigger(311, pZ, 152.2f, () => player.InjectJumpInput());

            // Drop down around Z=170 to Y=0 platform
            CheckTrigger(312, pZ, 200.2f, () => player.InjectSlideInput());
            CheckTrigger(313, pZ, 220.2f, () => player.InjectJumpInput());
        }
        #endregion

        #region Level 4 Actions
        private static void ExecuteLevel4Actions(PlayerParkourController player, float pZ)
        {
            // Vault Z=14
            CheckTrigger(401, pZ, 12.2f, () => player.InjectJumpInput());
            // Slide Z=24
            CheckTrigger(402, pZ, 22.2f, () => player.InjectSlideInput());
            // Gap 1 Z=32
            CheckTrigger(403, pZ, 30.5f, () => player.InjectJumpInput());
            // Vault Z=46
            CheckTrigger(404, pZ, 44.2f, () => player.InjectJumpInput());
            // Gap 2 Z=58
            CheckTrigger(405, pZ, 56.5f, () => player.InjectJumpInput());
            // Slide Z=68
            CheckTrigger(406, pZ, 66.2f, () => player.InjectSlideInput());
            // Vault Z=74
            CheckTrigger(407, pZ, 72.2f, () => player.InjectJumpInput());

            // Final Gauntlet Wall Runs (Z=80 to 120)
            CheckTrigger(408, pZ, 81.5f, () => {
                player.InjectJumpInput();
                player.SetWallInput(false, true);
            });
            CheckTrigger(409, pZ, 95.0f, () => player.InjectDirectionalJump(-1));
            CheckTrigger(410, pZ, 104.0f, () => player.InjectDirectionalJump(1));
            CheckTrigger(411, pZ, 114.0f, () => {
                player.InjectJumpInput();
                player.ClearWallInputOverride();
            });

            // Elevated Platform Y=6
            CheckTrigger(412, pZ, 144.2f, () => player.InjectSlideInput());
            CheckTrigger(413, pZ, 163.2f, () => player.InjectJumpInput());

            // Drop down to Y=0
            CheckTrigger(414, pZ, 203.2f, () => player.InjectSlideInput());
            CheckTrigger(415, pZ, 222.2f, () => player.InjectJumpInput());
        }
        #endregion

        private static void CheckTrigger(int id, float currentZ, float triggerZ, System.Action action)
        {
            if (!handledObstacles.Contains(id) && currentZ >= triggerZ && currentZ <= triggerZ + 2.5f)
            {
                handledObstacles.Add(id);
                action?.Invoke();
            }
        }

        private static void RecordAttemptResult(bool won, float finalZ, string note)
        {
            var rep = new AttemptReport();
            rep.attemptNumber = currentAttempt;
            rep.levelNumber = currentTestingLevel;
            rep.strategy = currentMode == TestMode.FiveAttemptsLevel1 ? (currentAttempt switch
            {
                1 => "Standard Human Reaction (~1.5m Lead)",
                2 => "Early Buffering (~2.2m Lead)",
                3 => "Late Reaction (~0.9m Lead)",
                4 => "Aggressive Sprinting (Burst Shift on Cooldown)",
                5 => "Precision Flow & Final Stretch Sprint",
                _ => "Standard"
            }) : $"All-Levels Playthrough - Level {currentTestingLevel}";
            rep.completed = won;
            rep.finalZ = finalZ;
            rep.minChaserDistance = minChaserDistInRun;
            rep.stumbleCount = stumblesInRun;
            rep.stumbleLocations = new List<string>(stumbleLocsInRun);
            rep.observations.Add(note);
            rep.runDuration = Time.time - attemptStartTime;

            allReports.Add(rep);
            Debug.Log($"[PlaythroughTester] Level {currentTestingLevel} (Run #{currentAttempt}) Result: Won={won}, FinalZ={finalZ:F1}m, MinChaserDist={minChaserDistInRun:F2}m, Stumbles={stumblesInRun}, Time={rep.runDuration:F1}s");
        }

        private static void FinishAllAttempts()
        {
            isTestingActive = false;
            EditorApplication.update -= OnTick;
            Debug.Log($"[PlaythroughTester] Testing Complete! Total runs logged: {allReports.Count}");
        }
    }
}
