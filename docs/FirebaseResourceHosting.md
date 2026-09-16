# Firebase 리소스 배포

빌드·카탈로그 수정·배포 전 [Addressables 오류와 재발 방지](AddressablesDeploymentPitfalls.md)를 읽는다. 아래 배포 이력의 초기 성공 기록에는 이후 기기에서 발견된 오류도 있으므로 최신 복구 기록을 함께 확인한다.

## 개발 Hosting 정리 (2026-09-16)

- 사용자 지시: 개발 단계에서는 미사용 리소스와 과거 릴리스를 누적 보존하지 않는다. 아래 과거 배포 이력의 전체 보존 방침보다 이 방침을 우선한다. 현재 앱이 사용하는 카탈로그와 의존성, 콘텐츠 업데이트 기준 상태 파일은 계속 보존한다.
- 현재 앱의 `Library/com.unity.addressables/aa/Android/settings.json`이 지정한 `0.0.0/Android/catalog_2026.09.15.09.20.47.json`을 기준으로 정리했다. 파일명 시각이 더 늦은 카탈로그를 임의로 선택하지 않았다.
- 공개 리소스 438개 / 767.9 MiB에서 251개 / 168.2 MiB로 축소했다(manifest 제외). 미참조 187개 / 599.7 MiB 제거. 남긴 파일은 카탈로그·해시 2개와 원격 의존 번들 249개다. 로컬 raytracing 번들의 RuntimePath 토큰은 그대로다.
- 카탈로그를 재직렬화하거나 리소스를 재빌드하지 않았다. 남긴 파일 전부를 CDN에서 내려받아 기존 SHA-256·크기·CORS를 대조했다. 삭제한 카탈로그·번들 대표 경로는 HTTP 404 확인. 카탈로그 SHA-256: `6d9dea5ead7dca715eb0fa0c39a9afca8aa765d145833a0e7f85900a3f03727e`.
- 정리 배포 버전 `879bf5c9bce202f6`만 FINALIZED로 남겼다. 이전 Hosting 버전 14개는 DELETE 후 DELETED 상태를 확인했다. live 채널 `retainedReleaseCount`는 999999에서 API 최소값 1로 낮췄다. Hosting API가 보고한 현 버전 크기는 160,739,517 bytes이며, 원본 리소스 합계와 집계 기준이 다르다. 콘솔 사용량 감소는 별도 반영될 수 있다.
- 검사에 명시적 정리 모드를 추가했다. `FIREBASE_RESOURCE_PRUNE_CATALOG`에 유지할 카탈로그 상대 경로를 설정한 경우에만 이전 파일 누락을 허용한다. 이 모드는 해당 카탈로그·해시·참조 파일만 포함해야 하며, 남기는 모든 파일이 기존 CDN manifest와 같은 해시여야 한다. `--offline`과 동시 사용은 차단한다. 일반 배포의 기존 파일/immutable 번들 보호는 유지한다. 검사 테스트 15개 통과.
- 작업 기록: `Build/HostingCleanup-20260916/`의 `plan.json`, `cdn-verification.json`, `cleanup-result.json`. 로컬에서 제외한 파일은 같은 폴더의 `removed-local/`로 옮겼으며 Hosting에는 올라가지 않는다.

### 다음 개발 리소스 정리 절차

1. 실제 지원 중인 앱의 카탈로그를 확인하고 원격 참조 파일 전체와 카탈로그·해시를 유지한다. 여러 앱을 동시에 지원해야 하면 단일 카탈로그 정리 모드를 사용하지 않는다.
2. `ServerData`에서 나머지 파일을 제외한다. 배포 전 카탈로그·해시·참조 파일이 기존 CDN과 동일한지 확인한다.
3. PowerShell에서 아래처럼 이번 명령에만 정리 모드를 적용한다. 새 콘텐츠 업데이트와 미사용 파일 정리는 별도 배포로 수행한다.

```powershell
$env:FIREBASE_RESOURCE_PRUNE_CATALOG = '0.0.0/Android/catalog_2026.09.15.09.20.47.json'
try { firebase.cmd deploy --only hosting --project bm-cardbattle }
finally { Remove-Item Env:FIREBASE_RESOURCE_PRUNE_CATALOG }
```

