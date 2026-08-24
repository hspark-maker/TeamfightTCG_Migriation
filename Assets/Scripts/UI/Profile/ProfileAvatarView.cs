using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 프로필 그림 한 덩어리(밑판·판·얼굴·링). 이걸 쓰는 화면은 층을 따로 알 필요가 없다.
public class ProfileAvatarView : MonoBehaviour
{
    [Tooltip("얼굴 뒤 판(아바타 색). 이 스프라이트 모양이 곧 얼굴이 잘리는 모양이다.")]
    [SerializeField] Image plateImage;
    [Tooltip("아바타 그림. 판을 꽉 채우고 판 모양대로 오려진다.")]
    [SerializeField] Image faceImage;
    [Tooltip("맨 위 — 아바타 위에 덧씌우는 속이 뚫린 테두리 링(프레임 색).")]
    [SerializeField] Image ringImage;

    [Tooltip("얼굴을 판 모양대로 오려내는 재질(UI/ProfileAvatarMask). 미배선이면 얼굴이 판 밖까지 네모로 그려진다.")]
    [SerializeField] Material faceMaskMaterial;

    // 뷰마다 복제하면 UI 배칭이 뷰 수만큼 끊겨 한 벌을 나눠 쓴다.
    static Material s_faceMaskInstance;
    static readonly int MASK_TEX_ID = Shader.PropertyToID("_MaskTex");

    Sequence m_pressSeq;

    /// <summary>세 층을 한 벌로 그린다.</summary>
    public void Render(in ProfileLook _look)
    {
        ApplyLayer(this.plateImage, _look.Plate, _look.PlateColor);
        ApplyLayer(this.faceImage, _look.Face, null);
        ApplyLayer(this.ringImage, _look.Ring, _look.RingColor);

        this.ApplyFaceMask(_look.Plate);
    }

    /// <summary>한 축만 남기고 끈다. 선택 칸처럼 "그 항목 자체"만 보여야 하는 자리가 쓴다.</summary>
    public void ShowOnly(EProfileAxis _axis)
    {
        SetLayerActive(this.plateImage, _axis == EProfileAxis.Avatar);
        SetLayerActive(this.faceImage,  _axis == EProfileAxis.Avatar);
        SetLayerActive(this.ringImage,  _axis == EProfileAxis.Frame);
    }

    /// <summary>누르는 동안 오므린다. 밑판만 제자리에 남아 그림이 안으로 눌린 것처럼 보인다.</summary>
    public void PressDown(float _shrink, float _duration)
    {
        this.KillPress();

        this.m_pressSeq = DOTween.Sequence().SetLink(gameObject);
        this.InsertScale(0f, Mathf.Max(0.01f, 1f - _shrink), _duration, Ease.OutQuad);
    }

    /// <summary>떼면 한 번 부풀렸다 원래 크기로 돌아온다.</summary>
    public void PressUp(float _pop, float _popDuration, float _settleDuration)
    {
        this.KillPress();

        this.m_pressSeq = DOTween.Sequence().SetLink(gameObject);
        this.InsertScale(0f, 1f + _pop, _popDuration, Ease.OutQuad);
        this.InsertScale(_popDuration, 1f, _settleDuration, Ease.OutBack);
    }

    // 풀 반납·목록 갱신으로 꺼지면 눌린 배율이 그대로 굳는다.
    void OnDisable()
    {
        this.KillPress();
        SetScale(this.plateImage, 1f);
        SetScale(this.ringImage, 1f);
    }

    // 밑판은 뺀다 — 홀로 제자리에 남아야 눌린 깊이가 드러난다.
    void InsertScale(float _at, float _to, float _duration, Ease _ease)
    {
        InsertOne(this.m_pressSeq, _at, this.plateImage, _to, _duration, _ease);
        InsertOne(this.m_pressSeq, _at, this.ringImage, _to, _duration, _ease);
    }

    void KillPress()
    {
        if (this.m_pressSeq == null) return;

        this.m_pressSeq.Kill();
        this.m_pressSeq = null;
    }

    static void InsertOne(Sequence _seq, float _at, Image _image, float _to, float _duration, Ease _ease)
    {
        if (_image == null) return;

        _seq.Insert(_at, _image.transform.DOScale(_to, _duration).SetEase(_ease));
    }

    static void SetScale(Image _image, float _scale)
    {
        if (_image != null) _image.transform.localScale = Vector3.one * _scale;
    }

    static void SetLayerActive(Image _image, bool _on)
    {
        if (_image != null) _image.gameObject.SetActive(_on);
    }

    // 스프라이트가 없으면 저작값을 그대로 둔다 — 설정 미배선이 빈 칸으로 드러나지 않게.
    static void ApplyLayer(Image _image, Sprite _sprite, Color? _color)
    {
        if (_image == null || _sprite == null) return;

        _image.sprite = _sprite;
        if (_color.HasValue) _image.color = _color.Value;
    }

    // uGUI Mask는 스텐실 이진 판정이라 원형 경계가 계단으로 남는다 — 판 알파를 곱해 오려낸다.
    void ApplyFaceMask(Sprite _plate)
    {
        if (this.faceImage == null || this.faceMaskMaterial == null) return;
        if (_plate == null || _plate.texture == null) return;

        Material t_mask = MaskMaterialWith(this.faceMaskMaterial, _plate.texture);
        if (this.faceImage.material != t_mask) this.faceImage.material = t_mask;
    }

    static Material MaskMaterialWith(Material _source, Texture _maskTex)
    {
        // 에셋을 그대로 물리면 SetTexture가 에셋 파일을 고친다 — 인스턴스를 하나 떠서 그쪽만 만진다.
        if (s_faceMaskInstance == null) s_faceMaskInstance = new Material(_source) { hideFlags = HideFlags.HideAndDontSave };

        if (s_faceMaskInstance.GetTexture(MASK_TEX_ID) != _maskTex) s_faceMaskInstance.SetTexture(MASK_TEX_ID, _maskTex);

        return s_faceMaskInstance;
    }
}
