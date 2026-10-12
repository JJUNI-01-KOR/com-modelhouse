# 모델하우스 컨피규레이터

> Asset Store 에셋 Modern Apartment로 꾸민 개방형 아파트를 1인칭으로 걸으며, 벽지·바닥재·주방 상판·소파·러그·그림·화분의 색을 바꾸면 기본 대비 추가 비용이 붙는 Unity 컨피규레이터.
> 시각 버튼(06·09·12·18·22시)으로 시간대별 햇빛과 실내 조도를 보고, 주방의 커피 로봇팔을 클릭하면 로봇팔이 보관대의 컵을 꺼내 커피 머신에서 커피를 받아 받침대에 놓는다.

<!-- 시연 영상: 10/14 추가 -->
<!-- [![시연 영상](docs/images/thumbnail.png)](유튜브 링크) -->

| 항목 | 내용 |
| --- | --- |
| 팀 | COM (2인) |
| 기간 | 2026.10.06 ~ 2026.10.14 (10/13 12:30 기능 동결, 10/14 발표) |
| 엔진 | Unity `6000.4.0f1` (두 사람 같은 버전) |
| 렌더링 | URP (Forward+, 간접광 굽기 Baked Indirect, 반사 프로브, 포스트 프로세싱 Volume) |
| 언어 | C# |
| 공간 에셋 | [Modern Apartment](https://assetstore.unity.com/packages/3d/props/interior/modern-apartment-375248) (Zeps3D, URP 전용) — 저장소에 포함하지 않음, 각자 구매 후 Import |
| 플랫폼 | Windows 10/11 (64비트) |
| 요구사항 | [SRS v2.5](docs/SRS_COM_v2.5.docx) — 기능 요구사항 13개 (전부 Must) |
| 일정 | [WBS](docs/WBS_모델하우스_컨피규레이터.xlsx) |

---

## 팀과 역할

| 이름 | 역할 | 담당 | 핵심 기능 |
| --- | --- | --- | --- |
| 권오민 | 팀장 | 공간·옵션 표현·UI | 1인칭 투어, 구역·재실 조명, 옵션 색상 교체, 좌측 상단 메뉴·기록·시각 버튼 UI, 현관 조명·거울 반사 |
| 김찬중 | 팀원 · 기술 리드 | 이벤트·로봇팔·시간대 | 중앙 이벤트(HomeEvents), 커피 로봇(IK), 시간대별 햇빛과 조도, 커피 스테이션(머신·컵 보관대·받침대) |

---

## 주요 기능 (SRS v2.5)

| 기능 | 요구사항 |
| --- | --- |
| 1인칭 이동, 구역(현관·거실·주방·침실) 정보 표시 | FR-01, FR-02 |
| 부분 클릭 옵션: 벽·바닥·주방 상판·소파·러그·그림·화분을 클릭하면 옵션 목록 | FR-03 |
| 구역에 들어가면 조명 켜짐, 나가고 3초 뒤 꺼짐 | FR-04, FR-05 |
| 좌측 상단 메뉴 [옵션] [기록] [초기화] + 추가 비용 합계 | FR-11, FR-31 |
| 커피 로봇: 클릭하면 컵 꺼내기 → 커피 머신에서 받기(5초) → 받침대에 놓기 | FR-13, FR-15 |
| 이벤트 기록(밀리초), 초기화(고른 옵션은 유지) | FR-25, FR-26 |
| 옵션 7개 파트 색상 교체 | FR-29-01 ~ FR-29-07 |
| 시각 버튼으로 햇빛·하늘·환경광·창문 밝기와 구역 조도(lx) 변경 | FR-30 |

v2.5에서 삭제: FR-07 중문 자동 개폐, FR-27 URDF 로봇 불러오기. 로봇팔 직접 조종은 하지 않고 클릭으로 커피 만들기만 시연한다.

### 커피 로봇 동작

1. 로봇팔 클릭 (C 키는 에디터 테스트용)
2. 받침대에 지난번 잔이 있으면 집어서 싱크대에 넣는다
3. 보관대의 다음 컵(1 → 2 → 3번)을 꺼내 커피 머신 추출구 아래에서 5초 동안 커피를 받는다
4. 커피가 담긴 컵을 받침대에 놓고 일자 자세로 돌아간다
5. 3번 컵까지 쓰고 다시 1번 차례가 되면 싱크대의 컵들이 빈 컵으로 보관대 제자리에 돌아온다

로봇팔은 실제 공장 로봇처럼 정해진 패턴으로만 움직인다: **물러나기 → 운반 자세로 접기 → 조인트 1만 회전 → 목표 위에서 펴기 → 직선 진입** (컵·받침대·싱크대는 위에서 아래로, 커피 머신은 앞에서 수평으로). 팔 닿는 범위는 밑동 중심에서 수평 0.60m까지(최대 0.65m).

### 옵션과 추가 비용 (예시 금액, SRS 부록 A-7)

모든 옵션은 색상만 바꾼다. 에셋 원본 재질은 고치지 않고 실행 중에만 쓰는 재질 복사본의 색을 바꾼다.

| 부분 | 에셋 재질 | 선택지 |
| --- | --- | --- |
| 바닥재 | `m_floor` | 원목 마루(기본), 강마루 +150만, 네이비 마루 +320만, 레드 포인트 +100만 |
| 벽지 | `m_walls` | 화이트 페인트(기본), 그레이지 +40만, 세이지 그린 +40만, 패브릭 베이지 +70만 |
| 주방 상판 | `m_countertop` | 화이트(기본), 다크 스톤 +180만 |
| 소파 패브릭 | `m_white_fabric` | 화이트(기본), 웜 그레이 +30만, 네이비 +30만 |
| 러그 | `m_carpet` | 기본, 베이지 +25만, 차콜 +25만 |
| 그림 액자 | `m_details` | 기본, 웜 톤 +15만, 쿨 톤 +15만 |
| 화분 | `m_white` | 기본, 테라코타 +8만, 차콜 +8만 |

### 시각별 햇빛 (SRS 부록 A-8)

| 시각 | 해 고도 / 방위 | 햇빛 세기 | 창 쪽 조도 |
| --- | --- | --- | --- |
| 06:00 | 5° / 90°(동) | 10% | 100 lx |
| 09:00 | 35° / 120° | 60% | 400 lx |
| 12:00 | 60° / 180°(남) | 100% | 800 lx |
| 18:00 | 5° / 270°(서) | 10% | 100 lx |
| 22:00 | 해 끔 | 0% | 0 lx |

구역 조도 = 창 쪽 조도 × 구역 계수(거실·주방 1.0, 침실 0.6, 현관 0.3) + 300 lx × 천장 조명 밝기

---

## 조작법

| 입력 | 동작 |
| --- | --- |
| W A S D | 이동 (바라보는 방향 기준) |
| 마우스 오른쪽 버튼 누른 채 끌기 | 시점 회전 |
| 마우스 왼쪽 클릭 | 버튼, 옵션 부분(옵션 목록), 커피 로봇팔(커피 만들기) |
| 좌측 상단 메뉴 | [옵션] [기록] [초기화] |
| 우측 상단 시각 버튼 | 06:00 · 09:00 · 12:00 · 18:00 · 22:00 |
| C (에디터에서만) | 커피 로봇팔 클릭 흉내 |

---

## 구조

옵션 선택과 센서는 `HomeEvents`(중앙 이벤트 관리자)에 알리기만 하고, 재질·조명·로봇·UI는 `HomeEvents`를 구독만 한다. 시간대는 `ITimeOfDay` 인터페이스로 UI와 분리했다.

```mermaid
flowchart LR
  subgraph 발행
    CS["ConfigurationState<br/>옵션 · 추가 비용 합계"]
    Zone["ZoneVolume<br/>구역 재실 감지"]
    SM[ScenarioManager<br/>초기화]
    CP["CursorPicker<br/>5m 클릭"]
  end
  HE(("HomeEvents"))
  subgraph 구독
    OP["OptionPart<br/>재질 색 교체"]
    ZL["ZoneLight<br/>구역 조명"]
    UI["HUDController · LogPanelUI<br/>ZoneInfoUI · OptionPanelUI"]
    CT["CoffeeTask<br/>커피 작업 순서"]
  end
  CS --> HE
  Zone --> HE
  SM --> HE
  CP --> HE
  HE --> OP & ZL & UI & CT
  CT --> RA["RobotArm<br/>IK · 이동 패턴"] & CM["CoffeeMachine<br/>추출 · 증기"]
  CL[ClockUI 시각 버튼] -.->|ITimeOfDay| TD["TimeOfDayController<br/>햇빛 · 하늘 · 창문 · 조도"]
```

### 폴더 구조

```
Assets/
├── Scenes/        MainScene.unity (메인), Test_RobotArm.unity, Test_Sensors.unity
├── _Project/
│   ├── Scripts/
│   │   ├── Core/      HomeEvents, IClickable, ITimeOfDay, ScenarioManager, ZoneData, ZoneVolume
│   │   ├── Player/    PlayerController (1인칭), CursorPicker
│   │   ├── Options/   OptionCatalog, OptionItem, ConfigurationState, OptionPart, PartType
│   │   ├── Devices/   CoffeeMachine, CoffeeBrewEffect, ZoneLight, TimeOfDayController
│   │   ├── RobotArm/  RobotArm, CoffeeTask
│   │   ├── UI/        HUDController, PanelUI, ClockUI, LogPanelUI, OptionPanelUI, ZoneInfoUI
│   │   ├── Tools/     EvidenceCapture
│   │   └── Editor/    상단 메뉴 COM·Tools/COM 세팅 도구
│   ├── Prefabs/       RobotArm (UR3 + 그리퍼)
│   ├── Data/          ZoneData 4개, OptionCatalog
│   ├── Materials/     옵션·로봇·커피 머신·받침대 재질
│   └── Settings/      URP, 거울 반사 프로브
├── ArmRobot/      Unity 로보틱스 데모의 UR3·그리퍼 (ArticulationBody)
└── Zeps3D/        Modern Apartment (.gitignore로 제외, 각자 Import)
docs/              SRS v2.5, WBS, 클래스 다이어그램(drawio)
```

### 에디터 메뉴

| 메뉴 | 하는 일 |
| --- | --- |
| COM → 전체 세팅·점검 | 구역·플레이어·UI·시각 버튼·현관 조명 등 빠진 것만 채우고 점검 결과를 txt로 남김 |
| Tools → COM → 로봇팔을 식탁 위에 올리기 | 로봇팔을 아일랜드 식탁 높이에 맞춤 |
| Tools → COM → 시간대 컨트롤러 붙이기 | TimeOfDayController 생성·연결 |
| Tools → COM → 로봇팔 색을 집 분위기에 맞추기 | 로봇팔을 오프화이트·차콜·브라스 톤으로 |
| Tools → COM → 커피 머신 새 디자인으로 바꾸기 | 에스프레소 머신을 기본 도형으로 새로 만듦 |
| Tools → COM → 찻잔 받침대 만들기 / 컵 보관대 만들기 | 받침대·컵 3개 보관대 생성, CoffeeTask에 연결 |

---

## 개발 환경 세팅

### 처음 만드는 사람 (김찬중)

1. GitHub에서 빈 저장소 `com-modelhouse` 생성 (README, .gitignore 추가 체크 해제)
2. Unity Hub → Unity `6000.4.0f1` 설치 → New project → **Universal 3D** → 이름 `com-modelhouse`
3. 이 README, `.gitignore`, `.gitattributes`를 프로젝트 루트(`Assets` 폴더 옆)에 복사, `docs/SRS_COM_v2.5.docx` 추가
4. 버전 관리 설정: **Edit → Project Settings**
   - 왼쪽 목록의 **Version Control** → Mode: **Visible Meta Files** (Unity 6에서는 이게 기본값이라 이미 선택돼 있으면 그대로 둔다)
   - 왼쪽 목록의 **Editor** → Asset Serialization → Mode: **Force Text** (기본값이면 그대로 둔다)
   - Version Control은 Editor 안이 아니라 Project Settings 왼쪽 목록에 따로 있는 항목이다
5. Window → Package Manager → Input System, Cinemachine, ProBuilder, Animation Rigging 설치
6. Asset Store에서 **Modern Apartment**(Zeps3D)를 각자 구매 → Package Manager → My Assets → Import, Project 창에서 `Assets/Zeps3D`를 `Assets/ThirdParty/` 아래로 옮김
   - 데모 씬 라이트맵은 우리 씬에서 다시 굽는다
   - 커피 머신과 머그컵은 에셋에 없으므로 ProBuilder로 만든다
7. URP 설정: Rendering Path = Forward+, 고정 물체는 Static + 간접광만 굽기(Mixed, Baked Indirect), 구역마다 Reflection Probe, Global Volume(Tonemapping ACES, Bloom, SSAO). 햇빛은 시계로 실시간 변경
8. 첫 커밋

```bash
cd com-modelhouse
git init
git status                 # Library/, Temp/, Assets/ThirdParty/ 가 목록에 없는지 확인
git add .
git commit -m "chore: Unity 프로젝트 초기 설정 및 README"
git branch -M main
git remote add origin https://github.com/<계정>/com-modelhouse.git
git push -u origin main
```

9. Settings → Collaborators → 권오민 초대

### 받아서 여는 사람 (권오민)

```bash
git clone https://github.com/<계정>/com-modelhouse.git
```

Unity Hub → Add → Add project from disk → 클론한 폴더 선택 (Unity `6000.4.0f1`), Modern Apartment는 각자 Import

---

## 협업 규칙

| 항목 | 규칙 | 예 |
| --- | --- | --- |
| 브랜치 | `main`은 발표용 최종본. 통합은 `develop`, 기능은 `feature/이름-기능`에서 작업 후 `develop`으로 PR | `feature/chanjung-robot-finish` |
| 커밋 | `타입: FR번호 한 줄 요약` (feat, fix, refactor, docs, chore) | `feat: FR-13 커피 로봇 컵 보관대·싱크대` |
| 리뷰 | PR은 상대방이 리뷰 후 머지. 오너가 설명하지 못하는 코드는 미완성 | |
| 씬 | `MainScene.unity`는 둘 다 수정하므로 작업 전 `develop`을 pull, 씬 충돌은 오브젝트 단위로 합친다 | |
| 마감 | 매일 19:00 기록 공유 전에 PR 머지 | |

---

## 일정

작업 가능 시간: 09:30 ~ 19:10 (점심 12:20 ~ 13:20)

| 날짜 | 단계 | 끝나면 있어야 할 것 |
| --- | --- | --- |
| 10/6 (화) | 기획·설계 | 기획서, 클래스 설계, SRS |
| 10/7 (수) | 에셋·씬 구축 | Modern Apartment 임포트, 4구역, 조명 굽기, 로봇팔 리그 |
| 10/8 (목) | 기능 구현 1 | 1인칭 이동, 옵션 색상 교체, 커피 로봇 IK |
| 10/12 (월) | 기능 구현 2 | Must 전부 동작 (MVP), 시간대, 커피 스테이션, 통합 |
| 10/13 (화) | 폴리싱·빌드 | 12:30 기능 동결, 빌드 테스트 1 |
| 10/14 (수) | 발표 | 빌드 테스트 2, 시연 영상, 발표, 회고 |

---

## 트러블슈팅

| 날짜 | 문제 | 원인 | 해결 |
| --- | --- | --- | --- |
| 10/12 | 씬에 로봇팔이 안 보임 | 프리팹 안 UR3가 19m 떨어진 곳에 있었음 | UR3 위치를 0으로, 데모용 식탁은 끔 |
| 10/12 | 로봇팔이 클릭되지 않음 | 로봇 마디가 Default 레이어, 커서는 Interactable 레이어만 검사 | 마디 레이어를 Interactable로 |
| 10/12 | 시각 버튼을 눌러도 밤에 창문이 밝음 | 창문 유리 재질 `m_glass`가 HDR 발광(약 13) | 시각마다 실행 중 복사본의 발광 세기를 낮춤 |
| 10/12 | 커피 머신이 엉뚱한 곳에서 컵을 받음 | 머신 몸체·추출구를 옮겨 컵 자리와 어긋남 | 시작할 때 컵 자리를 추출구 바로 아래로 자동 정렬 |
| 10/12 | 로봇팔이 식탁·머신을 뚫고, 마디끼리 겹침 | 매번 IK를 여러 번 풀어 고르는 방식이라 자세 계열이 바뀌며 엉뚱한 길로 이동 | 정해진 이동 패턴(물러나기 → 접기 → 조인트1 회전 → 펴기 → 직선 진입)으로 재설계 |
| 10/12 | 바꾼 Inspector 기본값이 적용 안 됨 | 씬에 저장된 값이 코드 기본값보다 우선 | 의미가 바뀐 값은 새 필드 이름으로 |
| 10/12 | 두 사람이 고친 MainScene 병합 충돌 | 둘 다 새 오브젝트를 추가해 파일 안 같은 위치에 들어감 | 오브젝트(fileID) 단위로 합치고 참조 끊김 0개 확인 |

---

## 코드 리뷰 · 멘토링 기록

| 날짜 | PR / 주제 | 리뷰 내용 | 결과 |
| --- | --- | --- | --- |
| | | | |

---

## 회고

<!-- 10/14 작성: 잘한 점, 아쉬운 점, 다음에 바꿀 점 -->
