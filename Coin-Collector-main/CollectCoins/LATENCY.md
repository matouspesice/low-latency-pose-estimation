# Reducing latency (body pose → Unity ball)

The ball uses **direct position mapping**: tilt maps to lane position (no force/inertia), so movement should already feel responsive. If you still notice delay, try the steps below.

## 1. **PoseBridge → BodyTiltInput (Inspector)**

- **Output Smoothing**: set to **0**. Any value &gt; 0 adds a Lerp that delays the signal.
- **Max Lean**: tune for sensitivity; does not add delay.

## 2. **Unity Quality / Frame rate**

- **VSync**: in **Project Settings → Quality**, turn **VSync Count** off (or use “Don’t Sync”) so the game is not locked to 60 Hz and can run at higher FPS. Higher FPS = less delay per input.
- **Target frame rate** (optional): in **Application.targetFrameRate** set e.g. 120 or 144 if your display supports it.

## 3. **Physics (Fixed Timestep)**

- **Edit → Project Settings → Time → Fixed Timestep**: default is 0.02 (50 Hz). Lower = more physics steps per second and often snappier response (e.g. **0.0083** ≈ 120 Hz). Cost: more CPU.
- The ball is moved in **FixedUpdate** using the latest pose from **Update**; more frequent FixedUpdate reduces how “old” the last pose can be.

## 4. **Pipeline (what runs when)**

- **PoseReceiver**: reads UDP in **Update()**, non-blocking, overwrites **latestPose**.
- **PoseGestureDetector**: runs in **Update()**, computes torso lean.
- **BodyTiltInput**: returns raw lean (or smoothed if Output Smoothing &gt; 0).
- **BodyTiltCoinCollectorPlayer**: in **FixedUpdate()** maps **TiltAxis** directly to ball X (no force), so position follows tilt with at most one physics step delay.

No extra buffering; the only smoothing is **BodyTiltInput.outputSmoothing**.

## 5. **Python / capture side**

- Use a fast webcam pipeline (e.g. minimal buffering, direct read).
- Send pose over UDP at high rate (e.g. every frame or every inference).
- Run pose inference as fast as possible (small model, GPU if available).

## 6. **Optional: apply tilt in Update (advanced)**

For absolute minimum latency you could sample the tilt in **Update** and apply it in **FixedUpdate** using that stored value (already the case), or move horizontal movement to **Update** and only keep physics (e.g. collisions) in FixedUpdate. That requires care so physics and input stay consistent.

---

**Quick checklist for lowest latency:**  
Output Smoothing = 0, VSync off (or high refresh), high FPS, lower Fixed Timestep if needed, and a fast Python capture/inference pipeline.
