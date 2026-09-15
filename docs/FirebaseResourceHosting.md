# Firebase 리소스 배포

- 프로젝트: `bm-cardbattle`
- 전용 Hosting 사이트: `bm-cardbattle-assets`
- 주소: `https://bm-cardbattle-assets.web.app/{앱 버전}/{플랫폼}/`
- 배포 폴더: 저장소 루트의 `ServerData/` (Git 제외)
- 원격 대상: `CardArt` 그룹의 `Cards`·`Packs` 라벨과 `RemoteUI` 그룹의 `RemoteUI` 라벨. UI 그룹은 프리팹·아틀라스 18개·UI 카탈로그·튜토리얼 폰트 등 48개 명시적 항목과 그 의존성을 포함한다.
- 시작 씬의 로딩·로그인·실패 복구 UI는 다운로드 전에 표시해야 하므로 앱에 남긴다. 씬·설정 SO에 직접 연결된 자산을 모두 원격화하는 변경은 아니다.

## 첫 빌드·배포

`Tools > Card Battle > 릴리즈 관리 > 리소스` 탭에서 **리소스 전체 빌드 → 배포 전 검사 → Firebase Hosting 배포**를
순서대로 실행할 수 있다. 대상 앱 버전·플랫폼·다운로드 주소와 명령 성공/실패 결과도 같은 탭에 표시된다.
배포는 Firebase CLI 로그인 계정을 사용한다. 아래는 개별 메뉴·터미널로 실행하는 방법이다.

1. Unity에서 플레이를 종료한다. Build Profiles에서 배포 플랫폼(Android/iOS 등)을 선택한다.
2. Player Settings의 앱 버전을 확인한다. 버전이 원격 경로의 일부다.
3. `Tools > Addressables > Build Firebase Resources`를 실행한다.
4. 저장소 루트에서 `firebase deploy --only hosting --project bm-cardbattle` 실행.
   PowerShell에서는 `firebase.cmd`를 사용한다. Functions·Firestore는 배포하지 않는다.
5. 같은 플랫폼·앱 버전으로 앱을 빌드한다. Addressables의 Build Addressables on Player Build는 `Do Not Build Addressables content on Player Build`로 유지한다. 이미 빌드·배포한 리소스를 앱에 연결하며, 리소스 변경 시에는 3~4번을 다시 실행한다. 첫 설치에서 카드와 UI를 합친 다운로드 용량이 표시되는지 확인한다. UI 로컬→원격 전환은 새 앱 빌드가 필요하다. 설정을 바꿔 앱 빌드가 리소스를 다시 생성했다면 마지막 산출물을 배포한다.
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

`CardArt`와 `RemoteUI`는 `Pack Separately`로 빌드한다. 카드·팩 이미지는 등록 항목별,
UI는 등록된 프리팹·아틀라스·설정 자산별 번들을 만든다. 캐시가 있으면 콘텐츠 업데이트에서
변경되거나 새로 추가된 번들만 받는다. UI 전체를 한 번들로 다시 받지 않는다.
단, 아틀라스 안의 이미지 하나를 수정해도 해당 아틀라스 번들은 전체 다운로드한다.
공유 의존성 변경으로 관련 번들도 바뀔 수 있으므로 원본 파일 하나와 다운로드 번들 하나가 항상 일치하지는 않는다.

`RemoteUI`의 `Pack Together` → `Pack Separately` 전환은 새 앱 버전의 전체 리소스 빌드·배포와
그에 맞는 앱 빌드로 새 기준을 만든다. 이전 묶음 설정으로 출시한 상태 파일에 이번 그룹 설정 변경을
그대로 적용해 콘텐츠 업데이트하지 않는다. 새 기준으로 출시한 뒤의 리소스 수정부터
`Update a Previous Build`를 사용한다. 이번 설정 변경만으로 기존 Hosting 산출물이 갱신되지는 않는다.

시작 UI가 원격 통신을 기다리지 않도록 자동 카탈로그 업데이트를 끄고,
로그인·스펙 동기화 이후 비동기로 카탈로그를 확인·갱신한다. 이어 Cards·Packs·RemoteUI
라벨 번들의 미캐시 용량을 중복 없이 합산하고 다운로드한다.
다운로드가 끝난 뒤 CardArtCache·PackArtCache·UiPrefabCache가 적재를 시작한다. UI 카탈로그와
튜토리얼 폰트도 비동기로 선로드해 동기 소비자에 주입한다. 선택형 화면의 지연 적재 정책은 유지한다.
다운로드 실패는 기존 복구 화면에 표시하며, 재시도는 리소스 단계부터 진행한다.
번들 요청은 30초 타임아웃·2회 재시도를 사용한다. 정상 다운로드를 로딩 연출 시간 제한으로 끊지 않는다.
에디터 단독 씬의 UI 폴백은 AssetDatabase를 사용하며 원격 HTTP를 동기로 기다리지 않는다.

## UI 원격 전환 검증 (2026-09-14)

- Android 리소스 빌드 성공. `RemoteUI` 항목 48개(아틀라스 18개 포함)가 원격 UI 번들 한 개, 27,040,485 bytes로 생성됐다.
- 빌드 보고서 `Library/com.unity.addressables/BuildReports/buildlayout_2026.09.14.16.47.38.json`에서 HTTPS LoadPath와 명시적 자산·라벨, 번들 간 중복 자산 0개를 확인했다.
- 배포 전 로컬 검사 통과. `SpecData.bytes` SHA-256 변경 없음.
- UI 적재 실패 재시도는 이전 Abort 컨텍스트를 재사용하지 않고 프로필을 보존한 새 컨텍스트로 이어간다.
- 앱 빌드·캐시가 없는 실기기 다운로드 및 망 복구 재시도는 미실행이다. 리소스 배포 결과는 아래 기록을 따른다.

