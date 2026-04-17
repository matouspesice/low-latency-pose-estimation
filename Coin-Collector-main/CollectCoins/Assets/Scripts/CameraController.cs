using UnityEngine;

public class CameraController : MonoBehaviour
{
    private Vector3 distance;
    public GameObject player;
    [Tooltip("Raise the camera for a more top-down view. Keep small (1–3) so the ball stays in view.")]
    public float heightOffset = 2f;
    [Tooltip("Keep camera at least this far behind the ball. Ensures ball is in view.")]
    public float minDistanceZ = -12f;
    [Tooltip("Point to look at: 0 = ball, negative = behind ball (more ball in frame), positive = ahead.")]
    public float lookAheadZ = -2f;

    void Start()
    {
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;
        distance = transform.position - player.transform.position;
        distance = new Vector3(0f, distance.y + heightOffset, distance.z);
        if (distance.z > minDistanceZ) distance.z = minDistanceZ;
    }

    void LateUpdate()
    {
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;
        transform.position = player.transform.position + distance;
        Vector3 lookAt = player.transform.position + new Vector3(0f, 0.5f, lookAheadZ);
        transform.LookAt(lookAt);
    }
}
 