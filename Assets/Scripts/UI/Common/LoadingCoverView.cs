using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 앱 기동 화면 = 시작 화면 겸 로딩 화면(씬에 활성 상태로 저작 — 런타임 생성 아님).
//
// LobbyScene이 빌드 0번 씬이라 이 커버가 앱의 첫 프레임부터 화면 전체를 덮는다.
// 부트 로딩(DataLibrary의 Addressables UI 프리팹 로드)이 끝나고 최소 노출 시간이 지날 때까지 유지되며,
// 그 사이 슬라이더가 진행도를 그린다. 슬라이더 목표치는 "실제 진행도"와 "최소 시간 진척" 중 느린 쪽이라
// 로딩이 즉시 끝나도 바가 순간이동하지 않는다.
//
// 부트 직후 씬을 떠나는 흐름(튜토리얼 AutoBattle)에서는 씬 언로드와 함께 이 오브젝트가 파괴되므로
// 커버가 켜진 채로 다음 씬에 넘어간다 — 전환 여부를 판정하는 분기가 없는 이유.
public class LoadingCoverView : MonoBehaviour
{
    [Tooltip("페이드 대상. 커버 캔버스 루트의 CanvasGroup.")]
    [SerializeField] CanvasGroup canvasGroup;

    [Tooltip("진행도 슬라이더. 미배선이면 표시 없이 대기만 한다(min/max 무관하게 정규값으로 쓴다).")]
    [SerializeField] Slider progressBar;

    [Tooltip("로딩이 즉시 끝나도 이 시간(초)만큼은 노출한다 — 한 프레임 깜빡임 방지.")]
    [SerializeField] float minDuration = 1f;

    [Tooltip("슬라이더가 목표치를 따라가는 속도(비율/초).")]
    [SerializeField] float barFollowSpeed = 2f;

    [Tooltip("로딩이 끝나지 않아도 이 시간(초)이 지나면 커버를 걷는다 — 무한 대기 방지.")]
    [SerializeField] float maxDuration = 15f;

    [Tooltip("페이드아웃 시간(초). 0이면 즉시 해제.")]
    [SerializeField] float fadeDuration = 0.3f;

    Tween m_fade;

    void Awake()
    {
        if (canvasGroup == null)
        {
            Debug.LogWarning("[LoadingCoverView] canvasGroup 미배선 — 커버를 걷을 수 없어 비활성화합니다.");
            gameObject.SetActive(false);
            return;
        }

        // 씬 저작값이 어긋나 있어도 첫 프레임은 반드시 덮고 입력을 막는다.
        canvasGroup.alpha          = 1f;
        canvasGroup.blocksRaycasts = true;

        if (progressBar != null) progressBar.normalizedValue = 0f;
    }

    void Start()
    {
        StartCoroutine(CoDismiss());
    }

    void OnDestroy()
    {
        // 씬 전환으로 파괴되는 것이 정상 경로라 트윈이 살아 있는 채 죽는다.
        m_fade?.Kill();
        m_fade = null;
    }

    IEnumerator CoDismiss()
    {
        float t_elapsed = 0f;
        float t_shown   = 0f;   // 실제로 슬라이더에 그려지는 값(보간 결과)

        while (true)
        {
            t_elapsed += Time.unscaledDeltaTime;

            // 로딩 완료와 최소 노출 중 느린 쪽이 목표 — 둘 다 충족돼야 100%에 도달한다.
            float t_target = Mathf.Min(DataLibrary.LoadProgress, t_elapsed / Mathf.Max(minDuration, 0.01f));
            t_shown = Mathf.MoveTowards(t_shown, t_target, barFollowSpeed * Time.unscaledDeltaTime);

            if (progressBar != null) progressBar.normalizedValue = t_shown;

            if (t_shown >= 1f) break;

            if (t_elapsed >= maxDuration)
            {
                Debug.LogWarning($"[LoadingCoverView] 로딩이 {maxDuration}초 안에 끝나지 않아 커버를 강제로 걷습니다.");
                break;
            }

            yield return null;
        }

        if (progressBar != null) progressBar.normalizedValue = 1f;

        if (fadeDuration <= 0f)
        {
            Hide();
            yield break;
        }

        // 로딩 중 timeScale이 0인 화면에서도 걷히도록 unscaled.
        canvasGroup.blocksRaycasts = false;   // 페이드 시작 = 조작 허용
        m_fade = canvasGroup.DOFade(0f, fadeDuration).SetUpdate(true).OnComplete(Hide);
    }

    void Hide()
    {
        m_fade = null;
        canvasGroup.blocksRaycasts = false;
        gameObject.SetActive(false);   // Overlay 캔버스 드로우콜 제거
    }
}