## 테스트 리소스 배포 완료 (2026-09-14)

- `0.0.0/Android` 리소스를 `bm-cardbattle-assets` Hosting 사이트에 배포했다. Functions·Firestore 배포는 실행하지 않았다.
- 배포 전 팩 이미지 4개의 초기 적재 순서를 수정했다. `Cards`·`Packs`·`RemoteUI`를 모두 다운로드한 뒤 각 캐시를 적재하며, 팩 적재 실패도 리소스 단계부터 재시도한다.
- 원본 프로젝트에서 최종 빌드 성공. 보고서: `Library/com.unity.addressables/BuildReports/buildlayout_2026.09.14.17.07.21.json`. CardArt 162개, RemoteUI 48개, 번들 간 중복 자산 0개.
- 배포 검사 테스트 6개와 기존 배포 파일 보존 검사 통과. `SpecData.bytes` SHA-256 변경 없음.
- CDN의 리소스 172개 모두 HTTP 성공·CORS·캐시 헤더 검사 통과. 카탈로그·해시와 카드·팩·UI 대표 번들 등 12개를 실제 다운로드하여 원본 SHA-256과 일치함을 확인했다.
- CDN 검증 기록: `Build/ResourceDeploymentChecks/1789373378492.json`. 공개 파일 목록: `https://bm-cardbattle-assets.web.app/resources-manifest.json`.
- 이번 배포는 리소스만이다. 새 Android 앱은 같은 버전·플랫폼으로 빌드한다. 앱 빌드에서 Addressables를 재생성했다면 마지막 리소스를 다시 배포한다.
- Unity 연결 장애 동안 만든 `Build/ResourceBuildSnapshot-20260914-1701`은 사용하지 않았다. 삭제가 자동 승인 검토에서 정책상 차단되어 임시 복사본이 남아 있다.

## APK 최초 실행 카탈로그 404 복구 (2026-09-14)

- APK 빌드가 새로 생성한 `catalog_2026.09.14.08.21.50.hash`를 요청했지만, Hosting에는 이전 리소스 빌드까지만 배포되어 있었다.
- 해당 카탈로그 JSON·해시를 기존 리소스와 함께 추가 배포했다. 기존 APK는 재빌드 없이 앱 재실행으로 다시 확인할 수 있다.
- 프로젝트의 `BuildAddressablesWithPlayerBuild`를 `DoNotBuildWithPlayer`로 저장했다. 이후 리소스 전체 빌드 → Hosting 배포 → 앱 빌드 순서에서 앱 빌드가 새 카탈로그를 생성하지 않는다.
- 배포 후 174개 파일 HTTPS 응답 검사와 14개 다운로드 SHA-256 대조 통과. 검증 기록: `Build/ResourceDeploymentChecks/1789374520098.json`.

## 카드 일러스트 단독 패치 (2026-09-15)

- 대상: `Image_Card_Campbean_Stage1` (`Assets/Assets/Images/Cards/Stage1/Image_Card_Campbean_Stage1.png`).
  내장 imagegen으로 목 장식·불꽃 문양 천의 붉은색을 청록색으로 변경했다. 원본과 생성 프롬프트는 아래 작업 기록에 보관한다.
- `0.0.0/Android`의 기존 카탈로그 7개에 카드 번들 하나만 교체해 `bm-cardbattle-assets` Hosting 배포 완료.
  새 번들은 `cardart_assets_image_card_campbean_stage1_a9fa04b207b055e0c3e643157c2d824c.bundle`, 521,424 bytes다.
  기존 번들 캐시가 있는 앱의 추가 리소스는 이 번들 약 509 KiB와 갱신 카탈로그·해시다.
- 현재 소스에는 이전 배포 이후 UI 수정이 있으므로 표준 `BuildContentUpdate` 결과를 격리 빌드한 뒤,
  기존 공개 카탈로그에서 해당 leaf 번들의 URL·request options만 선택 반영했다. 키·타입·provider·의존성과
  다른 모든 번들 참조는 유지했다. UI 개별 번들 설정은 이번 배포에 포함하지 않았다.
- 새 번들은 해당 GUID 자산 1개, 의존성 0개. Unity에서 CRC 검사와 기존 내부 주소의 Sprite 로드 성공.
  카탈로그 7개 전체 의미 비교, 기존 공개 파일 보존 검사, CDN manifest 186개 일치 및 변경 파일 15개
  실제 다운로드 SHA-256 대조 완료. 연결된 Android 기기가 없어 앱 재실행 화면 검증은 미실행이다.
- 원본·출시 카탈로그 백업·격리 빌드·패치 스크립트·검증: `Build/CardIllustrationPatch-20260915/`.
  `patch-report.json`, `bundle-layout-proof.json`, `verification.txt`, `cdn-verification.json`을 보존한다.
  이 선택 패치는 격리 빌드 전체와 다른 카탈로그이므로 격리 빌드를 다음 출시 기준으로 간주하지 않는다.
  다음 새 앱 릴리스에서 전체 빌드·상태 파일을 새로 확정한다. `SpecData.bytes` SHA-256은 변경 없음.

## 확인 명령

```powershell
node scripts/validate-resource-hosting.cjs --offline
firebase.cmd hosting:sites:list --project bm-cardbattle
firebase.cmd deploy --only hosting --project bm-cardbattle
```

공식 문서: https://firebase.google.com/docs/hosting/full-config
