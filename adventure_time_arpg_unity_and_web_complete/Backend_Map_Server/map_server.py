#!/usr/bin/env python3
"""
map_server.py
Read-Only Player Server & Unity Shared Data Synchronizer for Adventure Time: Realms of Ooo (2D Pixel ARPG).

Design Principles:
1. Player-Only Web Portal (`/play`):
   - Serves the playable browser version at `/play` (and redirects `/` -> `/play`).
   - Exposes NO web admin UI, NO content-modification POST/PUT/DELETE endpoints, and embeds
     NO passwords or secret keys in browser or server code.
2. Shared Content Between Unity Editor (Map Studio) & Web (`/play`):
   - Developers design and validate maps inside Unity Editor using Map Studio
     (`Window -> Adventure Time ARPG -> Map Studio`), which saves stage map overrides to:
     `Unity_Project_AdventureTime_25D/Assets/Resources/StageMapOverrides/stage_<ch>_<st>_override.json`
     and `Unity_Project_AdventureTime_25D/Assets/StreamingAssets/StageMapOverrides/`.
   - Running `python3 Backend_Map_Server/map_server.py --sync-from-unity` (or starting the server)
     automatically loads all validated Unity Map Studio overrides, updates `shared_game_data.json`
     and `Unity_Project_AdventureTime_25D/Assets/StreamingAssets/shared_game_data.json`, and serves
     them to both Unity Play Mode and Web `/play`.
3. Soul Knight Prequel Multi-Room Dungeon Layout & BFS Path Validation:
   - Multi-room dungeon structure: Entrance Room -> Central Combat Room -> Side Treasure Room
     + Side Event Shrine Room -> Boss/Exit Room, connected by corridors and doors.
   - Verifies via BFS that every map has a walkable path from Entrance (`spawn`) to Exit (`portal`)
     and all monsters, bosses, chests, and events are reachable.
"""

import os
import sys
import glob
import json
import copy
import time
import random
from collections import deque
from http.server import HTTPServer, ThreadingHTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.abspath(os.path.join(BASE_DIR, ".."))
HISTORY_FILE = os.path.join(BASE_DIR, "seed_history.json")
SHARED_DATA_FILE = os.path.join(BASE_DIR, "shared_game_data.json")
UNITY_STREAMING_DIR = os.path.join(PROJECT_ROOT, "Unity_Project_AdventureTime_25D", "Assets", "StreamingAssets")
UNITY_OVERRIDES_DIR = os.path.join(PROJECT_ROOT, "Unity_Project_AdventureTime_25D", "Assets", "Resources", "StageMapOverrides")
UNITY_STREAM_OVERRIDES_DIR = os.path.join(UNITY_STREAMING_DIR, "StageMapOverrides")
WEB_HTML_FILE = os.path.join(PROJECT_ROOT, "Web_HTML5_25D_ARPG", "adventure-time-arpg-25d.html")


def convert_unity_flat_grid_if_needed(map_obj):
    """Converts Unity's 1D `flatGrid` (JsonUtility format) to 2D `grid` if needed."""
    if not isinstance(map_obj, dict):
        return map_obj
    W = int(map_obj.get("width", 38))
    H = int(map_obj.get("height", 26))
    if "grid" not in map_obj and isinstance(map_obj.get("flatGrid"), list):
        flat = map_obj["flatGrid"]
        if len(flat) == W * H:
            map_obj["grid"] = [flat[y * W:(y + 1) * W] for y in range(H)]
    return map_obj


