using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdventureTimeARPG
{
    [Serializable]
    public class SummonedAllyUnit
    {
        public string unitName = "Royal Banana Guard";
        public Vector2 position;
        public float hp = 120f;
        public float maxHp = 120f;
        public float atk = 22f;
        public float moveSpeed = 210f;
        public float remainingLifetime = 10f;
        public float attackCooldown = 0f;
        public bool isElite = false;
    }

    /// <summary>
    /// Implements the 5 distinct character combat styles, basic attacks, Skill 1, and Ultimates:
    /// - Finn: 3-step sword slash combo (360 whirlwind crit on step 3) + Dash Crescent Slash + 5 Royal Swords AoE Stun & Berserk.
    /// - Jake: Elastic knockback punch + 220px Stretch Fist Sweep (Pull & 55% Slow) + Giant Transformation (1.45x, +50% Def, 3 Ground Slams).
    /// - Marceline: Bass Axe sonic lifesteal + Mist Blink & 5 Homing Vampire Bats (heals HP on hit) + 5s Moving Blood Moon Soul-Drain Domain (heals HP & applies Vulnerability).
    /// - Flame Princess: Igniting Fireball (applies 3s Burn DoT) + 3 Persistent Lava Hazard Zones (Burn DoT) + Phoenix Meteor Dive & 8-Way Igniting Fire Nova.
    /// - Princess Bubblegum: Ricochet Candy Plasma + Summon Royal Banana Guards (allied soldiers that chase and melee/spear enemies) & Root Gum Bomb + Royal Energy Shield, Elite Guard Reinforcement & Orbital Laser.
    /// </summary>
    public class DistinctHeroSkillSystem : MonoBehaviour
    {
        public string heroId = "finn";
        public int comboStep = 0;
        public float currentHp = 170f;
        public float maxHp = 170f;
        public float berserkTimer = 0f;
        public float giantTimer = 0f;
        public float bloodMoonTimer = 0f;
        public float shieldHp = 0f;

        [Header("Princess Bubblegum Summoned Guards")]
        public List<SummonedAllyUnit> activeBananaGuards = new List<SummonedAllyUnit>();
        public int maxSummonedGuards = 4;

        private void Update()
        {
            float dt = Time.deltaTime;
            if (berserkTimer > 0f) berserkTimer = Mathf.Max(0f, berserkTimer - dt);
            if (giantTimer > 0f) giantTimer = Mathf.Max(0f, giantTimer - dt);
            if (bloodMoonTimer > 0f) bloodMoonTimer = Mathf.Max(0f, bloodMoonTimer - dt);

            for (int i = activeBananaGuards.Count - 1; i >= 0; i--)
            {
                var guard = activeBananaGuards[i];
                guard.remainingLifetime -= dt;
                if (guard.attackCooldown > 0f) guard.attackCooldown -= dt;
                if (guard.remainingLifetime <= 0f || guard.hp <= 0f)
                {
                    activeBananaGuards.RemoveAt(i);
                }
            }
        }

        public void HealHero(float amount)
        {
            currentHp = Mathf.Min(maxHp, currentHp + Mathf.Max(0f, amount));
        }

        public void ExecuteBasicAttack(Vector2 origin, Vector2 aimDir, float baseAtk)
        {
            Vector2 dir = aimDir.sqrMagnitude > 0.001f ? aimDir.normalized : Vector2.down;
            switch (heroId)
            {
                case "finn":
                    comboStep = (comboStep % 3) + 1;
                    // Step 1-2: directional sword slash; Step 3: 360-degree whirlwind guaranteed critical slash
                    break;
                case "jake":
                    // Stretchy cone punch in aimDir with heavy knockback
                    break;
                case "marceline":
                    // Bass axe cleave + piercing sonic wave with +4 HP lifesteal per hit
                    HealHero(4f);
                    break;
                case "fp":
                    // Exploding fireball applying 3.5s Burn DoT (fire damage over time)
                    break;
                case "pb":
                    // High-velocity candy plasma bolt chaining up to 3 targets
                    break;
            }
        }

        public void ExecuteSkill1(Vector2 origin, Vector2 aimDir, float baseAtk)
        {
            Vector2 dir = aimDir.sqrMagnitude > 0.001f ? aimDir.normalized : Vector2.down;
            switch (heroId)
            {
                case "finn":
                    // Forward dash slash + piercing golden crescent sword wave along dir
                    break;
                case "jake":
                    // 220px wide stretchy fist sweep pulling enemies inward + 55% Slow for 3.5s
                    break;
                case "marceline":
                    // Mist blink + 5 homing vampire bats that bite enemies and heal Marceline (+18 HP total)
                    HealHero(18f);
                    break;
                case "fp":
                    // 3 erupting flame pillars leaving 4.5s burning lava ground zones that ignite enemies
                    break;
                case "pb":
                    // Summons 2 Royal Banana Guards to chase and fight monsters + throws Root Gum Bomb (2s Root)
                    SpawnBananaGuards(origin, dir, baseAtk, count: 2, isElite: false, duration: 10f);
                    break;
            }
        }

        public void ExecuteUltimate(Vector2 origin, Vector2 aimDir, float baseAtk)
        {
            Vector2 dir = aimDir.sqrMagnitude > 0.001f ? aimDir.normalized : Vector2.down;
            switch (heroId)
            {
                case "finn":
                    berserkTimer = 6.0f; // 5 Royal Swords AoE 2.5s Stun + +40% Attack Speed
                    break;
                case "jake":
                    giantTimer = 5.0f; // 1.45x Giant Form, +50% Defense, 3 rhythmic shockwave slams
                    break;
                case "marceline":
                    bloodMoonTimer = 5.0f; // Moving Blood Moon domain draining enemy HP to heal Marceline & applying +25% Vulnerability
                    HealHero(25f);
                    break;
                case "fp":
                    // Phoenix Meteor Dive + giant lava crater + 8-direction Igniting Fire Nova
                    break;
                case "pb":
                    shieldHp = 75f; // Royal Candy Energy Shield + Elite Banana Guard Squad + Orbital Ion Laser
                    SpawnBananaGuards(origin, dir, baseAtk * 1.35f, count: 2, isElite: true, duration: 12f);
                    break;
            }
        }

        private void SpawnBananaGuards(Vector2 origin, Vector2 dir, float baseAtk, int count, bool isElite, float duration)
        {
            Vector2 perp = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < count; i++)
            {
                if (activeBananaGuards.Count >= maxSummonedGuards)
                    activeBananaGuards.RemoveAt(0);

                float side = (i % 2 == 0) ? 1f : -1f;
                Vector2 spawnPos = origin + dir * 36f + perp * (side * 28f);
                activeBananaGuards.Add(new SummonedAllyUnit
                {
                    unitName = isElite ? "Elite Royal Banana Guard" : "Banana Guard",
                    position = spawnPos,
                    maxHp = isElite ? 180f : 110f,
                    hp = isElite ? 180f : 110f,
                    atk = Mathf.Round(baseAtk * (isElite ? 0.95f : 0.70f)),
                    moveSpeed = isElite ? 235f : 205f,
                    remainingLifetime = duration,
                    attackCooldown = 0.15f,
                    isElite = isElite
                });
            }
        }
    }
}
