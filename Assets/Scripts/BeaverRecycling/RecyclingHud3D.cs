using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BeaverRecycling
{
    public sealed class RecyclingHud3D : MonoBehaviour
    {
        [Header("HUD")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private Text _day;
        [SerializeField] private Text _score;
        [SerializeField] private Text _wood;
        [SerializeField] private Text _carry;
        [SerializeField] private Text _controls;
        [SerializeField] private Text _message;
        [SerializeField] private Text _hover;
        [SerializeField] private Text _toast;
        [SerializeField] private Button _pauseButton;

        [Header("Result and upgrades")]
        [SerializeField] private GameObject _modal;
        [SerializeField] private Text _modalTitle;
        [SerializeField] private Text _modalBody;
        [SerializeField] private Button _primary;
        [SerializeField] private Text _primaryText;
        [SerializeField] private Button _secondary;
        [SerializeField] private Text _secondaryText;
        [SerializeField] private GameObject _upgradeGroup;
        [SerializeField] private Button[] _upgradeButtons;
        [SerializeField] private Text[] _upgradeTexts;
        [Header("Sorting window")]
        [SerializeField] private RecyclingSortingWindow3D _sortingWindowPrefab;
        private float _messageUntil;
        private float _toastUntil;
        private bool _displayedGamepadConnected;
        private Button[] _modalButtons;
        private Outline[] _focusOutlines;

        /// <summary>게임패드 연결/해제 시 인게임 HUD 키 안내를 현재 장치에 맞게 갱신한다.</summary>
        private void RefreshControls(bool force = false)
        {
            if (_controls == null) return;
            bool gamepadConnected = Managers.Input.GamePadConnected;
            if (!force && _displayedGamepadConnected == gamepadConnected) return;
            _displayedGamepadConnected = gamepadConnected;
            _controls.text = gamepadConnected
                ? "왼쪽 스틱: 이동   South: 집기/놓기   West: 묶음 분리   Menu: 일시정지"
                : "방향키: 이동   X: 집기/놓기   Z: 묶음 분리   ESC: 일시정지";
        }

        /// <summary>게임 콜백을 받아 씬에 저장된 버튼과 업그레이드 조작을 연결한다.</summary>
        public void Initialize(RecyclingGame3D game)
        {
            _pauseButton.onClick.AddListener(game.TogglePause);
            _primary.onClick.AddListener(game.PrimaryAction);
            _secondary.onClick.AddListener(game.SecondaryAction);
            for (int i = 0; i < _upgradeButtons.Length; i++)
            {
                int index = i;
                _upgradeButtons[i].onClick.AddListener(() => game.BuyUpgrade(index));
            }
            _hover.transform.parent.gameObject.SetActive(false);
            _toast.transform.parent.gameObject.SetActive(false);
            _message.text = "";
            RefreshControls(true);
            SetupModalFocus();
        }

        /// <summary>모달 버튼에 게임패드 내비게이션과 선택 시 강조할 굵은 외곽선을 준비한다.</summary>
        private void SetupModalFocus()
        {
            _modalButtons = new Button[_upgradeButtons.Length + 2];
            _modalButtons[0] = _primary;
            _modalButtons[1] = _secondary;
            Array.Copy(_upgradeButtons, 0, _modalButtons, 2, _upgradeButtons.Length);
            _focusOutlines = new Outline[_modalButtons.Length];

            foreach (Button button in _modalButtons)
            {
                Navigation navigation = button.navigation;
                navigation.mode = Navigation.Mode.Automatic;
                button.navigation = navigation;
            }

            for (int i = 0; i < _modalButtons.Length; i++)
            {
                Outline outline = _modalButtons[i].gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 0.94f, 0.25f, 1f);
                outline.effectDistance = new Vector2(6f, -6f);
                outline.useGraphicAlpha = false;
                outline.enabled = false;
                _focusOutlines[i] = outline;
            }

            // During gameplay the pad south button means pickup, not UI submission.
            // In recycling mode, Escape/Menu opens the modal; the pointer is disabled.
            Navigation pauseNavigation = _pauseButton.navigation;
            pauseNavigation.mode = Navigation.Mode.None;
            _pauseButton.navigation = pauseNavigation;
        }

        /// <summary>현재 모달에서 선택 가능한 버튼에 포커스를 유지하고 없으면 기본 버튼을 선택한다.</summary>
        private void FocusModal()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || !_modal.activeInHierarchy) return;
            if (IsValidModalSelection(eventSystem.currentSelectedGameObject)) return;
            foreach (Button button in _modalButtons)
            {
                if (!button.gameObject.activeInHierarchy || !button.IsInteractable()) continue;
                eventSystem.SetSelectedGameObject(button.gameObject);
                return;
            }
        }

        private bool IsValidModalSelection(GameObject selected)
        {
            if (selected == null || _modalButtons == null) return false;
            foreach (Button button in _modalButtons)
                if (button.gameObject == selected && button.gameObject.activeInHierarchy && button.IsInteractable())
                    return true;
            return false;
        }

        /// <summary>패드 제어 스킴이 활성화돼도 키보드 방향키와 확인 키로 모달을 조작할 수 있게 한다.</summary>
        private void HandleKeyboardModalInput()
        {
            // The normal UI input module handles keyboard events when it receives them.
            // Only dispatch a fallback when its active control is not the keyboard.
            if (!Managers.Input.GamePadConnected) return;
            Keyboard keyboard = Keyboard.current;
            EventSystem eventSystem = EventSystem.current;
            if (keyboard == null || eventSystem == null) return;

            InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
            InputAction moveAction = module != null && module.move != null ? module.move.action : null;
            InputAction submitAction = module != null && module.submit != null ? module.submit.action : null;

            MoveDirection direction = MoveDirection.None;
            if (keyboard.upArrowKey.wasPressedThisFrame) direction = MoveDirection.Up;
            else if (keyboard.downArrowKey.wasPressedThisFrame) direction = MoveDirection.Down;
            else if (keyboard.leftArrowKey.wasPressedThisFrame) direction = MoveDirection.Left;
            else if (keyboard.rightArrowKey.wasPressedThisFrame) direction = MoveDirection.Right;

            if (direction != MoveDirection.None &&
                !(moveAction != null && moveAction.activeControl != null &&
                  moveAction.activeControl.device == keyboard && moveAction.WasPerformedThisFrame()))
            {
                Vector2 vector = direction == MoveDirection.Up ? Vector2.up :
                    direction == MoveDirection.Down ? Vector2.down :
                    direction == MoveDirection.Left ? Vector2.left : Vector2.right;
                AxisEventData data = new AxisEventData(eventSystem) { moveDir = direction, moveVector = vector };
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, data, ExecuteEvents.moveHandler);
            }

            bool confirm = keyboard.spaceKey.wasPressedThisFrame ||
                keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            if (confirm && !(submitAction != null && submitAction.activeControl != null &&
                submitAction.activeControl.device == keyboard && submitAction.WasPressedThisFrame()))
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject,
                    new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
        }

        /// <summary>일차·시간·점수·목재·운반 정보를 받아 최소 HUD 수치를 갱신한다.</summary>
        public void Refresh(int day, string phase, float seconds, int score, int target, int wood, string carry, bool working)
        {
            _day.text = day + "일차  ·  " + phase + (working ? " " + Mathf.CeilToInt(seconds) + "초" : "");
            _score.text = "획득 점수 " + score + " / " + target;
            _wood.text = "목재 " + wood;
            _carry.text = carry;
            _pauseButton.interactable = working;
        }

        /// <summary>제목·결과 문구·버튼 이름과 상점 여부를 받아 정산 또는 준비 패널을 표시한다.</summary>
        public void ShowModal(string title, string body, string primary, string secondary, bool upgrades)
        {
            _modal.SetActive(true);
            _pauseButton.interactable = false;
            _modalTitle.text = title;
            _modalBody.text = body;
            _primaryText.text = primary;
            _secondary.gameObject.SetActive(!string.IsNullOrEmpty(secondary));
            _secondaryText.text = secondary;
            _upgradeGroup.SetActive(upgrades);
            RectTransform primaryRect = (RectTransform)_primary.transform;
            primaryRect.anchoredPosition = new Vector2(string.IsNullOrEmpty(secondary) ? 0f : -190f, primaryRect.anchoredPosition.y);
            _hover.transform.parent.gameObject.SetActive(false);
            FocusModal();
        }

        /// <summary>출근 또는 재개 시 고정 모달 패널을 닫는다.</summary>
        public void HideModal()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && IsValidModalSelection(eventSystem.currentSelectedGameObject))
                eventSystem.SetSelectedGameObject(null);
            _modal.SetActive(false);
        }

        /// <summary>제출 아이템과 투입구 종류를 받아 중앙 분류 창을 표시하고 제외 후 정산 결과를 알린다.</summary>
        public void ShowSorting(IReadOnlyList<RecyclingDefinition> definitions, RecyclingCategory chute,
            Action<RecyclingSubmission, int> completed)
        {
            RecyclingSortingWindow3D window = Instantiate(_sortingWindowPrefab, _root);
            window.transform.SetAsLastSibling();
            window.Play(definitions, chute, (submission, includedCount) =>
            {
                Destroy(window.gameObject);
                completed(submission, includedCount);
            });
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _hover.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>업그레이드 인덱스와 설명·구매 가능 상태를 받아 해당 구매 버튼을 갱신한다.</summary>
        public void SetUpgrade(int index, string description, bool available)
        {
            _upgradeTexts[index].text = description;
            _upgradeButtons[index].interactable = available;
            FocusModal();
        }

        /// <summary>짧은 조작 결과를 받아 HUD에 제한 시간 동안 표시한다.</summary>
        public void Message(string message)
        {
            _message.text = message;
            _messageUntil = Time.unscaledTime + 2f;
        }

        /// <summary>포인터 또는 기물의 화면 위치와 설명을 받아 화면 안에 호버 표지를 배치한다.</summary>
        public void Hint(string text, Vector2 screen)
        {
            _hover.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(text));
            if (string.IsNullOrEmpty(text)) return;
            _hover.text = text;
            Place((RectTransform)_hover.transform.parent, screen + new Vector2(24f, 35f));
        }

        /// <summary>점수·목재 문구와 정산 위치를 받아 월드 위치에 붙는 피드백을 표시한다.</summary>
        public void Event(string text, Vector3 world, Camera camera, Color color)
        {
            _toast.text = text;
            _toast.color = color;
            _toast.transform.parent.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 1.5f;
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f) screen = new Vector3(Screen.width * 0.5f, 130f, 0f);
            Place((RectTransform)_toast.transform.parent, screen);
        }

        /// <summary>UI 대상과 화면 좌표를 받아 Canvas 좌표로 변환하고 안전 영역 안에 둔다.</summary>
        private void Place(RectTransform rect, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
            Vector2 half = _root.rect.size * 0.5f;
            local.x = Mathf.Clamp(local.x, -half.x + 180f, half.x - 180f);
            local.y = Mathf.Clamp(local.y, -half.y + 120f, half.y - 110f);
            rect.anchoredPosition = local;
        }

        void Update()
        {
            RefreshControls();
            if (Time.unscaledTime > _messageUntil) _message.text = "";
            if (Time.unscaledTime > _toastUntil) _toast.transform.parent.gameObject.SetActive(false);
            if (_focusOutlines == null) return;
            if (_modal.activeInHierarchy)
            {
                FocusModal();
                HandleKeyboardModalInput();
            }
            GameObject selected = _modal.activeInHierarchy && EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            for (int i = 0; i < _focusOutlines.Length; i++)
                _focusOutlines[i].enabled = selected != null &&
                    _modalButtons[i].gameObject == selected && _modalButtons[i].IsInteractable();
        }
    }
}
