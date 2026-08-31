using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class FirebaseAuthProbe : MonoBehaviour
{
    [SerializeField] Text stateText;
    [SerializeField] Text uidText;
    [SerializeField] Text errorText;

    FirebaseAuthService Service => FirebaseAuthService.Instance;

    void OnEnable()
    {
        this.Service.OnStateChanged += Refresh;
        Refresh();
    }

    void Start()
    {
        this.Service.InitializeAsync().Forget();
    }

    void OnDisable()
    {
        this.Service.OnStateChanged -= Refresh;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            this.Service.InitializeAsync().Forget();
    }

    void Refresh()
    {
        string t_log = $"[FirebaseAuthTest] State={this.Service.State} UserId={this.Service.UserId}";
        if (string.IsNullOrEmpty(this.Service.LastError))
            Debug.Log(t_log);
        else
            Debug.LogWarning($"{t_log} Error={this.Service.LastError}");

        if (this.stateText != null)
            this.stateText.text = $"STATE  {this.Service.State.ToString().ToUpperInvariant()}";
        if (this.uidText != null)
            this.uidText.text = string.IsNullOrEmpty(this.Service.UserId)
                ? "UID  -"
                : $"UID  {this.Service.UserId}";
        if (this.errorText != null)
            this.errorText.text = string.IsNullOrEmpty(this.Service.LastError)
                ? string.Empty
                : $"ERROR  {this.Service.LastError}\nPRESS R TO RETRY";
    }
}
