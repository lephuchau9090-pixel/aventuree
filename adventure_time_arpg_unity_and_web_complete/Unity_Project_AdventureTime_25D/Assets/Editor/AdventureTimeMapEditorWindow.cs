using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace AdventureTimeARPG.EditorTools
{
    /// <summary>
    /// Unity Editor Map Studio (Window -> Adventure Time ARPG -> Map Studio).
    /// Replaces any web admin tool: developers design and edit maps strictly inside Unity Editor.
    /// Features:
    /// - Paint Floor, Corridor, Wall, Door, Entrance (Spawn), Exit (Portal), Monsters, Bosses,
    ///   Chests, Royal Chests, Events (Shrines), and Decorative Props.
    /// - 1-click Soul Knight Prequel inspired multi-room dungeon generator (rooms + corridors + branches + boss room).
    /// - Visual map preview with live BFS shortest-path overlay from Entrance to Exit.
    /// - Saves validated map data to both Unity Resources/StreamingAssets and the shared Web `/play` data file.
    /// </summary>
    public class AdventureTimeMapEditorWindow : EditorWindow
    {
        private CustomMapData currentMap;
        private Stack<string> undoStack = new Stack<string>();
        private Stack<string> redoStack = new Stack<string>();

        private int targetChapter = 1;
        private int targetStage = 1;
        private string activeTool = "floor";
        private int brushSize = 1;
        private int selectedPropCol = 0;
        private int selectedEnemyIdx = 0;
        private int selectedBossIdx = 0;
        private bool showBfsPathPreview = true;
        private Vector2 scrollPos;
        private string validationMessage = "Ready to validate.";
        private bool isMapValid = true;
        private List<Vector2Int> currentBfsPath = new List<Vector2Int>();

        private static readonly string[] StageLabels = {
            "Ải 1-1: Thảo Nguyên Nhà Cây (Ch.1)",
            "Ải 1-2: Đường Gạch Vàng & Trạm Gác (Ch.1)",
            "Ải 1-3: Đại Sảnh Bá Tước Chanh [Boss] (Ch.1)",
            "Ải 2-1: Rừng Đêm Huyết Nguyệt (Ch.2)",
            "Ải 2-2: Hành Lang Hộ Pháp Ma Cà Rồng (Ch.2)",
            "Ải 2-3: Ngai Vàng Bóng Đêm [Boss] (Ch.2)",
            "Ải 3-1: Cổng Hầm Ngục Xương (Ch.3)",
            "Ải 3-2: Tầng Hầm Pháp Sư Xương (Ch.3)",
            "Ải 3-3: Lõi Tuyệt Diệt Của The Lich [Boss] (Ch.3)",
            "Ải 4-1: Phế Tích Hố Bom & Sa Mạc (Ch.4)",
            "Ải 4-2: Dòng Sông Dung Nham (Ch.4)",
            "Ải 4-3: Nhà Ngục Pha Lê Vũ Trụ [Boss] (Ch.4)"
        };

        private static readonly string[] EnemyTypeKeys = {
            "candy_zombie", "lemon_guard", "me_mow", "gunter", "demon_cat",
            "skeleton", "ooze_zombie", "necromancer", "magic_man", "iceclops",
            "goliad", "empress", "ricardio"
        };

        private static readonly string[] BossTypeKeys = {
            "lemongrab", "hierophant", "vampire_king", "the_lich", "ice_king", "citadel_guardian"
        };

        [MenuItem("Window/Adventure Time ARPG/Map Studio (Thiết Kế Dungeon & Đồng Bộ Web)")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<AdventureTimeMapEditorWindow>("Map Studio");
            wnd.minSize = new Vector2(960, 620);
        }

        private void OnEnable()
        {
            if (currentMap == null)
            {
                LoadStageMapForEditing(targetChapter, targetStage);
            }
        }

        private string GetOverrideFilePath(int ch, int st)
        {
            string resDir = Path.Combine(Application.dataPath, "Resources/StageMapOverrides");
            Directory.CreateDirectory(resDir);
            return Path.Combine(resDir, $"stage_{ch}_{st}_override.json");
        }

        private string GetStreamingSharedFilePath()
        {
            string streamDir = Path.Combine(Application.dataPath, "StreamingAssets");
            Directory.CreateDirectory(streamDir);
            return Path.Combine(streamDir, "shared_game_data.json");
        }

        private void LoadStageMapForEditing(int ch, int st)
        {
            targetChapter = ch;
            targetStage = st;
            undoStack.Clear();
            redoStack.Clear();

            string overridePath = GetOverrideFilePath(ch, st);
            if (File.Exists(overridePath))
            {
                currentMap = JsonUtility.FromJson<CustomMapData>(File.ReadAllText(overridePath));
            }
            else
            {
                currentMap = CustomMapData.CreateSoulKnightPrequelTemplate(38, 26, ch, st);
            }
            ValidateCurrentMap();
            Repaint();
        }

        private void RecordUndo()
        {
            undoStack.Push(JsonUtility.ToJson(currentMap));
            redoStack.Clear();
        }

        private void PerformUndo()
        {
            if (undoStack.Count == 0) return;
            redoStack.Push(JsonUtility.ToJson(currentMap));
            currentMap = JsonUtility.FromJson<CustomMapData>(undoStack.Pop());
            ValidateCurrentMap();
            Repaint();
        }

        private void PerformRedo()
        {
            if (redoStack.Count == 0) return;
            undoStack.Push(JsonUtility.ToJson(currentMap));
            currentMap = JsonUtility.FromJson<CustomMapData>(redoStack.Pop());
            ValidateCurrentMap();
            Repaint();
        }

        private void ValidateCurrentMap()
        {
            isMapValid = MapPathValidator.ValidateMap(currentMap, out validationMessage, out currentBfsPath);
            currentMap.bfsVerified = isMapValid;
        }

        private void OnGUI()
        {
            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Map Studio chỉ dùng trong chế độ chỉnh sửa ngoài trận đấu (Unity Editor Mode).",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Undo", EditorStyles.toolbarButton, GUILayout.Width(55))) PerformUndo();
            if (GUILayout.Button("Redo", EditorStyles.toolbarButton, GUILayout.Width(55))) PerformRedo();
            if (GUILayout.Button("Sinh Map SKP Đa Phòng", EditorStyles.toolbarButton, GUILayout.Width(160)))
            {
                RecordUndo();
                currentMap = CustomMapData.CreateSoulKnightPrequelTemplate(currentMap.width, currentMap.height, targetChapter, targetStage);
                ValidateCurrentMap();
            }
            if (GUILayout.Button("Kiểm Tra Lối Đi BFS", EditorStyles.toolbarButton, GUILayout.Width(135))) ValidateCurrentMap();
            showBfsPathPreview = GUILayout.Toggle(showBfsPathPreview, "Xem Trước Đường Đi BFS", EditorStyles.toolbarButton, GUILayout.Width(160));
            if (GUILayout.Button("Lưu Map & Cập Nhật Dữ Liệu Chung", EditorStyles.toolbarButton, GUILayout.Width(220))) SaveAndSyncSharedMap();
            if (GUILayout.Button("Khôi Phục Map Mặc Định", EditorStyles.toolbarButton, GUILayout.Width(160))) RestoreOriginalStageMap();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(validationMessage, isMapValid ? MessageType.Info : MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            // Left Palette Panel
            EditorGUILayout.BeginVertical(GUILayout.Width(280));
            GUILayout.Label("1. Chọn Ải Cần Thiết Kế", EditorStyles.boldLabel);
            int curIdx = (targetChapter - 1) * 3 + (targetStage - 1);
            int nextIdx = EditorGUILayout.Popup("Màn chơi", curIdx, StageLabels);
            if (nextIdx != curIdx)
            {
                LoadStageMapForEditing((nextIdx / 3) + 1, (nextIdx % 3) + 1);
            }

            int newW = EditorGUILayout.IntSlider("Chiều rộng (W)", currentMap.width, 24, 44);
            int newH = EditorGUILayout.IntSlider("Chiều cao (H)", currentMap.height, 18, 30);
            if (newW != currentMap.width || newH != currentMap.height)
            {
                RecordUndo();
                currentMap.Resize(newW, newH);
                ValidateCurrentMap();
            }
            brushSize = EditorGUILayout.IntSlider("Cỡ cọ (Brush)", brushSize, 1, 3);

            GUILayout.Space(6);
            GUILayout.Label("2. Công Cụ Vẽ Địa Hình & Đối Tượng", EditorStyles.boldLabel);
            string[] tools = {
                "floor", "path", "wall", "door",
                "spawn", "portal", "enemy", "boss",
                "chest", "royal_chest", "shrine", "prop",
                "erase", "erase_entity"
            };
            string[] toolNames = {
                "Sàn Phòng (Floor)", "Hành Lang (Corridor)", "Tường 2.5D (Wall)", "Cửa Phòng (Door)",
                "Cửa Vào (Start Spawn)", "Cửa Ra (Exit Portal)", "Đặt Quái (Enemy)", "Đặt Boss (Boss)",
                "Rương Thường (Chest)", "Rương Kho Báu (Royal Chest)", "Sự Kiện Đài Cổ Ngữ (Shrine)", "Vật Trang Trí (Prop)",
                "Xóa Ô Địa Hình (Erase Tile)", "Xóa Thực Thể (Erase Entity)"
            };
            for (int i = 0; i < tools.Length; i++)
            {
                GUI.backgroundColor = (activeTool == tools[i]) ? Color.yellow : Color.white;
                if (GUILayout.Button(toolNames[i])) activeTool = tools[i];
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);
            GUILayout.Label("3. Tuỳ Chọn Quái / Boss / Vật Trang Trí", EditorStyles.boldLabel);
            selectedEnemyIdx = EditorGUILayout.Popup("Loại Quái", selectedEnemyIdx, EnemyTypeKeys);
            selectedBossIdx = EditorGUILayout.Popup("Loại Boss", selectedBossIdx, BossTypeKeys);
            selectedPropCol = EditorGUILayout.IntSlider("Mẫu Vật Trang Trí (0-5)", selectedPropCol, 0, 5);
            EditorGUILayout.EndVertical();

            // Right Interactive Preview & Painting Canvas
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            float cellPx = 22f;
            Rect gridRect = GUILayoutUtility.GetRect(currentMap.width * cellPx, currentMap.height * cellPx);
            Event e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && gridRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown) RecordUndo();
                int tx = Mathf.FloorToInt((e.mousePosition.x - gridRect.x) / cellPx);
                int ty = Mathf.FloorToInt((e.mousePosition.y - gridRect.y) / cellPx);
                string toolToUse = (e.button == 1) ? "erase_entity" : activeTool;
                currentMap.ApplyStudioBrush(
                    tx,
                    ty,
                    toolToUse,
                    brushSize,
                    selectedPropCol,
                    EnemyTypeKeys[selectedEnemyIdx],
                    BossTypeKeys[selectedBossIdx]);
                ValidateCurrentMap();
                e.Use();
                Repaint();
            }

            HashSet<Vector2Int> pathSet = new HashSet<Vector2Int>(currentBfsPath);

            for (int y = 0; y < currentMap.height; y++)
            {
                for (int x = 0; x < currentMap.width; x++)
                {
                    Rect r = new Rect(gridRect.x + x * cellPx, gridRect.y + y * cellPx, cellPx - 1, cellPx - 1);
                    int v = currentMap.GetCell(x, y);
                    Color c = v == 1 ? new Color(0.22f, 0.45f, 0.30f) :
                              v == 2 ? new Color(0.68f, 0.56f, 0.34f) :
                              v == 4 ? new Color(0.95f, 0.58f, 0.20f) :
                              v == 3 ? new Color(0.45f, 0.25f, 0.35f) : new Color(0.08f, 0.06f, 0.14f);

                    if (showBfsPathPreview && pathSet.Contains(new Vector2Int(x, y)))
                        c = Color.Lerp(c, new Color(0.0f, 0.95f, 1.0f), 0.38f);

                    if (currentMap.spawn.tx == x && currentMap.spawn.ty == y) c = Color.green;
                    else if (currentMap.portal.tx == x && currentMap.portal.ty == y) c = Color.cyan;
                    EditorGUI.DrawRect(r, c);
                }
            }

            // Draw Entity Overlays on Preview Canvas
            foreach (var p in currentMap.props)
            {
                Rect r = new Rect(gridRect.x + p.tx * cellPx + 4, gridRect.y + p.ty * cellPx + 4, cellPx - 9, cellPx - 9);
                EditorGUI.DrawRect(r, new Color(0.55f, 0.36f, 0.18f));
            }
            foreach (var ch in currentMap.chests)
            {
                Rect r = new Rect(gridRect.x + ch.tx * cellPx + 3, gridRect.y + ch.ty * cellPx + 3, cellPx - 7, cellPx - 7);
                EditorGUI.DrawRect(r, ch.isRoyal ? new Color(1.0f, 0.84f, 0.10f) : new Color(0.90f, 0.65f, 0.20f));
            }
            foreach (var sh in currentMap.shrines)
            {
                Rect r = new Rect(gridRect.x + sh.tx * cellPx + 3, gridRect.y + sh.ty * cellPx + 3, cellPx - 7, cellPx - 7);
                EditorGUI.DrawRect(r, new Color(0.0f, 0.95f, 1.0f));
            }
            foreach (var sp in currentMap.spawns)
            {
                Rect r = new Rect(gridRect.x + sp.tx * cellPx + 4, gridRect.y + sp.ty * cellPx + 4, cellPx - 9, cellPx - 9);
                EditorGUI.DrawRect(r, sp.isBoss ? new Color(1.0f, 0.15f, 0.55f) : new Color(1.0f, 0.28f, 0.28f));
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();
        }

        private void SaveAndSyncSharedMap()
        {
            ValidateCurrentMap();
            if (!isMapValid)
            {
                EditorUtility.DisplayDialog("Map Không Hợp Lệ", validationMessage, "Đã Hiểu");
                return;
            }
            currentMap.isCustomMap = true;
            currentMap.isStageOverride = true;
            currentMap.bfsVerified = true;

            string json = JsonUtility.ToJson(currentMap, true);
            string overridePath = GetOverrideFilePath(targetChapter, targetStage);
            File.WriteAllText(overridePath, json);

            // Also write a copy to StreamingAssets so Web & Unity builds share the exact same map override
            string streamDir = Path.Combine(Application.dataPath, "StreamingAssets/StageMapOverrides");
            Directory.CreateDirectory(streamDir);
            File.WriteAllText(Path.Combine(streamDir, $"stage_{targetChapter}_{targetStage}_override.json"), json);

            AssetDatabase.Refresh();
            Debug.Log($"[Map Studio] Saved & synced Stage {targetChapter}-{targetStage} map override ({currentBfsPath.Count} BFS path tiles).");
        }

        private void RestoreOriginalStageMap()
        {
            string overridePath = GetOverrideFilePath(targetChapter, targetStage);
            if (File.Exists(overridePath)) File.Delete(overridePath);
            currentMap = CustomMapData.CreateSoulKnightPrequelTemplate(38, 26, targetChapter, targetStage);
            ValidateCurrentMap();
            AssetDatabase.Refresh();
            Repaint();
        }
    }
}
