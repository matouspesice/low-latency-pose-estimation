using UnityEngine;

/// <summary>
/// Attached to the ball in Coin Mine. Forwards coin-trigger collisions back to the manager.
/// </summary>
public class CoinMineBallTrigger : MonoBehaviour
{
    public CoinMineGameManager manager;

    void OnTriggerEnter(Collider other)
    {
        if (manager == null || other == null) return;
        // Identify coins by the rotator component rather than a tag, because
        // the "Coin" tag would need to be registered in the project TagManager
        // (assigning an undefined tag throws and leaves the object untagged,
        // so the ball would silently pass through every coin).
        var rotator = other.GetComponent<CoinMineCoinRotator>()
                      ?? other.GetComponentInParent<CoinMineCoinRotator>();
        if (rotator == null) return;
        manager.OnCoinCollected(rotator.gameObject);
    }
}

/// <summary>Simple rotator so coins visually spin in place.</summary>
public class CoinMineCoinRotator : MonoBehaviour
{
    public Vector3 rotationSpeed = new Vector3(15f, 45f, 30f);
    void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}
