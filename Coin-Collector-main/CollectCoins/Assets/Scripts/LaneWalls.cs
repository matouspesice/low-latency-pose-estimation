using UnityEngine;

/// <summary>
/// Creates two walls on left and right so the ball cannot fall off.
/// Walls run the full length of the lane (long enough for 30 sec at forward speed).
/// Coins spawn only inside these bounds.
/// </summary>
public class LaneWalls : MonoBehaviour
{
    [Tooltip("Walls at X = ±sideBound (match player sideBound and floor width).")]
    public float sideBound = 5f;
    public float wallHeight = 3f;
    [Tooltip("Length of the lane (forward). Match EnsureFloor length so you don't run out in 30 sec.")]
    public float wallDepth = 600f;
    public bool createAtStart = true;

    void Start()
    {
        if (createAtStart) CreateWalls();
    }

    public void CreateWalls()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith("LaneWall"))
                Destroy(child.gameObject);
        }
        CreateWall("LaneWallLeft", -sideBound);
        CreateWall("LaneWallRight", sideBound);
    }

    void CreateWall(string name, float x)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(x, wallHeight * 0.5f, wallDepth * 0.5f);
        go.transform.localScale = new Vector3(1f, wallHeight, wallDepth);
        go.transform.localRotation = Quaternion.identity;
        var col = go.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = false;
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.enabled = false;
    }
}
