using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// 카드팩 개봉 연출 뷰. 흐름: 3D 팩 표시 → 클릭 1회 → 팩 숨김 → 결과 패널 fade in →
// fade 완료 후 뽑힌 카드 타일 생성 → OnRevealComplete(컨트롤러가 획득 버튼 노출).
// 진입은 컨트롤러가 넘기는 OpenedPack(BeginOpen)뿐 — 구매·소유·덱은 이 뷰 밖의 책임.
public class PackRevealView : MonoBehaviour
{
    // 카드 배치 완료 시 1회 발화.
    public event Action OnRevealComplete;

    [Header("3D 팩")]
    [SerializeField] PackClickHandle packHandle;   // 씬의 3D 팩(클릭 인터랙션)
    [SerializeField] GameObject packRoot;          // 팩 모델 루트. 미배선이면 packHandle의 오브젝트를 쓴다.

    [Header("결과 패널")]
    [SerializeField] CanvasGroup revealPanel;      // 결과 패널(시작 alpha 0 / 입력 off)
    [SerializeField] Transform cardGrid;           // GridLayoutGroup(3열) 컨테이너
    [SerializeField] CollectionCardView cardPrefab;// 카드 타일 프리팹(Card.prefab)
    [SerializeField] float panelFadeDuration = 0.35f;

    // Idle → PackShown(클릭 대기) → Revealing(fade+배치) → Done.
    enum EViewState { Idle, PackShown, Revealing, Done }

    EViewState m_state = EViewState.Idle;

    // 이번 개봉 세션 결과(클릭 시 이 카드로 타일 생성).
    OpenedPack m_pending;

    // 이번 개봉으로 생성된 카드 타일들(정리용).
    readonly List<CollectionCardView> m_spawned = new List<CollectionCardView>();

    /// <summary>개봉 세션 시작: 3D 팩을 보이고 클릭 대기.</summary>
    public void BeginOpen(OpenedPack _opened)
    {
        if (m_state != EViewState.Idle) return;   // 재진입 = 중복 개봉 방지
        if (_opened == null || !_opened.Success)
        {
            Debug.LogWarning("[PackRevealView] BeginOpen에 유효하지 않은 OpenedPack — 개봉 취소.");
            return;
        }

        m_pending = _opened;
        ClearSpawned();
        ResetPanel();

        m_state = EViewState.PackShown;

        var t_root = ResolvePackRoot();
        if (t_root != null) t_root.SetActive(true);

        if (packHandle == null)
        {
            // 클릭 인터랙션 미배선: 바로 다음 단계로(소프트락 방지).
            Debug.LogWarning("[PackRevealView] packHandle 미배선 → 클릭 생략하고 바로 공개 진행.");
            OnPackClicked();
            return;
        }

        packHandle.Arm(OnPackClicked);
    }

    // 패널 초기화: 완전 투명 + 입력 차단(fade 완료까지 획득 버튼도 못 눌리게).
    void ResetPanel()
    {
        if (revealPanel == null) return;

        revealPanel.DOKill();
        revealPanel.alpha = 0f;
        revealPanel.blocksRaycasts = false;
        revealPanel.interactable = false;
    }

    // 팩 클릭 확정: 팩 숨김 → 패널 fade in → 완료 시 카드 배치.
    void OnPackClicked()
    {
        if (m_state != EViewState.PackShown) return;   // 중복 클릭/오작동 방어
        m_state = EViewState.Revealing;

        var t_root = ResolvePackRoot();
        if (t_root != null) t_root.SetActive(false);

        if (revealPanel == null)
        {
            Debug.LogWarning("[PackRevealView] revealPanel 미배선 → fade 생략하고 카드 배치.");
            SpawnCards();
            return;
        }

        revealPanel.DOKill();
        revealPanel.DOFade(1f, panelFadeDuration)
            .SetLink(revealPanel.gameObject)   // 패널이 파괴되면 트윈도 함께 죽는다.
            .OnComplete(() =>
            {
                if (revealPanel == null) return;
                revealPanel.blocksRaycasts = true;
                revealPanel.interactable = true;
                SpawnCards();
            });
    }

    // 뽑힌 카드를 그리드에 생성. 3열 배치는 GridLayoutGroup 담당(좌표 계산 없음).
    void SpawnCards()
    {
        if (m_state != EViewState.Revealing) return;

        var t_cards = m_pending != null ? m_pending.Cards : null;
        int t_count = t_cards != null ? t_cards.Count : 0;

        if (cardPrefab == null || cardGrid == null)
            Debug.LogWarning("[PackRevealView] cardPrefab/cardGrid 미배선 → 카드 표시 생략.");
        else if (t_count == 0)
            Debug.LogWarning("[PackRevealView] 개봉 성공했으나 표시할 카드가 없음.");
        else
        {
            for (int t_i = 0; t_i < t_count; t_i++)
            {
                var t_drawn = t_cards[t_i];
                if (t_drawn.Card == null) continue;

                var t_view = Instantiate(cardPrefab, cardGrid);
                t_view.Bind(t_drawn.Card, true);   // 개봉 카드는 항상 소유
                m_spawned.Add(t_view);
            }
        }

        // 미배선/0장이어도 발화 — 획득 버튼 대기 데드락 방지.
        m_state = EViewState.Done;
        OnRevealComplete?.Invoke();
    }

    GameObject ResolvePackRoot()
        => packRoot != null ? packRoot : (packHandle != null ? packHandle.gameObject : null);

    // 이전 개봉 타일 정리.
    void ClearSpawned()
    {
        for (int t_i = 0; t_i < m_spawned.Count; t_i++)
            if (m_spawned[t_i] != null) Destroy(m_spawned[t_i].gameObject);
        m_spawned.Clear();
    }

    // 연출 중 비활성 시 좀비 트윈 정리 + 상태 리셋(타일은 다음 BeginOpen의 ClearSpawned가 정리).
    // 클릭 입력도 함께 내려야 재활성 후 "armed지만 Idle" 상태로 클릭이 먹히는 교착을 막는다.
    void OnDisable()
    {
        if (revealPanel != null) revealPanel.DOKill();
        if (packHandle != null) packHandle.Disarm();

        m_state = EViewState.Idle;
    }
}
