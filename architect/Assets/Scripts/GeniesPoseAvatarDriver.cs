using System;
using Cysharp.Threading.Tasks;
using Genies.Sdk;
using UnityEngine;

/// <summary>
/// Loads Genies SDK avatars and retargets them from COCO pose keypoints (via PoseAvatarDriver).
/// Replaces the debug skeleton when active.
/// </summary>
public class GeniesPoseAvatarDriver : MonoBehaviour
{
    public enum GeniesAvatarPreset
    {
        DebugSkeleton,
        Sample,
        Default,
        User,
        Definition,
    }

    [Serializable]
    public struct AvatarPresetEntry
    {
        public string displayName;
        public GeniesAvatarPreset preset;

        [Tooltip("For Definition presets: Resources path to a JSON avatar definition (no extension).")]
        public string definitionResourcePath;
    }

    [Header("Dependencies")]
    public PoseReceiver poseReceiver;
    public PoseAvatarDriver poseAvatarDriver;

    [Header("Avatars")]
    public AvatarPresetEntry[] avatarPresets = DefaultPresets;

    [Tooltip("Load the saved preset when Play mode starts.")]
    public bool loadOnStart = true;

    [Tooltip("After the first avatar loads, preload the remaining Genies presets in the background. " +
             "Off by default: preloading every avatar on startup is slow and amplifies network failures.")]
    public bool preloadAllGeniesPresets = false;

    [Tooltip("Hide the debug skeleton while a Genies avatar is loaded.")]
    public bool replaceDebugSkeleton = true;

    [Tooltip("Seconds before an avatar load is treated as failed (prevents menu hangs).")]
    public float loadTimeoutSeconds = 20f;

    [Tooltip("Extra scale multiplier on top of avatarScale matching.")]
    public float modelScaleMultiplier = 1f;

    [Tooltip("Rotate the Genies model around Y so it faces the camera.")]
    public float modelYawOffsetDegrees = 180f;

    [Tooltip("Retarget individual bones from pose keypoints (heavier; can look odd).")]
    public bool enableBoneRetargeting = false;

    [Header("Clock mode")]
    public bool cycleInClockMode = false;
    public float clockCycleIntervalSeconds = 30f;

    const string PrefKeySelectedPreset = "Architect.GeniesAvatarPreset";
    const float ReferenceAvatarScale = 3.5f;

    const string SampleDefinitionResourcePath = "Genies/SampleAvatarDefinitions/sample_def";

    // Only presets that work with the public Avatar SDK in this project are offered.
    // The "Test"/demo avatar is intentionally excluded: it requires the SDK to be initialized
    // in demo mode (AvatarSdk.InitializeDemoModeAsync is internal and not reachable here), so
    // loading it in the normal online flow throws the native "class definition" NullReference.
    static readonly AvatarPresetEntry[] DefaultPresets =
    {
        new AvatarPresetEntry { displayName = "Debug Skeleton", preset = GeniesAvatarPreset.DebugSkeleton },
        new AvatarPresetEntry { displayName = "Genies Default", preset = GeniesAvatarPreset.Default },
        new AvatarPresetEntry { displayName = "Sample Avatar", preset = GeniesAvatarPreset.Definition, definitionResourcePath = SampleDefinitionResourcePath },
    };

    sealed class CachedGeniesAvatar
    {
        public ManagedAvatar Avatar;
        public Animator Animator;
        public float ReferenceUnityScale = 1f;
        public float RestModelHeight = 1.7f;
        public bool LoadFailed;
        public readonly System.Collections.Generic.Dictionary<HumanBodyBones, Quaternion> RestWorldRotations =
            new System.Collections.Generic.Dictionary<HumanBodyBones, Quaternion>();
        public readonly System.Collections.Generic.Dictionary<HumanBodyBones, Vector3> RestWorldDirections =
            new System.Collections.Generic.Dictionary<HumanBodyBones, Vector3>();

        public bool IsReady => !LoadFailed && Avatar != null && !Avatar.IsDisposed && Avatar.IsLoadingComplete;
    }

