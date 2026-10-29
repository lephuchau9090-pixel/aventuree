using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AdventureTimeARPG
{
    [Serializable]
    public class InventoryLootItem
    {
        public string uid;
        public string baseId;
        public string itemName;
        public string slot; // weapon, armor, accessory
        public int rarityIndex; // 0..5
        public string rarityName;
        public int atk;
        public int hp;
        public float def;
        public int spd;
        public float crit;
        public int powerScore;
        public int sellValue;
    }

    /// <summary>
    /// Handles Player Death (HP <= 0), Game Over Screen UI, Sequential Stage Progression (must beat Stage 1 first),
    /// Post-Battle Loot Bag Storage, and 3-for-1 Item Randomization:
    /// 1. Stage Progression:
    ///    - New players start at Stage 1-1 (`unlockedChapter = 1, unlockedStage = 1`).
    ///    - Players cannot select future stages freely; a stage is selectable only after clearing the preceding stage
    ///      (or replaying an already-cleared stage).
    /// 2. Post-Battle Loot -> Personal Inventory Bag (`savedBackpackInventory`):
    ///    - All items dropped/picked up during the stage (`stageCollectedLoot`) are stored in the player's personal
    ///      inventory bag upon pickup and swept/persisted at stage completion.
    /// 3. 3-for-1 Item Randomizer (`FuseThreeItemsIntoOneRandomItem`):
    ///    - Removes any "free 3 items" generator. Players must sacrifice 3 arbitrary items from `savedBackpackInventory`
    ///      to randomly roll 1 new item of a different type.
    /// 4. Game Over Flow:
    ///    - "Về sảnh chờ" (`ReturnToLobbyFromGameOver`): Cleans up combat state, preserves saved progress.
    ///    - "Chơi lại màn này" (`RetryCurrentStageFromGameOver`): Reloads current stage cleanly without duplicate clear rewards.
    /// </summary>
    public class GameOverAndStageFlowController : MonoBehaviour
    {
        [Header("Game Over UI References")]
        public GameObject gameOverPanel;
        public Text gameOverTitleText;
        public Text gameOverSummaryText;
        public Button btnReturnToLobby;
        public Button btnRetryCurrentStage;

        [Header("Lobby & Gameplay Screen Roots")]
        public GameObject lobbyScreenRoot;
        public GameObject gameplayHudRoot;

        [Header("Runtime Stage & Progression State")]
        public int currentChapter = 1;
        public int currentStage = 1;
        public int unlockedChapter = 1;
        public int unlockedStage = 1;
        public List<string> clearedStageKeys = new List<string>();

        [Header("Personal Inventory Bag & Stage Loot")]
        public int maxBackpackSlots = 60;
        public List<InventoryLootItem> savedBackpackInventory = new List<InventoryLootItem>();
        public List<InventoryLootItem> stageCollectedLoot = new List<InventoryLootItem>();

        public bool IsPlayerDead { get; private set; }
        public bool IsInCombatStage { get; private set; }
        public bool StageClearRewardClaimed { get; private set; }

        public event Action OnCleanCombatEntitiesRequested;
        public event Action<int, int> OnStageReloadRequested;
        public event Action OnReturnedToLobby;
        public event Action OnInventoryUpdated;

        private DistinctHeroSkillSystem heroSkills;

        private static readonly string[] SlotPool = { "weapon", "armor", "accessory" };
        private static readonly string[] RarityNames = {
            "Phổ Thông (Common)", "Khá (Uncommon)", "Hiếm (Rare)",
            "Sử Thi (Epic)", "Huyền Thoại (Legendary)", "Thần Thoại (Mythic)"
        };
        private static readonly string[] TemplateNames = {
            "Kiếm Vàng Scarlet", "Kiếm Rễ Cây (Root Sword)", "Kiếm Máu Quỷ (Demon Blood)",
            "Kiếm Cỏ (Grass Sword)", "Finn Sword", "Đàn Rìu Bass Axe",
            "Súng Khoa Học Plasma", "Trượng Lửa Hoàng Gia", "Bộ Giáp Jake Suit",
            "Giáp Đội Vệ Binh Chuối", "Áo Khoác Da Nightosphere", "Sách Cổ The Enchiridion",
            "Vương Miện Hồng Ngọc Vua Băng", "Gấu Bông Hambo Của Marcy"
        };

        private void Awake()
        {
            heroSkills = FindObjectOfType<DistinctHeroSkillSystem>();
            if (btnReturnToLobby != null)
                btnReturnToLobby.onClick.AddListener(ReturnToLobbyFromGameOver);
            if (btnRetryCurrentStage != null)
                btnRetryCurrentStage.onClick.AddListener(RetryCurrentStageFromGameOver);

            if (gameOverPanel != null)
                gameOverPanel.SetActive(false);
        }

        public static string GetStageKey(int chapter, int stage)
        {
            return $"{chapter}-{stage}";
        }

        public static int GetLinearStageIndex(int chapter, int stage)
        {
            return (Mathf.Clamp(chapter, 1, 4) - 1) * 3 + (Mathf.Clamp(stage, 1, 3) - 1);
        }

        /// <summary>
        /// Enforces strict sequential progression:
        /// - Stage 1-1 is unlocked at the start.
        /// - Any subsequent stage (1-2 .. 4-3) is selectable ONLY if the player has cleared the previous stage
        ///   or has already cleared this stage.
        /// </summary>
        public bool IsStageUnlocked(int chapter, int stage)
        {
            if (chapter == 1 && stage == 1)
                return true;

            string targetKey = GetStageKey(chapter, stage);
            if (clearedStageKeys.Contains(targetKey))
                return true;

            int linearIdx = GetLinearStageIndex(chapter, stage);
            if (linearIdx <= 0)
                return true;

            int prevLinear = linearIdx - 1;
            int prevChapter = (prevLinear / 3) + 1;
            int prevStage = (prevLinear % 3) + 1;
            string prevKey = GetStageKey(prevChapter, prevStage);
            return clearedStageKeys.Contains(prevKey);
        }

        public bool IsStageCleared(int chapter, int stage)
        {
            return clearedStageKeys.Contains(GetStageKey(chapter, stage));
        }

        public bool TrySelectAndStartStage(int chapter, int stage, out string message)
        {
            if (!IsStageUnlocked(chapter, stage))
            {
                message = $"Ải {chapter}-{stage} đang khóa! Bạn phải chiến thắng ải trước đó mới có thể chọn màn này.";
                return false;
            }
            BeginStageSession(chapter, stage);
            message = $"Bắt đầu Chương {chapter} - Ải {stage}!";
            return true;
        }

        public void BeginStageSession(int chapter, int stage)
        {
            if (!IsStageUnlocked(chapter, stage))
            {
                chapter = 1;
                stage = 1;
            }

            currentChapter = chapter;
            currentStage = stage;
            IsPlayerDead = false;
            IsInCombatStage = true;
            StageClearRewardClaimed = false;
            stageCollectedLoot.Clear();

            if (gameOverPanel != null)
                gameOverPanel.SetActive(false);
            if (lobbyScreenRoot != null)
                lobbyScreenRoot.SetActive(false);
            if (gameplayHudRoot != null)
                gameplayHudRoot.SetActive(true);

            if (heroSkills != null)
            {
                heroSkills.currentHp = heroSkills.maxHp;
                heroSkills.activeBananaGuards.Clear();
            }
        }

        /// <summary>
        /// Called when the player picks up a dropped item during combat or opens a chest.
        /// Immediately stores the item in the stage loot list and personal backpack inventory.
        /// </summary>
        public bool CollectStageLootItem(InventoryLootItem item)
        {
            if (item == null) return false;
            stageCollectedLoot.Add(item);
            if (savedBackpackInventory.Count < maxBackpackSlots)
            {
                savedBackpackInventory.Add(item);
                OnInventoryUpdated?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 3-for-1 Item Transmutation ("Bỏ 3 món đồ bất kỳ để random ra 1 món đồ khác"):
        /// Consumes 3 items from `savedBackpackInventory` by their UIDs and rolls 1 new random item of a different template.
        /// </summary>
        public bool FuseThreeItemsIntoOneRandomItem(string uid1, string uid2, string uid3, out InventoryLootItem newItem, out string statusMessage)
        {
            newItem = null;
            if (string.IsNullOrEmpty(uid1) || string.IsNullOrEmpty(uid2) || string.IsNullOrEmpty(uid3) ||
                uid1 == uid2 || uid1 == uid3 || uid2 == uid3)
            {
                statusMessage = "Phải chọn đủ 3 món đồ khác nhau trong Kho Túi Đồ để ghép!";
                return false;
            }

            InventoryLootItem item1 = savedBackpackInventory.Find(i => i.uid == uid1);
            InventoryLootItem item2 = savedBackpackInventory.Find(i => i.uid == uid2);
            InventoryLootItem item3 = savedBackpackInventory.Find(i => i.uid == uid3);

            if (item1 == null || item2 == null || item3 == null)
            {
                statusMessage = "Không tìm thấy đủ 3 món đồ đã chọn trong Kho Túi Đồ!";
                return false;
            }

            HashSet<string> sacrificedBaseIds = new HashSet<string> { item1.baseId, item2.baseId, item3.baseId };
            int avgRarity = Mathf.RoundToInt((item1.rarityIndex + item2.rarityIndex + item3.rarityIndex) / 3f);
            int targetRarity = Mathf.Clamp(avgRarity + (UnityEngine.Random.value < 0.45f ? 1 : 0), 0, RarityNames.Length - 1);

            savedBackpackInventory.Remove(item1);
            savedBackpackInventory.Remove(item2);
            savedBackpackInventory.Remove(item3);

            newItem = GenerateRandomLootItem(currentChapter, targetRarity, sacrificedBaseIds);
            savedBackpackInventory.Add(newItem);
            OnInventoryUpdated?.Invoke();

            statusMessage = $"Đã bỏ 3 món đồ và nhận được [{newItem.rarityName}] {newItem.itemName}!";
            return true;
        }

        public InventoryLootItem GenerateRandomLootItem(int chapter, int rarityIdx, HashSet<string> excludedBaseIds = null)
        {
            int chosenIdx = UnityEngine.Random.Range(0, TemplateNames.Length);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int cand = UnityEngine.Random.Range(0, TemplateNames.Length);
                string candBaseId = "tpl_" + cand;
                if (excludedBaseIds == null || !excludedBaseIds.Contains(candBaseId))
                {
                    chosenIdx = cand;
                    break;
                }
            }

            int rIdx = Mathf.Clamp(rarityIdx, 0, RarityNames.Length - 1);
            float mult = (1f + rIdx * 0.28f) * (1f + (Mathf.Max(1, chapter) - 1) * 0.12f);
            int atk = Mathf.RoundToInt(UnityEngine.Random.Range(14, 34) * mult);
            int hp = Mathf.RoundToInt(UnityEngine.Random.Range(20, 75) * mult);
            float def = Mathf.Round(UnityEngine.Random.Range(0.03f, 0.12f) * 100f) / 100f;
            int spd = Mathf.RoundToInt(UnityEngine.Random.Range(4, 18) * (1f + rIdx * 0.15f));
            float crit = Mathf.Round(UnityEngine.Random.Range(0.04f, 0.16f) * 100f) / 100f;
            int power = Mathf.RoundToInt(atk * 2.5f + hp * 0.6f + def * 300f + spd * 1.2f + crit * 250f);

            return new InventoryLootItem
            {
                uid = "it_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                baseId = "tpl_" + chosenIdx,
                itemName = TemplateNames[chosenIdx],
                slot = SlotPool[chosenIdx % SlotPool.Length],
                rarityIndex = rIdx,
                rarityName = RarityNames[rIdx],
                atk = atk,
                hp = hp,
                def = def,
                spd = spd,
                crit = crit,
                powerScore = power,
                sellValue = Mathf.Max(12, Mathf.RoundToInt(power * 0.35f))
            };
        }

        public void ApplyDamageToPlayer(float finalDamage, string attackerName = "Quái vật")
        {
            if (IsPlayerDead || !IsInCombatStage || heroSkills == null)
                return;

            heroSkills.currentHp = Mathf.Max(0f, heroSkills.currentHp - Mathf.Max(1f, finalDamage));
            if (heroSkills.currentHp <= 0f)
            {
                TriggerPlayerDeath(attackerName);
            }
        }

        public void TriggerPlayerDeath(string defeatedBy = "Quái vật hầm ngục")
        {
            if (IsPlayerDead) return;
            IsPlayerDead = true;
            IsInCombatStage = false;

            if (gameOverTitleText != null)
                gameOverTitleText.text = "BẠN ĐÃ GỤC NGÃ!";
            if (gameOverSummaryText != null)
                gameOverSummaryText.text = $"Gục ngã tại Chương {currentChapter} - Ải {currentStage} ({defeatedBy}). Tiến trình, Cấp độ, Vàng và Kho Túi Đồ ({savedBackpackInventory.Count} món) được giữ nguyên.";

            if (gameOverPanel != null)
                gameOverPanel.SetActive(true);
        }

        public void ReturnToLobbyFromGameOver()
        {
            CleanupCombatArenaState();
            IsPlayerDead = false;
            IsInCombatStage = false;

            if (gameOverPanel != null)
                gameOverPanel.SetActive(false);
            if (gameplayHudRoot != null)
                gameplayHudRoot.SetActive(false);
            if (lobbyScreenRoot != null)
                lobbyScreenRoot.SetActive(true);

            OnReturnedToLobby?.Invoke();
        }

        public void RetryCurrentStageFromGameOver()
        {
            CleanupCombatArenaState();
            BeginStageSession(currentChapter, currentStage);
            OnStageReloadRequested?.Invoke(currentChapter, currentStage);
        }

        /// <summary>
        /// Claims stage clear rewards once, marks the current stage as cleared, unlocks the immediate next stage,
        /// and ensures the stage reward item + all stage loot are stored in `savedBackpackInventory`.
        /// </summary>
        public bool TryClaimStageClearReward(out int goldReward, out int expReward, out InventoryLootItem stageRewardItem)
        {
            stageRewardItem = null;
            if (StageClearRewardClaimed || IsPlayerDead)
            {
                goldReward = 0;
                expReward = 0;
                return false;
            }
            StageClearRewardClaimed = true;
            goldReward = 60 + currentChapter * 25;
            expReward = 85 + currentChapter * 20;

            string curKey = GetStageKey(currentChapter, currentStage);
            if (!clearedStageKeys.Contains(curKey))
                clearedStageKeys.Add(curKey);

            if (currentStage < 3)
            {
                if (currentChapter == unlockedChapter)
                    unlockedStage = Mathf.Max(unlockedStage, currentStage + 1);
            }
            else if (currentChapter < 4)
            {
                if (currentChapter >= unlockedChapter)
                {
                    unlockedChapter = currentChapter + 1;
                    unlockedStage = 1;
                }
            }

            stageRewardItem = GenerateRandomLootItem(currentChapter, currentStage == 3 ? 3 : 2);
            CollectStageLootItem(stageRewardItem);
            return true;
        }

        private void CleanupCombatArenaState()
        {
            if (heroSkills != null)
            {
                heroSkills.activeBananaGuards.Clear();
                heroSkills.berserkTimer = 0f;
                heroSkills.giantTimer = 0f;
                heroSkills.bloodMoonTimer = 0f;
                heroSkills.shieldHp = 0f;
            }
            OnCleanCombatEntitiesRequested?.Invoke();
        }
    }
}
