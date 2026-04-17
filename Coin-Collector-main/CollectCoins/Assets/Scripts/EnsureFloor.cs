using UnityEngine;

/// <summary>
/// Creates a narrow long floor strip at runtime: same width as the lane (between walls),
/// long enough so you don't run out before the 30 sec game ends. Floor does not extend outside the walls.
/// </summary>
public class EnsureFloor : MonoBehaviour
{
    [Tooltip("Create floor when the game starts.")]
    public bool createAtStart = true;

    [Header("Lane (match LaneWalls and player sideBound)")]
    [Tooltip("Half-width of the lane (walls at ±sideBound). Floor width = 2 * sideBound.")]
    public float sideBound = 5f;
    [Tooltip("Length of the strip (forward). Must be > forwardSpeed * gameDuration (e.g. 16 * 30 = 480).")]
    public float length = 600f;
    [Tooltip("Extra floor behind the start so the ball isn't at the edge.")]
    public float backExtension = 15f;
    public float thickness = 0.5f;
    [Tooltip("Y position of the floor's top surface.")]
    public float surfaceY = 0f;

    void Start()
    {
        if (createAtStart) CreateFloor();
    }

    public void CreateFloor()
    {
        if (transform.Find("FloorPlane") != null) return;

        float width = 2f * sideBound;
        float totalLength = length + backExtension;
        float centerZ = (length - backExtension) * 0.5f; // floor runs from -backExtension to length
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "FloorPlane";
        floor.transform.SetParent(transform);
        floor.transform.localPosition = new Vector3(0f, surfaceY - thickness * 0.5f, centerZ);
        floor.transform.localScale = new Vector3(width, thickness, totalLength);
        floor.transform.localRotation = Quaternion.identity;

        var col = floor.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = false;

        var renderer = floor.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.4f, 0.5f, 0.35f);
            renderer.material = mat;
        }

        Debug.Log("[EnsureFloor] Created narrow lane floor (width=" + width + ", length=" + totalLength + ", backExtension=" + backExtension + ").");
    }
}
