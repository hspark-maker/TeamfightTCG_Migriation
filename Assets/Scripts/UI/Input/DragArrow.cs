using System;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class DragArrow : MonoBehaviour
{
    public event Action<RaycastHit2D> OnReleased;

    LineRenderer line;
    bool isDragging;

    void Awake()
    {
        this.line = GetComponent<LineRenderer>();
        this.line.positionCount = 2;
        this.line.useWorldSpace = true;
        this.line.startWidth = 0.10f;
        this.line.endWidth   = 0.03f;
        this.line.material   = new Material(Shader.Find("Sprites/Default"));
        this.line.startColor = new Color(1f, 0.35f, 0.1f, 0.9f);
        this.line.endColor   = new Color(1f, 0.9f, 0.2f, 0.9f);
        this.line.sortingOrder = 20;
        this.line.enabled = false;
    }

    public void BeginDrag(Vector3 _from)
    {
        this.isDragging = true;
        this.line.enabled = true;
        this.line.SetPosition(0, _from);
        this.line.SetPosition(1, _from);
    }

    public void EndDrag()
    {
        this.isDragging = false;
        this.line.enabled = false;
    }

    void Update()
    {
        if (!this.isDragging) return;

        Vector3 t_mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        t_mouse.z = -1f;
        this.line.SetPosition(1, t_mouse);

        if (!Input.GetMouseButtonUp(0)) return;

        Vector2 t_pos2D = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D t_hit = Physics2D.Raycast(t_pos2D, Vector2.zero);
        EndDrag();
        OnReleased?.Invoke(t_hit);
    }
}