    Transform _anchor;
    ManagedAvatar _managedAvatar;
    Animator _animator;
    int _presetIndex;
    int _loadGeneration;
    int _loadingPresetIndex = -1;
    bool _poseDisplayActive;
    bool _isCycling;
    float _cycleTimer;
    float _savedAvatarOffsetX;
    float _savedAvatarScale;
    float _referenceUnityScale = 1f;
    bool _sdkInitialized;
    string _lastLoadError;
    readonly System.Collections.Generic.Dictionary<int, CachedGeniesAvatar> _cache =
        new System.Collections.Generic.Dictionary<int, CachedGeniesAvatar>();

    static readonly (HumanBodyBones bone, HumanBodyBones child, int from, int to)[] LimbChains =
    {
        (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, CocoKeypointIndex.LeftShoulder, CocoKeypointIndex.LeftElbow),
        (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, CocoKeypointIndex.LeftElbow, CocoKeypointIndex.LeftWrist),
        (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, CocoKeypointIndex.RightShoulder, CocoKeypointIndex.RightElbow),
        (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, CocoKeypointIndex.RightElbow, CocoKeypointIndex.RightWrist),
        (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, CocoKeypointIndex.LeftHip, CocoKeypointIndex.LeftKnee),
        (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, CocoKeypointIndex.LeftKnee, CocoKeypointIndex.LeftAnkle),
        (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, CocoKeypointIndex.RightHip, CocoKeypointIndex.RightKnee),
        (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, CocoKeypointIndex.RightKnee, CocoKeypointIndex.RightAnkle),
    };

    public string CurrentAvatarName =>
        avatarPresets != null && avatarPresets.Length > 0
            ? avatarPresets[Mathf.Clamp(_presetIndex, 0, avatarPresets.Length - 1)].displayName
            : "Genies Avatar";

    public bool IsLoaded => !IsDebugSkeletonSelected && GetCache(_presetIndex)?.IsReady == true;
    public bool IsDebugSkeletonSelected =>
        avatarPresets != null &&
        avatarPresets.Length > 0 &&
        avatarPresets[Mathf.Clamp(_presetIndex, 0, avatarPresets.Length - 1)].preset == GeniesAvatarPreset.DebugSkeleton;
    public bool IsSelectionReady => IsDebugSkeletonSelected || IsLoaded;
    public bool IsLoading => _loadingPresetIndex >= 0;
    public bool IsLoadingPreset(int index) => _loadingPresetIndex == index;

    /// <summary>True when the currently selected Genies avatar finished loading but failed
    /// (the debug skeleton is shown as a fallback).</summary>
    public bool IsCurrentSelectionFailed =>
        !IsDebugSkeletonSelected && !IsLoading && (GetCache(_presetIndex)?.LoadFailed ?? false);
    public string LastLoadError => _lastLoadError;
    public int SelectedPresetIndex => _presetIndex;
    public int PresetCount => avatarPresets != null ? avatarPresets.Length : 0;
    public event Action SelectionChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureOnPoseBridge()
    {
        var bridge = UnityEngine.Object.FindFirstObjectByType<PoseReceiver>();
        if (bridge == null)
            return;

        var go = bridge.gameObject;
        var driver = go.GetComponent<GeniesPoseAvatarDriver>();
        if (driver == null)
        {
            driver = go.AddComponent<GeniesPoseAvatarDriver>();
            Debug.Log("[GeniesPoseAvatarDriver] Added missing component to PoseBridge.");
        }

        if (driver.poseReceiver == null)
            driver.poseReceiver = bridge;
        if (driver.poseAvatarDriver == null)
            driver.poseAvatarDriver = go.GetComponent<PoseAvatarDriver>();

        WireClockModeReference(driver);
        WirePoseTestModeReference(driver);
    }

