using UnityEngine;

/// <summary>
/// Spawns one coin per row (one desired path): at each Z step there is exactly one coin
/// in one of the three lanes (left, middle, right), so the path zigzags. Covers full 30 sec.
/// </summary>
public class CoinSpawner : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public GameObject coinPrefab;
    public BodyTiltCoinCollectorPlayer gamePlayer;

    [Header("Lanes (X positions inside walls: left, middle, right)")]
    public float leftLaneX = -3f;
    public float middleLaneX = 0f;
    public float rightLaneX = 3f;

    [Header("Spawning (pre-spawn at Start)")]
    [Tooltip("Z offset from the player where the first coin appears.")]
    public float firstCoinOffsetZ = 25f;
    [Tooltip("Distance between consecutive coins along Z (one coin per row).")]
    public float gapBetweenCoins = 4f;
    [Tooltip("Height above the floor (Y).")]
    public float coinHeight = 1f;
    [Tooltip("Total rows (one coin per row). Enough to cover forwardSpeed * 30 sec.")]
    public int totalCoinsToSpawn = 120;
    [Tooltip("Lane length (forward). Coins stop spawning at this Z so they don't appear past the lane end. Match LaneWalls wallDepth / EnsureFloor length.")]
    public float laneLength = 600f;

    void Start()
    {
        if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player == null) player = FindObjectOfType<BodyTiltCoinCollectorPlayer>()?.transform;
        if (gamePlayer == null) gamePlayer = FindObjectOfType<BodyTiltCoinCollectorPlayer>();
        if (coinPrefab == null)
        {
            Debug.LogWarning("[CoinSpawner] No coin prefab assigned.");
            return;
        }
        if (player == null)
        {
            Debug.LogWarning("[CoinSpawner] Player not found.");
            return;
        }

        float startZ = player.position.z + firstCoinOffsetZ;
        float laneEndZ = player.position.z + laneLength;
        int coinsToSpawn = totalCoinsToSpawn;
        int maxCoinsInLane = Mathf.Max(0, Mathf.FloorToInt((laneEndZ - startZ) / gapBetweenCoins) + 1);
        if (maxCoinsInLane < coinsToSpawn)
            coinsToSpawn = maxCoinsInLane;

        int prevLane = -1;
        for (int i = 0; i < coinsToSpawn; i++)
        {
            float spawnZ = startZ + i * gapBetweenCoins;
            // One coin per row; never same lane as previous (no coins next to each other)
            int lane;
            if (prevLane < 0)
                lane = Random.Range(0, 3);
            else
            {
                int a = (prevLane + 1) % 3;
                int b = (prevLane + 2) % 3;
                lane = Random.Range(0, 2) == 0 ? a : b;
            }
            prevLane = lane;
            float x = lane == 0 ? leftLaneX : (lane == 1 ? middleLaneX : rightLaneX);
            Vector3 pos = new Vector3(x, coinHeight, spawnZ);
            GameObject coin = Instantiate(coinPrefab, pos, Quaternion.identity);
            coin.name = "Coin_" + i;
            coin.tag = "Coin";
            coin.SetActive(true);
            var col = coin.GetComponent<Collider>();
            if (col == null)
            {
                var sc = coin.AddComponent<SphereCollider>();
                sc.radius = 0.5f;
                col = sc;
            }
            col.isTrigger = true;
            if (coin.GetComponent<CoinRotator>() == null)
                coin.AddComponent<CoinRotator>();
        }
        Debug.Log("[CoinSpawner] Spawned " + coinsToSpawn + " coins (one per row) from Z=" + startZ + ", last coin before lane end Z=" + laneEndZ + ".");
    }
}