def validate_map_bfs(map_obj):
    """
    Breadth-First Search (BFS) & collision validator for dungeon maps.
    Verifies a continuous walkable path from Entrance (`spawn`) to Exit (`portal`)
    and ensures all chests, shrines/events, and monster/boss spawns are reachable.
    """
    issues = []
    unreachable_points = []

    if not isinstance(map_obj, dict):
        return {"valid": False, "bfsVerified": False, "issues": ["Dữ liệu bản đồ không hợp lệ!"], "unreachablePoints": [], "pathCells": []}

    map_obj = convert_unity_flat_grid_if_needed(map_obj)
    W = int(map_obj.get("width", 0))
    H = int(map_obj.get("height", 0))
    grid = map_obj.get("grid")

    if W < 12 or H < 12 or not isinstance(grid, list) or len(grid) != H:
        return {"valid": False, "bfsVerified": False, "issues": ["Kích thước lưới bản đồ không hợp lệ!"], "unreachablePoints": [], "pathCells": []}

    spawn = map_obj.get("spawn") or {}
    portal = map_obj.get("portal") or {}
    if "tx" not in spawn or "ty" not in spawn:
        issues.append("Thiếu Cửa Vào (Player Spawn)!")
    if "tx" not in portal or "ty" not in portal:
        issues.append("Thiếu Cửa Ra (Exit Portal)!")
    if issues:
        return {"valid": False, "bfsVerified": False, "issues": issues, "unreachablePoints": unreachable_points, "pathCells": []}

    sx, sy = int(spawn["tx"]), int(spawn["ty"])
    px, py = int(portal["tx"]), int(portal["ty"])

    solid_props = set()
    for p in map_obj.get("props") or []:
        if p.get("solid", True):
            solid_props.add((int(p.get("tx", -1)), int(p.get("ty", -1))))

    def is_passable(x, y):
        if x < 0 or x >= W or y < 0 or y >= H:
            return False
        cell = grid[y][x]
        if cell not in (1, 2, 4):
            return False
        if (x, y) in solid_props:
            return False
        return True

    if not is_passable(sx, sy):
        issues.append(f"Cửa Vào ({sx},{sy}) đang đặt trên Tường hoặc bị Vật cản đè lên!")
        unreachable_points.append({"tx": sx, "ty": sy, "label": "Start bị chặn"})

    if not is_passable(px, py):
        issues.append(f"Cửa Ra ({px},{py}) đang đặt trên Tường hoặc bị Vật cản đè lên!")
        unreachable_points.append({"tx": px, "ty": py, "label": "Exit bị chặn"})

    if sx == px and sy == py:
        issues.append("Cửa Vào và Cửa Ra không được trùng nhau!")

    spawns = map_obj.get("spawns") or []
    chests = map_obj.get("chests") or []
    shrines = map_obj.get("shrines") or []

    if len(spawns) == 0 and len(chests) == 0:
        issues.append("Bản đồ cần có ít nhất 1 Quái/Boss hoặc 1 Rương mục tiêu!")

    visited = set()
    parent = {}
    q = deque()

    if is_passable(sx, sy):
        visited.add((sx, sy))
        q.append((sx, sy))

    while q:
        cx, cy = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = cx + dx, cy + dy
            if (nx, ny) not in visited and is_passable(nx, ny):
                visited.add((nx, ny))
                parent[(nx, ny)] = (cx, cy)
                q.append((nx, ny))

    path_cells = []
    if (px, py) not in visited:
        issues.append(f"Không có đường đi hợp lệ (BFS) từ Cửa Vào ({sx},{sy}) tới Cửa Ra ({px},{py})!")
        unreachable_points.append({"tx": px, "ty": py, "label": "Cổng bị cô lập"})
    else:
        cur = (px, py)
        while cur is not None:
            path_cells.append({"tx": cur[0], "ty": cur[1]})
            cur = parent.get(cur)
        path_cells.reverse()

    for c in chests:
        ctx, cty = int(c.get("tx", -1)), int(c.get("ty", -1))
        if not is_passable(ctx, cty) or (ctx, cty) not in visited:
            issues.append(f"Rương tại ô ({ctx},{cty}) nằm trong tường hoặc bị chặn đường!")
            unreachable_points.append({"tx": ctx, "ty": cty, "label": "Rương bị kẹt"})

    for s in shrines:
        stx, sty = int(s.get("tx", -1)), int(s.get("ty", -1))
        if not is_passable(stx, sty) or (stx, sty) not in visited:
            issues.append(f"Sự kiện Đài Cổ Ngữ tại ô ({stx},{sty}) nằm trong tường hoặc bị chặn đường!")
            unreachable_points.append({"tx": stx, "ty": sty, "label": "Sự kiện bị kẹt"})

    for sp in spawns:
        ex, ey = int(sp.get("tx", -1)), int(sp.get("ty", -1))
        if not is_passable(ex, ey) or (ex, ey) not in visited:
            issues.append(f"Quái/Boss ({sp.get('typeKey', 'enemy')}) tại ô ({ex},{ey}) nằm trong tường hoặc ngoài vùng đi được!")
            unreachable_points.append({"tx": ex, "ty": ey, "label": "Quái bị kẹt"})

    valid = len(issues) == 0
    return {
        "valid": valid,
        "bfsVerified": valid,
        "issues": issues,
        "unreachablePoints": unreachable_points,
        "pathCells": path_cells
    }


