# Coin Collector — Body Tilt Mode (UDP Pose)

Same graphics; gameplay changed to **body-only** control:

- **Ball moves forward automatically** on a **narrow lane**. No joystick or keyboard.
- **Tilt = lane position**: your body tilt directly sets the ball’s left/right position (not force), for a low-latency feel. Walls keep the ball on the lane.
- **Walls** on the sides so you cannot fall off; floor and coins are only inside the lane.
- **One game = 30 seconds.** Collect as many coins as you can; at the end you see your score.
- **First 1–2 seconds:** no coins spawn so you can get used to the controls.

---

## Quick setup (one click)

1. Open the scene you use (e.g. **MainScene** or **MainScene 1**).
2. Menu: **Tools → Body Tilt Coin Collector → Setup Scene**.
3. The setup **creates** (if missing): PoseBridge, CoinSpawner, LaneWalls, BodyTiltFloor, **CountText / TimerText / WinText** on the Canvas, and **centers the Player** (X=0) on the lane.
4. It creates:
   - **PoseBridge** (UDP port 5555, body tilt)
   - Replaces **PlayerController** / **PlayerControllerAndroid** on the Player with **BodyTiltCoinCollectorPlayer**
   - **CoinSpawner** (spawns coins in 3 lanes ahead; assign **Coin** prefab in Inspector if needed)
   - **LaneWalls** (invisible walls at X = ±5)
   - **BodyTiltFloor** (a fallback floor so the ball doesn’t fall when Park/level assets fail to load)

5. If **CoinSpawner** has no **Coin Prefab**: drag `Assets/Prefab/Coin.prefab` onto it (the setup also tries to assign it automatically).
6. Ensure the **Player** has **Tag = "Player"** (for spawner and camera). The setup centers the ball (X=0) so you start in the middle of the lane.

---

## How to run

1. **Start pose stream** (thesis `app` folder):
   ```bash
   cd app
   python pose.py --udp-port 5555 --no-viz
   ```
2. **Play** in Unity. Stand in front of the webcam; **lean left/right** to move the ball. It rolls forward automatically. You have **30 seconds** to collect as many coins as you can; then the game shows your score.

---

## If the ball falls (no floor)

If you see errors like **"Could not create asset"** or **"Couldn't read file ... NaturePark.fbx"** (or Park textures), the original scene floor didn’t load. The setup adds **BodyTiltFloor** with an **EnsureFloor** script that creates a simple floor at runtime so the ball always has ground.

- Run **Tools → Body Tilt Coin Collector → Setup Scene** again to add **BodyTiltFloor** if it’s missing.
- Or add it manually: create an empty GameObject named **BodyTiltFloor**, add the **EnsureFloor** component, and leave **Create At Start** checked.

---

## Manual setup (if you prefer)

- **PoseBridge:** GameObject with **PoseReceiver** (port 5555), **PoseGestureDetector**, **BodyTiltInput**. Or use **GameObject → Pose → Create Pose Bridge (Coin Collector)**.
- **Player:** Remove **PlayerController** and **PlayerControllerAndroid**. Add **BodyTiltCoinCollectorPlayer**; set **Count Text**, **Win Text**, **Side Bound** (e.g. 5).
- **CoinSpawner:** Empty GameObject with **CoinSpawner**; assign **Player** and **Coin** prefab. Lanes: Left X = -3, Middle = 0, Right = 3.
- **LaneWalls:** Empty GameObject with **LaneWalls**; **Side Bound** = 5, then it creates two walls at runtime (or call **CreateWalls** from Inspector).

---

## Tuning

- **BodyTiltCoinCollectorPlayer:** **Forward Speed**, **Horizontal Speed**, **Side Bound**, **Min Tilt To Move** (dead zone).
- **BodyTiltInput** (on PoseBridge): **Max Lean** for sensitivity.
- **CoinSpawner:** **Spawn Distance Ahead**, **Min Gap Between Coins**, **Left/Middle/Right Lane X**.

### Responsiveness of the ball to body movement

- **Player** (ball) → **BodyTiltCoinCollectorPlayer**: **Forward Speed**, **Side Bound** (lane width), **Tilt Lane Range** (how much of the lane tilt uses; 1 = full lane), **Min Tilt To Move** (dead zone).
- **PoseBridge** → **BodyTiltInput**: **Max Lean** (sensitivity), **Output Smoothing** (0 = minimum latency).

### Low latency (pose → ball)

If the ball feels laggy compared to the Python video, see **LATENCY.md** for steps to reduce delay (smoothing off, VSync, Fixed Timestep, etc.).
