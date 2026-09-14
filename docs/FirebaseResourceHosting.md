# Firebase 리소스 배포

- 프로젝트: `bm-cardbattle`
- 전용 Hosting 사이트: `bm-cardbattle-assets`
- 주소: `https://bm-cardbattle-assets.web.app/{앱 버전}/{플랫폼}/`
- 배포 폴더: 저장소 루트의 `ServerData/` (Git 제외)
- 첫 원격 대상: `CardArt` 그룹의 `Cards` 라벨. UI·폰트·설정·팩 아트는 로컬 유지.

## 첫 빌드·배포

`Tools > Card Battle > 릴리즈 관리 > 리소스` 탭에서 **리소스 전체 빌드 → 배포 전 검사 → Firebase Hosting 배포**를
순서대로 실행할 수 있다. 대상 앱 버전·플랫폼·다운로드 주소와 명령 성공/실패 결과도 같은 탭에 표시된다.
배포는 Firebase CLI 로그인 계정을 사용한다. 아래는 개별 메뉴·터미널로 실행하는 방법이다.

1. Unity에서 플레이를 종료한다. Build Profiles에서 배포 플랫폼(Android/iOS 등)을 선택한다.
2. Player Settings의 앱 버전을 확인한다. 버전이 원격 경로의 일부다.
3. `Tools > Addressables > Build Firebase Resources`를 실행한다.
4. 저장소 루트에서 `firebase deploy --only hosting --project bm-cardbattle` 실행.
   PowerShell에서는 `firebase.cmd`를 사용한다. Functions·Firestore는 배포하지 않는다.
5. 같은 플랫폼·앱 버전으로 앱을 빌드한다. 첫 설치에서 카드 리소스 다운로드 용량이 표시되는지 확인한다.
6. 다시 실행했을 때 캐시된 번들의 다운로드 용량이 0인지, 다운로드 중 망을 끊었다 복구 화면에서 재시도가 되는지 확인한다.

Editor의 Use Asset Database 모드는 HTTP 다운로드를 하지 않는다. 원격 다운로드 검증은
실기기 빌드 또는 해당 플랫폼 리소스를 빌드한 뒤 Use Existing Build 모드에서 한다.

## 패치와 기존 앱 보존

- 최초 전체 빌드의 `addressables_content_state.bin`은 빌드 메뉴가
  `Build/AddressablesState/{앱 버전}/{플랫폼}/{시각}/`에 복사한다. CI 아티팩트나 별도 보관소에 보존한다.
- 출시된 앱의 리소스 패치는 Addressables Groups의 `Build > Update a Previous Build`에서
  **그 앱을 출시할 때의 상태 파일**을 선택한다. 전체 빌드는 새 앱 릴리스용이다.
- 이후 같은 Hosting 배포 명령을 실행한다. 카탈로그·해시는 재검증하고, 해시 이름의 번들은 장기 캐시한다.
- 플랫폼·앱 버전이 다른 카탈로그를 섞지 않는다. 앱 버전을 바꿨다면 새 경로와 앱 빌드가 필요하다.
- **ServerData의 기존 버전·플랫폼·번들을 유지한다.** Firebase Hosting은 폴더 전체를 새 릴리스로 올리므로
  이전 파일을 지우면 아직 그 파일을 쓰는 앱이 다운로드에 실패한다.
- 새 작업 PC/CI에서는 이전 `ServerData` 아티팩트를 먼저 복원한다. predeploy 검사는 기존 공개 목록과
  대조해 파일 누락·같은 이름의 번들 덮어쓰기를 막는다. `--offline`은 로컬 검사 전용이다.
- `SpecData.bytes`와 콘텐츠 업데이트 상태 파일은 Hosting에 업로드하지 않는다.

## 다운로드 동작

시작 UI의 동기 로딩이 원격 통신을 기다리지 않도록 자동 카탈로그 업데이트를 끄고,
로그인·스펙 동기화 이후 비동기로 카탈로그를 확인·갱신한다. 이어 Cards 라벨의
미캐시 번들 용량을 조회하고 다운로드한 다음 CardArtCache가 Sprite를 적재한다.
다운로드 실패는 기존 복구 화면에 표시하며, 재시도는 리소스 단계부터 진행한다.
번들 요청은 30초 타임아웃·2회 재시도를 사용한다. 정상 다운로드를 로딩 연출 시간 제한으로 끊지 않는다.

## 확인 명령

```powershell
node scripts/validate-resource-hosting.cjs --offline
firebase.cmd hosting:sites:list --project bm-cardbattle
firebase.cmd deploy --only hosting --project bm-cardbattle
```

공식 문서: https://firebase.google.com/docs/hosting/full-config
