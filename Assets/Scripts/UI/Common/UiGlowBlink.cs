using Coffee.UIEffects;
using UnityEngine;

// 제자리에서 밝기만 오가는 상시 발광 호흡. 자리를 옮기거나 크기를 바꾸는 연출과 같은 화면에 둘 수 있다 —
// 축이 갈려 있어야 "여기를 눌러라"와 "저것이 목표다"가 서로를 먹지 않는다.
//
// 대상 UIEffect의 colorIntensity 하나만 만진다. 컴포넌트 자체를 끄지 않는 것이 계약이다 —
// 같은 UIEffect를 무채색화(UiGrayscale)가 toneFilter 축으로 나눠 쓰고 있어, 끄면 잠김 표현이 함께 죽는다.
//
// 꺼질 때 저작값이 아니라 restIntensity로 되돌린다. 이 부품이 꺼진 자리는 "발광이 없는 상태"이지
// "저작한 세기로 켜진 상태"가 아니다 — 저작값으로 되돌리면 꺼도 밝은 원판이 남는다.
public class UiGlowBlink : MonoBehaviour
{
    [Tooltip("호흡할 발광. 비우면 자기 자신에서 찾는다.\n" +
             "이 컴포넌트를 끄지 마라 — 무채색화가 같은 UIEffect를 재사용한다.")]
    [SerializeField] UIEffect target;

    [Tooltip("가장 옅을 때의 발광 세기.")]
    [SerializeField] float intensityLow;

    [Tooltip("가장 짙을 때의 발광 세기. 폭을 좁게 잡으면 깜빡이는 것으로 안 읽힌다 —\n" +
             "0.1↔0.42로 저작했다가 \"그대로다\"라는 판정을 받고 0↔0.9로 벌렸다.")]
    [SerializeField] float intensityHigh = 0.9f;

    [Min(0.1f)]
    [Tooltip("한 번 오갔다 제자리로 돌아오는 시간(초).")]
    [SerializeField] float period = 1.3f;

    [Range(0f, 1f)]
    [Tooltip("시작 위상. 한 화면에 여럿이 있으면 서로 다르게 준다 — 같은 박이면 화면이 통째로 뛴다.")]
    [SerializeField] float phase;

    [Tooltip("꺼질 때 되돌릴 발광 세기.")]
    [SerializeField] float restIntensity;

    UIEffect m_effect;
    bool m_resolved;

    // 런타임에 받은 위상. 저작값(phase)을 덮어쓰지 않는다 — 덮어쓰면 에디트 모드 호출에 인스펙터 값이 조용히 사라진다.
    float? m_phaseOverride;

    /// <summary>시작 위상을 런타임에 준다. 같은 화면에 여럿 켤 때 호출자가 순번으로 흩뿌린다.</summary>
    public void SetPhase(float _phase) => this.m_phaseOverride = Mathf.Repeat(_phase, 1f);

    void Awake() => this.Resolve();

    void OnEnable()
    {
        this.Resolve();
        this.Apply(Time.unscaledTime);
    }

    void OnDisable()
    {
        if (this.m_effect == null) return;

        this.m_effect.colorIntensity = this.restIntensity;
    }

    void Update()
    {
        this.Resolve();
        this.Apply(Time.unscaledTime);
    }

    void Apply(float _time)
    {
        if (this.m_effect == null) return;

        float t_phase = this.m_phaseOverride ?? this.phase;
        float t_wave  = (Mathf.Sin((_time + t_phase * this.period) * Mathf.PI * 2f / this.period) + 1f) * 0.5f;

        this.m_effect.colorIntensity = Mathf.Lerp(this.intensityLow, this.intensityHigh, t_wave);
    }

    // 못 찾았으면 다음 기회에 다시 찾는다 — 한 번 실패한 걸 굳히면 그 정점만 영영 안 빛난다.
    void Resolve()
    {
        if (this.m_resolved) return;

        this.m_effect = this.target != null ? this.target : this.GetComponent<UIEffect>();
        this.m_resolved = this.m_effect != null;
    }
}
