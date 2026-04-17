# Architect -- Game Principle and Purpose

## Purpose (Master Thesis Context)

**Architect** is the target application of the real-time pose pipeline from the thesis: it demonstrates **body-driven control** of a 3D avatar using **MMPose/RTMPose** pose estimation from a webcam. The game serves as proof that the low-latency pose stack (Python app) can drive a real-time interactive experience (Unity).

- **Input:** Live webcam -> pose estimation (rtmlib/MMPose) -> streamed keypoints.
- **Output:** A 3D avatar in Unity that mirrors the user's pose in real time.

The name *Architect* suggests the user "builds" or "designs" with their body -- e.g. placing or manipulating objects through gesture -- while the core mechanic is **you are the avatar**: your skeleton drives the character.

---

## Core Principle

1. **Single user, single avatar**
   One person in front of the camera controls one in-game avatar. The pipeline is tuned for one tracked person (first/best detection).

2. **Pose as input, not keyboard/mouse**
   Movement and posture come from 2D pose keypoints (e.g. COCO 17) sent over the network. No traditional controls are required for the avatar (optional fallback can be added later).

3. **Real-time mirroring**
   The avatar should reflect the user's pose with minimal delay. Latency is kept low via:
   - Lightweight models and optional GPU in the Python app
   - UDP broadcast of keypoints (no handshake)
   - Unity consuming the latest pose each frame

4. **Extensible for thesis experiments**
   The same link (UDP/JSON) can be used to:
   - Compare backends (rtmlib vs MMPose) on latency and accuracy
   - Test different camera sources (e.g. lab vs home)
   - Add future gameplay (e.g. "architect" building gestures, menus, calibration)

---

## Technical Design

| Component        | Role |
|-----------------|------|
| **Python app**  | Webcam -> RTMPose/MMPose -> 2D keypoints -> UDP broadcast (JSON). |
| **Unity**       | UDP listener -> parse JSON -> drive avatar (skeleton or Humanoid). |
| **Protocol**    | One JSON message per pose: `keypoints` (normalized or pixel), `width`, `height`, optional `scores`. COCO 17 keypoint order. |

---

## Keypoint Convention (COCO 17)

Index | Name
-----|------
0 | nose
1 | left_eye
2 | right_eye
3 | left_ear
4 | right_ear
5 | left_shoulder
6 | right_shoulder
7 | left_elbow
8 | right_elbow
9 | left_wrist
10 | right_wrist
11 | left_hip
12 | right_hip
13 | left_knee
14 | right_knee
15 | left_ankle
16 | right_ankle

Each keypoint: `[x, y, score]`. Coordinates could be normalized [0,1] or in image pixels; `width`/`height` in the message disambiguate.

---

## Scene setup (Unity)

### Quick start (one click)

Use this when starting fresh or after clearing the scene (e.g. to fix doubled objects):

1. **Empty scene:** Delete everything in the Hierarchy, or **File -> New Scene** (leave Main Camera, Directional Light, EventSystem if you prefer).
2. **Architect -> Create Complete Setup (Bridge + Games + UI)**  
   Creates in one go: PoseBridge, all games (Dodge, Single-Leg Balance, Lean Balance, Pose Test), GameSelector, and the full GameCanvas with all panels and wiring.
3. Save the scene when ready (Ctrl+S or **File -> Save As** to name it).
4. Press **Play** to test.

If you had a messy scene with doubled GameSelectors or GameCanvases, delete the scene contents (or create a new scene) and run **Create Complete Setup** once. Do not run the individual Architect steps multiple times on the same scene.

---

### Step-by-step (manual, if needed)

The Architect menu only exposes **Create Complete Setup**. The following is for reference if you set up by hand (e.g. no menu).

**Step 1 -- Pose Bridge (manual)**

**Option A -- If you had the old menu:** Create Pose Bridge is no longer in the menu; use **Create Complete Setup** instead.

**Option B -- Manual (no menu)**

1. In the **Hierarchy**, right-click -> **Create Empty**. Rename to **PoseBridge**.
2. With **PoseBridge** selected, in the **Inspector** click **Add Component**.
3. Search for **Pose Receiver** and add it. Set **Port** to `5555`.
4. Click **Add Component** again, search for **Pose Avatar Driver** and add it.
5. On **Pose Avatar Driver**, set **Avatar Scale** to `2`, ensure **Create Debug Skeleton** is checked.
6. Save the scene.

**What's on the bridge**

- **PoseReceiver** -- listens on UDP port `5555` (match `--udp-port` in Python).
- **PoseAvatarDriver** -- joints (spheres) + optional **limb sticks** (capsules). Options:
  - **Mirror Flip X** -- on by default so your right = avatar right (mirror view).
  - **Smoothing** -- 0 = off (lowest latency), 0.3-0.5 = light; higher adds lag.
  - **Create Limb Sticks** -- capsules between joints (arms, legs, torso).

