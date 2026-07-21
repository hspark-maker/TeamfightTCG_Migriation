using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class BattleCamera : MonoBehaviour
{
    public static BattleCamera Instance { get; private set; }

    [SerializeField] float cinemaZoom     = 1.5f;
    [SerializeField] float cinemaZMove    = 2f;
    [SerializeField] float cinemaDuration = 0.25f;

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
        this.cam.DOOrthoSize(this.baseOrthoSize + this.cinemaZoom, this.cinemaDuration);

        var t_tcs = new UniTaskCompletionSource();
        transform.DOMoveZ(this.baseZ - this.cinemaZMove, this.cinemaDuration)
            .OnComplete(() => t_tcs.TrySetResult());
        return t_tcs.Task;
    }

    public void ExitCinema()
    {
        if (this.cam == null) return;
        this.cam.DOOrthoSize(this.baseOrthoSize, this.cinemaDuration);
        transform.DOMoveZ(this.baseZ, this.cinemaDuration);
    }
}
