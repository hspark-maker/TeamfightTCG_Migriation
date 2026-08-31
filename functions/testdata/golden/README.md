# Battle golden corpus

Unity의 실제 멀티 전투 규칙 실행에서 생성된 재생 입력과 체크포인트를 보관한다.

- 에디터 메뉴 `Tools/Card Battle/Golden/Enable Capture`로 캡처를 켠다.
- 또는 Unity 실행 환경에 `BATTLE_GOLDEN_CAPTURE=1`을 설정한다.
- 출력 위치는 기본적으로 이 디렉터리이며 `BATTLE_GOLDEN_CAPTURE_DIR`로 바꿀 수 있다.
- `BattleGoldenReplayHarness.Run`을 `-executeMethod`로 호출하면 전투 씬을 열지 않고 코퍼스를 재생한다.
- 배치 하네스는 유효 골든 12개 미만이면 실패한다.
- 튜토리얼과 AI 인수 경기는 JSON에는 제외 사유를 남기되 재생 대상에서는 제외한다.

필수 범위: 9종 시너지, 처형 재공격, 교활 스왑, 무쌍 스플래시, 언데드 부활,
낙인 선피해, 반격사, 멀리건 교체와 스킵.
