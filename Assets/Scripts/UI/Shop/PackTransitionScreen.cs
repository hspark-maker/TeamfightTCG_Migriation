using System;
using System.Collections.Generic;
using DG.Tweening;
using TransitionScreenPackage;
using UnityEngine;
using UnityEngine.UI;

// 모양 있는 화면 전환. 목적은 ScreenFlash와 같다 — 그 밑에서 화면을 갈아치우는 것이다.
// 다른 점은 덮는 것이 단색 판이 아니라 팩마다 다른 모양·색·질감이라는 것뿐이다.
//
// 전환 패키지 프리팹의 구조가 이 기능과 맞아떨어진다: 모양은 Mask가 스프라이트를 갈아끼워 만들고,
// 실제로 보이는 색·질감은 그 자식 그래픽 하나가 담당한다. 그래서 색·머티리얼은 자식만 손대면 된다.
//
// ⚠ 독립 루트 캔버스로 선다(ScreenFlash와 같은 이유 — 중첩 캔버스는 sortingOrder를 올려도
//   부모 루트가 그려지는 자리 안에서만 정렬된다). 패키지 프리팹은 자기 Canvas·CanvasScaler를 들고 오는데
//   그 sortingOrder가 0이라 개봉 오버레이(100) 아래로 깔린다 — 인스턴스에서 걷어내고 이 레이어의 정렬을 쓴다.
// ⚠ 마스크 모양은 1920x1080 가로로 저작됐다. 스트레치로 붙이면 세로 화면(1440x3120)에서 2배 넘게 늘어나
//   원이 길쭉한 타원이 되고 대각 와이프의 각도가 바뀐다. 화면 긴 변 기준 정사각으로 잡아 비율을 지키고 넘치게 덮는다.
// ⚠ GraphicRaycaster를 붙이지 않고 그래픽의 raycastTarget도 끈다 — 덮여 있는 동안 터치를 먹으면
//   도착 화면이 첫 입력을 잃는다(ScreenFlash와 같은 규칙).
public class PackTransitionScreen : MonoBehaviour
{
    // 자가 설치 노드 이름. 씬에 미리 둘 수도 있지만 없으면 런타임에 세운다(프리팹 편집 없이).
    const string NODE_NAME = "PackTransitionLayer";

    // ScreenFlash(32000) 바로 위. 둘이 같이 뜨는 일은 없지만(택일) 순서는 정해 둔다.
    const int SORTING_ORDER = 32001;

    // 덮임 예비 통지 시각(원본 재생 기준, 초). 패키지 최장 reveal이 1.33초라 그보다 뒤에 둔다 —
    // 정상 상황에선 애니메이션 이벤트가 항상 먼저 오고, 이 값은 이벤트가 영영 오지 않는 경우만 구한다.
    const float COVER_FALLBACK = 1.6f;

    // 시퀀스 길이 못(원본 기준). 최장 reveal+hide가 2.6초다 — 이보다 짧으면 걷히는 도중에 인스턴스가 파괴된다.
    const float LIFE_FALLBACK = 3f;

    // 빛이 모양을 훑는 축. AllIn1의 샤인은 위치를 스스로 굴리지 않아 누군가 밀어줘야 움직인다 —
    // 단색 채움 위에서 눈에 보이는 효과가 사실상 이것뿐이라 전환의 질감은 대부분 여기서 나온다.
    const string SHINE_PROPERTY = "_ShineLocation";

    // 한 번 훑는 데 걸리는 시간(원본 기준). 덮이는 구간(최장 1.33초) 안에 지나가야 걷힌 뒤 잔광이 남지 않는다.
    const float SHINE_SWEEP = 1.4f;

    // 정사각 덮개를 화면보다 조금 크게 잡는 배수. 층마다 저작된 물림·오프셋이 몇 픽셀씩 어긋나므로
    // 딱 맞게 잡으면 그 오차가 화면 가장자리의 실금으로 드러난다. 2%는 눈에 띄지 않는다.
    const float COVER_OVERSCALE = 1.02f;