4. CDN의 남긴 파일을 검증한 뒤, 어떤 채널에서도 서비스하지 않는 과거 Hosting 버전만 삭제한다. 현재 버전과 다른 활성 preview 채널 버전은 삭제하지 않는다. 릴리스 보관 수 최소값은 1이다([Firebase 채널 API](https://firebase.google.com/docs/reference/hosting/rest/v1beta1/sites.channels)).

## RuntimeContentCatalog 로드 오류 복구·실기기 통과 (2026-09-16)

- PC 경로 복구 후 `Unable to load asset of type RuntimeContentCatalog`가 발생했다. 앞선 카탈로그 재직렬화가 의존성 순서도 바꿔 본체 번들이 26번째로 밀렸다. Addressables Provider는 첫 번들인 `monoscripts`에서 자산을 찾다가 실패했다. 같은 문제가 있는 자산은 63개였다.
- 기존 검증에서 의존성 목록을 정렬해 비교한 것이 순서 변경을 숨겼다. 수동 검증도 본체 번들을 직접 골라 읽어 실제 Provider의 첫 번들 선택 규칙을 놓쳤다.
- 원본 빌드의 Entry/Bucket/Key 데이터를 복원하고, URL 4개·캐시 해시 2개만 유지했다. 번들·APK 변경 없이 카탈로그·해시만 재배포했다. 복구 해시: `6a89913098c4ba6b1b5792524c34338a`.
- 692개 위치·830개 키·256개 자산의 의존성 순서 보존을 독립 검증했다. 첫 번들 선택 방식으로 `RuntimeContentCatalog` 타입 로드와 필수 설정 참조 검사 통과. **재배포 후 사용자가 실제 기기에서 통과했다고 확인했다.**
- 원인·검증 누락·재발 방지 규칙은 [오류 기록 6번](AddressablesDeploymentPitfalls.md)에 정리했다. 상세 증거: `Build/RuntimeCatalogLoadFix-20260916/`.

## Android 공통 번들 경로 오류 복구 (2026-09-16)

- 12:43 기기 로그의 `Library/com.unity.addressables/aa/Android/Android/...` 오류는 직전 배포의 카탈로그 재직렬화로 발생했다. `ContentCatalogData.CreateLocator()`는 `{UnityEngine.AddressableAssets.Addressables.RuntimePath}`를 현재 에디터 경로로 해석한다. 그 위치 객체를 그대로 `SetData()`에 넣어 저장하면서 PC 경로가 CDN 카탈로그에 고정됐다. 원본 빌드 JSON에는 정상 토큰이 있었다.
- 앞선 검증은 원격 번들 247개만 CRC 로딩하고 로컬 의존성 3개를 제외해 이 오류를 놓쳤다. PC의 CRC 로딩 성공만으로 Android 경로 호환성을 판단하지 않는다. 카탈로그를 고칠 때는 원본 미해석 경로를 보존하며, 위치 객체를 왕복 직렬화하지 않는다.
- 별도로 기본 그룹의 공통 `monoscripts`·`unitybuiltinassets` 번들이 로컬 경로였다. 리소스 업데이트 중 새 공통 번들이 생기면 기존 APK 안에는 없을 수 있으므로, 비어 있는 기본 그룹의 Build/Load Path를 Remote로 설정했다. Can Change Post Release도 확인했다. `FirebaseResourceBuild`는 이 설정이 되돌아가면 빌드를 차단한다.
- 현재 `09.20.47` 카탈로그의 잘못된 경로 3개만 수정했다. 공통 번들 2개는 검증한 기존 빌드 파일을 Hosting으로 복사해 HTTPS로 연결하고, 기존 raytracing 번들은 APK 런타임 경로 토큰을 복원했다. 자산 키·옵션·의존성 직렬화는 변경하지 않았다. 이번 복구는 리소스 재빌드나 APK 재빌드를 하지 않는다.
- Android APK 경로 모의 해석과 공통 번들 크기·CRC 검증 통과. 배포 검사에 Library·절대 로컬 경로 거부를 추가했고, 정상 RuntimePath 허용을 포함한 테스트 14개가 통과했다. Firebase Hosting 재배포 완료. 기록: `Build/AddressablesPathFix-20260916/`.
- 재배포 후 CDN 파일 438개 HTTP·CORS·캐시 헤더와 manifest 일치 확인. 변경된 카탈로그·해시 및 추가 공통 번들 2개, 합계 4개를 다운로드해 SHA-256 대조를 통과했다. 실제 기기 재실행 검증은 별도다.
- `Build/UiAtlasDeployment-20260916/fix-immutable-catalog.cs.txt`는 오류를 만든 과거 작업 기록이다. 재사용하지 않는다.

## 전투 UI 아틀라스 보완 (2026-09-16 12:37 KST 빌드)

- 전투 덱 배경·턴 타이머 이미지 3개를 `UIBattle`에 추가했다. 19개 아틀라스 / 원본 307개 전체의 Android 바인딩과 중복 없음 확인. `UIBattle`은 ASTC 6×6, 2048×2048 한 페이지를 유지한다. 상세 제외 항목은 `UIAtlases.md`를 따른다.
- 현재 프로젝트가 사용하는 `catalog_2026.09.15.09.20.47`의 콘텐츠 업데이트를 빌드했다. 출시 기준 상태 파일은 `Build/AddressablesState/0.0.0/Android/20260915-092344-669/addressables_content_state.bin`이다. CDN에 별도로 존재하는 `13.16.42`를 포함한 다른 카탈로그는 보존했으며 이번 업데이트 대상으로 바꾸지 않았다.
- 빌드 보고서 `Library/com.unity.addressables/BuildReports/buildlayout_2026.09.16.12.37.15.json`에 BuildError 없음. 기존 자산 키 517개의 타입·내부 경로·프로바이더 보존, 현재 카탈로그 번들 247개의 크기·CRC 로딩 검증 통과.
- 로컬에 없던 기존 배포 파일 71개(122.87 MiB)를 CDN에서 받아 SHA-256을 검증하고 복원했다. 기존 번들 2개는 재빌드 시 파일명은 같지만 CRC와 바이트가 달라졌다. 기존 배포 파일을 복원하고 새 번들에 별도 URL·캐시 해시를 부여했으며, Unity의 `ContentCatalogData.SetData`로 카탈로그를 직렬화한 뒤 모든 키·타입·옵션·의존성을 대조했다. 다음 리소스 빌드도 기존 번들 불변성 검사를 통과해야 한다.
- `bm-cardbattle-assets` Hosting 배포 완료. 카탈로그 10개 / 리소스 파일 436개를 유지하며 신규 번들 13개와 변경 카탈로그·해시의 합계는 34.56 MiB다. 이는 현재 작업 폴더의 기존 UI·폰트 등 변경분도 포함한 리소스 업데이트다.
- 배포 후 436개 파일의 HTTP·CORS·캐시 헤더와 공개 manifest 일치 확인. 신규·변경 파일 15개를 실제 다운로드해 크기·SHA-256을 모두 대조했다.
- `SpecData.bytes` 변경 없음. APK 빌드는 실행하지 않았다. 빌드·감사·카탈로그 보정·배포 대조 기록은 `Build/UiAtlasDeployment-20260916/`에 보관한다.

## 매칭 VS 왼쪽 바 복구 (2026-09-15 19:26 KST 빌드)

- `MatchmakingRoot.prefab`의 왼쪽 `Title_Line03_Divider`만 비활성화되어 있었다. 연출은 위치만 움직이고 활성 상태를 바꾸지 않아 매칭 성사 후에도 나타나지 않았다. 해당 오브젝트의 `m_IsActive` 한 값만 1로 수정했다.
- Android 콘텐츠 업데이트 결과 `SyncUiPrefabCatalog` 번들 하나가 변경됐다. 이 번들에는 매칭 프리팹과 공용 UI가 함께 들어 있어, 직전 배포 기준 카탈로그 포함 다운로드는 10.80 MiB다. 다른 UI 번들은 유지된다.
- 빌드 보고서 `Library/com.unity.addressables/BuildReports/buildlayout_2026.09.15.19.26.15.json`의 BuildError 없음. 빌드된 프리팹에서 좌우 바의 활성·표시·스프라이트 상태, 의존 번들의 CRC 로딩, 기존 카탈로그 자산 주소 보존을 확인했다. `SpecData.bytes` 변경 없음.
- 작업 기록과 배포 검증은 `Build/VsDivider-20260915/`에 보관한다. 업데이트 대상은 기존 `catalog_2026.09.15.09.20.47`이며 APK 재빌드는 필요하지 않다.
- Hosting 배포 완료. 전체 352개 파일의 HTTP·CORS·캐시 헤더와 변경 파일 3개의 다운로드 SHA-256 대조를 통과했다. 검증 PC의 Node 기본 연결은 시간 초과가 발생했으나 `--dns-result-order=ipv4first`로 전체 검증을 완료했다.

## 미션 닫기 버튼 패치 (2026-09-15 18:51 KST 빌드)

- `MissionOverlay.prefab` 닫기 버튼의 슬라이스 배율을 0.01 → 1, 크기를 300×110 → 280×96, 글자 크기를 45 → 40으로 조정했다. 기존 이미지·닫기 버튼 배선·입력 처리는 유지한다.
- 변경 전후 Unity UI 렌더링과 Android 번들의 버튼 배선·크기·슬라이스·입력 설정을 확인했다. 비교 이미지: `Build/MissionClose-20260915/button-before-after.png`.
- 렌더링 과정에서 동적 `Jalnan2 SDF 1`에 닫기 글자 등의 글리프·아틀라스 캐시가 생성되어 해당 공유 폰트 번들도 갱신됐다. 글꼴 종류·스타일은 변경하지 않았다.
- 기존 `catalog_2026.09.15.09.20.47`의 콘텐츠 업데이트로 배포했다. 신규 번들은 미션 UI 18.52 KiB와 공유 폰트 82.79 KiB, 합계 약 101 KiB다. 카탈로그 포함 전송량은 약 532 KiB이며, 직전 폰트 분리 패치를 받은 앱에는 다른 UI 번들을 다시 요구하지 않는다.
- 보고서: `Library/com.unity.addressables/BuildReports/buildlayout_2026.09.15.18.51.27.json`. 기존 자산 주소·타입·내부 경로 보존과 의존 번들 10개의 CRC 로딩 검증을 통과했다. APK 재빌드는 필요하지 않다. 기록은 `Build/MissionClose-20260915/`에 보관한다.
- 배포 후 전체 351개 파일의 HTTP·CORS·캐시 헤더와 신규 번들·변경 카탈로그 4개의 실제 다운로드 SHA-256 검증을 통과했다. `SpecData.bytes` 변경 없음.

## 공용 폰트 분리 패치 (2026-09-15 18:41 KST 빌드)

- `RemoteFonts` 그룹을 `Pack Separately`로 추가했다. 중복되던 TMP 폰트 8개, 동적 원본 글꼴 3개, 공유 재질 2개, 외부 폰트 아틀라스 1개를 `SharedFonts/` 주소와 `RemoteUI` 라벨로 등록했다. 기존 `RemoteUI` 69개 번들의 개별 분리와 다운로드 코드는 유지한다.
- 폰트 파일·재질 값·시작 씬의 직접 참조는 변경하지 않았다. 다운로드 전 LoadingCover는 APK 내 폰트를 계속 사용한다. 실제 `Resources/Fonts & Materials`의 LiberationSans와 fallback은 이번 분리 대상에서 제외했다.
- 원격 번들 합계가 215.41 → 167.53 MiB로 47.88 MiB 감소했다. RemoteUI 117.03 → 63.18 MiB, RemoteScenes 25.00 → 19.82 MiB, 신규 RemoteFonts 11.15 MiB다. 분리한 14개 자산의 번들 내 포함 횟수는 각각 1회다. 전체 중복 자산은 134개가 남으며, 이번 검증은 모든 의존성의 중복 제거를 뜻하지 않는다.
- 설치된 앱의 `catalog_2026.09.15.09.20.47`를 `ContentUpdateScript.BuildContentUpdate`로 갱신했다. 기준 상태 파일은 `Build/AddressablesState/0.0.0/Android/20260915-092344-669/addressables_content_state.bin`이다. 새 카탈로그 이름을 만드는 전체 빌드는 실행하지 않았다.
- 기존 카탈로그의 자산 주소·타입·내부 경로 보존을 검증했다. 의존성 포함 번들 15개를 CRC 검사로 로딩하고 TMP 폰트 8개 및 동적 폰트 5개의 한글·숫자 추가를 확인했다. 폰트 원본 SHA-256은 변경 없음. 실기기 화면 검증은 별도다.
- 구조 전환으로 새 번들 49개가 필요하다. 직전 배포를 캐시한 앱의 추가 다운로드는 번들 약 52.1 MiB(카탈로그 포함 52.5 MiB)다. 전환 자체에는 이 다운로드가 한 번 필요하며, 이후 공유 폰트의 중복 포함으로 인한 다운로드 증가를 줄인다.
- Hosting 배포 완료. 기존 300개 파일을 보존했고, 전체 349개 파일의 HTTP·CORS·캐시 헤더와 새 번들·변경 카탈로그 51개의 다운로드 SHA-256을 검증했다. `SpecData.bytes` SHA-256도 변경 없음. 현재 앱은 재빌드 없이 재실행하여 패치를 받을 수 있다.
- 보고서: `Library/com.unity.addressables/BuildReports/buildlayout_2026.09.15.18.41.36.json`. 백업·빌드 결과·로딩 검증·배포 대조 기록은 `Build/SharedFonts-20260915/`에 보관한다. 다음 콘텐츠 업데이트도 위 출시 기준 상태 파일을 사용한다.

## UI 아틀라스 보완 후 배포 (2026-09-15 18:01 KST 빌드)

- Android / 앱 버전 `0.0.0` 리소스 전체 빌드 및 `bm-cardbattle-assets` Hosting 업로드 완료. UI 이미지 115개를 추가한 아틀라스 19개와 새 필터 UI를 포함한다. 아틀라스 세부 검수는 `UIAtlases.md`를 따른다.
- 빌드 보고서: `Library/com.unity.addressables/BuildReports/buildlayout_2026.09.15.18.01.16.json`. `BuildError` 없음, 기존 C#·셰이더 경고는 남아 있다. RemoteUI는 현재 설정대로 `Pack Separately`, 69개 번들 / 135.49 MiB다.
- 전체 원격 번들 합계 235.80 MiB. 보고서의 번들 간 중복 자산은 147개이며 공용 폰트·재질 등을 포함한다. 아틀라스 원본 GUID 중복 0개와는 별도 지표다. 이번 작업에서 공용 의존성 분리나 그룹 정책은 변경하지 않았다.
- 기존 Hosting 파일을 모두 보존했다. 신규 파일 74개 / 162.81 MiB가 추가됐고 기존 이름 파일의 변경은 없다. 공개 목록은 카탈로그 8개를 포함해 리소스 260개다.
- 배포 후 260개 파일의 HTTP·CORS·캐시 헤더를 확인했다. 신규 74개는 CDN에서 모두 다운로드해 SHA-256·크기를 대조했고, 공개 manifest 전체도 로컬과 일치했다.
- 기록: `Build/UiAtlasDeployment-20260915/cdn-verification.json`, `deployment-diff.json`, `build-summary.json`, `metadata-validation.json`. 소스 메타와 기존 아틀라스 백업도 같은 폴더에 있다.
- `SpecData.bytes` SHA-256은 작업 전후 `FF94F57DE95767C4F486CA304E4AEF2960C7DF60B536DC9AE6D496146BA19204`로 동일하다. Functions·Firestore·앱 빌드는 실행하지 않았다. 현재 전체 빌드와 맞는 새 앱을 만들 때는 아래 절차대로 Addressables를 다시 생성하지 않는다.

## 배포 설정과 절차

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
- **현재 지원하는 앱의 버전·플랫폼·번들을 유지한다.** 개발 단계 미사용 파일은 위 정리 절차에 따라 제외할 수 있다. Firebase Hosting은 폴더 전체를 새 릴리스로 올리므로
  이전 파일을 지우면 아직 그 파일을 쓰는 앱이 다운로드에 실패한다.
- 새 작업 PC/CI에서는 이전 `ServerData` 아티팩트를 먼저 복원한다. predeploy 검사는 기존 공개 목록과
  대조해 파일 누락·같은 이름의 번들 덮어쓰기를 막는다. `--offline`은 로컬 검사 전용이다.
- `SpecData.bytes`와 콘텐츠 업데이트 상태 파일은 Hosting에 업로드하지 않는다.

## 다운로드 동작

`CardArt`·`RemoteUI`·`RemoteFonts`는 `Pack Separately`로 빌드한다. 카드·팩 이미지는 등록 항목별,
UI는 등록된 프리팹·아틀라스·설정 자산별 번들을 만들고, 공용 폰트 의존성은 `RemoteFonts`에서 공유한다. 캐시가 있으면 콘텐츠 업데이트에서
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

## 최신 Android 카탈로그 404 복구 (2026-09-15)

- 앱이 요청한 `catalog_2026.09.15.09.20.47.hash`는 로컬 리소스 빌드에는 있었지만 Hosting에 업로드되지 않아 404가 발생했다.
- 성공한 `buildlayout_2026.09.15.18.20.47.json`의 산출물을 Hosting에 배포했다. 기존 260개 파일을 보존하고 카탈로그·번들 40개를 추가했다.
- 요청된 해시는 HTTP 200과 로컬 내용 일치를 확인했다. 전체 300개 파일의 HTTP·CORS·캐시 헤더, 새 파일 40개의 실제 다운로드 SHA-256, 공개 manifest 일치 검증을 통과했다.
- 검증 기록: `Build/ProfileMaskFix-20260915/cdn-verification.json`. `SpecData.bytes` 변경 없음. 이 404 복구에 APK 재빌드는 필요하지 않으며 앱 재실행으로 재확인한다. 실기기 재실행 확인은 미실행이다.

## 확인 명령

```powershell
node scripts/validate-resource-hosting.cjs --offline
firebase.cmd hosting:sites:list --project bm-cardbattle
firebase.cmd deploy --only hosting --project bm-cardbattle
```

공식 문서: https://firebase.google.com/docs/hosting/full-config
