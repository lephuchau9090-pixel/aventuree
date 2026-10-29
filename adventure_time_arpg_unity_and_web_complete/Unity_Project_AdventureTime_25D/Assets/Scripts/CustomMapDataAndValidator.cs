using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdventureTimeARPG
{
    [Serializable]
    public struct GridCoord
    {
        public int tx;
        public int ty;
    }

    [Serializable]
    public class MapRoomEntry
    {
        public int id;
        public string role; // "spawn", "combat", "treasure", "event", "boss", "exit"
        public int x;
        public int y;
        public int w;
        public int h;
    }

    [Serializable]
    public class MapPropEntry
    {
        public int tx;
        public int ty;
        public int propCol;
        public bool tall25D = true;
        public bool solid = true;
    }

    [Serializable]
    public class MapChestEntry
    {
        public int tx;
        public int ty;
        public bool isRoyal;
    }

    [Serializable]
    public class MapShrineEntry
    {
        public int tx;
        public int ty;
        public string eventType = "shrine_heal_buff";
        public bool activated;
    }

    [Serializable]
    public class MapDoorEntry
    {
        public int tx;
        public int ty;
        public bool opened;
    }

    [Serializable]
    public class MapEnemySpawnEntry
    {
        public int tx;
        public int ty;
        public string typeKey;
        public bool isBossRoom;
        public bool isBoss;
        public bool isMiniBoss;
    }

    [Serializable]
    public class CustomMapData
    {
        public int seed = 999001;
        public bool isCustomMap = true;
        public bool isStageOverride = true;
        public string mapName = "Stage Map Studio";
        public int chapterId = 1;
        public int stageNum = 1;
        public int width = 38;
        public int height = 26;
        public bool bfsVerified = true;
        public GridCoord spawn;
        public GridCoord portal;
        public int[] flatGrid;
        public List<MapRoomEntry> rooms = new List<MapRoomEntry>();
        public List<MapPropEntry> props = new List<MapPropEntry>();
        public List<MapChestEntry> chests = new List<MapChestEntry>();
        public List<MapShrineEntry> shrines = new List<MapShrineEntry>();
        public List<MapDoorEntry> doors = new List<MapDoorEntry>();
        public List<MapEnemySpawnEntry> spawns = new List<MapEnemySpawnEntry>();

        public int GetCell(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return 0;
            if (flatGrid == null || flatGrid.Length != width * height) return 0;
            return flatGrid[y * width + x];
        }

        public void SetCell(int x, int y, int val)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            if (flatGrid == null || flatGrid.Length != width * height)
                flatGrid = new int[width * height];
            flatGrid[y * width + x] = val;
        }

        public void Resize(int newW, int newH)
        {
            int[] next = new int[newW * newH];
            for (int y = 0; y < Mathf.Min(height, newH); y++)
            {
                for (int x = 0; x < Mathf.Min(width, newW); x++)
                {
                    next[y * newW + x] = GetCell(x, y);
                }
            }
            width = newW;
            height = newH;
            flatGrid = next;
        }

        public static List<MapEnemySpawnEntry> GetDefaultStageEnemyRoster(int ch, int st)
        {
            string[] normalPool = ch == 1 ? (st == 1 ? new[] { "candy_zombie", "lemon_guard" } : new[] { "candy_zombie", "lemon_guard", "me_mow" }) :
                                  ch == 2 ? new[] { "gunter", "demon_cat" } :
                                  ch == 3 ? (st == 1 ? new[] { "skeleton", "ooze_zombie" } : new[] { "skeleton", "ooze_zombie", "necromancer" }) :
                                            new[] { "magic_man", "iceclops" };

            string miniBoss = (ch == 1 && st == 2) ? "goliad" :
                              (ch == 2 && st == 2) ? "empress" :
                              (ch == 3 && st == 2) ? "necromancer" :
                              (ch == 4 && st == 2) ? "ricardio" : null;

            string boss = (ch == 1 && st == 3) ? "lemongrab" :
                          (ch == 2 && st == 2) ? "hierophant" :
                          (ch == 2 && st == 3) ? "vampire_king" :
                          (ch == 3 && st == 3) ? "the_lich" :
                          (ch == 4 && st == 2) ? "ice_king" :
                          (ch == 4 && st == 3) ? "citadel_guardian" : null;

            int normalCount = 8 + st * 2;
            var list = new List<MapEnemySpawnEntry>();
            for (int i = 0; i < normalCount; i++)
            {
                list.Add(new MapEnemySpawnEntry
                {
                    typeKey = normalPool[i % normalPool.Length],
                    isBossRoom = (i >= normalCount - 2),
                    isBoss = false,
                    isMiniBoss = false
                });
            }
            if (!string.IsNullOrEmpty(miniBoss))
            {
                list.Add(new MapEnemySpawnEntry { typeKey = miniBoss, isBossRoom = true, isMiniBoss = true });
            }
            if (!string.IsNullOrEmpty(boss))
            {
                list.Add(new MapEnemySpawnEntry { typeKey = boss, isBossRoom = true, isBoss = true });
            }
            return list;
        }

        private void ClearEntitiesAt(int cx, int cy)
        {
            props.RemoveAll(p => p.tx == cx && p.ty == cy);
            chests.RemoveAll(c => c.tx == cx && c.ty == cy);
            shrines.RemoveAll(s => s.tx == cx && s.ty == cy);
            doors.RemoveAll(d => d.tx == cx && d.ty == cy);
            spawns.RemoveAll(sp => sp.tx == cx && sp.ty == cy);
        }

        /// <summary>
        /// Full Map Studio brush tool supporting floor, corridor, wall, entrance (spawn),
        /// exit (portal), doors, monsters, bosses, chests, events/shrines, and decorative props.
        /// </summary>
        public void ApplyStudioBrush(
            int cx,
            int cy,
            string tool,
            int brushSize,
            int propCol,
            string enemyTypeKey,
            string bossTypeKey)
        {
            if (cx < 0 || cx >= width || cy < 0 || cy >= height) return;

            if (tool == "spawn")
            {
                ClearEntitiesAt(cx, cy);
                SetCell(cx, cy, 1);
                spawn = new GridCoord { tx = cx, ty = cy };
                return;
            }
            if (tool == "portal")
            {
                ClearEntitiesAt(cx, cy);
                SetCell(cx, cy, 1);
                portal = new GridCoord { tx = cx, ty = cy };
                return;
            }
            if (tool == "door")
            {
                ClearEntitiesAt(cx, cy);
                SetCell(cx, cy, 4);
                doors.Add(new MapDoorEntry { tx = cx, ty = cy, opened = false });
                return;
            }
            if (tool == "prop")
            {
                ClearEntitiesAt(cx, cy);
                if (GetCell(cx, cy) == 0 || GetCell(cx, cy) == 3) SetCell(cx, cy, 1);
                props.Add(new MapPropEntry { tx = cx, ty = cy, propCol = propCol, tall25D = true, solid = true });
                return;
            }
            if (tool == "chest" || tool == "royal_chest")
            {
                ClearEntitiesAt(cx, cy);
                if (GetCell(cx, cy) == 0 || GetCell(cx, cy) == 3) SetCell(cx, cy, 1);
                chests.Add(new MapChestEntry { tx = cx, ty = cy, isRoyal = (tool == "royal_chest") });
                return;
            }
            if (tool == "shrine" || tool == "event")
            {
                ClearEntitiesAt(cx, cy);
                if (GetCell(cx, cy) == 0 || GetCell(cx, cy) == 3) SetCell(cx, cy, 1);
                shrines.Add(new MapShrineEntry { tx = cx, ty = cy, eventType = "shrine_heal_buff", activated = false });
                return;
            }
            if (tool == "enemy")
            {
                ClearEntitiesAt(cx, cy);
                if (GetCell(cx, cy) == 0 || GetCell(cx, cy) == 3) SetCell(cx, cy, 1);
                spawns.Add(new MapEnemySpawnEntry
                {
                    tx = cx,
                    ty = cy,
                    typeKey = string.IsNullOrEmpty(enemyTypeKey) ? "candy_zombie" : enemyTypeKey,
                    isBossRoom = false,
                    isBoss = false
                });
                return;
            }
            if (tool == "boss")
            {
                ClearEntitiesAt(cx, cy);
                if (GetCell(cx, cy) == 0 || GetCell(cx, cy) == 3) SetCell(cx, cy, 1);
                spawns.Add(new MapEnemySpawnEntry
                {
                    tx = cx,
                    ty = cy,
                    typeKey = string.IsNullOrEmpty(bossTypeKey) ? "the_lich" : bossTypeKey,
                    isBossRoom = true,
                    isBoss = true
                });
                return;
            }
            if (tool == "erase_entity")
            {
                ClearEntitiesAt(cx, cy);
                if (GetCell(cx, cy) == 4) SetCell(cx, cy, 2);
                return;
            }

            int v = tool == "floor" ? 1 : (tool == "path" ? 2 : (tool == "wall" ? 3 : 0));
            for (int dy = 0; dy < brushSize; dy++)
            {
                for (int dx = 0; dx < brushSize; dx++)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        if (v == 0 || v == 3) ClearEntitiesAt(nx, ny);
                        SetCell(nx, ny, v);
                    }
                }
            }
        }

        /// <summary>
        /// Generates a Soul Knight Prequel inspired multi-room dungeon:
        /// Entrance Room -> Central Combat Room -> Side Treasure Branch + Side Event Branch -> Boss/Exit Room,
        /// connected by corridors and doors.
        /// </summary>
        public static CustomMapData CreateSoulKnightPrequelTemplate(int w, int h, int chapter, int stage)
        {
            var m = new CustomMapData
            {
                seed = 900000 + chapter * 100 + stage,
                isCustomMap = true,
                isStageOverride = true,
                mapName = $"Chương {chapter} - Ải {stage} (SKP Multi-Room Dungeon)",
                chapterId = chapter,
                stageNum = stage,
                width = w,
                height = h,
                flatGrid = new int[w * h]
            };

            int midY = h / 2;
            m.rooms = new List<MapRoomEntry>
            {
                new MapRoomEntry { id = 0, role = "spawn",    x = 2,      y = midY - 3, w = 6, h = 6 },
                new MapRoomEntry { id = 1, role = "combat",   x = 11,     y = midY - 4, w = 8, h = 8 },
                new MapRoomEntry { id = 2, role = "treasure", x = 12,     y = 2,        w = 6, h = 5 },
                new MapRoomEntry { id = 3, role = "event",    x = 18,     y = h - 7,    w = 6, h = 5 },
                new MapRoomEntry { id = 4, role = "boss",     x = w - 10, y = midY - 4, w = 8, h = 8 }
            };

            foreach (var rm in m.rooms)
            {
                for (int y = rm.y; y < Mathf.Min(h - 1, rm.y + rm.h); y++)
                {
                    for (int x = rm.x; x < Mathf.Min(w - 1, rm.x + rm.w); x++)
                    {
                        m.SetCell(x, y, 1);
                    }
                }
            }

            void CarveCorridor(MapRoomEntry a, MapRoomEntry b, bool placeDoor)
            {
                int cx = a.x + a.w / 2;
                int cy = a.y + a.h / 2;
                int bx = b.x + b.w / 2;
                int by = b.y + b.h / 2;
                bool doorAdded = false;
                while (cx != bx)
                {
                    for (int d = -1; d <= 1; d++)
                    {
                        if (cy + d > 1 && cy + d < h - 1 && m.GetCell(cx, cy + d) == 0)
                            m.SetCell(cx, cy + d, 2);
                    }
                    if (placeDoor && !doorAdded && Mathf.Abs(cx - bx) == 2)
                    {
                        m.SetCell(cx, cy, 4);
                        m.doors.Add(new MapDoorEntry { tx = cx, ty = cy, opened = false });
                        doorAdded = true;
                    }
                    cx += (bx >= cx) ? 1 : -1;
                }
                while (cy != by)
                {
                    for (int d = -1; d <= 1; d++)
                    {
                        if (cx + d > 1 && cx + d < w - 1 && m.GetCell(cx + d, cy) == 0)
                            m.SetCell(cx + d, cy, 2);
                    }
                    cy += (by >= cy) ? 1 : -1;
                }
            }

            CarveCorridor(m.rooms[0], m.rooms[1], true);
            CarveCorridor(m.rooms[1], m.rooms[2], true);
            CarveCorridor(m.rooms[1], m.rooms[3], false);
            CarveCorridor(m.rooms[1], m.rooms[4], true);
            CarveCorridor(m.rooms[3], m.rooms[4], false);

            m.spawn = new GridCoord { tx = m.rooms[0].x + m.rooms[0].w / 2, ty = m.rooms[0].y + m.rooms[0].h / 2 };
            m.portal = new GridCoord { tx = m.rooms[4].x + m.rooms[4].w / 2 + 1, ty = m.rooms[4].y + m.rooms[4].h / 2 };

            m.props.Add(new MapPropEntry { tx = m.rooms[0].x + 1, ty = m.rooms[0].y + 1, propCol = 0, solid = true });
            m.props.Add(new MapPropEntry { tx = m.rooms[1].x + 1, ty = m.rooms[1].y + 1, propCol = 1, solid = true });
            m.props.Add(new MapPropEntry { tx = m.rooms[1].x + m.rooms[1].w - 2, ty = m.rooms[1].y + m.rooms[1].h - 2, propCol = 2, solid = true });
            m.props.Add(new MapPropEntry { tx = m.rooms[4].x + 1, ty = m.rooms[4].y + 1, propCol = 4, solid = true });

            m.chests.Add(new MapChestEntry { tx = m.rooms[2].x + m.rooms[2].w / 2, ty = m.rooms[2].y + m.rooms[2].h / 2, isRoyal = true });
            m.chests.Add(new MapChestEntry { tx = m.rooms[1].x + m.rooms[1].w - 2, ty = m.rooms[1].y + 1, isRoyal = false });
            m.shrines.Add(new MapShrineEntry { tx = m.rooms[3].x + m.rooms[3].w / 2, ty = m.rooms[3].y + m.rooms[3].h / 2 });

            var defaultRoster = GetDefaultStageEnemyRoster(chapter, stage);
            int idx = 0;
            foreach (var unit in defaultRoster)
            {
                MapRoomEntry targetRm = unit.isBoss || unit.isMiniBoss || unit.isBossRoom
                    ? m.rooms[4]
                    : (idx % 3 == 0 ? m.rooms[2] : (idx % 3 == 1 ? m.rooms[3] : m.rooms[1]));

                int ex = Mathf.Clamp(targetRm.x + 1 + (idx * 2) % Mathf.Max(2, targetRm.w - 2), 1, w - 2);
                int ey = Mathf.Clamp(targetRm.y + 1 + (idx * 3) % Mathf.Max(2, targetRm.h - 2), 1, h - 2);
                if (ex == m.portal.tx && ey == m.portal.ty) ex = Mathf.Max(targetRm.x + 1, ex - 2);

                m.SetCell(ex, ey, 1);
                m.spawns.Add(new MapEnemySpawnEntry
                {
                    tx = ex,
                    ty = ey,
                    typeKey = unit.typeKey,
                    isBossRoom = unit.isBossRoom,
                    isBoss = unit.isBoss,
                    isMiniBoss = unit.isMiniBoss
                });
                idx++;
            }

            return m;
        }
    }

    public static class MapPathValidator
    {
        public static bool ValidateMap(CustomMapData map, out string report, out List<Vector2Int> shortestPath)
        {
            shortestPath = new List<Vector2Int>();
            if (map == null)
            {
                report = "Map data is null.";
                return false;
            }
            if (map.spawns.Count == 0 && map.chests.Count == 0)
            {
                report = "Map cần có ít nhất 1 Quái/Boss hoặc 1 Rương kho báu!";
                return false;
            }

            HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();
            foreach (var p in map.props)
            {
                if (p.solid) blocked.Add(new Vector2Int(p.tx, p.ty));
            }

            bool IsWalkable(int x, int y)
            {
                if (x < 0 || x >= map.width || y < 0 || y >= map.height) return false;
                if (blocked.Contains(new Vector2Int(x, y))) return false;
                int c = map.GetCell(x, y);
                return c == 1 || c == 2 || c == 4;
            }

            Vector2Int start = new Vector2Int(map.spawn.tx, map.spawn.ty);
            Vector2Int end = new Vector2Int(map.portal.tx, map.portal.ty);
            if (!IsWalkable(start.x, start.y))
            {
                report = $"Cửa vào / Spawn ({start.x},{start.y}) đang đặt trên tường hoặc bị vật cản chặn!";
                return false;
            }
            if (!IsWalkable(end.x, end.y))
            {
                report = $"Cửa ra / Portal ({end.x},{end.y}) đang đặt trên tường hoặc bị vật cản chặn!";
                return false;
            }
            if (start == end)
            {
                report = "Cửa vào và Cửa ra không được trùng nhau!";
                return false;
            }

            HashSet<Vector2Int> visited = new HashSet<Vector2Int> { start };
            Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int>();
            Queue<Vector2Int> q = new Queue<Vector2Int>();
            q.Enqueue(start);
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var d in dirs)
                {
                    var nxt = cur + d;
                    if (!visited.Contains(nxt) && IsWalkable(nxt.x, nxt.y))
                    {
                        visited.Add(nxt);
                        parent[nxt] = cur;
                        q.Enqueue(nxt);
                    }
                }
            }

            if (!visited.Contains(end))
            {
                report = $"Không có lối đi hợp lệ (BFS) từ Cửa vào ({start.x},{start.y}) tới Cửa ra ({end.x},{end.y})!";
                return false;
            }

            Vector2Int trace = end;
            while (true)
            {
                shortestPath.Add(trace);
                if (trace == start || !parent.ContainsKey(trace)) break;
                trace = parent[trace];
            }
            shortestPath.Reverse();

            foreach (var sp in map.spawns)
            {
                var pos = new Vector2Int(sp.tx, sp.ty);
                if (!visited.Contains(pos))
                {
                    report = $"Quái/Boss ({sp.typeKey}) tại ({sp.tx},{sp.ty}) bị cô lập, không thể tiếp cận!";
                    return false;
                }
            }
            foreach (var ch in map.chests)
            {
                var pos = new Vector2Int(ch.tx, ch.ty);
                if (!visited.Contains(pos))
                {
                    report = $"Rương tại ({ch.tx},{ch.ty}) bị cô lập, không có lối đi tới!";
                    return false;
                }
            }
            foreach (var sh in map.shrines)
            {
                var pos = new Vector2Int(sh.tx, sh.ty);
                if (!visited.Contains(pos))
                {
                    report = $"Sự kiện Đài Cổ Ngữ tại ({sh.tx},{sh.ty}) bị cô lập, không có lối đi tới!";
                    return false;
                }
            }

            report = $"BFS Hợp Lệ: Lối đi tới cửa ra dài {shortestPath.Count} ô ({visited.Count} ô sàn, {map.spawns.Count} Quái/Boss, {map.chests.Count} Rương, {map.shrines.Count} Sự kiện).";
            return true;
        }
    }
}
