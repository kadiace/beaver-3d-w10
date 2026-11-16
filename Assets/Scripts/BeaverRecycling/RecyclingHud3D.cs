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
        private const int HEART_COUNT = 5;
        private static readonly Color HeartFilled = new Color(1f, 0.28f, 0.39f, 1f);
        private static readonly Color HeartEmpty = new Color(0.3f, 0.16f, 0.21f, 0.9f);
        private readonly Image[] _lifeHearts = new Image[HEART_COUNT];
        private Sprite _heartSprite;
        private Texture2D _heartTexture;
        private int _shownLives = -1;

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

        // Bottom-edge labels may overlap. Each visible item has its own text.
        private const float OFFSCREEN_BASE_Y = 157f;
        private const float OFFSCREEN_LABEL_WIDTH = 160f;
        private readonly Dictionary<RecyclingItem, Text> _offscreenLabelsByItem =
            new Dictionary<RecyclingItem, Text>();
        private readonly Stack<Text> _offscreenUnusedLabels = new Stack<Text>();
        private readonly HashSet<RecyclingItem> _offscreenVisibleItems = new HashSet<RecyclingItem>();
        private readonly List<RecyclingItem> _offscreenExpiredItems = new List<RecyclingItem>();

        /// <summary>게임패드 연결/해제 시 인게임 HUD 키 안내를 현재 장치에 맞게 갱신한다.</summary>
        private void RefreshControls(bool force = false)
        {
            if (_controls == null) return;
            bool gamepadConnected = Managers.Input.GamePadConnected;
            if (!force && _displayedGamepadConnected == gamepadConnected) return;
            _displayedGamepadConnected = gamepadConnected;
            _controls.text = gamepadConnected
                ? "왼쪽 스틱: 이동/방향 전환 (운반 중 자동 전진)   South: 집기/놓기   West: 묶음 분리   Menu: 일시정지"
                : "방향키: 이동/방향 전환 (운반 중 자동 전진)   Z: 집기/놓기   X: 묶음 분리   ESC: 일시정지";
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
            CreatePixelHearts();
            RefreshLives(HEART_COUNT);
            RefreshControls(true);
            SetupModalFocus();
        }


        /// <summary>캔버스 좌측 상단에 13x11 도트 하트 5개를 배치한다. UI와 텍스처는 최초 1회 생성한다.</summary>
        private void CreatePixelHearts()
        {
            // Every lit texel is a crisp square; no font, emoji or texture filtering involved.
            string[] rows =
            {
                "0011100011100",
                "0111110111110",
                "1111111111111",
                "1111111111111",
                "1111111111111",
                "0111111111110",
                "0011111111100",
                "0001111111000",
                "0000111110000",
                "0000011100000",
                "0000001000000"
            };
            int width = rows[0].Length;
            int height = rows.Length;
            _heartTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Recycling Pixel Heart",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = rows[height - y - 1][x] == '1'
                        ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            _heartTexture.SetPixels32(pixels);
            _heartTexture.Apply(false, true);
            _heartSprite = Sprite.Create(_heartTexture, new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.5f), 100f);

            GameObject holder = new GameObject("Pixel Hearts", typeof(RectTransform));
            RectTransform holderRect = (RectTransform)holder.transform;
            holderRect.SetParent(_root, false);
            holderRect.anchorMin = holderRect.anchorMax = new Vector2(0f, 1f);
            holderRect.pivot = new Vector2(0f, 1f);
            holderRect.anchoredPosition = new Vector2(30f, -30f);
            holderRect.sizeDelta = new Vector2(215f, 33f);

            for (int i = 0; i < HEART_COUNT; i++)
            {
                GameObject icon = new GameObject("Heart " + (i + 1), typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                RectTransform rect = (RectTransform)icon.transform;
                rect.SetParent(holderRect, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(i * 44f, 0f);
                rect.sizeDelta = new Vector2(39f, 33f);

                Image heart = icon.GetComponent<Image>();
                heart.sprite = _heartSprite;
                heart.raycastTarget = false;
                Outline outline = icon.AddComponent<Outline>();
                outline.effectColor = new Color(0.07f, 0.04f, 0.07f, 0.95f);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = false;
                _lifeHearts[i] = heart;
            }
        }

        /// <summary>남아 있는 목숨만 붉은 도트 하트로, 잃은 목숨은 어두운 하트로 표시한다.</summary>
        public void RefreshLives(int lives)
        {
            int count = Mathf.Clamp(lives, 0, HEART_COUNT);
            if (_shownLives == count) return;
            _shownLives = count;
            for (int i = 0; i < HEART_COUNT; i++)
                if (_lifeHearts[i] != null)
                    _lifeHearts[i].color = i < count ? HeartFilled : HeartEmpty;
        }

        void OnDestroy()
        {
            if (_heartSprite != null) Destroy(_heartSprite);
            if (_heartTexture != null) Destroy(_heartTexture);
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
            Action<RecyclingSubmission, int, IReadOnlyList<RecyclingDefinition>> completed)
        {
            RecyclingSortingWindow3D window = Instantiate(_sortingWindowPrefab, _root);
            window.transform.SetAsLastSibling();
            window.Play(definitions, chute, (submission, includedCount, rejectedDefinitions) =>
            {
                Destroy(window.gameObject);
                completed(submission, includedCount, rejectedDefinitions);
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


        /// <summary>화면 아래(-Z)로 벗어난 기물을 오브젝트마다 한 개의 재질색 텍스트로 표시한다.</summary>
        public void RefreshOffscreenItems(IEnumerable<RecyclingItem> items, Camera camera, float groundY, bool show)
        {
            _offscreenVisibleItems.Clear();
            if (show && camera != null && _root != null)
            {
                float width = Mathf.Max(1f, _root.rect.width);
                foreach (RecyclingItem item in items)
                {
                    if (item == null || !item.isActiveAndEnabled || item.IsResolved ||
                        item.IsHeld || item.Definition == null) continue;

                    // Height must not move a downstream (-Z) object to another screen edge.
                    Vector3 position = item.Body.position;
                    position.y = groundY;
                    Vector3 viewport = camera.WorldToViewportPoint(position);
                    if (viewport.z <= 0f || viewport.y >= 0f) continue;

                    _offscreenVisibleItems.Add(item);
                    if (!_offscreenLabelsByItem.TryGetValue(item, out Text label))
                    {
                        label = _offscreenUnusedLabels.Count > 0
                            ? _offscreenUnusedLabels.Pop() : CreateOffscreenLabel();
                        _offscreenLabelsByItem.Add(item, label);
                    }

                    label.text = "▼ " + RecyclingRules.Name(item.Definition.Category);
                    label.color = RecyclingRules.Color(item.Definition.Category);
                    RectTransform rect = (RectTransform)label.transform;
                    // No horizontal spacing or rows: labels at similar positions may overlap.
                    rect.anchoredPosition = new Vector2(
                        Mathf.Clamp(viewport.x * width, OFFSCREEN_LABEL_WIDTH * 0.5f,
                            Mathf.Max(OFFSCREEN_LABEL_WIDTH * 0.5f, width - OFFSCREEN_LABEL_WIDTH * 0.5f)),
                        OFFSCREEN_BASE_Y);
                    if (!label.gameObject.activeSelf)
                    {
                        // Unity UI draws later siblings over earlier ones. Reused labels
                        // move to the top only on appearance; existing labels keep their order.
                        label.transform.SetAsLastSibling();
                        label.gameObject.SetActive(true);
                    }
                }
            }

            _offscreenExpiredItems.Clear();
            foreach (KeyValuePair<RecyclingItem, Text> entry in _offscreenLabelsByItem)
                if (!_offscreenVisibleItems.Contains(entry.Key))
                {
                    entry.Value.gameObject.SetActive(false);
                    _offscreenUnusedLabels.Push(entry.Value);
                    _offscreenExpiredItems.Add(entry.Key);
                }
            foreach (RecyclingItem item in _offscreenExpiredItems)
                _offscreenLabelsByItem.Remove(item);
        }

        private Text CreateOffscreenLabel()
        {
            GameObject indicator = new GameObject("Offscreen material", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = (RectTransform)indicator.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(OFFSCREEN_LABEL_WIDTH, 27f);
            Text label = indicator.GetComponent<Text>();
            label.font = _controls.font;
            label.fontSize = 20;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            indicator.SetActive(false);
            return label;
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
