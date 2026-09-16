# EXP 보상 아이콘

## 적용

- 자산: `Assets/Assets/Images/UI/Item_AccountExp.png` (투명 RGBA, 원본 1254×1254, Unity 최대 크기 512).
- 기존 게임 UI에 맞춘 크림색 바탕·금색 테두리·갈색 EXP 글자의 메달이다.
- `MissionRow`와 `MissionOverlay`의 완료 보상에 EXP 아이콘과 서버가 내려준 경험치 수량을 표시한다. 경험치가 0이면 숨기며 기존 재화 보상과 함께 표시한다.
- 미션 행의 기존 재화 아이콘·수량 글꼴·위치·크기는 경험치 추가 전 저작값을 유지한다. EXP는 보상 영역 위쪽 여백에 28px 아이콘과 최대 22px 숫자로 표시하며 행 높이와 버튼 배치를 바꾸지 않는다.
- 미션과 보상 팝업 제목에는 경험치 문구를 덧붙이지 않는다. EXP 전용 표시를 사용하려면 변경된 UI 리소스를 반영해야 한다.
- 미션·전투가 공유하는 `RewardClaimPopup`의 경험치 패널에도 같은 아이콘을 사용한다.
- 생성 방법: 내장 ImageGen. 생성 PNG를 수정 없이 복사하고 Unity Sprite 메타데이터를 작성했다.

## 생성 프롬프트

```text
Create one production-ready transparent PNG reward icon for a Korean casual fantasy card game. Square 1024x1024 canvas, genuinely transparent alpha background, no white backdrop or checkerboard pixels. A single chunky rounded hexagonal experience medallion, front-facing with subtle dimensional bevel and a small lower-right rim showing thickness. Warm honey gold rim, creamy ivory enamel center. In the center, the ONLY text must be exactly 'EXP' (uppercase E X P), very large, bold rounded block letters in deep warm brown with soft gold bevel; letters occupy most of the center and remain instantly readable at 48 pixels. Match polished mobile game inventory icons: thick smooth silhouette, warm golden highlights from upper left, subtle amber shading, friendly tactile painted 3D/cartoon finish, no photorealism. Keep decoration extremely simple: one thin inner gold border, no extra symbols or tiny ornament. Isolated medallion occupies 90 percent of canvas with equal transparent padding. No extra text, no numbers, no border around canvas, no glow cloud, no ground plane, no cast shadow outside the silhouette. This is an actual in-game sprite asset, not a UI mockup or presentation.
```

## 2026-09-16 반영 상태

- CSV와 테스트 서버 6.44: 활성 미션 28개에 일일 50, 주간 200, 가이드 100 경험치가 이미 들어 있다. 이번 UI 작업에서는 스펙 값을 변경하지 않았다.
- `bm-cardbattle` / `asia-northeast3`의 `getMissions`, `claimMission`을 경험치 지원 코드로 배포했고 두 함수의 `ACTIVE` 상태를 확인했다.
- 라이브 서버 6.6은 Mission에 `accountExp` 열이 없고 Reward에 AccountLevel 보상이 없다. 라이브 적용에는 해당 스펙의 별도 발행이 필요하다. 이번 작업에서 라이브 표를 발행하지 않았다.
- 모바일에는 변경된 클라이언트 코드와 UI 프리팹·아이콘 리소스 반영이 필요하다. 이번 작업에서 APK나 Addressables를 빌드·배포하지 않았다.
- 검증: 전체 런타임 C# 오프라인 컴파일, 서버 빌드·린트, 경험치 서버 테스트 13개, 세 프리팹의 Int64 ID·참조·소유 관계·계층·배선 및 보상 영역 간격 검증 통과.
- Unity 연결 권한이 해제된 상태라 에디터 Play 화면과 기기에서의 최종 렌더링은 확인하지 못했다.
- 표시 회귀 수정 검증: 기존 미션 행 오브젝트 95개를 경험치 아이콘 추가 전 백업과 비교하여 추가 배선 외 설정이 모두 동일함을 확인했다. EXP 사각형이 행 내부에 있고 제목·게이지·재화 아이콘·수량·버튼과 겹치지 않음을 저작 좌표로 검증했으며 전체 런타임 C# 컴파일을 다시 통과했다.