    static PackTransitionScreen s_instance;

    /// <summary>
    /// 전환 레이어를 얻는다. 씬에 없으면 자가 설치한다(프리팹·씬 편집 없이).
    /// 씬이 바뀌면 함께 파괴되고 다음 호출이 다시 세운다 — 화면을 덮는 물건이 씬을 넘어 살아남지 않게.
    /// </summary>
    public static bool TryGet(out PackTransitionScreen _screen)
    {
        if (s_instance == null) s_instance = FindFirstObjectByType<PackTransitionScreen>(FindObjectsInactive.Include);
        if (s_instance == null) s_instance = Install();

        _screen = s_instance;
        return _screen != null;
    }

    /// <summary>
    /// 덮었다 걷히는 전환을 조립해 돌려준다(재생은 호출자 시퀀스에 맡긴다 — 중단이 함께 걷히도록).
    /// _onCover는 화면이 완전히 덮인 순간 1회 불린다 — 개봉 화면은 반드시 그때 열어야 전환 프레임이 드러나지 않는다.
    /// 저작이 비었거나 프리팹 구성이 예상과 다르면 null — 호출부는 예전 흰 플래시로 돌아가면 된다.
    /// </summary>
    public Sequence BuildCover(PackOpenTransition _style, Action _onCover)
    {
        if (_style == null || !_style.IsAuthored) return null;

        var t_go = Instantiate(_style.screenPrefab, transform);
        var t_manager = t_go.GetComponent<TransitionScreenManager>();
        var t_animator = t_go.GetComponent<Animator>();

        // 애니메이터가 모양을 만들고 매니저가 다 덮였음을 알린다 — 둘 중 하나라도 없으면 이 축은 성립하지 않는다.
        if (t_manager == null || t_animator == null)
        {
            Destroy(t_go);
            return null;
        }

        StripNestedCanvas(t_go);

        float t_speed = Mathf.Max(0.01f, _style.speed);
        t_animator.speed = t_speed;

        var t_fillMaterial = Dress(t_go, _style);

        // 덮임 통지는 정확히 1회. 애니메이션 이벤트·예비 시각·중단 셋 중 먼저 온 것이 이긴다 —
        // 이벤트만 믿으면 애니메이터가 꺼지는 순간 개봉 화면이 영영 열리지 않는다(카드는 이미 지급됐다).
        bool t_covered = false;
        void Cover()
        {
            if (t_covered) return;
            t_covered = true;

            _onCover?.Invoke();
            if (t_manager != null) t_manager.Hide();   // 덮은 뒤에야 걷는다 — 순서가 뒤집히면 교체 프레임이 드러난다.
        }

        t_manager.FinishedRevealEvent += Cover;

        var t_seq = DOTween.Sequence().SetLink(gameObject);

        // Reveal은 시퀀스 안에서 쏜다 — 만든 프레임에 바로 쏘면 아직 갱신되지 않은 애니메이터가 트리거를 흘린다.
        t_seq.InsertCallback(0f, () => { if (t_manager != null) t_manager.Reveal(); });
        t_seq.InsertCallback(COVER_FALLBACK / t_speed, Cover);
        t_seq.InsertCallback(LIFE_FALLBACK / t_speed, () => { });   // 길이 못(위 주석).

        StageShine(t_seq, t_fillMaterial, t_speed);

        t_seq.OnKill(() =>
        {
            if (t_manager != null) t_manager.FinishedRevealEvent -= Cover;

            Cover();                                  // 중단이어도 개봉 화면은 반드시 연다.

            // 잔해를 남기지 않는다(ScreenFlash.StageBurst와 같은 정리 규칙). 머티리얼은 사본이라 함께 걷는다.
            if (t_go != null) Destroy(t_go);
            if (t_fillMaterial != null) Destroy(t_fillMaterial);
        });

        return t_seq;
    }

