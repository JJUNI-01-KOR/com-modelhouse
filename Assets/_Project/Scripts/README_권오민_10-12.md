# 10/12 권오민 작업 — 적용 방법과 점검표

기준: SRS v2.3, WBS 10/12 권오민 담당 (4.24 제외)

## 한 일

| WBS | 내용 | 파일 |
|---|---|---|
| 4.01 | 바닥재 | 10/8에 완료 (변경 없음) |
| 4.02~4.04 | 상판·소파·러그 | 10/8에 완료 → 옵션 목록만 OptionCatalog(부록 A-7)로 통일 |
| 4.05 | 그림 액자 색상 | `Option_Painting` (통합 메뉴이 생성) |
| 4.06 | 화분 색상 | `Option_Plant` (통합 메뉴이 생성) |
| 4.07 | 옵션 목록·가격·선택 표시 | `UI/OptionPanelUI.cs` 다시 씀, `UI/HUDController.cs` 수정 |
| 4.08 | 시각 버튼 UI (SRS v2.4: 시계 → 버튼 5개) | `UI/ClockUI.cs` 신규 |
| 4.11 | 시각 버튼 연결·시간 변경 기록 | `Core/ITimeOfDay.cs` 신규, ClockUI가 자동 연결 |
| 4.13 | 최신순 기록 패널 | `UI/LogPanelUI.cs` 신규 |
| 4.18 | 초기화 때 옵션 유지·패널 닫기·12시 | `UI/HUDController.cs`, `UI/ClockUI.cs` |
| 4.15, 4.16 | 로봇팔 월드 UI | ❌ 안 함 — SRS v2.3에서 FR-34(상호작용 창) 삭제 |
| 4.22 | 필수 기능 점검 | 아래 점검표 |

수정한 기존 파일: `Options/OptionPart.cs`(소품 클릭 범위 옵션 1개 추가), `Editor/COMSetupTools.cs`(5·6·8번 메뉴를 카탈로그 방식에 맞춤, 안 쓰는 색 표 삭제)

## 적용 순서

1. 파일을 프로젝트에 덮어쓰기 → Unity로 돌아가서 컴파일 에러 없는지 확인
2. `MainScene` 열기
3. 상단 메뉴 **COM → 전체 세팅·점검 (1~12)** 한 번 (1~12 통합 메뉴, 이미 있는 건 건너뜀)
   - `COM_System` (ConfigurationState + ScenarioManager), `Option_Painting`, `Option_Plant`, 우측 상단 시각 버튼, 기록 패널(스크롤)이 생김
   - Console에서 `Option_Painting` / `Option_Plant` 줄 확인: **어느 재질을 바꾸는지** 나와 있음
4. **Ctrl+S**로 씬 저장
5. Play


## 4.22 점검표 (Must, 기본 기능 흐름)

| # | 확인 | 관련 |
|---|---|---|
| 1 | Play → 현관에서 시작, 우측 상단 `12:00 낮` 버튼이 파란색 | FR-26, FR-30 |
| 2 | WASD 이동, 우클릭 끌 때만 시점 회전, 패널 위에서 우클릭은 회전 안 함 | FR-01 |
| 3 | 거실·주방·침실 들어가면 구역 정보 창 3초 | FR-02 |
| 4 | 벽·바닥·상판·소파·러그·액자·화분 5m 안에서 마우스 올리면 밝아짐 → 클릭하면 옵션 패널이 그 탭으로 열림 | FR-03 |
| 5 | 액자는 **그림 부분**을 눌러도 열림 | FR-03 |
| 6 | 옵션 고르면 1초 안에 색 바뀜, 고른 줄 파란색, 다른 파트 선택 유지 | FR-29 |
| 7 | 좌측 상단 합계 = 고른 옵션 금액 합 (전부 기본이면 +0원) | FR-31, TC-49 |
| 8 | [옵션]→[기록] 누르면 옵션 패널 닫히고 기록 패널 열림, 같은 버튼 다시 누르면 닫힘 | FR-11 |
| 9 | 기록 패널: 맨 위가 최신, `HH:mm:ss.fff  대상  내용` 형식, 열어 둔 채 걸어 다니면 0.5초 안에 새 줄 | FR-25 |
| 10 | 시각 버튼(06·09·12·18·22) 누르면 그 버튼이 파란색, 기록에 `시각 변경 18:00` 한 줄 | FR-30 |
| 11 | 옵션 몇 개 바꾸고 이동 → [초기화]: 현관으로 이동, 패널 닫힘, 시각 12:00, **옵션 색과 합계는 그대로** | FR-26 |
| 12 | 초기화 후 기록에 `시나리오 초기화` | FR-25, FR-26 |

실패한 항목은 WBS 4.23(김찬중)으로 넘길 때 번호와 증상을 같이 적기.

## 김찬중에게 공유할 것

- **TimeOfDayController(4.09·4.10)**: `ITimeOfDay`를 구현해 주면 시각 버튼이 자동으로 찾아서 붙음
  - SRS v2.4로 시각은 버튼 5개(06·09·12·18·22시)만 → A-8 값을 그대로 쓰고 보간은 필요 없음
  - `CurrentHour`, `SetHour(int)`, `GetZoneLux(ZoneVolume)` 세 개
  - `SetHour`에서 `HomeEvents.RaiseHourChanged(hour)`만 보내고 **Log는 하지 말 것** (버튼이 누를 때 남김)
  - 초기화 때 12시로 되돌리고 `RaiseHourChanged(12)`
  - 붙기 전까지 버튼 아래 조도는 `-`로 나옴
- **EventLogger(4.12)**: `LogPanelUI`가 `HomeEvents.OnLog`를 직접 받아 100건 FIFO로 보관하고 있음. EventLogger를 따로 만들면 `LogPanelUI.Entries`만 그쪽으로 바꾸면 됨
- 옵션 패널·합계는 이제 **OptionCatalog + ConfigurationState 하나만** 봄. 가격·색은 `OptionCatalogBuilder` 표에서 고치고 통합 메뉴 또는 Tools → COM → 옵션 카탈로그 만들기
