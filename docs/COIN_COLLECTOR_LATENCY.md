# Coin Collector — latency and responsiveness (Architect)

This document preserves the practical tuning notes from the former standalone **CollectCoins / Body Tilt Coin Collector** prototype. The same behaviour now lives under **`architect/`** as `CoinMineGameManager` + shared `PoseBridge` (`PoseReceiver`, `PoseGestureDetector`, `BodyTiltInput`).

## 1. Body tilt signal (`PoseBridge` → `BodyTiltInput`)

- **Output Smoothing:** keep at **0**. Any value &gt; 0 lerps the axis and adds perceptible lag.
- **Max Lean:** sensitivity only; does not add delay by itself.

## 2. Unity quality / frame rate

- **VSync:** in **Project Settings → Quality**, turn **VSync** off (or “Don’t Sync”) if you want the game loop to run above the display refresh where the GPU allows it.
- **Coin Mine → Target Frame Rate While Playing** (`CoinMineGameManager`): optional. If set to e.g. **120**, the manager temporarily sets `Application.targetFrameRate` for the active round and restores the previous value when the round ends or the mode is torn down.

## 3. Physics (Fixed Timestep)

- **Edit → Project Settings → Time → Fixed Timestep:** default **0.02** (50 Hz). Lower values (e.g. **0.0083** ≈ 120 Hz) mean more `FixedUpdate` steps per second and can make steering feel slightly tighter at the cost of CPU.

## 4. Pipeline order (what runs when)

- **PoseReceiver:** reads UDP in **Update()**, non-blocking, overwrites the latest pose.
- **PoseGestureDetector:** **Update()**, computes torso lean.
- **BodyTiltInput:** exposes **TiltAxis** (smoothed only if output smoothing &gt; 0).
- **CoinMineGameManager:** **FixedUpdate()** maps tilt to ball **X** (with optional position smoothing on the manager side).

## 5. Python / capture side

- Prefer a low-latency capture path (resolution, `threaded`, lightweight model, GPU inference where available).
- Send pose UDP every frame you care about; match **Unity `PoseReceiver` port**.

## 6. Warm-up before the first coins

The standalone game left the first **~1–2 seconds** of travel without coins. In Architect, **`firstCoinOffsetZ`** controls how far ahead the first coin row is placed (default tuned for ~2 s at default forward speed).

---

**Quick checklist:** `BodyTiltInput.outputSmoothing = 0`, VSync off if needed, optional **Target Frame Rate While Playing**, consider lower Fixed Timestep, fast `pose.py` pipeline.