    // 팩의 색·질감을 입히고 비율·입력을 보정한다. 만든 머티리얼 사본을 돌려준다(없으면 null) —
    // 호출부가 그 값을 굴리고 끝나면 걷는다.
    Material Dress(GameObject _instance, PackOpenTransition _style)
    {
        // 덮여 있는 동안 아무것도 터치를 먹지 않는다 — 도착 화면이 첫 입력을 잃는다(ScreenFlash와 같은 규칙).
        var t_all = _instance.GetComponentsInChildren<Image>(true);
        for (int t_i = 0; t_i < t_all.Length; t_i++) t_all[t_i].raycastTarget = false;

        var t_mask = _instance.GetComponentInChildren<Mask>(true);
        if (t_mask == null) return null;

        SquareUpLayers(_instance.transform, t_mask.transform);

        var t_maskImage = t_mask.GetComponent<Image>();

        // 에셋을 그대로 물리지 않고 사본을 만들어 쓴다 — 훑는 값을 굴리면 에셋에 눌러붙어
        // 다음 개봉이 '이미 훑고 지나간' 상태에서 시작한다(에디터에서는 그 오염이 파일로 저장된다).
        Material t_material = _style.fillMaterial != null ? new Material(_style.fillMaterial) : null;

        // 팩 색을 입히는 대상은 마스크의 자식 그래픽뿐이다. 마스크 자신은 스텐실만 쓰므로(showMaskGraphic 꺼짐)
        // 여기서 색을 칠하면 아무 데도 나타나지 않고 모양 판정만 망친다.
        // Outline 층은 마스크의 부모라 이 순회에 걸리지 않는다 — 그게 맞다. 패키지가 흰 테두리로 저작한 그림이고
        // 팩 색으로 덮으면 테두리가 사라진다.
        var t_graphics = t_mask.GetComponentsInChildren<Image>(true);
        for (int t_i = 0; t_i < t_graphics.Length; t_i++)
        {
            var t_fill = t_graphics[t_i];
            if (t_fill == t_maskImage) continue;

            t_fill.sprite = _style.fillSprite;   // 비면 단색 판이 된다(패키지가 들고 오는 장식 배경을 대체).
            t_fill.color = _style.fillColor;

            if (t_material != null) t_fill.material = t_material;
        }

        return t_material;
    }

    // 빛이 모양을 훑고 지나간다. 머티리얼에 SHINE_ON이 구워져 있지 않으면 이 값은 셰이더가 무시하므로
    // 따로 가리지 않는다 — 굴리는 비용이 float 하나다.
    void StageShine(Sequence _seq, Material _material, float _speed)
    {
        if (_material == null || !_material.HasProperty(SHINE_PROPERTY)) return;

        _material.SetFloat(SHINE_PROPERTY, 0f);

        _seq.Insert(0f, DOTween.To(() => _material.GetFloat(SHINE_PROPERTY),
                                   _v => _material.SetFloat(SHINE_PROPERTY, _v),
                                   1f, SHINE_SWEEP / _speed).SetEase(Ease.InOutSine));
    }

