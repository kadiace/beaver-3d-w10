using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RestoreCastle3D
{
    /// <summary>원본 2D RestoreCastle UI 프리팹을 3D 물리 게임의 상태와 연결한다.</summary>
    public sealed class CastleHud3D : MonoBehaviour
    {
        private readonly Button[] _cards = new Button[3];
        private readonly Text[] _names = new Text[3];
        private readonly Image[] _silhouettes = new Image[3];
        private readonly Image[] _backgrounds = new Image[3];
        private readonly string[] _shownIds = new string[3];
        private readonly Dictionary<string, Sprite> _generatedIcons = new Dictionary<string, Sprite>();

        private CastleGame3D _game;
        private Text _stage;
        private Text _round;
        private Text _durability;
        private Text _target;
        private Text _rerollStat;
        private Text _rerollAction;
        private Text _detail;
        private Text _effects;
        private Text _stageTitle;
        private Text _stageBody;
        private Text _completeBody;
        private Text _rulesBody;
        private Text _tooltipText;
        private GameObject _tooltip;
        private GameObject _stagePopup;
        private GameObject _completePopup;
        private GameObject _rulesPopup;
        private Button _rerollButton;
        private Button _topRerollButton;
        private CastlePiece3D _hoveredPiece;
        private int _hoveredCard = -1;
        private const float TooltipMargin = 14f;
        private const float TooltipGap = 16f;

        [Header("Candidate panel slide")]
        [SerializeField, Min(0.05f)] private float candidateSlideDuration = 0.36f;
        [SerializeField]
        private AnimationCurve candidateSlideCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        private RectTransform _candidatePanel;
        private Vector2 _candidatePanelShown;
        private float _candidateSlide;
        private CastleDropGuideGraphic3D _dropGuide;

        /// <summary>게임 인스턴스 및 원본 HUD 프리팹의 버튼과 상태 텍스트를 연결한다.</summary>
        public void Initialize(CastleGame3D game)
        {
            _game = game;
            _stage = FindText("TopBar/StageStat/Value");
            _round = FindText("TopBar/RoundStat/Value");
            _durability = FindText("TopBar/DurabilityStat/Value");
            _target = FindText("TopBar/TargetStat/Value");
            _rerollStat = FindText("TopBar/RerollStat/Label");
            _detail = FindText("PlacementDetailPanel/DetailText");
            _effects = FindText("ActiveEffectsPanel/Hint");
            _stagePopup = transform.Find("StageResultPopup").gameObject;
            _stageTitle = FindText("StageResultPopup/Heading/TitleText");
            _stageBody = FindText("StageResultPopup/Contents/BodyText");
            _completePopup = transform.Find("RunCompletePopup").gameObject;
            _completeBody = FindText("RunCompletePopup/Contents/BodyText");
            _rulesPopup = transform.Find("RulesPopup").gameObject;
            _rulesBody = FindText("RulesPopup/Contents/BodyText");
            _tooltip = transform.Find("CandidateMaterialTooltip").gameObject;
            _tooltipText = FindText("CandidateMaterialTooltip/CandidateMaterialTooltipText");
            _tooltipText.supportRichText = true;
            _effects.supportRichText = true;

            // The original 2D HUD is instantiated into a different game scene.
            // Use an explicit top-left anchor and pivot for predictable placement.
            RectTransform tooltipRect = (RectTransform)_tooltip.transform;
            tooltipRect.anchorMin = new Vector2(0f, 1f);
            tooltipRect.anchorMax = new Vector2(0f, 1f);
            tooltipRect.pivot = new Vector2(0f, 1f);
            tooltipRect.localRotation = Quaternion.identity;
            tooltipRect.localScale = Vector3.one;
            tooltipRect.SetAsLastSibling();

            // Limit text rendering to the panel so lengthy descriptions cannot escape.
            _tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tooltipText.verticalOverflow = VerticalWrapMode.Truncate;
            _tooltipText.resizeTextForBestFit = true;
            _tooltipText.resizeTextMinSize = 12;
            _tooltipText.resizeTextMaxSize = 19;

            _candidatePanel = transform.Find("CandidateSelectionRow").GetComponent<RectTransform>();
            _candidatePanelShown = _candidatePanel.anchoredPosition;

            // One Graphic combines all dashes into a single UI draw call.
            GameObject guide = new GameObject("Drop Prediction Dashed Line",
                typeof(RectTransform), typeof(CanvasRenderer),
                typeof(CastleDropGuideGraphic3D));
            guide.transform.SetParent(transform, false);
            RectTransform guideRect = (RectTransform)guide.transform;
            guideRect.anchorMin = Vector2.zero;
            guideRect.anchorMax = Vector2.one;
            guideRect.offsetMin = Vector2.zero;
            guideRect.offsetMax = Vector2.zero;
            guideRect.SetAsFirstSibling();
            _dropGuide = guide.GetComponent<CastleDropGuideGraphic3D>();
            _dropGuide.color = new Color(0.33f, 0.96f, 0.92f, 0.86f);
            _dropGuide.raycastTarget = false;
            guide.SetActive(false);

            Transform candidates = transform.Find("CandidateSelectionRow/CandidateRow");
            for (int i = 0; i < _cards.Length; i++)
            {
                int index = i;
                Transform candidate = candidates.GetChild(i);
                _cards[i] = candidate.GetComponent<Button>();
                _names[i] = candidate.Find("MaterialName").GetComponent<Text>();
                _silhouettes[i] = candidate.Find("Silhouette").GetComponent<Image>();
                _backgrounds[i] = candidate.GetComponent<Image>();
                _cards[i].onClick.AddListener(() => _game.SelectOffer(index));
                CastleCandidateHover3D hover = candidate.gameObject.AddComponent<CastleCandidateHover3D>();
                hover.Initialize(this, index);
            }

            _rerollButton = transform.Find("CandidateSelectionRow/CandidateActions/RerollActionButton").GetComponent<Button>();
            _rerollAction = _rerollButton.GetComponentInChildren<Text>();
            _rerollButton.onClick.AddListener(_game.Reroll);
            _topRerollButton = transform.Find("TopBar/RerollStat").GetComponent<Button>();
            _topRerollButton.onClick.AddListener(_game.Reroll);

            // The 2D manual removal action was replaced by the barrel explosion in 3D.
            transform.Find("TopBar/BombStat").gameObject.SetActive(false);
            transform.Find("CandidateSelectionRow/CandidateActions/BombActionButton").gameObject.SetActive(false);
            transform.Find("TopBar/RulesButton").GetComponent<Button>().onClick.AddListener(ShowRules);
            transform.Find("RulesPopup/CloseButton").GetComponent<Button>().onClick.AddListener(HideRules);
            transform.Find("StageResultPopup/CloseButton").GetComponent<Button>().onClick.AddListener(_game.ContinueStage);
            transform.Find("RunCompletePopup/CloseButton").GetComponent<Button>().onClick.AddListener(_game.Restart);

            _stagePopup.SetActive(false);
            _completePopup.SetActive(false);
            _rulesPopup.SetActive(false);
            _tooltip.SetActive(false);
            _rulesBody.text = "자재를 선택한 뒤 빈 공간을 클릭해 높은 곳에서 떨어뜨리세요.\n"
                + "접촉 중인 기물끼리 내구도 능력이 적용됩니다.\n"
                + "우클릭 드래그: 카메라 XZ 이동 / 휠: 확대·축소\n"
                + "Q/E: 기물 회전 / Esc: 선택 취소\n"
                + "오크통: 첫 충돌 1초 뒤 주변 기물을 날림 (오크통 유지)\n"
                + "첫 드랍 전에는 후보를 변경할 수 있습니다.\n"
                + "매 드랍 후 선택 UI를 숨깁니다. Esc/우클릭으로 다시 열어 다른 자재 선택 가능.\n"
                + "첫 드랍 이후에는 리롤 불가. 라운드 전체 5개 배치.\n"
                + "5라운드마다 내구도를 판정하며, 통과 팝업을 닫으면 홍수에 닿은 자재가 제거됩니다.";
            Refresh();
        }

        private void Update()
        {
            if (_game == null) return;
            AnimateCandidatePanel();
            Refresh();
        }

        /// <summary>첫 실제 드랍을 시작할 때 아래로 숨기고 다음 라운드에 다시 올린다.</summary>
        private void AnimateCandidatePanel()
        {
            if (_candidatePanel == null) return;
            float target = _game.IsRoundPlacing ? 1f : 0f;
            _candidateSlide = Mathf.MoveTowards(_candidateSlide, target,
                Time.unscaledDeltaTime / Mathf.Max(0.05f, candidateSlideDuration));
            float eased = candidateSlideCurve == null
                ? Mathf.SmoothStep(0f, 1f, _candidateSlide)
                : candidateSlideCurve.Evaluate(_candidateSlide);
            float hideDistance = _candidatePanel.rect.height + _candidatePanelShown.y + 32f;
            _candidatePanel.anchoredPosition = _candidatePanelShown
                + Vector2.down * hideDistance * eased;
        }

        /// <summary>낙하 시작점과 미리보기를 잇고 배치 가능 여부에 따라 점선 색을 바꾼다.</summary>
        public void ShowDropGuide(Vector2 launchScreen, Vector2 landingScreen, bool canPlace)
        {
            if (_dropGuide == null) return;
            Color guideColor = canPlace
                ? new Color(0.33f, 0.96f, 0.92f, 0.86f)
                : new Color(1f, 0.18f, 0.18f, 0.86f);
            if (_dropGuide.color != guideColor) _dropGuide.color = guideColor;
            if (!_dropGuide.gameObject.activeSelf) _dropGuide.gameObject.SetActive(true);
            _dropGuide.SetScreenEndpoints(launchScreen, landingScreen);
        }

        public void HideDropGuide()
        {
            if (_dropGuide != null && _dropGuide.gameObject.activeSelf)
                _dropGuide.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (Sprite sprite in _generatedIcons.Values)
            {
                if (sprite == null) continue;
                Texture2D texture = sprite.texture;
                Destroy(sprite);
                if (texture != null) Destroy(texture);
            }
        }

        /// <summary>프리팹의 하위 경로에서 UGUI 텍스트 참조를 찾아 반환한다.</summary>
        private Text FindText(string path) => transform.Find(path).GetComponent<Text>();

        /// <summary>상단 스테이지 정보, 하단 카드, 버튼, 팝업을 현재 게임 상태로 갱신한다.</summary>
        private void Refresh()
        {
            _stage.text = "STAGE " + _game.StageNumber + " / " + _game.StageCount;
            _round.text = "ROUND " + _game.CurrentStageRound + " / " + _game.RoundsPerStage
                + (_game.PlacementsThisRound > 0 ? "   배치 " + _game.PlacementsThisRound
                    + " / " + _game.PlacementsPerRound : "");
            _durability.text = "내구도 " + _game.Durability;
            _target.text = "목표 " + _game.TargetDurability;
            _rerollStat.text = "리롤 " + _game.RerollCount;
            _rerollAction.text = "리롤\n" + _game.RerollCount;
            if (_game.IsRoundPlacing && _hoveredCard >= 0)
            {
                _hoveredCard = -1;
                _tooltip.SetActive(false);
            }
            _rerollButton.interactable = _game.CanReroll && _game.RerollCount > 0;
            _topRerollButton.interactable = _rerollButton.interactable;
            _detail.text = _game.StatusMessage ?? "";
            if (_hoveredPiece != null && !_game.IsPlacedPiece(_hoveredPiece))
            {
                _hoveredPiece = null;
                if (_hoveredCard < 0) _tooltip.SetActive(false);
            }
            _effects.text = _hoveredPiece == null
                ? "현재 내구도 " + _game.Durability
                    + "\n자재에 마우스를 올리면 내구도와 접촉 이웃을 확인할 수 있습니다."
                : "현재 내구도 " + _game.Durability
                    + "\n\n" + BuildPlacedDetails(_hoveredPiece);
            if (_hoveredPiece != null && _hoveredCard < 0 && _tooltip.activeSelf)
            {
                string description = BuildPlacedDetails(_hoveredPiece);
                if (_tooltipText.text != description)
                {
                    ShowDescription(description, Managers.Input.HasPointer ? Managers.Input.PointerPosition : Vector2.zero);
                }
            }

            for (int i = 0; i < _cards.Length; i++)
            {
                CastleMaterial3D info = _game.CurrentOffers[i];
                if (info == null) continue;
                if (_shownIds[i] != info.id)
                {
                    _shownIds[i] = info.id;
                    _names[i].text = info.name;
                    _silhouettes[i].sprite = Icon(info);
                    _silhouettes[i].color = Color.white;
                    _silhouettes[i].preserveAspect = true;
                }
                _cards[i].interactable = _game.CanSelect;
                _backgrounds[i].color = _game.SelectedOffer == i
                    ? new Color(0.29f, 0.46f, 0.57f, 1f)
                    : new Color(0.2f, 0.3f, 0.37f, 1f);
            }

            if (!_game.CanShowItemInfo)
            {
                _hoveredPiece = null;
                _hoveredCard = -1;
                _tooltip.SetActive(false);
            }
            _stagePopup.SetActive(_game.AwaitingStage && !_game.IsFinished);
            _completePopup.SetActive(_game.IsFinished);
            if (_game.AwaitingStage)
            {
                _stageTitle.text = _game.StagePassed ? "스테이지 통과" : "스테이지 실패";
                _stageBody.text = "내구도 " + _game.Durability + " / 목표 " + _game.TargetDurability
                    + "\n" + (_game.StagePassed ? "홍수를 충분히 대비하는데 성공했습니다." : "처음부터 다시 시작합니다.")
                    + "\n" + (_game.StatusMessage ?? "");
            }
            if (_game.IsFinished)
                _completeBody.text = "최종 내구도 " + _game.Durability
                    + "\n자재 " + _game.PieceCount + "개가 남았습니다.\n"
                    + "닫기 버튼을 누르면 다시 시작합니다.";
        }

        /// <summary>원본의 후보 이미지 영역에 표시할 저해상도 픽셀 실루엣을 생성한다.</summary>
        private Sprite Icon(CastleMaterial3D info)
        {
            if (_generatedIcons.TryGetValue(info.id, out Sprite sprite)) return sprite;
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            Material material = Resources.Load<Material>("Materials/" + info.id + "_Base");
            Color color = material == null ? Color.gray : material.color;
            Color shadow = new Color(color.r * 0.53f, color.g * 0.53f, color.b * 0.53f, 1f);
            int shape = Shape(info.id, info.category);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - 15.5f) / 14f;
                    float v = (y - 15.5f) / 14f;
                    bool filled = shape == 0 ? Mathf.Abs(u) < 0.28f && Mathf.Abs(v) < 0.85f
                        : shape == 1 ? Mathf.Abs(u) < 0.8f && Mathf.Abs(v) < 0.36f
                        : shape == 2 ? (u * u + v * v < 0.78f && u * u + v * v > 0.22f)
                        : shape == 3 ? (u * u + v * v < 0.72f && v > 0.08f) || (Mathf.Abs(u) < 0.14f && v < 0.35f && v > -0.9f)
                        : shape == 4 ? Mathf.Abs(u) < 0.68f && Mathf.Abs(v) < 0.72f
                        : shape == 5 ? Mathf.Abs(u) + Mathf.Abs(v) < 1.15f
                        : Mathf.Abs(u) < 0.66f && v < 0.6f && v > -0.78f;
                    Color pixel = filled ? (x < 15 || y < 15 ? shadow : color) : Color.clear;
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply(false);
            sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _generatedIcons.Add(info.id, sprite);
            return sprite;
        }

        /// <summary>재료 ID와 종류에 따라 카드의 간략한 픽셀 실루엣 유형을 결정한다.</summary>
        private static int Shape(string id, string category)
        {
            if (id == "tire" || id == "life_ring") return 2;
            if (id == "sunflower" || id == "living_tree" || id == "growing_vine") return 3;
            if (id == "branch" || id == "leafy_branch" || id == "vine") return 0;
            if (id == "shoe" || id == "log" || id == "umbrella") return 1;
            if (id == "pebble" || id == "large_stone" || id == "fossil") return 5;
            if (category == "폐기물") return 6;
            return 4;
        }

        /// <summary>원본 규칙 팝업을 표시한다.</summary>
        private void ShowRules()
        {
            _hoveredPiece = null;
            _hoveredCard = -1;
            _tooltip.SetActive(false);
            _game.SetRulesOpen(true);
            _rulesPopup.SetActive(true);
        }

        /// <summary>원본 규칙 팝업을 닫는다.</summary>
        private void HideRules()
        {
            _rulesPopup.SetActive(false);
            _hoveredPiece = null;
            _hoveredCard = -1;
            _tooltip.SetActive(false);
            _game.SetRulesOpen(false);
        }

        /// <summary>원본의 후보 자재 설명 형식을 사용해 커서 옆 툴팁을 표시한다.</summary>
        public void ShowTooltip(int index, Vector2 screen)
        {
            if (!_game.CanShowItemInfo) return;
            CastleMaterial3D material = _game.CurrentOffers[index];
            if (material == null) return;
            _hoveredCard = index;
            _hoveredPiece = null;
            ShowDescription(BuildCandidateDetails(material), screen);
        }

        /// <summary>기물에 물리 레이캐스트가 적중하면 개별 내구도와 접촉 정보를 표시한다.</summary>
        public void SetHoveredPiece(CastlePiece3D piece, Vector2 screen)
        {
            if (!_game.CanShowItemInfo || !_game.IsPlacedPiece(piece))
                piece = null;

            if (piece != _hoveredPiece)
            {
                _hoveredPiece = piece;
                if (_hoveredCard < 0)
                {
                    if (piece == null) _tooltip.SetActive(false);
                    else ShowDescription(BuildPlacedDetails(piece), screen);
                }
            }
            if (piece != null && _hoveredCard < 0)
            {
                // The UI card exit can hide the shared tooltip after this piece is selected.
                if (!_tooltip.activeSelf) ShowDescription(BuildPlacedDetails(piece), screen);
                else MoveTooltip(screen);
            }
        }

        /// <summary>원본 후보 툴팁과 동일하게 기본 내구도, 고유 능력, 분류를 조합한다.</summary>
        private static string BuildCandidateDetails(CastleMaterial3D material)
        {
            string[] abilities = material.abilities == null
                ? Array.Empty<string>()
                : material.abilities.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            string description = abilities.Length == 0 ? "고유 능력 없음" : string.Join("\n", abilities);
            return ColorTags("[" + material.category + "] [" + DisplayRarity(material.rarity) + "]")
                + "\n" + material.name + "\n기본 내구도: " + material.baseReinforcement
                + "\n\n" + ColorTags(description);
        }

        /// <summary>원본의 배치 기물 정보 패널처럼 개별 내구도와 접촉 자재를 표시한다.</summary>
        private string BuildPlacedDetails(CastlePiece3D piece)
        {
            CastleMaterial3D material = _game.GetPieceMaterial(piece);
            if (material == null) return "";
            CastlePiece3D[] neighbors = _game.GetTouchingPieces(piece);
            List<string> lines = new List<string>
            {
                "[" + material.category + "] [" + DisplayRarity(material.rarity) + "]",
                material.name,
                "기본 내구도 " + material.baseReinforcement
            };
            if (piece.PermanentBonus != 0)
                lines.Add("추가 내구도 " + (piece.PermanentBonus > 0 ? "+" : "") + piece.PermanentBonus);
            lines.Add("기물 내구도 " + _game.GetPieceReinforcement(piece));
            if (piece.AbilitySuppressed) lines.Add("특수능력: 덩쿨 부착으로 제거됨");
            else if (piece.HasAttachmentTarget) lines.Add("덩쿨: 최초 접촉 기물에 영구 부착");
            lines.Add("접촉 자재:");
            if (neighbors.Length == 0) lines.Add("· 없음");
            else
                foreach (CastlePiece3D neighbor in neighbors.OrderBy(n => n.PlacedTurn))
                    lines.Add("· " + _game.GetPieceMaterial(neighbor).name
                        + " (" + _game.GetPieceReinforcement(neighbor) + ")");
            lines.Add("");
            if (piece.AbilitySuppressed)
                lines.Add("고유 능력 무효화됨");
            else if (material.abilities == null || material.abilities.Length == 0)
                lines.Add("고유 능력 없음");
            else
                lines.AddRange(material.abilities.Where(x => !string.IsNullOrWhiteSpace(x)));
            return ColorTags(string.Join("\n", lines));
        }

        /// <summary>툴팁에서 희귀와 고급의 표시 문구만 교체하고 실제 희귀도 값은 유지한다.</summary>
        private static string DisplayRarity(string rarity)
        {
            return rarity;
        }

        /// <summary>자재 분류와 희귀도 대괄호 태그에 각 색상을 적용하고 나머지 설명은 유지한다.</summary>
        private static string ColorTags(string text)
        {
            return Regex.Replace(text, @"\[(석재|목재|식물|잡동사니|쓰레기|일반|희귀|고급)\]", match =>
            {
                string color = match.Groups[1].Value switch
                {
                    "석재" => "#B8C9DC",
                    "목재" => "#C88C58",
                    "식물" => "#82C96A",
                    "잡동사니" => "#E1BE72",
                    "쓰레기" => "#D98987",
                    "일반" => "#FFFFFF",
                    "희귀" => "#BD86ED",
                    _ => "#65ADFF"
                };
                return "<color=" + color + ">" + match.Value + "</color>";
            });
        }

        /// <summary>텍스트에 맞게 크기를 제한한 뒤 전체 패널을 화면 안으로 배치한다.</summary>
        private void ShowDescription(string description, Vector2 screen)
        {
            _tooltipText.text = description;
            _tooltip.SetActive(true);
            RectTransform panel = (RectTransform)_tooltip.transform;
            RectTransform canvas = (RectTransform)transform;

            float maxWidth = Mathf.Max(1f, canvas.rect.width - TooltipMargin * 2f);
            float maxHeight = Mathf.Max(1f, canvas.rect.height - TooltipMargin * 2f);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Min(360f, maxWidth));
            Canvas.ForceUpdateCanvases();
            float preferredHeight = _tooltipText.preferredHeight + 24f;
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Min(maxHeight, Mathf.Max(Mathf.Min(64f, maxHeight), preferredHeight)));

            // Layout can change when the tooltip becomes active.
            Canvas.ForceUpdateCanvases();
            MoveTooltip(screen);
        }

        /// <summary>화면 좌표를 Canvas의 좌상단 앵커 좌표로 변환하고 경계를 보장한다.</summary>
        public void MoveTooltip(Vector2 screen)
        {
            if (!_tooltip.activeSelf) return;
            RectTransform tooltip = (RectTransform)_tooltip.transform;
            RectTransform canvas = (RectTransform)transform;
            Canvas uiCanvas = GetComponent<Canvas>();
            Camera eventCamera = uiCanvas != null && uiCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? uiCanvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas, screen, eventCamera, out Vector2 pointer)) return;

            Rect bounds = canvas.rect;
            float width = tooltip.rect.width;
            float height = tooltip.rect.height;

            // Prefer below-right; flip left or above at the edges.
            float left = pointer.x + TooltipGap;
            if (left + width > bounds.xMax - TooltipMargin)
                left = pointer.x - TooltipGap - width;
            float top = pointer.y - TooltipGap;
            if (top - height < bounds.yMin + TooltipMargin)
                top = pointer.y + TooltipGap + height;

            float minLeft = bounds.xMin + TooltipMargin;
            float maxLeft = Mathf.Max(minLeft, bounds.xMax - TooltipMargin - width);
            float minTop = bounds.yMin + TooltipMargin + height;
            float maxTop = Mathf.Max(minTop, bounds.yMax - TooltipMargin);
            left = Mathf.Clamp(left, minLeft, maxLeft);
            top = Mathf.Clamp(top, minTop, maxTop);

            // Anchors and pivot are (0,1), so convert from Canvas-local to
            // top-left-anchored coordinates before assigning anchoredPosition.
            tooltip.anchoredPosition = new Vector2(left - bounds.xMin, top - bounds.yMax);
        }

        /// <summary>후보에서 포인터가 벗어나면 툴팁 소유권을 해제한다.</summary>
        public void HideTooltip()
        {
            _hoveredCard = -1;
            _tooltip.SetActive(false);
        }
    }

    /// <summary>원본 후보 프리팹 버튼의 마우스 호버를 3D UI 툴팁에 전달한다.</summary>
    public sealed class CastleCandidateHover3D : MonoBehaviour, IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler
    {
        private CastleHud3D _hud;
        private int _index;

        /// <summary>호버 대상이 가리키는 카드 인덱스를 초기화한다.</summary>
        public void Initialize(CastleHud3D hud, int index)
        {
            _hud = hud;
            _index = index;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hud.ShowTooltip(_index, eventData.position);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            _hud.MoveTooltip(eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hud.HideTooltip();
        }
    }
}
