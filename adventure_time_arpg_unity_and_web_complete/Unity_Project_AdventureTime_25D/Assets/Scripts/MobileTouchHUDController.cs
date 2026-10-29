using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AdventureTimeARPG
{
    /// <summary>
    /// Safe-area responsive mobile & web touch controls matching the Adventure Time pixel-art theme:
    /// - Left virtual joystick with deadzone and unified visual/touch coordinates.
    /// - Right 6-button action cluster in bottom-right safe area:
    ///   Basic Attack, Skill 1, Ultimate, Dodge/Dash, Heal Potion, and Context Interact.
    /// - Uses the exact same RectTransform screen bounds for both rendering and touch hit detection
    ///   while keeping PC Keyboard (WASD/Arrows + J/K/L/Shift/Q/F) controls active.
    /// </summary>
    public class MobileTouchHUDController : MonoBehaviour
    {
        [Header("Safe Area Containers")]
        public RectTransform safeAreaContainer;
        public RectTransform joystickBackground;
        public RectTransform joystickKnob;
        public float joystickRadius = 46f;
        [Range(0.10f, 0.30f)] public float joystickDeadzone = 0.18f;

        public Vector2 JoystickDirection { get; private set; }
        public bool IsAttackHeld { get; private set; }

        [Header("6 Action Buttons (Matched Render & Hit Rects)")]
        public Button btnAttack;
        public Button btnSkill1;
        public Button btnUltimate;
        public Button btnDash;
        public Button btnPotion;
        public Button btnInteract;

        public event Action OnBasicAttackPressed;
        public event Action OnSkill1Pressed;
        public event Action OnUltimatePressed;
        public event Action OnDashPressed;
        public event Action OnPotionPressed;
        public event Action OnInteractPressed;

        private Rect lastSafeArea;
        private int activeJoystickPointerId = -999;

        private void Awake()
        {
            ApplySafeAreaLayout();
            BindButtons();
        }

        private void Update()
        {
            if (Screen.safeArea != lastSafeArea)
            {
                ApplySafeAreaLayout();
            }
            UpdateJoystickTouchInput();
        }

        public void ApplySafeAreaLayout()
        {
            lastSafeArea = Screen.safeArea;
            if (safeAreaContainer == null) return;

            Vector2 anchorMin = lastSafeArea.position;
            Vector2 anchorMax = lastSafeArea.position + lastSafeArea.size;
            anchorMin.x /= Mathf.Max(1f, Screen.width);
            anchorMin.y /= Mathf.Max(1f, Screen.height);
            anchorMax.x /= Mathf.Max(1f, Screen.width);
            anchorMax.y /= Mathf.Max(1f, Screen.height);

            safeAreaContainer.anchorMin = anchorMin;
            safeAreaContainer.anchorMax = anchorMax;
            safeAreaContainer.offsetMin = Vector2.zero;
            safeAreaContainer.offsetMax = Vector2.zero;
        }

        private void BindButtons()
        {
            if (btnAttack != null) btnAttack.onClick.AddListener(() => OnBasicAttackPressed?.Invoke());
            if (btnSkill1 != null) btnSkill1.onClick.AddListener(() => OnSkill1Pressed?.Invoke());
            if (btnUltimate != null) btnUltimate.onClick.AddListener(() => OnUltimatePressed?.Invoke());
            if (btnDash != null) btnDash.onClick.AddListener(() => OnDashPressed?.Invoke());
            if (btnPotion != null) btnPotion.onClick.AddListener(() => OnPotionPressed?.Invoke());
            if (btnInteract != null) btnInteract.onClick.AddListener(() => OnInteractPressed?.Invoke());
        }

        private void UpdateJoystickTouchInput()
        {
            if (joystickBackground == null || joystickKnob == null) return;

            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began && activeJoystickPointerId == -999)
                    {
                        if (RectTransformUtility.RectangleContainsScreenPoint(joystickBackground, t.position, null))
                        {
                            activeJoystickPointerId = t.fingerId;
                            ProcessJoystickScreenPoint(t.position);
                        }
                    }
                    else if (t.fingerId == activeJoystickPointerId)
                    {
                        if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                        {
                            ProcessJoystickScreenPoint(t.position);
                        }
                        else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                        {
                            ResetJoystick();
                        }
                    }
                }
            }
            else if (activeJoystickPointerId != -999 && !Input.GetMouseButton(0))
            {
                ResetJoystick();
            }
        }

        private void ProcessJoystickScreenPoint(Vector2 screenPos)
        {
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(joystickBackground, screenPos, null, out localPoint))
            {
                Vector2 clamped = Vector2.ClampMagnitude(localPoint, joystickRadius);
                joystickKnob.anchoredPosition = clamped;
                Vector2 norm = clamped / Mathf.Max(1f, joystickRadius);
                JoystickDirection = (norm.magnitude >= joystickDeadzone) ? norm : Vector2.zero;
            }
        }

        public void ResetJoystick()
        {
            activeJoystickPointerId = -999;
            JoystickDirection = Vector2.zero;
            if (joystickKnob != null)
                joystickKnob.anchoredPosition = Vector2.zero;
        }
    }
}
