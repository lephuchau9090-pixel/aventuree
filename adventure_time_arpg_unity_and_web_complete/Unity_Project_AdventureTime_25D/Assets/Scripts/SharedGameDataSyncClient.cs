using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace AdventureTimeARPG
{
    [Serializable]
    public class GameBalanceConfig
    {
        public float enemyHpMultiplier = 1.0f;
        public float enemyAtkMultiplier = 1.0f;
        public float expMultiplier = 1.0f;
        public float goldMultiplier = 1.0f;
        public float lootDropChance = 0.45f;
        public int startingPotions = 3;
        public float potionHealPercent = 0.45f;
    }

    [Serializable]
    public class SkillConfigEntry
    {
        public string name;
        public int cost;
        public float cd;
        public float mult;
        public string desc;
    }

    [Serializable]
    public class HeroBalanceEntry
    {
        public string id;
        public string name;
        public string role;
        public int baseHp;
        public int baseEn;
        public int baseAtk;
        public float baseDef;
        public float baseSpd;
        public float baseCrit;
        public float dashCd = 1.05f;
        public SkillConfigEntry skill1;
        public SkillConfigEntry ult;
    }

    [Serializable]
    public class LootTemplateEntry
    {
        public string id;
        public string name;
        public string slot;
        public int col;
        public int row;
        public int atk;
        public int hp;
        public float def;
        public int spd;
        public float crit;
    }

    [Serializable]
    public class PublishedSnapshotData
    {
        public string version = "2.0";
        public int versionNumber = 2;
        public string publishedAt;
        public string author = "Unity Map Studio";
        public string changelog;
        public GameBalanceConfig balanceConfig = new GameBalanceConfig();
        public List<HeroBalanceEntry> heroes = new List<HeroBalanceEntry>();
        public List<LootTemplateEntry> lootTemplates = new List<LootTemplateEntry>();
    }

    [Serializable]
    public class PublicGameDataResponse
    {
        public bool ok;
        public string role;
        public PublishedSnapshotData published;
    }

    /// <summary>
    /// Read-only shared data synchronizer between Unity (StreamingAssets / Resources) and the Web `/play` build.
    /// Does not expose any web admin or write APIs; content is authored exclusively inside Unity Editor's Map Studio.
    /// </summary>
    public class SharedGameDataSyncClient : MonoBehaviour
    {
        [Header("Read-Only Shared Data Endpoint (Optional for WebGL / Local Server)")]
        public string backendBaseUrl = "http://localhost:8080";
        public PublishedSnapshotData activePublishedData = new PublishedSnapshotData();

        private void Start()
        {
            LoadLocalStreamingAssetsFallback();
            StartCoroutine(FetchPublishedGameDataCoroutine());
        }

        public void LoadLocalStreamingAssetsFallback()
        {
            string streamingPath = Path.Combine(Application.streamingAssetsPath, "shared_game_data.json");
            if (File.Exists(streamingPath))
            {
                try
                {
                    string raw = File.ReadAllText(streamingPath);
                    var resp = JsonUtility.FromJson<PublicGameDataResponse>(raw);
                    if (resp != null && resp.published != null)
                    {
                        activePublishedData = resp.published;
                        return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SharedGameDataSyncClient] Could not parse StreamingAssets JSON: {e.Message}");
                }
            }
        }

        public IEnumerator FetchPublishedGameDataCoroutine(Action<PublishedSnapshotData> onComplete = null)
        {
            string url = backendBaseUrl.TrimEnd('/') + "/api/public/game-data";
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = 2;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var resp = JsonUtility.FromJson<PublicGameDataResponse>(req.downloadHandler.text);
                    if (resp != null && resp.ok && resp.published != null)
                    {
                        activePublishedData = resp.published;
                        Debug.Log($"[SharedGameDataSyncClient] Loaded read-only shared game data v{activePublishedData.version}.");
                        onComplete?.Invoke(activePublishedData);
                        yield break;
                    }
                }
                onComplete?.Invoke(activePublishedData);
            }
        }
    }
}
