# RestoreCastle 3D 이관

- 원본: [wt6248/RestoreCastle](https://github.com/wt6248/RestoreCastle) `develop`
- 대상: `kadiace/proof-of-concept/restore-castle-3d`

## 사용

Unity 6.3 LTS에서 `Assets/Scenes/Game.unity`를 열고 Play를 실행합니다.

하단의 원본 RestoreCastle 후보 카드를 선택하면 월드 영역에서 마우스를 움직일 때 **-Y Raycast로 충돌할 표면을 찾고 해당 자재의 반투명 3D 낙하 미리보기**를 표시합니다. 커서의 화면상 위치에서 미리보기 중심까지는 레이캐스트 가이드용 청록색 점선 UI를 그립니다(게임 월드/UI 뒤 가림에 영향을 주지 않으며 클릭 판정도 차단하지 않음). 같은 XZ 위치를 클릭하면 Y=7에서 Rigidbody 기물이 실제로 낙하합니다. 마우스가 UI 위에 있거나 후보가 선택되지 않았으면 미리보기를 숨깁니다. 미리보기는 현재 충돌체의 윗면을 기준으로 한 **정적 예상 위치**이며, 낙하 이후 회전·튕김·기존 기물 이동 등까지 물리 시뮬레이션하지는 않습니다. 우클릭 드래그로 카메라를 XZ 평행 이동하고 휠로 줌합니다. 카메라는 정사영, 수직 아래(-Y)로부터 10° 기울어진 고정 시점입니다. Q/E는 물체 Y축 배치 회전, Esc는 선택 취소입니다.

원본의 자재 30개(기반 및 자동 생성 덩굴 포함)를 `Assets/Resources/Prefabs/`의 30개 Unity 프리팹으로 저장했습니다. 프리팹은 Blockbench풍 기초 큐브·원기둥으로 구성되며 각 시각 메시에는 작은 픽셀 질감의 URP Lit 머티리얼이 적용됩니다. 프리팹 루트에는 Rigidbody, BoxCollider, `CastlePiece3D` 및 고마찰 물리 머티리얼이 직접 연결되어 있습니다.

## 5회 배치 라운드와 선택 UI

- **스테이지당 5라운드**로 복귀했습니다(5스테이지 총 25라운드).
- 매 라운드 총 **5개 기물을 배치**합니다. 매번 실제 드랍 직후 `CandidateSelectionRow`가 아래로 내려가지만 **Esc 또는 우클릭으로 팔레트를 다시 열어** 현재 제시된 3종 가운데 다른 자재를 선택할 수 있습니다. 고른 뒤에도 다음 드랍 전까지 다시 선택할 수 있습니다. **한 번이라도 드랍했다면 해당 라운드의 리롤은 비활성화**되고 후보 3종은 고정됩니다.
- 하단 UI 이동은 `CastleHud3D.candidateSlideCurve` 애니메이션 커브와 `candidateSlideDuration`을 사용합니다. 후보만 선택했을 때에는 UI가 열린 채 유지됩니다. 재개방하면 다시 올라오고, 다음 기물을 드랍하면 다시 내려갑니다. 다섯 번째 기물 정착 시 라운드를 정산하고 새 후보를 제시합니다.
- **이전 기물이 떨어지는 도중에도 다음 기물을 드랍할 수 있습니다.** 클릭할 때마다 기물을 즉시 생성하고 동시에 물리 낙하를 진행합니다. 연속 클릭 오작동 방지를 위해 직전 드랍 후 마우스 버튼을 놓고 다시 눌러야 하며, 마지막으로 인정한 드랍 이후 최소 `0.1초`(`dropClickCooldown`)가 지나야 합니다.
- 다섯 번째 기물이 생성되면 추가 드랍을 잠그고 **해당 라운드에서 진행 중인 5개 낙하 처리가 모두 끝난 후** 내구도 정산/주기적 성장/스테이지 판정을 라운드당 1회 실행합니다. 배치 진행도는 상단 ROUND 및 상태 패널에 표시합니다.
- 후보가 선택되지 않았거나 패널·팝업 위에 마우스가 있으면 미리보기 및 점선을 숨깁니다.

## 스테이지 홍수 연출과 접촉 판정

- 다섯 번째 라운드가 정산되면 **파란색 반투명 홍수 큐브**가 강바닥 전체와 카메라 화면 및 건축물 범위를 덮으며 **+Z → -Z** 방향으로 확장됩니다. 물 표면의 월드 Y 좌표는 씬에 설정한 고정값을 모든 스테이지에 사용합니다. 기본값은 `-1.75`이며 자재의 배치 상태에 따라 변하지 않습니다.
- 홍수 큐브의 콜라이더는 비활성 상태로 두고, 매 프레임 새로 채워진 구간에 `Physics.OverlapBox`를 실행합니다. 해당 구간과 자재 콜라이더가 겹치면 그 자재를 접촉 대상으로 기록합니다. 이미 채워진 구간은 다시 검사하지 않습니다.
- 홍수가 끝까지 찬 뒤 접촉 대상으로 기록된 자재를 제거합니다. 기반은 제거 대상에서 제외하며, 덩굴로 영구 부착된 자재는 기존 규칙대로 접촉 대상의 연결 묶음 전체를 함께 제거합니다.
- 판정은 메인 스레드에서 수행하고 Transform·Collider·GameObject를 백그라운드 작업에 전달하지 않습니다. 홍수 큐브는 시각 효과로만 사용하며 자재를 밀거나 물리적으로 움직이지 않습니다.
- 제거 후 홍수를 **-Y 방향으로 빠르게 내려** 사라지게 하고 스테이지 결과를 표시합니다. 이 과정에서는 신규 드랍 및 접촉 파괴 처리를 일시 중지합니다. 제거 후 점수 재계산은 한 번 수행합니다.
- `CastleGame3D`의 `floodFillDuration`, `floodDrainDuration`, `floodFillCurve`로 연출을 조절합니다.

## 홍수 파괴 미리보기

- **Tab**을 누르면 홍수 미리보기를 켜고 다시 누르면 끕니다. 새 입력은 `InputSystem_Actions.inputactions`의 `Player/ToggleFloodPreview`에 `<Keyboard>/tab`으로 저장하며 `Managers.Input`으로 읽습니다.
- 실제 홍수와 같은 고정 수면 높이에서 강바닥 전체를 덮도록 기존 홍수 오브젝트를 완전히 채운 상태로 표시합니다. 미리보기 중에도 자재 배치, 카메라 이동과 줌을 사용할 수 있습니다.
- 실제 홍수와 같은 영역 계산과 콜라이더 접촉 조회를 사용합니다. 기반은 제외하고, 직접 접촉한 자재와 덩굴로 영구 부착되어 함께 제거될 자재를 모두 붉게 표시합니다. 접촉 버퍼가 가득 차면 확장해서 재조회하므로 많은 자재가 겹쳐 있어도 누락하지 않습니다.
- 켜는 순간 즉시 계산하며 활성 상태에서는 기본 0.1초 간격으로 갱신합니다. 자재 생성·제거·영구 부착 직후에는 다음 프레임에 갱신합니다. 현재 위치와 회전, 연결 상태를 기준으로 한 예상이며 실제 홍수 진행 중 움직임에 따라 결과는 달라질 수 있습니다.
- 파괴 예상 자재의 메시를 깊이 가림 없이 마스크에 그리고, 물의 투명 렌더링 이후 붉은색을 한 번 합성합니다. 다른 자재나 물에 가려진 부분도 붉은 실루엣으로 보이며 여러 대상이 겹쳐도 색 농도는 일정합니다. 기존 재질과 호버 외곽선의 상태는 별도로 유지합니다.
- 실제 홍수 시작, 재시작, 스테이지 전환, 게임 종료, 규칙 창 열기 시에는 미리보기를 자동으로 종료합니다. 미리보기는 자재를 제거하거나 점수와 라운드를 변경하지 않습니다.
- 강조 색과 불투명도는 `Assets/Settings/CastleFloodPreview.mat`의 **Tint Color**와 **A**로 조절합니다(초기 A=0.7). 갱신 간격은 Game 씬의 Main Camera에 배치된 `CastleFloodPreview3D`의 **Refresh Interval**로 조절합니다.
- PC와 Mobile Renderer에 `CastleFloodPreviewFeature`를 등록해 표시 재질을 연결합니다. UI는 붉은색 합성 이후 정상 표시됩니다.

## 덩쿨 영구 부착과 특수능력 무효화

- `growing_vine`(성장 덩쿨)와 3라운드 주기로 생성되는 `vine`(덩쿨)은 **최초로 닿은 기물 한 개에 `FixedJoint`로 영구 결합**합니다. 이미 부착한 덩쿨은 다른 기물에 다시 부착하지 않습니다.
- 붙잡힌 기물은 **게임 월드에 계속 남고 고유 기본 내구도 유지**하지만 자체 특수능력 및 그 능력으로 얻는 추가 내구도는 비활성화합니다. 덩쿨은 대상의 **기본 내구도 수치만큼 자신의 영구 보너스**를 얻습니다(상대 기본 내구도는 감소하지 않음).
- 붙은 쌍은 물리적으로 같이 이동합니다. 홍수에 닿은 자재가 있으면 기존 영구 부착 규칙에 따라 연결된 자재 묶음 전체를 함께 제거합니다.
- `foul_smelling_lump`(고약한 냄새가 나는 덩어리)와 덩쿨이 닿으면 **덩쿨이 우선 부착**하고 덩어리의 접촉 파괴 능력을 봉인합니다. 그 결과 덩쿨은 덩어리에 파괴되지 않습니다.
- 기반(`foundation`)은 스테이지 시스템상 홍수 제거 예외이므로 덩쿨 영구 부착 대상에서도 제외합니다.

## 게임 규칙

- `CastlePiece3D.OnCollisionEnter/Stay/Exit`를 통해 실제로 접촉한 기물 사이의 양방향 인접 그래프를 관리합니다.
- **아이템 상세 정보 호버:** 하단 후보 카드 위에서는 `기본 내구도 / 고유 능력 / 카테고리` 툴팁을 표시합니다. 월드에 배치한 3D 기물 위에서는 Physics Raycast로 커서 아래 기물을 찾아 이름·분류·기본/현재 내구도·직접 접촉한 이웃 기물·고유 능력을 툴팁에 표시합니다. 포인터가 떠나거나 UI/팝업 위로 이동하면 숨깁니다.
- Game 씬에 없던 `EventSystem`과 `InputSystemUIInputModule`을 실행 시 자동으로 준비해 후보 카드의 원본 호버 이벤트가 작동하도록 합니다.
- 인접 기물에 따른 내구도, 기간·성장 보너스, 덩굴 파괴·흡수, 폐기물 봉지의 접촉 거리 효과를 3D 접촉 기준으로 계산합니다.
- 오크통은 **처음 물리 충돌한 후 3초 뒤 폭발**해 반경 2.5 이내 주변 기물의 Rigidbody에 거리 기반 폭발 충격(`AddExplosionForce`)을 줍니다. **오크통 자신과 주변 기물 모두 삭제되지 않습니다.** 첫 충돌에는 바닥도 포함됩니다. Inspector의 `barrelExplosionImpulse`(기본 18), `barrelExplosionUpwardsModifier`(기본 0.6)로 힘을 조절합니다. 오크통은 한 번만 폭발하며, 폭발 완료 후에는 점화 대기 상태가 해제되어 이후 라운드 정산에서 불필요하게 기다리지 않습니다.
- **스테이지당 5라운드, 라운드당 5개 배치**합니다(총 5스테이지, 최대 125개 직접 배치). 현재 남은 기물의 내구도 합계로 목표 내구도(45 × 스테이지²)를 판정하며, 과거 라운드의 내구도는 누적하지 않습니다. 살아있는 나무의 후보 등장 확률은 기존의 절반입니다.
- 스테이지 홍수와 미리보기의 수면은 `Game` 씬의 `@SceneContext`에 있는 `GameSceneContext` 컴포넌트 **Flood Surface Y**로 정합니다. 기본값은 월드 Y `-1.75`이며 모든 스테이지에서 동일하게 사용합니다. 플레이 중 값을 변경하면 미리보기의 다음 갱신과 이후에 시작하는 홍수부터 반영됩니다. 자재 제거는 홍수 영역과 콜라이더의 접촉으로 판정합니다.
- 배치 보조 Plane/바닥 메시가 화면에 보이지 않도록 했으며, 중력 낙하를 받쳐 주는 **보이지 않는 물리 바닥 콜라이더**는 유지합니다.
- `Assets/Resources/Materials/HighFriction.physicMaterial`: 정지 마찰 1.5, 동적 마찰 1.2, Maximum 결합, 반발 0. 런타임 Rigidbody linearDamping=2, angularDamping=5.
- `Assets/Resources/Textures/BlockPixel8.tga`: 공용 8×8 픽셀 패턴, Point 필터링 및 mipmap 비활성화.

## 파일

- `Assets/Resources/Data/materials.json` — 자재 전체와 오크통 능력 설명
- `Assets/Resources/Prefabs/*.prefab` — 30개 3D 기물 프리팹
- `Assets/Resources/Prefabs/UIs/RestoreCastleHud.prefab` — 원본 2D 씬에서 가져온 Canvas·TopBar·하단 CandidateSelectionRow·원본 스타일 팝업
- `Assets/Resources/Prefabs/UIs/StatCell.prefab` 및 `CandidateCard.prefab` — 원본 프리팹 그대로 이관한 상태 셀/후보 카드 레이아웃. 원본 2D 전용 `CandidateCardView` 스크립트는 제거함.
- `Assets/Scripts/RestoreCastle3D/CastleHud3D.cs` — 원본 UI 버튼과 3D 게임 연결 및 재료별 간이 픽셀 실루엣 생성
- `Assets/Resources/Materials/*.mat` — 기물별 기본/디테일 머티리얼
- `Assets/Scripts/RestoreCastle3D/` — 게임 상태, 3D 접촉, 능력 및 점수 계산
- `Assets/Scripts/RestoreCastle3D/CastleDropPreview3D.cs` — 실제 자재 프리팹의 반투명 충돌 예상 위치 미리보기(-Y Raycast)
- `Assets/Scripts/RestoreCastle3D/CastleDropGuideGraphic3D.cs` — 커서부터 미리보기까지 연결하는 점선 UGUI Graphic
- `Assets/Scripts/RestoreCastle3D/CastleFloodEffect3D.cs` — 실제 홍수와 미리보기가 공유하는 반투명 홍수 표시
- `Assets/Scripts/RestoreCastle3D/CastleFloodQuery3D.cs` — 공통 홍수 영역·콜라이더 접촉·영구 부착 묶음 판정
- `Assets/Scripts/RestoreCastle3D/CastleFloodPreview3D.cs` — 카메라별 홍수 미리보기 상태와 주기적 대상 갱신
- `Assets/Scripts/RestoreCastle3D/CastleFloodPreviewFeature.cs` 및 `CastleFloodPreview.shader` — 파괴 예상 메시의 관통 마스크와 붉은 tint 합성
- `Assets/Scripts/Scenes/GameSceneContext.cs` — 기존 Game 씬에서 신규 게임 시작

## 남은 확인

원본 UI의 상하단 레이아웃과 팝업은 **Unity 직렬화 프리팹 구조를 이관**했습니다. 원본 스프라이트 이미지 자체는 가져오지 못해 후보 그림은 런타임에 생성하는 간이 픽셀 실루엣으로 대체했습니다. 카메라·원본 버튼 레이아웃·클릭 바인딩은 확인이 필요합니다.

GitHub에서 코드·텍스트 프리팹은 등록했지만 이 환경에서 Unity 에디터 컴파일 및 실제 물리 Play 테스트는 수행하지 못했습니다. 임포트 후 개별 프리팹, URP 렌더링, 접촉 유지, Rigidbody 낙하, 오크통 폭발, 스테이지 전환을 확인해야 합니다. 복합 형상은 프로토타입 단계의 단일 BoxCollider이며 Blockbench에서 직접 만든 정교한 메시가 아닙니다.
