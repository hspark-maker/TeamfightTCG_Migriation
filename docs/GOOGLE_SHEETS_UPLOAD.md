# 로컬 CSV → SpecData Google Sheet

진입: **Tools → Card Battle → 릴리즈 관리 → Google Sheet**.

## 최초 연결

1. Google Cloud에서 Google Sheets API를 사용 설정한다.
2. OAuth 동의 화면을 설정하고 **데스크톱 앱** 유형 OAuth 클라이언트를 만든다. 테스트 상태인 앱은 사용할 Google 계정을 테스트 사용자에 등록한다.
3. 릴리즈 관리의 **로그인 → 구글 OAuth 클라이언트 설정**에 클라이언트 ID·보안 비밀을 입력한다. 기존 설정이 있다면 재사용한다.
4. **Google Sheet → Google Sheets 연결**에서 대상 문서의 편집 권한이 있는 계정으로 로그인하고 시트 권한을 허용한다. Firebase 관리자 로그인과는 별도다.

OAuth 설정은 EditorPrefs, Sheets 액세스 토큰은 에디터 세션에만 저장한다. 연결이 만료되면 다시 연결한다. 키·토큰을 CSV나 저장소에 넣지 않는다.

참조: [Google 데스크톱 앱 OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Sheets API 권한](https://developers.google.com/workspace/sheets/api/scopes).

## 업로드

1. 기본 문서 ID는 기존 `SpecDataAsset.asset`의 `SheetId`에서 읽는다. 다른 문서는 URL 또는 ID를 입력한다.
2. `docs/SpecData/*_sheet.csv` 중 올릴 파일을 선택한다. `Reward_sheet.csv`는 `Reward` 탭에 대응한다. 처음에는 모두 선택 해제되어 있다.
3. **변경 내용 미리보기**에서 문서명·표·행 수·변경/비워질 셀·수식 충돌을 확인한다. 설명·필드·타입 헤더와 메모 열도 CSV 내용에 포함된다.
4. **미리본 변경 업로드**를 누르고 대상 문서와 파일을 확인한다.
5. 완료 메시지의 원격 재조회 검증 결과를 확인한다.

선택한 CSV를 A1부터 배치하고 기존 값 중 CSV 범위 밖은 비운다. 셀 서식·메모·선택하지 않은 탭은 보존한다. 없는 탭은 새로 만든다. 원격 수식은 결과가 CSV와 같으면 그대로 보존하며, 다르면 그 업로드를 차단한다. 원격 수식 검토 또는 해당 CSV 선택 해제 후 다시 미리 본다. CSV의 `=`로 시작하는 문자열은 수식으로 실행하지 않는다.

CSV 또는 원격 값이 미리보기 후 바뀌면 업로드를 중단한다. 여러 선택 탭은 하나의 [batchUpdate](https://developers.google.com/workspace/sheets/api/guides/batch)로 반영한다. 다만 최종 조회와 쓰기 사이의 다른 편집까지 잠그는 기능은 아니므로 업로드 중 동시 편집을 피한다. 통신 중단·취소 시 이미 반영됐을 수 있어, 무조건 재시도하지 않고 미리보기로 상태를 다시 확인한다.

이 기능은 **Google Sheet만 갱신**한다. `SpecData.bytes`, 생성 C#, Firestore 발행, 빌드·배포는 실행하지 않는다.

## 로컬 검증

**Tools → Card Battle → 검증 → Google Sheet 업로드 계획**은 네트워크나 로그인 없이 CSV 파싱, 숫자 범위, 수식 보존, 셀 축소·신규 탭 요청을 검사한다. 실제 Google 계정 권한·원격 API 통신의 통합 검증을 대신하지 않는다.
