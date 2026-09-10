# Battle entry save wait tests

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/BattleEntrySaveTests/run.ps1
```

`PlayerSaveCloud.FlushForBattleEntryAsync`의 실제 소스를 메서드 시작 및 다음 `SuspendUploadsAsync` 경계로 추출해 컴파일한다. 대기 알고리즘 사본은 저장하지 않는다. 메서드가 이동하거나 형태가 바뀌면 추출 단계가 실패한다.

10개 동작 테스트가 정상 저장, 업로드 봉인, 기존 업로드와 추가 변경분, 전송 실패, 세션 상태, 취소, 세션 교체를 검증한다. 호출부의 전역 flush 제거, 반환값 검사, 기존 15초 상한은 소스 검사로 확인한다.

한계: 업로드 전송·직렬화·서버 명령은 제어 가능한 모형이다. 전체 `PlayerSaveCloud`나 `DeckLockSubmission`을 실행하는 통합 테스트가 아니며, 실제 Firestore 왕복·보상 표시·Unity PlayerLoop 동작은 검증하지 않는다. 마지막 단계는 추출한 실제 메서드를 설치된 Unity·UniTask 어셈블리로 컴파일해 API 호환성을 확인한다.

Unity를 실행하지 않으며 Firebase 호출, 프로젝트 에셋·SpecData 변경은 없다. 컴파일 산출물은 고유한 시스템 임시 폴더에만 기록한다.
