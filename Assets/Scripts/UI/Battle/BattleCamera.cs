using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class BattleCamera : MonoBehaviour
{
    public static BattleCamera Instance { get; private set; }

    [SerializeField] float cinemaZoom     = 1.5f;
    [SerializeField] float cinemaZMove    = 2f;

    // 시네마 지속시간은 BattleTimingConfig 단일 진실원 (AttackSequence와 공유, 배율 적용)
    float CinemaDuration => GameTiming.Battle.CinemaDuration;

    Camera cam;
    float baseOrthoSize;
    float baseZ;

    void Awake()
    {
        Instance = this;
        this.cam = GetComponent<Camera>();
        if (this.cam == null) return;
        this.baseOrthoSize = this.cam.orthographicSize;
        this.baseZ = transform.position.z;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public UniTask EnterCinema()
    {
        if (this.cam == null) return UniTask.CompletedTask;
        float t_dur = CinemaDuration;
        this.cam.DOOrthoSize(this.baseOrthoSize + this.cinemaZoom, t_dur);

        var t_tcs = new UniTaskCompletionSource();
        transform.DOMoveZ(this.baseZ - this.cinemaZMove, t_dur)
            .OnComplete(() => t_tcs.TrySetResult());
        return t_tcs.Task;
    }

    public void ExitCinema()
    {
        if (this.cam == null) return;
        float t_dur = CinemaDuration;
        this.cam.DOOrthoSize(this.baseOrthoSize, t_dur);
        transform.DOMoveZ(this.baseZ, t_dur);
    }
}
