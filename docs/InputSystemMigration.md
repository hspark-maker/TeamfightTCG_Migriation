# Unity Input System 전환

2026-09-17. 설치된 Input System 1.18.0을 사용하며 Player Settings의 Active Input Handling은 **Input System Package (New)** (`activeInputHandler: 1`)이다.

## 입력 경로

- UI: `InputSystemUIInputModule`의 기본 UI 액션을 사용한다. 자체 씬의 구형 `StandaloneInputModule`도 교체했다.
- 전투 카드: `CardView`의 포인터 이벤트로 시작하고 카메라의 `Physics2DRaycaster`로 `Collider2D`를 찾는다. BattleScene, AttackTestScene, AttackAnimScene에 배선했다.
- 화면 탭·팩 개봉·멀리건: `GameInput`에서 입력을 읽는다. 키보드 단축키는 `Keyboard.current`를 사용한다.
- 드래그·롱프레스: `InputPointer`가 시작 장치와 touch ID를 보관한다. 다른 손가락으로 넘어가지 않으며 취소·장치 제거·포커스 상실은 제스처를 취소한다.
- `EnhancedTouchSupport`는 씬 로드 전에 활성화한다. 자체 런타임 코드에 구형 `Input.*`/`OnMouse*` 입력은 남기지 않았다.

튜토리얼 카드 강조 패널은 시각 효과이므로 레이캐스트를 받지 않는다. 안내 탭을 기다리는 별도 마스크는 기존 입력 차단 역할을 유지한다.

## 검증

플레이 모드에서 Game 뷰에 포커스를 두고 `Tools > Input > Validate Input System Migration`을 실행한다. 실제 Input System 이벤트 큐에 테스트 장치 입력을 넣어 마우스, 다중 터치, 장치별 touch ID, 이동·해제·취소, 빠른 탭, 장치 비활성화·제거를 검증한다. 격리된 UI 버튼과 `Collider2D`에도 같은 입력을 보내 down/up/click 이벤트와 장치 정보를 확인한다. 테스트 중 게임 UI 모듈과 레이캐스터를 잠시 끄고 종료 시 복원한다.

에디터에서 새 백엔드로 재시작하고 StartScene에서 로비까지 초기화를 확인했다. 위 입력 검증은 UI 버튼·Collider2D 전달까지 PASS했고 최종 컴파일도 완료했다. Android/iOS 실기기 터치는 별도 확인이 필요하다. 매칭 서버에서 `content_fingerprint_mismatch`가 관찰되어 실제 전투 한 판의 전체 진행은 검증하지 못했다.

## 범위와 보존

게임에서 사용하는 자체 입력과 씬을 전환했다. 게임 진입 경로에 포함되지 않는 외부 패키지 데모의 구형 입력 코드는 패키지 원본으로 남아 있다. 사용 중인 외부 패키지는 새 Input System 분기를 확인했다.

에디터 재시작 전 열려 있던 LobbyScene의 미저장 편집은 저장했다. 저장 전 디스크 상태와 에디터 상태의 복사본은 `Temp/InputSystemMigrationBackup/`에 남겼다. 전환용 임시 에디터 도구는 제거했다.