### Step 2 -- Game Setup + UI (two menu clicks, fully automatic)

**Prerequisites:**

- **TMP Essentials must be imported.** If a "TMP Importer" window appears, click **Import TMP Essentials** (you can skip "Examples & Extras"). Unity needs these resources for TextMeshPro buttons and text.
- **PoseBridge must exist** (Step 1 above).

**Step 2 is no longer in the menu.** Use **Architect -> Create Complete Setup** to create games and UI in one go.

---

## Games

### Pose Dodge

Obstacles move toward the avatar. Match your pose to pass: **Duck** (crouch), **Jump** (arms up), **Stand** (neutral), **Lean Left**, **Lean Right**. Wrong pose = lose a life. Tuned for knee ROM (squat) and weight shift (lean).

### Single-Leg Balance

Hold single-leg stance and keep stable (low wobble). Get into position within a few seconds, then stay on one leg; putting the foot down or too much sway ends the round. Score from stability x time. Aligns with thesis proprioception/balance goals.

### Lean Balance (torso lean only — robust with noisy pose)

Uses **torso lean** only: shoulder center vs hip center (`PoseGestureDetector.TorsoLeanX`). No duck/jump/arms — just lean your body left or right. Intuitive ("lean to move the bar"), and robust because it uses four keypoints (shoulders + hips) and a *relative* measure (independent of where you stand in the frame). Goal: keep a cursor/bar in the green (neutral) zone. Score = time in zone.

**Adding Lean Balance to the scene:** Add a GameObject with `LeanBalanceGameManager`. Assign `PoseGestureDetector` (e.g. from PoseBridge). Add a UI Slider (for the bar), TMP_Text for timer/score/instruction, and panels for start prompt and game over. In the Inspector set `LeanBar Slider`, `Timer Text`, `Score Text`, etc. Optional: set `Target Time In Zone` (e.g. 30) to win after 30 s in zone, or `Out Of Zone Fail Seconds` (e.g. 3) to fail if you leave the zone too long. Add a Start button that calls `LeanBalanceGameManager.StartGame()`.

### Coin Mine (Temple Run–style, lean to collect)

You run in the middle; **lean left, center, or right** to move into one of three lanes. **Coins** spawn in a random lane and move toward you. Lean into the coin's lane when it reaches you to collect it. The UI shows **"You: LEFT / CENTER / RIGHT"** and a **hint for the next coin** (e.g. "← LEAN LEFT for coin!"). Uses the same torso lean as Lean Balance (`TorsoLeanX`). **End Run** shows your score; **Back to Menu** returns to the mode selection.

---

## Game design ideas (simple, intuitive, doable)

Based on thesis context (balance, external focus, knee rehab) and current pose quality:

| Idea | Input | Why it works |
|------|--------|--------------|
| **Lean Balance** | Torso lean only (shoulders vs hips) | One continuous value; robust; "lean to move" is instinctive. |
| **Lean to steer** | Same; steer a ball/cursor left–right | Clear cause–effect; no discrete gestures to mis-detect. |
| **Stay in the zone** | Keep `TorsoLeanX` in a neutral band for time | Balance challenge; score = time in zone. |
| **Lane dodge (lean only)** | Obstacles in 3 lanes; lean L/C/R to be in correct lane | Simpler than full Pose Dodge (no duck/jump); only 3 states from torso lean. |

**Recommendation:** Start with **Lean Balance** or **Stay in the zone**: one scalar (`TorsoLeanX`), simple UI (bar or cursor), easy to tune thresholds. Add Pose Dodge / Single-Leg once pose is more reliable.

---

## How to Run

1. **Start pose stream (Python)**
   From the thesis `app` folder:
   ```bash
   cd app
   python pose.py --udp-port 5555 --no-viz
   ```
   Use `--device cuda --mode lightweight` for lower latency if GPU is available.
   For a visible camera window, omit `--no-viz`.

2. **Start Unity**
   Press **Play** in the Architect scene. Ensure **PoseReceiver** port is `5555` (or match `--udp-port`).

3. **Play**
   The mode selection screen appears. Pick a game, click Start, and the avatar follows your body pose from the webcam.

---

## Possible Future Extensions (Thesis / Post-Thesis)

- **Calibration:** One-time or runtime mapping from camera space to avatar scale/orientation.
- **More games:** Weight-Shift Reach, Stability Challenge, Pose Simon Says (see game design plan).
- **Architect gameplay:** Use pose to place or rotate objects in a level, or to navigate a simple world.
