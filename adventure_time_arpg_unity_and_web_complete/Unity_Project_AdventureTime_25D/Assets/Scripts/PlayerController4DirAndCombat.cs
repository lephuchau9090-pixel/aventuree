using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdventureTimeARPG
{
    public enum FacingDirection
    {
        Down = 0,
        Up = 1,
        Left = 2,
        Right = 3
    }

    public enum HeroAnimState
    {
        Idle = 0,
        Walk = 1,
        Attack = 2,
        Skill1 = 3,
        Ultimate = 4,
        Hurt = 5,
        Death = 6
    }

    /// <summary>
    /// Synchronizes 4-directional movement, deadzone + axis hysteresis facing, and sprite sheet frame selection:
    /// - Eliminates rapid facing flips when standing still or moving diagonally (|x| == |y|) via Axis Hysteresis.
    /// - Preserves lastValidFacing when input stops (< inputDeadzone).
    /// - Advances walk frames ONLY when the character actually moves; uses a single stable standing frame per direction when stopped.
    /// - Aligns basic attack, Skill 1, and Ultimate animations with the current facing direction.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerController4DirAndCombat : MonoBehaviour
    {
        [Header("Movement & 4-Way Facing Settings")]
        public float moveSpeed = 3.5f;
        [Range(0.08f, 0.35f)] public float inputDeadzone = 0.18f;
        [Range(0.05f, 0.30f)] public float axisHysteresis = 0.15f;

        [Header("Runtime State")]
        public FacingDirection currentFacing = FacingDirection.Down;
        public HeroAnimState animState = HeroAnimState.Idle;
        public Vector2 aimDirection = Vector2.down;
        public bool isMoving = false;
        public float walkCycleTime = 0f;
        public float actionStateTimer = 0f;
        public float actionAnimDuration = 0.30f;

        [Header("Normalized 64x64 Sprite Sheet Frames (32 columns per hero row)")]
        public Sprite[] heroRowFrames = new Sprite[32];

        private SpriteRenderer spriteRenderer;
        private DistinctHeroSkillSystem skillSystem;
        private GameOverAndStageFlowController gameOverController;
        private MobileTouchHUDController touchHud;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            skillSystem = GetComponent<DistinctHeroSkillSystem>();
            gameOverController = FindObjectOfType<GameOverAndStageFlowController>();
            touchHud = FindObjectOfType<MobileTouchHUDController>();
        }

        private void Update()
        {
            if (gameOverController != null && gameOverController.IsPlayerDead)
            {
                isMoving = false;
                animState = HeroAnimState.Death;
                UpdateSpriteFrame(Time.deltaTime);
                return;
            }

            Vector2 rawInput = ReadCombinedInput();
            float inputMag = rawInput.magnitude;

            Vector3 prevPos = transform.position;
            if (inputMag >= inputDeadzone)
            {
                Vector2 moveDir = rawInput.normalized;
                aimDirection = moveDir;
                UpdateFacingWithHysteresis(rawInput);

                float speedMult = (skillSystem != null && skillSystem.berserkTimer > 0f) ? 1.25f : 1.0f;
                Vector3 delta = new Vector3(moveDir.x, moveDir.y, 0f) * (moveSpeed * speedMult * Time.deltaTime);
                transform.position += delta;
            }

            float movedDist = (transform.position - prevPos).magnitude;
            isMoving = (inputMag >= inputDeadzone) && (movedDist > 0.0005f);

            if (actionStateTimer > 0f)
            {
                actionStateTimer -= Time.deltaTime;
                if (actionStateTimer <= 0f)
                {
                    animState = isMoving ? HeroAnimState.Walk : HeroAnimState.Idle;
                }
            }
            else
            {
                if (isMoving)
                {
                    animState = HeroAnimState.Walk;
                    walkCycleTime += Time.deltaTime;
                }
                else
                {
                    animState = HeroAnimState.Idle;
                    walkCycleTime = 0f; // Lock to stable standing frame without jitter
                }
            }

            HandleCombatHotkeys();
            UpdateSpriteFrame(Time.deltaTime);
        }

        private Vector2 ReadCombinedInput()
        {
            float ix = 0f;
            float iy = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) iy += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) iy -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) ix -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) ix += 1f;

            if (touchHud != null && touchHud.JoystickDirection.sqrMagnitude > 0.01f)
            {
                ix += touchHud.JoystickDirection.x;
                iy += touchHud.JoystickDirection.y;
            }

            Vector2 combined = new Vector2(ix, iy);
            if (combined.magnitude > 1f) combined.Normalize();
            return combined;
        }

        /// <summary>
        /// Updates 4-way facing using deadzone and axis hysteresis so slight axis variations or
        /// diagonal movement (|x| == |y|) never cause rapid flipping between two sprite rows.
        /// </summary>
        public void UpdateFacingWithHysteresis(Vector2 input)
        {
            float ax = Mathf.Abs(input.x);
            float ay = Mathf.Abs(input.y);
            if (ax < inputDeadzone && ay < inputDeadzone)
                return; // Keep currentFacing unchanged when idle

            bool currentlyHorizontal = (currentFacing == FacingDirection.Left || currentFacing == FacingDirection.Right);
            if (currentlyHorizontal)
            {
                // Stay horizontal unless vertical axis clearly exceeds horizontal by axisHysteresis
                if (ay > ax + axisHysteresis)
                {
                    currentFacing = input.y >= 0f ? FacingDirection.Up : FacingDirection.Down;
                }
                else if (ax >= inputDeadzone)
                {
                    currentFacing = input.x >= 0f ? FacingDirection.Right : FacingDirection.Left;
                }
            }
            else
            {
                // Stay vertical unless horizontal axis clearly exceeds vertical by axisHysteresis
                if (ax > ay + axisHysteresis)
                {
                    currentFacing = input.x >= 0f ? FacingDirection.Right : FacingDirection.Left;
                }
                else if (ay >= inputDeadzone)
                {
                    currentFacing = input.y >= 0f ? FacingDirection.Up : FacingDirection.Down;
                }
            }
        }

        public void SetFacingFromAimVector(Vector2 aimVec)
        {
            if (aimVec.sqrMagnitude < 0.01f) return;
            aimDirection = aimVec.normalized;
            UpdateFacingWithHysteresis(aimDirection);
        }

        public void TriggerActionAnimation(HeroAnimState state, float duration, Vector2 attackDir)
        {
            SetFacingFromAimVector(attackDir);
            animState = state;
            actionAnimDuration = Mathf.Max(0.1f, duration);
            actionStateTimer = actionAnimDuration;
        }

        private void HandleCombatHotkeys()
        {
            if (skillSystem == null) return;
            if (Input.GetKeyDown(KeyCode.J))
            {
                TriggerActionAnimation(HeroAnimState.Attack, 0.28f, aimDirection);
                skillSystem.ExecuteBasicAttack(transform.position, aimDirection, 28f);
            }
            else if (Input.GetKeyDown(KeyCode.K))
            {
                TriggerActionAnimation(HeroAnimState.Skill1, 0.40f, aimDirection);
                skillSystem.ExecuteSkill1(transform.position, aimDirection, 28f);
            }
            else if (Input.GetKeyDown(KeyCode.L))
            {
                TriggerActionAnimation(HeroAnimState.Ultimate, 0.55f, aimDirection);
                skillSystem.ExecuteUltimate(transform.position, aimDirection, 28f);
            }
        }

        private void UpdateSpriteFrame(float dt)
        {
            if (spriteRenderer == null || heroRowFrames == null || heroRowFrames.Length < 32)
                return;

            int col = 0;
            bool flipX = false;

            switch (animState)
            {
                case HeroAnimState.Idle:
                    // Single stable standing frame matching the last valid facing direction (no idle flickering)
                    if (currentFacing == FacingDirection.Down) col = 0;
                    else if (currentFacing == FacingDirection.Up) col = 5;
                    else if (currentFacing == FacingDirection.Left) col = 8;
                    else col = 11;
                    flipX = false;
                    break;

                case HeroAnimState.Walk:
                    int step = Mathf.FloorToInt(walkCycleTime * 9f) % 3;
                    if (currentFacing == FacingDirection.Down) col = 2 + step;
                    else if (currentFacing == FacingDirection.Up) col = 5 + step;
                    else if (currentFacing == FacingDirection.Left) col = 8 + step; // Already flipped left in normalized sheet
                    else col = 11 + step;
                    flipX = false;
                    break;

                case HeroAnimState.Attack:
                    float atkProg = 1f - Mathf.Clamp01(actionStateTimer / actionAnimDuration);
                    col = 14 + Mathf.Min(3, Mathf.FloorToInt(atkProg * 4f));
                    flipX = (currentFacing == FacingDirection.Left);
                    break;

                case HeroAnimState.Skill1:
                    col = 18 + (Mathf.FloorToInt((1f - actionStateTimer / actionAnimDuration) * 4f) % 2);
                    flipX = (currentFacing == FacingDirection.Left);
                    break;

                case HeroAnimState.Ultimate:
                    col = 20 + (Mathf.FloorToInt((1f - actionStateTimer / actionAnimDuration) * 4f) % 2);
                    flipX = (currentFacing == FacingDirection.Left);
                    break;

                case HeroAnimState.Hurt:
                    col = 26 + (Mathf.FloorToInt(Time.time * 10f) % 2);
                    flipX = (currentFacing == FacingDirection.Left);
                    break;

                case HeroAnimState.Death:
                    col = 28;
                    flipX = false;
                    break;
            }

            if (heroRowFrames[col] != null)
            {
                spriteRenderer.sprite = heroRowFrames[col];
                spriteRenderer.flipX = flipX;
            }
        }
    }
}
