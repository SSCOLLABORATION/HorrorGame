using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Vector3D.Core;
using Vector3D.UI;

namespace Vector3D.EditorScripts
{
    /// <summary>
    /// Editor script to automatically generate and wire the complete UI and Game State overlay system.
    /// Executed via 'Tools/Vector3D/Build Complete Scene'.
    /// </summary>
    public static class ParkourSceneGenerator
    {
        [MenuItem("Tools/Vector3D/Build Complete Scene")]
        public static void BuildCompleteScene()
        {
            // 1. Ensure EventSystem with InputSystemUIInputModule (Strict New Input System compliance)
            SetupEventSystem();

            // 2. Ensure GameManager
            SetupGameManager();

            // 3. Build Full Single-Scene UI Canvas Overlay System
            BuildUISystem();

            // 4. Mark Dirty and Save Scene
            var activeScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);

            Debug.Log("<color=green>[Vector3D]</color> Complete Scene and UI System built and wired successfully!");
        }

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

            // CRITICAL: StandaloneInputModule MUST NOT be present with New Input System
            StandaloneInputModule standalone = esGo.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                Object.DestroyImmediate(standalone);
            }

            // Ensure InputSystemUIInputModule is attached
            InputSystemUIInputModule inputModule = esGo.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                esGo.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static void SetupGameManager()
        {
            GameManager gm = Object.FindFirstObjectByType<GameManager>();
            if (gm == null)
            {
                GameObject gmGo = new GameObject("GameManager");
                Undo.RegisterCreatedObjectUndo(gmGo, "Create GameManager");
                gmGo.AddComponent<GameManager>();
            }
        }

        private static void BuildUISystem()
        {
            // Built-in standard font (No external TMP asset dependencies)
            Font builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtinFont == null)
            {
                builtinFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            // Recreate or clean UI Canvas
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

            // ==========================================
            // 1. START SCREEN PANEL
            // ==========================================
            GameObject startPanel = new GameObject("StartScreenPanel", typeof(RectTransform), typeof(Image));
            startPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform startRect = startPanel.GetComponent<RectTransform>();
            startRect.anchorMin = Vector2.zero;
            startRect.anchorMax = Vector2.one;
            startRect.offsetMin = Vector2.zero;
            startRect.offsetMax = Vector2.zero;

            Image startImage = startPanel.GetComponent<Image>();
            startImage.color = new Color(0.05f, 0.05f, 0.08f, 0.85f); // Sleek dark overlay

            // Title Text
            CreateText("TitleText", startPanel.transform, "VECTOR 3D", 82, FontStyle.Bold,
                Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(900f, 120f), builtinFont);

            // Subtitle Text
            CreateText("SubtitleText", startPanel.transform, "ROOFTOP PURSUIT // OUTRUN THE ENFORCER", 22, FontStyle.Normal,
                new Color(0.6f, 0.8f, 1f, 0.9f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 75f), new Vector2(800f, 40f), builtinFont);

            // Controls Hint
            CreateText("ControlsHint", startPanel.transform, "[W / UP] Jump & Vault   |   [S / DOWN] Slide   |   [D / RIGHT] Sprint", 18, FontStyle.Normal,
                new Color(0.7f, 0.75f, 0.8f, 0.8f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(800f, 35f), builtinFont);

            // Play Button
            Button playButton = CreateButton("PlayButton", startPanel.transform, "PLAY RUN", 28,
                new Color(0.12f, 0.55f, 0.95f, 1f), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(300f, 65f), builtinFont);

            // Start Quit Button
            Button startQuitButton = CreateButton("QuitButton", startPanel.transform, "QUIT", 22,
                new Color(0.2f, 0.2f, 0.25f, 0.95f), new Color(0.85f, 0.85f, 0.85f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(300f, 55f), builtinFont);

            startPanel.SetActive(true);

            // ==========================================
            // 2. END GAME PANEL (Popup Card 600x400)
            // ==========================================
            GameObject endPanel = new GameObject("EndScreenPanel", typeof(RectTransform), typeof(Image));
            endPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform endRect = endPanel.GetComponent<RectTransform>();
            endRect.anchorMin = new Vector2(0.5f, 0.5f);
            endRect.anchorMax = new Vector2(0.5f, 0.5f);
            endRect.pivot = new Vector2(0.5f, 0.5f);
            endRect.anchoredPosition = Vector2.zero;
            endRect.sizeDelta = new Vector2(600f, 400f);

            Image endImage = endPanel.GetComponent<Image>();
            endImage.color = new Color(0.08f, 0.08f, 0.12f, 0.96f);

            // Status Header
            Text statusHeaderText = CreateText("StatusHeader", endPanel.transform, "CAUGHT BY HUNTER", 38, FontStyle.Bold,
                new Color(1f, 0.25f, 0.25f, 1f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), new Vector2(560f, 70f), builtinFont);

            // Status Subtitle
            CreateText("StatusSub", endPanel.transform, "The enforcer terminated your rooftop run.", 18, FontStyle.Normal,
                new Color(0.7f, 0.72f, 0.78f, 0.85f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 55f), new Vector2(500f, 40f), builtinFont);

            // Restart Button
            Button restartButton = CreateButton("RestartButton", endPanel.transform, "RESTART RUN", 26,
                new Color(0.12f, 0.55f, 0.95f, 1f), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -25f), new Vector2(280f, 60f), builtinFont);

            // End Quit Button
            Button endQuitButton = CreateButton("QuitButton", endPanel.transform, "QUIT", 22,
                new Color(0.2f, 0.2f, 0.25f, 0.95f), new Color(0.85f, 0.85f, 0.85f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(280f, 55f), builtinFont);

            endPanel.SetActive(false);

            // ==========================================
            // 3. WIRE UP UIMANAGER REFERENCES
            // ==========================================
            SerializedObject so = new SerializedObject(uiManager);
            so.FindProperty("_startScreenPanel").objectReferenceValue = startPanel;
            so.FindProperty("_endScreenPanel").objectReferenceValue = endPanel;
            so.FindProperty("_playButton").objectReferenceValue = playButton;
            so.FindProperty("_startQuitButton").objectReferenceValue = startQuitButton;
            so.FindProperty("_restartButton").objectReferenceValue = restartButton;
            so.FindProperty("_endQuitButton").objectReferenceValue = endQuitButton;

            SerializedProperty statusProp = so.FindProperty("_statusHeaderText");
            if (statusProp != null)
            {
                statusProp.objectReferenceValue = statusHeaderText;
            }
            so.ApplyModifiedProperties();
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

            // Child Text Label stretching across the button
            CreateText("Label", btnGo.transform, label, fontSize, FontStyle.Bold, textColor, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, font);

            return btn;
        }
    }
}