    static void WireClockModeReference(GeniesPoseAvatarDriver driver)
    {
        var clockModes = UnityEngine.Object.FindObjectsByType<ClockMode>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < clockModes.Length; i++)
        {
            if (clockModes[i] != null && clockModes[i].geniesAvatarDriver == null)
                clockModes[i].geniesAvatarDriver = driver;
        }
    }

    static void WirePoseTestModeReference(GeniesPoseAvatarDriver driver)
    {
        var poseTests = UnityEngine.Object.FindObjectsByType<PoseTestMode>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < poseTests.Length; i++)
        {
            if (poseTests[i] != null && poseTests[i].geniesAvatarDriver == null)
                poseTests[i].geniesAvatarDriver = driver;
        }
    }

    void Awake()
    {
        EnsureDefaultPresets();

        if (poseReceiver == null)
            poseReceiver = UnityEngine.Object.FindFirstObjectByType<PoseReceiver>();
        if (poseAvatarDriver == null)
            poseAvatarDriver = GetComponent<PoseAvatarDriver>();

        var anchorGo = new GameObject("GeniesAvatarAnchor");
        _anchor = anchorGo.transform;
        _anchor.SetParent(transform, false);
        _anchor.gameObject.SetActive(false);
        WireClockModeReference(this);
        WirePoseTestModeReference(this);
        RefreshVisibility();
    }

    void EnsureDefaultPresets()
    {
        avatarPresets = DefaultPresets;
        modelScaleMultiplier = 1f;
        enableBoneRetargeting = false;
        cycleInClockMode = false;
        preloadAllGeniesPresets = false;
    }

    async void Start()
    {
        if (!loadOnStart || avatarPresets == null || avatarPresets.Length == 0)
            return;

        int saved = PlayerPrefs.GetInt(PrefKeySelectedPreset, 0);
        await SelectPresetAsync(Mathf.Clamp(saved, 0, avatarPresets.Length - 1));

        if (preloadAllGeniesPresets)
            PreloadRemainingGeniesPresetsAsync().Forget();
    }

    public void SelectPreset(int index)
    {
        SelectPresetAsync(index).Forget();
    }

    async UniTask SelectPresetAsync(int index)
    {
        if (avatarPresets == null || avatarPresets.Length == 0)
            return;

        int wrapped = ((index % avatarPresets.Length) + avatarPresets.Length) % avatarPresets.Length;
        if (wrapped == _presetIndex && IsSelectionReady)
            return;

        PlayerPrefs.SetInt(PrefKeySelectedPreset, wrapped);
        PlayerPrefs.Save();
        _presetIndex = wrapped;

        var entry = avatarPresets[wrapped];
        if (entry.preset == GeniesAvatarPreset.DebugSkeleton)
        {
            CancelInFlightLoad();
            ApplySelectionInternal();
            return;
        }

        var cached = GetCache(wrapped);
        if (cached != null && cached.IsReady)
        {
            CancelInFlightLoad();
            ApplySelectionInternal();
            return;
        }

        await LoadGeniesPresetAsync(wrapped);
    }

    void CancelInFlightLoad()
    {
        ++_loadGeneration;
        _loadingPresetIndex = -1;
    }

    void Update()
    {
        if (!_poseDisplayActive || !_isCycling || avatarPresets == null || avatarPresets.Length <= 1 || IsLoading)
            return;

        _cycleTimer += Time.deltaTime;
        if (_cycleTimer >= clockCycleIntervalSeconds)
        {
            _cycleTimer = 0f;
            SelectPreset(_presetIndex + 1);
        }
    }

    void LateUpdate()
    {
        if (!_poseDisplayActive || !IsLoaded || poseAvatarDriver == null || !poseAvatarDriver.HasValidPose)
            return;

        ApplyPoseRetargeting();
    }

    /// <summary>
    /// Show the selected avatar in a pose-driven mode (Pose Test or Clock ROI).
    /// Applies the same offset/scale used by the debug skeleton for that mode.
    /// </summary>
    public void BeginPoseDisplay(float offsetX, float scale)
    {
        if (poseAvatarDriver != null)
        {
            _savedAvatarOffsetX = poseAvatarDriver.skeletonOffsetX;
            _savedAvatarScale = poseAvatarDriver.avatarScale;
            poseAvatarDriver.skeletonOffsetX = offsetX;
            poseAvatarDriver.avatarScale = scale;
            poseAvatarDriver.RefreshJointSizes();
        }

        _poseDisplayActive = true;
        RefreshVisibility();
    }

    /// <summary>Hide avatars and restore PoseAvatarDriver layout when leaving a pose mode.</summary>
    public void EndPoseDisplay()
    {
        _poseDisplayActive = false;
        StopCycling();

        if (poseAvatarDriver != null)
        {
            poseAvatarDriver.skeletonOffsetX = _savedAvatarOffsetX;
            poseAvatarDriver.avatarScale = _savedAvatarScale;
            poseAvatarDriver.RefreshJointSizes();
        }

        RefreshVisibility();
    }

    void RefreshVisibility()
    {
        bool showGenies = _poseDisplayActive && IsLoaded && !IsDebugSkeletonSelected;
        if (_anchor != null)
            _anchor.gameObject.SetActive(showGenies);

        if (poseAvatarDriver == null || !replaceDebugSkeleton)
            return;

        bool showDebug = _poseDisplayActive && (IsDebugSkeletonSelected || !IsLoaded);
        poseAvatarDriver.SetDebugVisualsVisible(showDebug);
    }

    public void StartCycling(float intervalSeconds)
    {
        if (!cycleInClockMode || avatarPresets == null || avatarPresets.Length <= 1)
            return;
        clockCycleIntervalSeconds = Mathf.Max(5f, intervalSeconds);
        _cycleTimer = 0f;
        _isCycling = true;
    }

    public void StopCycling() => _isCycling = false;

    async UniTask PreloadRemainingGeniesPresetsAsync()
    {
        if (avatarPresets == null)
            return;

        for (int i = 0; i < avatarPresets.Length; i++)
        {
            if (avatarPresets[i].preset == GeniesAvatarPreset.DebugSkeleton)
                continue;

            if (avatarPresets[i].preset == GeniesAvatarPreset.User && !AvatarSdk.IsLoggedIn)
                continue;

            var cached = GetCache(i);
            if (cached != null && (cached.IsReady || cached.LoadFailed))
                continue;

            await LoadGeniesPresetAsync(i, applySelection: false);
        }
    }

    void ApplySelectionInternal()
    {
        if (IsDebugSkeletonSelected)
        {
            HideAllCachedAvatars();
            _managedAvatar = null;
            _animator = null;
            _referenceUnityScale = 1f;
            _lastLoadError = null;
        }
        else
        {
            ApplyActiveCache(_presetIndex);
        }

        RefreshVisibility();
        SelectionChanged?.Invoke();
    }

    CachedGeniesAvatar GetCache(int index) =>
        _cache.TryGetValue(index, out var entry) ? entry : null;

    Transform GetPresetAnchor(int index)
    {
        string childName = $"Preset_{index}";
        var existing = _anchor.Find(childName);
        if (existing != null)
            return existing;

        var go = new GameObject(childName);
        go.transform.SetParent(_anchor, false);
        return go.transform;
    }

    void HideAllCachedAvatars()
    {
        foreach (var pair in _cache)
        {
            if (pair.Value.Avatar?.Root != null)
                pair.Value.Avatar.Root.SetActive(false);
        }
    }

    void ApplyActiveCache(int index)
    {
        HideAllCachedAvatars();

        var entry = GetCache(index);
        if (entry == null || !entry.IsReady)
        {
            _managedAvatar = null;
            _animator = null;
            return;
        }

        _managedAvatar = entry.Avatar;
        _animator = entry.Animator;
        _referenceUnityScale = entry.ReferenceUnityScale;

        if (_managedAvatar.Root != null)
            _managedAvatar.Root.SetActive(true);

        ResetAnchorFromPose();
    }

    async UniTask LoadGeniesPresetAsync(int index, bool applySelection = true)
    {
        if (avatarPresets == null || avatarPresets.Length == 0)
            return;

        index = Mathf.Clamp(index, 0, avatarPresets.Length - 1);
        var entry = avatarPresets[index];
        if (entry.preset == GeniesAvatarPreset.DebugSkeleton)
            return;

        var cached = GetCache(index);
        if (cached != null && cached.IsReady)
        {
            if (applySelection)
                ApplySelectionInternal();
            return;
        }

        if (cached == null)
        {
            cached = new CachedGeniesAvatar();
            _cache[index] = cached;
        }

        cached.LoadFailed = false;
        _lastLoadError = null;

        int generation = ++_loadGeneration;
        _loadingPresetIndex = index;
        SelectionChanged?.Invoke();

        if (!await EnsureSdkReadyAsync())
        {
            cached.LoadFailed = true;
            FinishLoading(index, generation, applySelection);
            return;
        }

        ManagedAvatar loaded = null;
        try
        {
            loaded = await LoadPresetInternalAsync(entry, GetPresetAnchor(index))
                .Timeout(TimeSpan.FromSeconds(Mathf.Max(5f, loadTimeoutSeconds)));
        }
        catch (TimeoutException)
        {
            _lastLoadError = "Timed out";
            Debug.LogWarning($"[GeniesPoseAvatarDriver] Timed out loading '{entry.displayName}'.");
        }
        catch (Exception ex)
        {
            _lastLoadError = ex.Message;
            Debug.LogWarning($"[GeniesPoseAvatarDriver] Failed to load '{entry.displayName}': {ex.Message}");
        }

        if (generation != _loadGeneration)
        {
            loaded?.Dispose();
            if (_loadingPresetIndex == index)
                _loadingPresetIndex = -1;
            return;
        }

        if (loaded == null)
        {
            cached.LoadFailed = true;
            FinishLoading(index, generation, applySelection);
            return;
        }

        cached.Avatar = loaded;
        cached.Animator = loaded.Animator;
        cached.LoadFailed = false;

        if (!loaded.IsLoadingComplete)
        {
            try
            {
                await UniTask
                    .WaitUntil(() => loaded == null || loaded.IsDisposed || loaded.IsLoadingComplete)
                    .Timeout(TimeSpan.FromSeconds(Mathf.Max(5f, loadTimeoutSeconds)));
            }
            catch (TimeoutException)
            {
                Debug.LogWarning($"[GeniesPoseAvatarDriver] Timed out waiting for '{entry.displayName}' to finish loading.");
            }
        }

        if (generation != _loadGeneration || loaded.IsDisposed || !loaded.IsLoadingComplete)
        {
            loaded.Dispose();
            _cache.Remove(index);
            cached.LoadFailed = true;
            FinishLoading(index, generation, applySelection);
            return;
        }

        if (loaded.Root != null)
        {
            loaded.Root.transform.localPosition = Vector3.zero;
            loaded.Root.transform.localRotation = Quaternion.identity;
            loaded.Root.SetActive(false);
        }

        if (cached.Animator != null)
        {
            cached.Animator.applyRootMotion = false;
            cached.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CaptureRestPose(cached);
            CalibrateReferenceScale(cached);
            cached.Animator.enabled = false;
        }

        FinishLoading(index, generation, applySelection);
        Debug.Log($"[GeniesPoseAvatarDriver] Cached: {entry.displayName}");
    }

    void FinishLoading(int index, int generation, bool applySelection)
    {
        if (generation != _loadGeneration)
            return;

        _loadingPresetIndex = -1;

        if (applySelection && _presetIndex == index)
            ApplySelectionInternal();
        else
            SelectionChanged?.Invoke();
    }
    async UniTask<bool> EnsureSdkReadyAsync()
    {
        if (_sdkInitialized)
            return true;

        if (!await AvatarSdk.InitializeAsync())
        {
            _lastLoadError = "SDK init failed. Run Tools > Genies > SDK Bootstrap Wizard.";
            Debug.LogError("[GeniesPoseAvatarDriver] AvatarSdk.InitializeAsync failed. Run Tools > Genies > SDK Bootstrap Wizard.");
            return false;
        }

        await AvatarSdk.TryInstantLoginAsync();
        if (!AvatarSdk.IsLoggedIn)
            await AvatarSdk.StartLoginAnonymousAsync();

        _sdkInitialized = true;
        return true;
    }

    async UniTask<ManagedAvatar> LoadPresetInternalAsync(AvatarPresetEntry entry, Transform parent)
    {
        switch (entry.preset)
        {
            case GeniesAvatarPreset.Definition:
            {
                string json = LoadDefinitionJson(entry.definitionResourcePath);
                if (string.IsNullOrEmpty(json))
                {
                    _lastLoadError = $"Definition not found at Resources/{entry.definitionResourcePath}";
                    Debug.LogWarning($"[GeniesPoseAvatarDriver] {_lastLoadError}");
                    return null;
                }
                return await AvatarSdk.LoadAvatarAsync(new LoadAvatarOptions.ByDefinition
                {
                    DefinitionToLoad = json,
                    AvatarName = entry.displayName,
                    Parent = parent,
                    ShowLoadingSilhouette = false,
                });
            }

            case GeniesAvatarPreset.Default:
                return await AvatarSdk.LoadAvatarAsync(new LoadAvatarOptions.Default
                {
                    AvatarName = entry.displayName,
                    Parent = parent,
                    ShowLoadingSilhouette = false,
                });

            case GeniesAvatarPreset.User:
                if (!AvatarSdk.IsLoggedIn)
                    return null;
                return await AvatarSdk.LoadAvatarAsync(new LoadAvatarOptions.User
                {
                    AvatarName = entry.displayName,
                    Parent = parent,
                    ShowLoadingSilhouette = false,
                });

            case GeniesAvatarPreset.Sample:
                // The dedicated test/demo avatar needs demo-mode init (not available through the
                // public SDK here). Fall back to the default avatar so a stale "Sample" selection
                // does not crash with the native "class definition" NullReference.
                return await AvatarSdk.LoadAvatarAsync(new LoadAvatarOptions.Default
                {
                    AvatarName = entry.displayName,
                    Parent = parent,
                    ShowLoadingSilhouette = false,
                });

            default:
                return null;
        }
    }

    static string LoadDefinitionJson(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
            return null;
        var asset = Resources.Load<TextAsset>(resourcePath);
        return asset != null ? asset.text : null;
    }

    void CalibrateReferenceScale(CachedGeniesAvatar entry)
    {
        if (poseAvatarDriver == null || entry.RestModelHeight < 0.01f)
        {
            entry.ReferenceUnityScale = 1f;
            return;
        }

        entry.ReferenceUnityScale = ReferenceAvatarScale / entry.RestModelHeight;
    }

    void CaptureRestPose(CachedGeniesAvatar entry)
    {
        entry.RestWorldRotations.Clear();
        entry.RestWorldDirections.Clear();
        if (entry.Animator == null || !entry.Animator.isHuman)
            return;

        var head = entry.Animator.GetBoneTransform(HumanBodyBones.Head);
        var leftFoot = entry.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = entry.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (head != null && (leftFoot != null || rightFoot != null))
        {
            float footY = leftFoot != null ? leftFoot.position.y : rightFoot.position.y;
            if (rightFoot != null && leftFoot != null)
                footY = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
            entry.RestModelHeight = Mathf.Max(0.5f, head.position.y - footY);
        }

        foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone)
                continue;
            var t = entry.Animator.GetBoneTransform(bone);
            if (t != null)
                entry.RestWorldRotations[bone] = t.rotation;
        }

        foreach (var chain in LimbChains)
            StoreRestDirection(entry, chain.bone, chain.child);
    }

    void StoreRestDirection(CachedGeniesAvatar entry, HumanBodyBones bone, HumanBodyBones reference, bool invert = false)
    {
        var boneT = entry.Animator.GetBoneTransform(bone);
        var refT = entry.Animator.GetBoneTransform(reference);
        if (boneT == null || refT == null)
            return;
        var dir = invert ? (boneT.position - refT.position).normalized : (refT.position - boneT.position).normalized;
        if (dir.sqrMagnitude > 1e-6f)
            entry.RestWorldDirections[bone] = dir;
    }

    void ApplyPoseRetargeting()
    {
        ResetAnchorFromPose();
        if (!enableBoneRetargeting || _animator == null || !_animator.isHuman)
            return;

        var entry = GetCache(_presetIndex);
        if (entry == null)
            return;

        foreach (var chain in LimbChains)
            AimBone(entry, chain.bone, chain.from, chain.to);
    }

    void ResetAnchorFromPose()
    {
        if (poseAvatarDriver == null || _animator == null)
            return;

        if (!TryGetCorePosePoints(out var hipCenter, out _, out _, out var shoulderCenter))
            return;

        float avatarScale = Mathf.Max(0.1f, poseAvatarDriver.avatarScale);
        float uniformScale = (avatarScale / ReferenceAvatarScale) * _referenceUnityScale * modelScaleMultiplier;

        _anchor.localPosition = Vector3.zero;
        _anchor.localRotation = Quaternion.Euler(0f, modelYawOffsetDegrees, 0f);
        _anchor.localScale = Vector3.one * uniformScale;

        var alignBone = _animator.GetBoneTransform(HumanBodyBones.Hips);
        var targetPoint = (hipCenter + shoulderCenter) * 0.5f;
        if (alignBone != null)
            _anchor.position += targetPoint - alignBone.position;
    }

    bool TryGetCorePosePoints(out Vector3 hipCenter, out Vector3 leftShoulder, out Vector3 rightShoulder, out Vector3 shoulderCenter)
    {
        hipCenter = default;
        leftShoulder = default;
        rightShoulder = default;
        shoulderCenter = default;

        if (!TryGetPoseJointWorld(CocoKeypointIndex.LeftShoulder, out leftShoulder) ||
            !TryGetPoseJointWorld(CocoKeypointIndex.RightShoulder, out rightShoulder) ||
            !TryGetPoseJointWorld(CocoKeypointIndex.LeftHip, out var leftHip) ||
            !TryGetPoseJointWorld(CocoKeypointIndex.RightHip, out var rightHip))
            return false;

        shoulderCenter = (leftShoulder + rightShoulder) * 0.5f;
        hipCenter = (leftHip + rightHip) * 0.5f;
        return true;
    }

    void AimBone(CachedGeniesAvatar entry, HumanBodyBones bone, int fromIndex, int toIndex)
    {
        if (!TryGetPoseJointWorld(fromIndex, out var from) || !TryGetPoseJointWorld(toIndex, out var to))
            return;
        AimBoneDirection(entry, bone, to - from);
    }

    void AimBoneDirection(CachedGeniesAvatar entry, HumanBodyBones bone, Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude < 1e-6f)
            return;
        var t = _animator.GetBoneTransform(bone);
        if (t == null || !entry.RestWorldRotations.TryGetValue(bone, out var restRotation))
            return;
        if (!entry.RestWorldDirections.TryGetValue(bone, out var restDirection))
        {
            if (t.childCount == 0) return;
            restDirection = (t.GetChild(0).position - t.position).normalized;
        }
        t.rotation = Quaternion.FromToRotation(restDirection, worldDirection.normalized) * restRotation;
    }

    bool TryGetPoseJointWorld(int index, out Vector3 worldPos)
    {
        worldPos = default;
        if (poseAvatarDriver == null || !poseAvatarDriver.TryGetSmoothedJointLocal(index, out var localPos))
            return false;
        worldPos = transform.TransformPoint(localPos);
        return true;
    }

    void DisposeAllCachedAvatars()
    {
        foreach (var pair in _cache)
        {
            if (pair.Value.Avatar != null && !pair.Value.Avatar.IsDisposed)
                pair.Value.Avatar.Dispose();
        }
        _cache.Clear();
        _managedAvatar = null;
        _animator = null;
    }

    void OnDestroy()
    {
        StopCycling();
        ++_loadGeneration;
        DisposeAllCachedAvatars();
        if (poseAvatarDriver != null && replaceDebugSkeleton)
            poseAvatarDriver.SetDebugVisualsVisible(true);
    }
}
