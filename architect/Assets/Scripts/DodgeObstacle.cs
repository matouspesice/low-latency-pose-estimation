using UnityEngine;
using TMPro;

/// <summary>
/// Single obstacle for Pose Dodge: moves toward the avatar, shows what action to take,
/// and changes color as it approaches the hit zone.
/// </summary>
public class DodgeObstacle : MonoBehaviour
{
    public enum ObstacleType
    {
        Duck,
        Jump,
        Stand,
        LeanLeft,
        LeanRight
    }

    public ObstacleType obstacleType;
    public float speed = 5f;
    public float hitZ = 0f;

    Renderer _renderer;
    Color _baseColor;
    float _spawnZ;
    GameObject _label;

    public bool ReachedHitZone => transform.position.z <= hitZ + 0.3f;
    public bool PastHitZone => transform.position.z < hitZ - 1f;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _spawnZ = transform.position.z;
    }

    void Update()
    {
        transform.Translate(0f, 0f, -speed * Time.deltaTime);

        if (_renderer != null)
        {
            float total = Mathf.Max(_spawnZ - hitZ, 1f);
            float remaining = Mathf.Max(transform.position.z - hitZ, 0f);
            float t = 1f - (remaining / total);
            _renderer.material.color = Color.Lerp(_baseColor, Color.white, t * 0.6f);
        }
    }

    public void Setup(ObstacleType type, float spd, float hz, float width, float height)
    {
        obstacleType = type;
        speed = spd;
        hitZ = hz;
        _spawnZ = transform.position.z;

        transform.localScale = new Vector3(width, height, 0.15f);

        _baseColor = GetColorForType(type);
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        if (_renderer != null) _renderer.material.color = _baseColor;

        CreateLabel(type);
    }

    void CreateLabel(ObstacleType type)
    {
        _label = new GameObject("Label");
        _label.transform.SetParent(transform, false);
        _label.transform.localPosition = new Vector3(0f, 0f, -0.6f);
        _label.transform.localScale = new Vector3(1f / Mathf.Max(transform.localScale.x, 0.1f),
                                                    1f / Mathf.Max(transform.localScale.y, 0.1f), 1f);

        var tm = _label.AddComponent<TextMesh>();
        tm.text = GetActionText(type);
        tm.characterSize = 0.15f;
        tm.fontSize = 64;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
    }

    static Color GetColorForType(ObstacleType type)
    {
        switch (type)
        {
            case ObstacleType.Duck:      return new Color(0.9f, 0.5f, 0.1f);
            case ObstacleType.Jump:      return new Color(0.2f, 0.6f, 0.9f);
            case ObstacleType.Stand:     return new Color(0.3f, 0.8f, 0.3f);
            case ObstacleType.LeanLeft:  return new Color(0.8f, 0.3f, 0.8f);
            case ObstacleType.LeanRight: return new Color(0.8f, 0.8f, 0.2f);
            default:                     return Color.gray;
        }
    }

    static string GetActionText(ObstacleType type)
    {
        switch (type)
        {
            case ObstacleType.Duck:      return "DUCK";
            case ObstacleType.Jump:      return "ARMS UP";
            case ObstacleType.Stand:     return "STAND";
            case ObstacleType.LeanLeft:  return "LEAN LEFT";
            case ObstacleType.LeanRight: return "LEAN RIGHT";
            default:                     return "?";
        }
    }

    public static bool GestureMatches(PoseGestureDetector.Gesture gesture, ObstacleType type)
    {
        switch (type)
        {
            case ObstacleType.Duck:      return gesture == PoseGestureDetector.Gesture.Crouch;
            case ObstacleType.Jump:      return gesture == PoseGestureDetector.Gesture.ArmsUp;
            case ObstacleType.Stand:     return gesture == PoseGestureDetector.Gesture.None || gesture == PoseGestureDetector.Gesture.TPose;
            case ObstacleType.LeanLeft:  return gesture == PoseGestureDetector.Gesture.LeanLeft;
            case ObstacleType.LeanRight: return gesture == PoseGestureDetector.Gesture.LeanRight;
            default: return false;
        }
    }
}
