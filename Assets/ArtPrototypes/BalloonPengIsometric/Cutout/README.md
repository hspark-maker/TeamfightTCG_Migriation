# 분리형 BalloonPeng 걷기

몸통·양날개·양발을 실제로 분리한 다섯 파트로 다시 구성했습니다. 이전 한 장 메시의 발 가중치가 배를 끌어당기던 문제를 제거합니다.

## 사용

- 세 모션을 함께 사용하는 프리팹은 `BalloonPeng_Cutout_Motions.prefab`입니다. `BalloonPeng_Motions_Preview.unity`에서 통통 뛰기가 기본 재생됩니다.
- `BalloonPeng_Motions.controller`의 정수 `Motion`으로 0=대기, 1=걷기, 2=통통 뛰기를 선택합니다. 표정 레이어는 세 모션에서 계속 재생합니다.
- `BalloonPeng_Cutout_Preview.unity`를 열고 Play를 누릅니다.
- 게임 씬에서 사용할 프리팹은 `BalloonPeng_Cutout_Walk.prefab`입니다.
- `Walk.anim`은 1.2초 제자리 두 걸음 루프입니다. 이동 경로 로직은 포함하지 않습니다.
- `ExpressionLoop.anim`은 걷기와 별도 레이어에서 4.8초마다 눈 깜빡임과 짧은 웃음을 재생합니다.
- 몸통은 실제 투명 RGBA `BalloonPeng_Parts.png`를 그대로 사용합니다. 팔다리는 가려지는 뿌리와 발목까지 채운 2×2 아틀라스 `BalloonPeng_LimbsExtended.png`를 사용합니다.

## 구조

### 대기 모션

- `BalloonPeng_Cutout_Idle.prefab`은 걷기 프리팹의 몸통·팔다리·얼굴 패치를 상속하는 variant입니다. 같은 그림·메시·머티리얼을 공유합니다.
- `BalloonPeng_Idle_Preview.unity`를 열어 Play로 확인합니다. `Idle.anim`은 2.4초 반복이며, 발은 고정하고 몸통과 날개를 천천히 움직입니다.
- `Idle.controller`의 `IsWalking`을 켜면 걷기, 끄면 대기로 0.18초 동안 전환합니다. 얼굴 표정은 별도의 4.8초 루프로 계속 재생합니다.
- 생성·갱신 메뉴: `Tools > Art Prototypes > Build BalloonPeng Cutout Idle`. 기존 걷기 프리팹·컨트롤러·클립과 아트는 보존합니다.
- 대기 미리보기: `output/balloonpeng-idle/BalloonPeng_Idle_Expressions.webp` (4.8초, 96프레임).
- Unity 검증: 반복 시작·끝 위치/회전 오차 0, 16개 발 고정 샘플·40개 표정 상태 검사 및 대기→걷기→대기 전환 통과. 48개 자세에서 접합부 1,728개 지정 지점 모두 몸통 알파 1로 가려짐. 원본 걷기 프리팹·컨트롤러·클립과 몸통 이미지·메시 해시 보존.

### 통통 뛰기와 통합 모션

- `Bounce.anim`은 1.2초 루프입니다. 준비 자세 → 몸과 양발이 함께 0.48 높이로 상승 → 착지 → 짧은 휴식 순서입니다. 몸통을 변형하지 않고 파츠 이동·회전만 사용합니다.
- `Tools > Art Prototypes > Build BalloonPeng Bounce`로 생성·갱신합니다. 통합 컨트롤러의 모션 전환 시간은 0.16초입니다.
- 비교 미리보기: `output/balloonpeng-motions/BalloonPeng_Idle_Walk_Bounce.webp` (왼쪽부터 대기·걷기·통통 뛰기). 단독 미리보기: `output/balloonpeng-bounce/BalloonPeng_Bounce_Expressions.webp`.
- 검증: 96프레임 렌더, 루프 시작·끝 오차 0, 접합부 1,728개 지정 지점 가림 통과, 9개 모션 전환 조합 및 40개 표정 상태 검사 통과. 점프 뒤 대기로 돌아오면 양발 원위치 복구. 기존 걷기·대기 클립/원본 프리팹과 몸통 메시 해시 보존. 관련 Unity 콘솔 오류 없음.

### 파츠 구성

