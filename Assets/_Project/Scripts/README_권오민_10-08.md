# 10/8 권오민 작업 스크립트 — 씬에 붙이는 법

폴더 구조는 저장소 README의 `Assets/_Project/` 기준.

| WBS | 파일 | 붙이는 곳 |
|---|---|---|
| 3.01~3.03 | `Player/PlayerController.cs` | Player (CharacterController 자동 추가) |
| 3.08 | `Player/CursorPicker.cs`, `Core/IClickable.cs` | Main Camera |
| 3.10 | `Options/PartType.cs`, `OptionItem.cs`, `OptionPart.cs` | 빈 오브젝트 `Option_Wall` 등 |
| 3.10 테스트 | `Options/OptionApplyTester.cs` | 아무 오브젝트 (숫자키 1~9) |
| 3.07 | `Core/ZoneData.cs`, `UI/ZoneInfoUI.cs` | Canvas/ZoneInfoPanel |
| 2.05/3.07 | `Core/ZoneVolume.cs`, `ZoneVolumeBox.cs` | Zone_Living 등 구역 부모 (박스는 자식) |
| 3.11 | `UI/PanelUI.cs`, `UI/HUDController.cs` | Canvas/HUD |
| 3.15 | `Devices/CoffeeBrewEffect.cs` | 커피 머신 |

## 1. Player
```
Player            (pos = 현관, PlayerController)
 └ Main Camera    (CursorPicker)
SpawnPoint        (빈 오브젝트, 현관 시작 위치·방향)
```
- PlayerController: Camera Pivot = Main Camera, Spawn Point = SpawnPoint
- CursorPicker: Interactable Mask = Interactable
- 프로젝트가 Input System 전용이라 `Input.GetKey`는 쓰지 말 것

## 2. 벽지 (3.10, 색상만 교체)
- 상단 메뉴 **COM → 4. 벽지 테스트 세팅** 한 번이면 끝
  (Option_Wall + OptionPart(Source = m_walls) + OptionApplyTester 4종 색상, 벽 메쉬 Interactable 레이어)
- Play → 숫자 1(기본) 2(그레이지) 3(세이지 그린) 4(패브릭 질감)
- 재질 복제 없음: Play 중에만 쓰는 복사본 색을 바꾸고, Play 끄면 원래대로

## 2-1. 구역 (ZoneVolume)
```
Zone_Living   (ZoneVolume, Data = ZoneData_Living, 콜라이더 없음)
 ├ Box_Main   (BoxCollider, Is Trigger, Ignore Raycast)
 └ Box_Corner (박스끼리 0.1~0.2m 겹치게)
```
- Data 폴더에서 Create → COM → Zone Data로 4개 만들고 각 구역에 연결
- Play 후 걸어 다니면 Console에 `입실: 거실` / `퇴실: 거실`, 화면에 구역 정보 창 3초

## 3. UI
- Canvas: Screen Space - Overlay, Canvas Scaler = Scale With Screen Size 1920×1080
- EventSystem의 Standalone Input Module → **Input System UI Input Module**로 교체 (Replace 버튼)
- ZoneInfoPanel: 화면 위쪽, CanvasGroup + TMP 텍스트 4개
- HUD: 좌측 상단 세로 버튼 3개 + 비용 텍스트, 패널 2개(PanelUI)는 메뉴 오른쪽

## 4. 커피 연출 (소리 없음, SRS v2.0)
- 머그컵 자식에 갈색 Cylinder(얇게) 이름 `CoffeeLiquid`, 꺼 두기
- 커피 머신: ParticleSystem `Steam`(Play On Awake 끔)
- Play 중 CoffeeBrewEffect 우클릭 → "테스트: 추출 시작"

## 김찬중에게 공유할 것
- `IClickable` 인터페이스: 로봇팔도 이걸 구현하면 (SRS v2.0: 커피 머신·컵은 클릭 대상 아님) CursorPicker 수정 없이 클릭됨
- `OptionItem`, `PartType`은 설계 초안 그대로 만들어 둠 → OptionCatalog에서 그대로 사용
- `OptionPart.Clicked`(임시 static 이벤트)는 HomeEvents 생기면 `OnPartClicked`로 교체
- CoffeeMachine.Brew()에서 `CoffeeBrewEffect.StartBrew / StopBrew / SetCupFilled` 호출
