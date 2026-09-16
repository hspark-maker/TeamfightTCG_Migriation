# 2026-09-16 병합 충돌 해결 기록

대상: `a0359fc06`의 작업 브랜치에 `4d9d19fe8734e58286d8606552e337f56d525036` 병합.

## 프리팹 개별 대조

공통 조상·현재 브랜치·들어오는 변경을 fileID별로 비교했다. YAML 블록의 단순 순서 이동과 실제 속성 변경을 구분하고, 들어오는 프리팹에 아래 기존 설정을 합쳤다.

| 프리팹 | 기존 설정 보존 | 들어오는 변경 보존 |
| --- | --- | --- |
| `LoadingCover.prefab` | 배경 컴포넌트 `784662356230184902`의 `verticalZoom: 1.1` | 로그인 패널 참조 교체, 시작 안내, 진행 표시 CanvasGroup 및 연출 연결 |
| `LobbyCanvas.prefab` | Match 탭 배경 참조 해제, BATTLE 텍스트의 외곽선 없는 재질 | 매칭·오버레이 위치/높이, 컬렉션 셀 크기, 로비 가이드 관련 오버라이드 |
| `MatchmakingRoot.prefab` | 양쪽 닉네임 줄바꿈 해제, 구분선 활성화 | Contents 활성화, 기존 viewContents와 BG 입력 차단 연결 |

`LoadingVideoBackground.cs`에는 병합 충돌과 별도로 원본 비율 유지 방식으로 바꾸는 미스테이징 수정이 있었다. 그 수정은 건드리지 않았다. 해당 코드에서는 위 `verticalZoom` 직렬화 값이 더 이상 사용되지 않는다.

## 나머지 충돌

- `ServerSaveCommands.cs`: 계정 경험치 누적치 보정과 온보딩 복구를 통합했다. 저장된 응답 재생·복구 경로에도 누적치 보정을 적용했다.
- `MissionPanel.cs`: 경험치/레벨업 표시, 보상 종료 콜백, 개별 카드 연출을 통합했다. 경험치만 있는 보상을 빈 보상으로 처리하지 않게 했다.
- `functions/lib/index.js`, `.map`: TypeScript의 양쪽 export를 확인하고 빌드로 재생성했다. `enhanceCard` 산출물도 원본 TS의 SpendShard 진행도 반영과 일치시켰다.
- 전투 경험치 테스트의 Firestore 모의 객체에 `parent`를 추가해 새 공용 저장 코드와 호환시켰다.
- `EventMessage.meta`와 `_Recovery/0 (2).unity.meta`: 이름 변경 오인으로 섞인 메타데이터를 각각 원래 브랜치에서 복구했다. GUID는 각각 `bb950ba81e4c6ca47acde820f6061ef1`, `ae3551fa51a1be842bb1934a6730bd1a`다.
- `AddressableAssetsData/link.xml.meta`: 양쪽 삭제를 유지했다. 대응 `link.xml`도 없다.

## 검증과 남은 확인

- 프리팹 YAML 파싱: LoadingCover 82개, LobbyCanvas 239개, MatchmakingRoot 106개 오브젝트.
- 중복 fileID, 누락된 내부 참조, 컴포넌트 소유자, 자식의 부모 연결, 외부 GUID, 중첩 프리팹 대상 fileID 검사 통과.
- 들어오는 프리팹과 최종 프리팹의 모든 속성을 비교해, 의도한 기존 설정 복원 외에 차이가 없음을 확인했다.
- 복구한 메타데이터 GUID는 Assets에서 각각 한 번만 선언된다.
- Functions 빌드, 온보딩 테스트, 전투 경험치 테스트 6개 시나리오 통과.
- 최종 Git 미해결 충돌 0개. 변경된 텍스트 파일 109개의 충돌 마커 검사 통과.
- 전체 staged diff 검사에는 Unity YAML/메타데이터 등의 줄 끝 공백 경고 115개가 남아 있다. 그 외 diff 경고는 없으며, 이번 충돌 해결에서 일괄 공백 정리는 하지 않았다.
- Unity 컴파일·플레이·실제 화면 확인은 미실행이다. Unity 연결이 철회된 상태이므로 우회 실행하지 않았다.
- `.gitattributes`의 “어느 쪽을 골랐든 Unity 로 열어 눈으로 확인하고 커밋할 것” 규칙에 따라 병합 커밋은 만들지 않았다. 실제 화면 확인 후 커밋해야 한다.

로컬 상세 대조 자료: `Build/MergeReview-20260916/` (Git 제외). SpecData 산출물, 빌드 배포, 푸시는 변경하지 않았다.