- 각 파트는 서로 다른 쿼드 메시와 MeshRenderer를 사용합니다. SkinnedMeshRenderer와 스킨 가중치는 없습니다.
- 머리·배·스카프를 포함한 몸통은 고정된 그림입니다. 전체의 작은 상하 이동과 기울기만 허용하며 스케일 애니메이션은 없습니다.
- 표정은 Body 아래 눈·부리 패치 5개로 표시합니다. 원본 얼굴 위에 생성한 눈 감음·웃음 그림의 해당 영역만 겹치고 가장자리를 부드럽게 섞습니다. 표정이 끝나면 원본 얼굴을 그대로 표시합니다. `FaceBlink.png`·`FaceSmile.png`의 배경 영역은 사용하지 않습니다.
- 발은 몸통 아래의 자식이 아니라 독립된 형제 관절입니다. 양발을 번갈아 0.1 단위 들어 올려도 몸통 정점이나 배 윤곽을 바꾸지 않습니다.
- 날개와 발이 몸통에 가려지는 접합부를 여유 있게 겹쳐 조립했습니다.
- 화면 왼쪽 날개는 몸 뒤(-2), 화면 오른쪽 날개(WingNear)는 몸 앞(1)에 표시합니다. 오른쪽 날개는 연결 연장부가 없는 원본 `BalloonPeng_Parts.png`의 짧은 파츠(폭 0.74, 원본 피벗)로 복원했습니다. 다른 날개와 발은 확장된 뿌리·발목을 유지합니다. 이전의 ‘네 접합부 모두 몸 뒤’ 가림 검증은 이 변경 전 결과입니다.
- 최신 짧은 앞날개 미리보기: `output/balloonpeng-short-wing/BalloonPeng_ShortWing_Motions.webp`. 96프레임에서 대기·걷기·통통 뛰기의 길이와 앞뒤 순서를 확인했습니다.
- 확장된 팔다리의 크기와 피벗은 `HiddenPartSizes`·`HiddenPartPivots`에서 정합니다. 기존 메시의 식별자는 유지하며 실제 정점·UV 버퍼를 갱신합니다.

## 아트 제작

내장 이미지 생성 도구로 기존 캐릭터에서 몸통과 팔다리를 분리하고 가려졌던 면을 채웠습니다. 생성 결과에 불투명 체크무늬가 남아 사용자의 명시적 승인 후 코드로 외부 배경만 제거했습니다. 그림 내부 색상과 흰 배·하이라이트는 보존했습니다. 생성 프롬프트는 `GenerationPrompts.txt`, 배경 제거 전 원본과 처리 도구는 프로젝트 루트 `output/balloonpeng-cutout/`에 있습니다.

## 검증

- Unity 생성·컴파일·Animator 재생과 검수 통과. 관련 콘솔 오류 없음.
- 몸통·팔다리 렌더러 5개와 표정 패치 5개, 스킨 렌더러 0개, 스케일 커브 0개.
- 발만 이동·회전했을 때 몸통 정점의 월드 좌표 변화 0.
- Animator 재생으로 두 발의 0.1 단위 들어 올리기가 번갈아 발생하는 것을 확인.
- 48개 시점의 실제 Unity 렌더 확인. 루프 시작·끝 위치와 회전 차이 0.
- 미리보기: `output/balloonpeng-cutout/BalloonPeng_Cutout_Walk.webp` (1.2초, 48프레임).
- 최신 미리보기: `output/balloonpeng-cutout-extended/BalloonPeng_Walk_CompletedParts.webp` (1.2초, 48프레임).
- 확장된 네 뿌리의 내부 지점 9개씩을 48개 자세에서 검사한 총 1,728회 샘플 모두 몸통 알파 1로 가려짐을 확인했습니다. 전체 픽셀을 검사한 결과가 아니라 지정한 접합부 지점의 검증이며, 렌더 시각 검토를 병행했습니다.
- 확장 전후 원본 몸통 이미지·메시·머티리얼 및 걷기 클립 네 파일의 해시 일치 확인.
- 표정 추가 후 몸통 이미지·메시·걷기 클립 해시 일치. 실제 Animator의 8개 시점에서 표정 패치 on/off 40회 확인, 걷기 단독 대비 위치 오차 0.00000006 이하·회전 오차 0. 셰이더 오류 없음.
- 표정 포함 미리보기: `output/balloonpeng-expressions/BalloonPeng_Walk_Expressions.webp` (4.8초, 96프레임). 생성 프롬프트: `ExpressionPrompts.txt`.

## 저작

생성: `Tools > Art Prototypes > Build BalloonPeng Cutout` (기존 에셋 덮어쓰기 거절).
모션 갱신: `Tools > Art Prototypes > Update BalloonPeng Cutout Motion` (클립 GUID 유지).
확장 파츠 갱신: `Tools > Art Prototypes > Update BalloonPeng Hidden Parts` (기존 네 팔다리 메시와 프리팹 참조 유지).
표정 생성·갱신: `Tools > Art Prototypes > Update BalloonPeng Expressions` (`BalloonPengExpressionBuilder.cs`).
빌더: `../Editor/BalloonPengCutoutBuilder.cs`. 본체가 nominal 3×2 셀 아래로 내려오는 실제 아트 배치를 고려해 파트별 검색 영역을 사용합니다.
