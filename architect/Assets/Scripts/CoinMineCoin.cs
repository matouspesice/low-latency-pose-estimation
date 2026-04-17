using UnityEngine;

/// <summary>
/// Single coin in Coin Mine: moves toward the player (negative Z). Lane 0=left, 1=center, 2=right.
/// </summary>
public class CoinMineCoin : MonoBehaviour
{
    public int Lane { get; set; }
    public float Speed { get; set; }
    public float CollectZ { get; set; }
    public bool ReachedCollectZone => transform.position.z <= CollectZ;

    void Update()
    {
        var p = transform.position;
        p.z -= Speed * Time.deltaTime;
        transform.position = p;
    }
}