class MapGeneratorEngine:
    def __init__(self, history_path=HISTORY_FILE):
        self.history_path = history_path
        self.used_seeds = set()
        self.recent_signatures = deque(maxlen=25)
        self._load_history()

    def _load_history(self):
        if os.path.exists(self.history_path):
            try:
                with open(self.history_path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                self.used_seeds = set(data.get("used_seeds", []))
                for sig in data.get("recent_signatures", [])[-25:]:
                    self.recent_signatures.append(set(sig))
            except Exception:
                pass

    def _save_history(self):
        try:
            data = {
                "used_seeds": list(self.used_seeds)[-500:],
                "recent_signatures": [list(s) for s in self.recent_signatures]
            }
            with open(self.history_path, "w", encoding="utf-8") as f:
                json.dump(data, f)
        except Exception:
            pass

    @staticmethod
    def jaccard_similarity(set_a, set_b):
        if not set_a or not set_b:
            return 0.0
        inter = len(set_a.intersection(set_b))
        union = len(set_a.union(set_b))
        return inter / float(union) if union > 0 else 0.0

    def generate_verified_map(self, chapter=1, stage=1, requested_seed=None):
        max_attempts = 40
        best_map = None

        for attempt in range(max_attempts):
            if requested_seed is not None and attempt == 0:
                seed = int(requested_seed)
            else:
                seed = 100000 + chapter * 10007 + stage * 1009 + attempt * 37

            candidate, signature = self._build_candidate(chapter, stage, seed)
            if not candidate["bfsVerified"]:
                continue

            max_sim = 0.0
            for old_sig in self.recent_signatures:
                sim = self.jaccard_similarity(signature, old_sig)
                if sim > max_sim:
                    max_sim = sim

            candidate["similarityScore"] = round(max_sim, 3)
            candidate["attemptCount"] = attempt + 1
            best_map = (candidate, signature, seed)
            break

        if best_map is None:
            raise RuntimeError("Failed to generate a valid BFS-verified map.")

        final_map, final_sig, final_seed = best_map
        self.used_seeds.add(final_seed)
        self.recent_signatures.append(final_sig)
        self._save_history()
        return final_map

    def _build_candidate(self, chapter, stage, seed):
        rng = random.Random(seed)
        W, H = 38, 26
        grid = [[0 for _ in range(W)] for _ in range(H)]

        rooms = [
            {"id": 0, "role": "spawn", "x": 2, "y": rng.randint(8, 12), "w": 6, "h": 6},
            {"id": 1, "role": "combat", "x": 11, "y": rng.randint(8, 12), "w": 7, "h": 7},
            {"id": 2, "role": "treasure", "x": rng.randint(12, 16), "y": 2, "w": 6, "h": 5},
            {"id": 3, "role": "event", "x": rng.randint(18, 22), "y": H - 8, "w": 6, "h": 6},
            {"id": 4, "role": "boss" if stage == 3 else "exit", "x": W - 10, "y": rng.randint(7, 11), "w": 8, "h": 8}
        ]

        for rm in rooms:
            for y in range(rm["y"], min(H - 1, rm["y"] + rm["h"])):
                for x in range(rm["x"], min(W - 1, rm["x"] + rm["w"])):
                    grid[y][x] = 1

        doors = []

        def carve_corridor(rm_a, rm_b):
            cx = rm_a["x"] + rm_a["w"] // 2
            cy = rm_a["y"] + rm_a["h"] // 2
            bx = rm_b["x"] + rm_b["w"] // 2
            by = rm_b["y"] + rm_b["h"] // 2
            placed_door = False
            while cx != bx:
                for d in (-1, 0, 1):
                    if 1 < cy + d < H - 1 and grid[cy + d][cx] == 0:
                        grid[cy + d][cx] = 2
                if not placed_door and abs(cx - bx) == 3 and rng.random() < 0.5:
                    grid[cy][cx] = 4
                    doors.append({"tx": cx, "ty": cy, "opened": False})
                    placed_door = True
                cx += 1 if bx > cx else -1
            while cy != by:
                for d in (-1, 0, 1):
                    if 1 < cx + d < W - 1 and grid[cy][cx + d] == 0:
                        grid[cy][cx + d] = 2
                cy += 1 if by > cy else -1

        carve_corridor(rooms[0], rooms[1])
        carve_corridor(rooms[1], rooms[2])
        carve_corridor(rooms[1], rooms[3])
        carve_corridor(rooms[1], rooms[4])
        carve_corridor(rooms[3], rooms[4])

        spawn = {"tx": rooms[0]["x"] + rooms[0]["w"] // 2, "ty": rooms[0]["y"] + rooms[0]["h"] // 2}
        portal = {"tx": rooms[-1]["x"] + rooms[-1]["w"] // 2, "ty": rooms[-1]["y"] + rooms[-1]["h"] // 2}

        occupied = {(spawn["tx"], spawn["ty"]), (portal["tx"], portal["ty"])}
        props = []
        chests = []
        shrines = []
        spawns = []

        enemy_pools = {
            1: ["candy_zombie", "lemon_guard", "me_mow"],
            2: ["gunter", "demon_cat"],
            3: ["skeleton", "ooze_zombie", "necromancer"],
            4: ["magic_man", "iceclops"]
        }
        boss_map = {
            (1, 3): "lemongrab",
            (2, 2): "hierophant",
            (2, 3): "vampire_king",
            (3, 3): "the_lich",
            (4, 2): "ice_king",
            (4, 3): "citadel_guardian"
        }
        pool = enemy_pools.get(chapter, ["candy_zombie"])

        for rm in rooms[1:]:
            if rm["role"] in ("treasure", "boss", "exit"):
                cx = rm["x"] + rm["w"] // 2 + (1 if rm["role"] in ("boss", "exit") else 0)
                cy = rm["y"] + rm["h"] // 2
                if (cx, cy) not in occupied and grid[cy][cx] in (1, 2):
                    occupied.add((cx, cy))
                    chests.append({"tx": cx, "ty": cy, "isRoyal": rm["role"] in ("treasure", "boss")})
            if rm["role"] == "event":
                sx_e = rm["x"] + rm["w"] // 2
                sy_e = rm["y"] + rm["h"] // 2
                if (sx_e, sy_e) not in occupied and grid[sy_e][sx_e] in (1, 2):
                    occupied.add((sx_e, sy_e))
                    shrines.append({"tx": sx_e, "ty": sy_e, "activated": False})

            e_count = 2 if rm["role"] == "treasure" else 4
            for _ in range(e_count):
                ex = rng.randint(rm["x"] + 1, rm["x"] + rm["w"] - 2)
                ey = rng.randint(rm["y"] + 1, rm["y"] + rm["h"] - 2)
                if (ex, ey) not in occupied and grid[ey][ex] in (1, 2):
                    occupied.add((ex, ey))
                    spawns.append({
                        "tx": ex,
                        "ty": ey,
                        "typeKey": rng.choice(pool),
                        "isBossRoom": rm["role"] in ("boss", "exit")
                    })

        boss_key = boss_map.get((chapter, stage))
        if boss_key:
            bx = portal["tx"] - 2
            by = portal["ty"]
            if (bx, by) not in occupied and grid[by][bx] in (1, 2):
                occupied.add((bx, by))
                spawns.append({"tx": bx, "ty": by, "typeKey": boss_key, "isBossRoom": True, "isBoss": True})

        candidate = {
            "seed": seed,
            "isCustomMap": False,
            "mapName": f"Chương {chapter} - Ải {stage}",
            "chapterId": chapter,
            "stageNum": stage,
            "width": W,
            "height": H,
            "bfsVerified": False,
            "similarityScore": 0.0,
            "spawn": spawn,
            "portal": portal,
            "rooms": rooms,
            "grid": grid,
            "props": props,
            "chests": chests,
            "shrines": shrines,
            "doors": doors,
            "spawns": spawns
        }

        v_res = validate_map_bfs(candidate)
        candidate["bfsVerified"] = v_res["bfsVerified"]

        signature = set()
        for y in range(0, H, 2):
            for x in range(0, W, 2):
                if grid[y][x] > 0:
                    signature.add(f"{x}_{y}_{grid[y][x]}")

        return candidate, signature


class SharedGameDataStore:
    """
    Read-only runtime store synced from Unity Map Studio files.
    """
    def __init__(self, path=SHARED_DATA_FILE):
        self.path = path
        self.state = self._load_or_init()
        self.sync_from_unity_overrides()

    def _default_snapshot(self):
        return {
            "version": "2.0",
            "versionNumber": 2,
            "publishedAt": time.strftime("%Y-%m-%d %H:%M:%S UTC", time.gmtime()),
            "author": "Unity Map Studio",
            "changelog": "Dữ liệu dùng chung giữa Unity và Web (/play): 4 Chương (12 Ải), 5 Nhân vật, 27 Trang bị.",
            "balanceConfig": {
                "enemyHpMultiplier": 1.0,
                "enemyAtkMultiplier": 1.0,
                "expMultiplier": 1.0,
                "goldMultiplier": 1.0,
                "lootDropChance": 0.45,
                "startingPotions": 3,
                "potionHealPercent": 0.45
            },
            "heroes": [
                {
                    "id": "finn",
                    "name": "Finn the Human",
                    "role": "Kiếm Sĩ Cận Chiến Liên Hoàn (Chém Kiếm 3 Nhịp & Lướt Kiếm Khí)",
                    "baseHp": 175, "baseEn": 100, "baseAtk": 29, "baseDef": 0.15, "baseSpd": 220, "baseCrit": 0.16,
                    "dashCd": 1.0,
                    "skill1": {"name": "Lướt Chém Kiếm Khí Bán Nguyệt", "cost": 20, "cd": 3.2, "mult": 1.95, "desc": "Lướt nhanh theo hướng nhìn, chém kiếm đôi và phóng luồng kiếm khí bán nguyệt xuyên thấu quái."},
                    "ult": {"name": "Thánh Kiếm Hoàng Kim Bão Tố", "cost": 45, "cd": 8.0, "mult": 3.35, "desc": "Giáng 5 Đại Kiếm Hoàng Kim gây Choáng (2.5s) toàn bộ quái xung quanh và tăng +40% Tốc đánh trong 6s."}
                },
                {
                    "id": "jake",
                    "name": "Jake the Dog",
                    "role": "Đấu Sĩ Co Giãn (Đấm Vươn Dài Gom Quái & Hóa Khổng Lồ)",
                    "baseHp": 230, "baseEn": 95, "baseAtk": 31, "baseDef": 0.20, "baseSpd": 200, "baseCrit": 0.12,
                    "dashCd": 1.05,
                    "skill1": {"name": "Nắm Đấm Vươn Dài Quét Ngang", "cost": 22, "cd": 3.6, "mult": 2.05, "desc": "Vươn dài cánh tay khổng lồ 220px đấm quét ngang, kéo gom quái về phía trước và Làm Chậm 55% trong 3.5s."},
                    "ult": {"name": "Hóa Khổng Lồ Địa Chấn 3 Nhịp", "cost": 45, "cd": 8.5, "mult": 3.5, "desc": "Hóa Khổng Lồ (1.45×) trong 5s (+50% Giáp), tự động dậm đất 3 nhịp tạo sóng chấn động làm choáng diện rộng."}
                },
                {
                    "id": "marceline",
                    "name": "Marceline",
                    "role": "Nữ Hoàng Ma Cà Rồng (Đàn Dơi Truy Kích & Hút Máu Hồi Phục)",
                    "baseHp": 155, "baseEn": 120, "baseAtk": 30, "baseDef": 0.12, "baseSpd": 235, "baseCrit": 0.22,
                    "dashCd": 0.95,
                    "skill1": {"name": "Đàn Dơi Huyết Hồi Máu", "cost": 22, "cd": 3.4, "mult": 1.80, "desc": "Biến ảnh trong sương đêm, phóng 5 Dơi Ma Cà Rồng tự động đuổi cắn quái và hút máu hồi HP trực tiếp cho Marceline."},
                    "ult": {"name": "Kết Giới Huyết Nguyệt Nightosphere", "cost": 48, "cd": 8.2, "mult": 3.4, "desc": "Dựng vòng Huyết Nguyệt đi theo Marceline suốt 5s, rút máu quái mỗi 0.5s để hồi HP và gây Suy Yếu (+25% DMG)."}
                },
                {
                    "id": "fp",
                    "name": "Flame Princess",
                    "role": "Công Chúa Lửa (Thiêu Đốt Gây Cháy DoT & Bão Dung Nham)",
                    "baseHp": 145, "baseEn": 135, "baseAtk": 34, "baseDef": 0.10, "baseSpd": 220, "baseCrit": 0.20,
                    "dashCd": 1.0,
                    "skill1": {"name": "Bão Cột Lửa & Thảm Dung Nham", "cost": 22, "cd": 3.4, "mult": 1.95, "desc": "Phóng 3 cột lửa liên hoàn để lại 3 Vùng Dung Nham cháy rực suốt 4.5s, gây hiệu ứng Cháy (Burn DoT) rút máu liên tục."},
                    "ult": {"name": "Phượng Hoàng Lửa Thiên Thạch", "cost": 50, "cd": 8.5, "mult": 3.7, "desc": "Hóa Phượng Hoàng Lửa lao tới phát nổ đại hồng liên, tạo hố dung nham lớn và bắn 8 cầu lửa gây Cháy 8 hướng."}
                },
                {
                    "id": "pb",
                    "name": "Princess Bubblegum",
                    "role": "Công Chúa Kẹo Cao Su (Gọi Lính Chuối Hộ Vệ & Khoa Học Hoàng Gia)",
                    "baseHp": 152, "baseEn": 145, "baseAtk": 28, "baseDef": 0.13, "baseSpd": 215, "baseCrit": 0.18,
                    "dashCd": 1.05,
                    "skill1": {"name": "Triệu Hồi Lính Chuối Hộ Vệ & Bom Keo", "cost": 24, "cd": 4.2, "mult": 1.70, "desc": "Gọi 2 Lính Chuối Hộ Vệ (Banana Guards) cầm giáo tự động truy kích đánh quái trong 10s, kèm ném Bom Keo Trói Chân (2s)."},
                    "ult": {"name": "Quân Đoàn Hộ Vệ & Laser Vệ Tinh", "cost": 46, "cd": 8.2, "mult": 3.35, "desc": "Tạo Khiên Năng Lượng Hoàng Gia, triệu hồi thêm 2 Lính Hộ Vệ Tinh Nhuệ và gọi Tia Laser Vệ Tinh quét mục tiêu 3.2s."}
                }
            ],
            "lootTemplates": [
                {"id": "wep_0", "name": "Kiếm Vàng Scarlet", "slot": "weapon", "col": 0, "row": 0, "atk": 16, "hp": 0, "def": 0, "spd": 0, "crit": 0.04},
                {"id": "wep_1", "name": "Kiếm Rễ Cây (Root Sword)", "slot": "weapon", "col": 1, "row": 0, "atk": 20, "hp": 20, "def": 0.02, "spd": 0, "crit": 0.05},
                {"id": "wep_2", "name": "Kiếm Máu Quỷ (Demon Blood)", "slot": "weapon", "col": 2, "row": 0, "atk": 26, "hp": 15, "def": 0, "spd": 5, "crit": 0.08},
                {"id": "wep_3", "name": "Kiếm Cỏ (Grass Sword)", "slot": "weapon", "col": 3, "row": 0, "atk": 29, "hp": 0, "def": 0, "spd": 16, "crit": 0.14},
                {"id": "wep_4", "name": "Finn Sword", "slot": "weapon", "col": 4, "row": 0, "atk": 34, "hp": 30, "def": 0.04, "spd": 10, "crit": 0.12},
                {"id": "wep_6", "name": "Kiếm Mắt Quỷ (Night Sword)", "slot": "weapon", "col": 6, "row": 0, "atk": 45, "hp": 35, "def": 0.05, "spd": 12, "crit": 0.16},
                {"id": "wep_7", "name": "Đàn Rìu Bass Axe", "slot": "weapon", "col": 7, "row": 0, "atk": 32, "hp": 25, "def": 0.03, "spd": 10, "crit": 0.12},
                {"id": "wep_8", "name": "Súng Khoa Học Plasma", "slot": "weapon", "col": 8, "row": 0, "atk": 28, "hp": 10, "def": 0.02, "spd": 12, "crit": 0.14},
                {"id": "wep_9", "name": "Trượng Lửa Hoàng Gia", "slot": "weapon", "col": 9, "row": 0, "atk": 35, "hp": 10, "def": 0, "spd": 8, "crit": 0.15},
                {"id": "arm_0", "name": "Mũ Gấu & Balo Xanh Finn", "slot": "armor", "col": 0, "row": 1, "atk": 4, "hp": 40, "def": 0.06, "spd": 8, "crit": 0},
                {"id": "arm_2", "name": "Bộ Giáp Jake Suit", "slot": "armor", "col": 2, "row": 1, "atk": 14, "hp": 110, "def": 0.14, "spd": 6, "crit": 0.04},
                {"id": "arm_7", "name": "Giáp Xương Chúa Tể Lich", "slot": "armor", "col": 7, "row": 1, "atk": 26, "hp": 90, "def": 0.12, "spd": 8, "crit": 0.10},
                {"id": "acc_0", "name": "Sách Cổ The Enchiridion", "slot": "accessory", "col": 0, "row": 2, "atk": 15, "hp": 50, "def": 0.05, "spd": 10, "crit": 0.08},
                {"id": "acc_1", "name": "Vương Miện Hồng Ngọc Vua Băng", "slot": "accessory", "col": 1, "row": 2, "atk": 20, "hp": 30, "def": 0.04, "spd": 8, "crit": 0.09},
                {"id": "acc_6", "name": "Băng Game Vàng 8-Bit BMO", "slot": "accessory", "col": 6, "row": 2, "atk": 15, "hp": 20, "def": 0, "spd": 22, "crit": 0.12}
            ],
            "stageMapOverrides": {}
        }

    def _load_or_init(self):
        snap = self._default_snapshot()
        if os.path.exists(self.path):
            try:
                with open(self.path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                if isinstance(data, dict) and "published" in data:
                    pub = data["published"]
                    if isinstance(pub.get("stageMapOverrides"), dict):
                        snap["stageMapOverrides"] = pub["stageMapOverrides"]
            except Exception:
                pass
        state = {"published": snap}
        self._save(state)
        return state

    def _save(self, state=None):
        if state is not None:
            self.state = state
        try:
            with open(self.path, "w", encoding="utf-8") as f:
                json.dump(self.state, f, ensure_ascii=False, indent=2)
            os.makedirs(UNITY_STREAMING_DIR, exist_ok=True)
            unity_shared_path = os.path.join(UNITY_STREAMING_DIR, "shared_game_data.json")
            with open(unity_shared_path, "w", encoding="utf-8") as f:
                json.dump({"ok": True, "role": "player", "published": self.state["published"]}, f, ensure_ascii=False, indent=2)
        except Exception:
            pass

    def sync_from_unity_overrides(self):
        """
        Loads any stage_<ch>_<st>_override.json files saved by Unity Editor's Map Studio
        into the shared game data store after verifying BFS path validity.
        """
        overrides = self.state["published"].setdefault("stageMapOverrides", {})
        synced_count = 0
        for search_dir in (UNITY_OVERRIDES_DIR, UNITY_STREAM_OVERRIDES_DIR):
            if not os.path.isdir(search_dir):
                continue
            for fpath in sorted(glob.glob(os.path.join(search_dir, "stage_*_*_override.json"))):
                try:
                    with open(fpath, "r", encoding="utf-8") as f:
                        m = json.load(f)
                    m = convert_unity_flat_grid_if_needed(m)
                    v_res = validate_map_bfs(m)
                    if v_res["valid"]:
                        ch = int(m.get("chapterId", 1))
                        st = int(m.get("stageNum", 1))
                        overrides[f"{ch}-{st}"] = m
                        synced_count += 1
                except Exception:
                    pass
        self._save()
        return synced_count

    def get_published(self):
        return self.state["published"]


ENGINE = MapGeneratorEngine()
DATA_STORE = SharedGameDataStore()


class PlayerPortalRequestHandler(BaseHTTPRequestHandler):
    def _send_cors(self):
        origin = self.headers.get("Origin", "*")
        self.send_header("Access-Control-Allow-Origin", origin if origin != "null" else "*")
        self.send_header("Access-Control-Allow-Methods", "GET, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")

    def _send_json(self, status_code, payload):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self._send_cors()
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_response(200)
        self._send_cors()
        self.end_headers()

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path

        if path == "/":
            self.send_response(302)
            self.send_header("Location", "/play")
            self.end_headers()
            return

        if path == "/play":
            if os.path.exists(WEB_HTML_FILE):
                with open(WEB_HTML_FILE, "rb") as f:
                    html_bytes = f.read()
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(html_bytes)))
                self.end_headers()
                self.wfile.write(html_bytes)
            else:
                self._send_json(404, {"error": "Web HTML file not found."})
            return

        if path == "/api/public/game-data":
            DATA_STORE.sync_from_unity_overrides()
            self._send_json(200, {
                "ok": True,
                "role": "player",
                "published": DATA_STORE.get_published()
            })
            return

        if path == "/api/map/generate":
            qs = parse_qs(parsed.query)
            ch = int(qs.get("chapter", ["1"])[0])
            st = int(qs.get("stage", ["1"])[0])
            seed_param = qs.get("seed", [None])[0]
            pub_overrides = DATA_STORE.get_published().get("stageMapOverrides") or {}
            stage_key = f"{ch}-{st}"
            if stage_key in pub_overrides and seed_param is None:
                self._send_json(200, pub_overrides[stage_key])
                return
            payload = ENGINE.generate_verified_map(ch, st, seed_param)
            self._send_json(200, payload)
            return

        self._send_json(404, {"error": "Read-only player server: endpoint not found."})


def run_self_test():
    print("=== Running Read-Only Player Server & Shared Unity Map Store Self-Test ===")
    for ch in range(1, 5):
        for st in range(1, 4):
            m = ENGINE.generate_verified_map(ch, st)
            assert m["bfsVerified"] is True, f"BFS failed on Chapter {ch} Stage {st}"
    synced = DATA_STORE.sync_from_unity_overrides()
    pub = DATA_STORE.get_published()
    print(f"[PASS] All 12 stages BFS-verified. Shared Data Version: v{pub['version']} (Synced {synced} Unity overrides).")


if __name__ == "__main__":
    if "--sync-from-unity" in sys.argv:
        cnt = DATA_STORE.sync_from_unity_overrides()
        print(f"[Sync Complete] Synced {cnt} stage map override(s) from Unity Map Studio to shared_game_data.json.")
        sys.exit(0)
    if "--test" in sys.argv:
        run_self_test()
        sys.exit(0)
    port = int(os.environ.get("PORT", "8080"))
    print(f"Adventure Time ARPG Read-Only Player Server listening on http://localhost:{port}/play")
    ThreadingHTTPServer(("0.0.0.0", port), PlayerPortalRequestHandler).serve_forever()