    // 루트에서 마스크까지의 층들을 화면 긴 변 기준 정사각으로 못 박는다.
    // 마스크 모양은 1920x1080 가로로 저작돼 있어 스트레치로 두면 세로 화면에서 2배 넘게 늘어난다.
    //
    // ⚠ 층이 하나가 아니다. Outlined 프리팹은 Outline > Mask > Background로 겹쳐 있고, 두 층이 부모 대비
    //   안팎으로 백여 픽셀씩 물려 있다(테두리 그림과 모양 그림을 맞추려고 저작된 값이다).
    //   스트레치 앵커에서 크기는 '부모 크기 + sizeDelta'이므로, 위층만 정사각으로 바꾸고 아래층을 그대로 두면
    //   아래층 크기가 '정사각 − 100'이 되어 화면 위아래로 빈 띠가 드러난다.
    //   그래서 물림을 누적해 각 층을 '정사각 + 누적치'로 절대 크기로 박는다 — 층 사이 관계는 그대로 남는다.
    //   anchoredPosition은 손대지 않는다: 스트레치든 중앙 앵커든 그 값의 뜻이 '부모 중심에서의 오프셋'으로 같다.
    void SquareUpLayers(Transform _root, Transform _mask)
    {
        var t_root = (RectTransform)_root;

        // ⚠ 자가설치된 바로 그 프레임에는 캔버스 rect가 아직 0이다 — 그때 접어버리면 '첫 구매만 전환이 없다'가 되므로
        //   화면 크기로 대신한다(이 캔버스는 CanvasScaler가 없어 rect가 곧 화면 픽셀이라 두 값이 같다).
        float t_base = Mathf.Max(t_root.rect.width, t_root.rect.height);
        if (t_base <= 0f) t_base = Mathf.Max(Screen.width, Screen.height);
        if (t_base <= 0f) return;

        t_base *= COVER_OVERSCALE;

        // 루트 → 마스크 경로를 위에서 아래 순서로 세운다(누적은 위에서부터여야 뜻이 맞다).
        var t_chain = new List<RectTransform>();
        for (Transform t_node = _mask; t_node != null && t_node != _root; t_node = t_node.parent)
            if (t_node is RectTransform t_layer) t_chain.Add(t_layer);
        t_chain.Reverse();

        var t_inset = Vector2.zero;
        for (int t_i = 0; t_i < t_chain.Count; t_i++)
        {
            var t_layer = t_chain[t_i];

            t_inset += t_layer.sizeDelta;   // 아직 손대지 않은 값이라 여기서 읽는 것이 곧 저작된 물림이다.

            t_layer.anchorMin = t_layer.anchorMax = t_layer.pivot = new Vector2(0.5f, 0.5f);
            t_layer.sizeDelta = new Vector2(t_base + t_inset.x, t_base + t_inset.y);
        }
    }

    // 패키지 프리팹이 들고 오는 자기 캔버스 한 벌을 걷는다. 정렬은 이 레이어가 쥔다(위 주석).
    // 파괴 순서를 지킨다 — 스케일러·레이캐스터가 Canvas를 요구하므로 Canvas를 먼저 걷으면 거부된다.
    static void StripNestedCanvas(GameObject _instance)
    {
        var t_raycaster = _instance.GetComponent<GraphicRaycaster>();
        if (t_raycaster != null) Destroy(t_raycaster);

        var t_scaler = _instance.GetComponent<CanvasScaler>();
        if (t_scaler != null) Destroy(t_scaler);

        var t_canvas = _instance.GetComponent<Canvas>();
        if (t_canvas != null) Destroy(t_canvas);
    }

    // 씬 최상위에 독립 루트 캔버스를 세운다. 어느 캔버스의 자식도 아니어야 sortingOrder가 전역으로 먹는다.
    // SafeArea 안쪽에 두지 않는 것도 ScreenFlash와 같은 이유다 — 노치까지 덮어야 프레임이 완전히 지워진다.
    static PackTransitionScreen Install()
    {
        var t_go = new GameObject(NODE_NAME, typeof(RectTransform), typeof(Canvas));

        var t_canvas = t_go.GetComponent<Canvas>();
        t_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        t_canvas.sortingOrder = SORTING_ORDER;

        // 캔버스 rect가 곧 화면이므로 늘려 붙이면 스케일러 없이도 전체를 덮는다.
        var t_rt = (RectTransform)t_go.transform;
        t_rt.anchorMin = Vector2.zero;
        t_rt.anchorMax = Vector2.one;
        t_rt.offsetMin = Vector2.zero;
        t_rt.offsetMax = Vector2.zero;

        return t_go.AddComponent<PackTransitionScreen>();
    }
}
