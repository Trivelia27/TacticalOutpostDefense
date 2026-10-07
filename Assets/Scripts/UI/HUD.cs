using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// IMGUI heads-up display: match info, reactor / player status, world-space enemy markers and the
    /// Utility AI inspector (hover an enemy to see how it scores its actions and targets).
    /// F1 labels · F2 target lines · F3 inspector
    /// </summary>
    public class HUD : MonoBehaviour
    {
        const float RefHeight = 720f;

        GUIStyle label, labelSmall, labelBig, labelTitle, labelCenter, labelMono;
        float scale = 1f;
        Camera cam;

        EnemyBrain selected;
        float lastPlayerHp = -1f;
        float hitFlash;
        int lastWave;
        float bannerUntil;
        Texture2D white;

        static readonly Color Panel = new Color(0.04f, 0.06f, 0.08f, 0.72f);
        static readonly Color Accent = new Color(0.25f, 0.85f, 1f);

        void Awake()
        {
            white = Texture2D.whiteTexture;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) AIDebug.ShowLabels = !AIDebug.ShowLabels;
            if (Input.GetKeyDown(KeyCode.F2)) AIDebug.ShowLines = !AIDebug.ShowLines;
            if (Input.GetKeyDown(KeyCode.F3)) AIDebug.ShowInspector = !AIDebug.ShowInspector;

            var gm = GameManager.Instance;
            bool playing = gm != null && gm.IsPlaying;
            Cursor.visible = !playing;

            var player = PlayerController.Instance;
            if (player != null)
            {
                float hp = player.Health.Current;
                if (lastPlayerHp >= 0f && hp < lastPlayerHp - 0.01f) hitFlash = 1f;
                lastPlayerHp = hp;
                TrackDamage(player);
            }
            hitFlash = Mathf.MoveTowards(hitFlash, 0f, Time.unscaledDeltaTime * 2.5f);

            var wm = WaveManager.Instance;
            if (wm != null && wm.CurrentWave != lastWave)
            {
                lastWave = wm.CurrentWave;
                if (lastWave > 0) bannerUntil = Time.unscaledTime + 3.5f;
            }
        }

        void EnsureStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, clipping = TextClipping.Overflow, normal = { textColor = Color.white } };
            labelSmall = new GUIStyle(label) { fontSize = 12 };
            labelBig = new GUIStyle(label) { fontSize = 28, fontStyle = FontStyle.Bold };
            labelTitle = new GUIStyle(label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            labelCenter = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            labelMono = new GUIStyle(labelSmall) { fontSize = 11, padding = new RectOffset(2, 2, 0, 0), clipping = TextClipping.Overflow };
        }

        // ---------------------------------------------------------------- drawing helpers

        void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void Bar(Rect r, float frac, Color fg, Color? bg = null)
        {
            Box(r, bg ?? new Color(0f, 0f, 0f, 0.55f));
            Box(new Rect(r.x + 1, r.y + 1, Mathf.Max(0f, (r.width - 2) * Mathf.Clamp01(frac)), r.height - 2), fg);
        }

        void Text(Rect r, string s, GUIStyle style, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f * c.a);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, style);
            GUI.color = c;
            GUI.Label(r, s, style);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- main

        void OnGUI()
        {
            EnsureStyles();
            if (cam == null) cam = Camera.main;
            scale = Screen.height / RefHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float W = Screen.width / scale, H = RefHeight;

            var gm = GameManager.Instance;
            if (gm == null) return;

            if (hitFlash > 0f) Box(new Rect(0, 0, W, H), new Color(0.8f, 0.05f, 0.05f, 0.28f * hitFlash));

            if (gm.State != GameState.Ready)
            {
                DrawTopBar(W, gm);
                DrawPlayerPanel(W, H);
                DrawMinimap();
                DrawPrompt(W, H);
                DrawWaveBanner(W, H);
                DrawWorldMarkers(H);
                if (AIDebug.ShowInspector) DrawInspector(W, H);
                DrawHelp(W, H);
                if (gm.State == GameState.Playing)
                {
                    DrawDamageDirections(W, H);
                    DrawCrosshair();
                    DrawSensitivityToast(W, H);
                }
            }

            switch (gm.State)
            {
                case GameState.Ready: DrawStartScreen(W, H); break;
                case GameState.Paused: DrawOverlay(W, H, "PAUSED", "Press ESC / P to resume", Accent); break;
                case GameState.Won: DrawEnd(W, H, gm, "OUTPOST SECURED", new Color(0.3f, 1f, 0.5f)); break;
                case GameState.Lost: DrawEnd(W, H, gm, "OUTPOST LOST", new Color(1f, 0.3f, 0.25f)); break;
            }
        }

        // ---------------------------------------------------------------- panels

        void DrawTopBar(float W, GameManager gm)
        {
            var wm = WaveManager.Instance;
            Box(new Rect(0, 0, W, 54), Panel);

            if (wm != null)
            {
                string title = wm.CurrentWave > 0 ? $"WAVE {wm.CurrentWave}/{wm.TotalWaves}  ·  {wm.CurrentTitle}" : "PREPARE DEFENSES";
                Text(new Rect(16, 6, 420, 26), title, label, Accent);

                string sub;
                if (wm.CurrentPhase == WaveManager.Phase.Waiting || wm.CurrentPhase == WaveManager.Phase.Intermission)
                    sub = $"Next wave in {Mathf.CeilToInt(Mathf.Max(0f, wm.Countdown))}s   [N] skip";
                else
                    sub = $"Hostiles remaining: {wm.EnemiesRemaining}";
                Text(new Rect(16, 30, 420, 20), sub, labelSmall, new Color(1, 1, 1, 0.8f));
            }

            // Reactor integrity (centre)
            var reactor = Reactor.Instance;
            if (reactor != null)
            {
                float cx = W * 0.5f;
                Text(new Rect(cx - 150, 3, 300, 20), "REACTOR INTEGRITY", labelSmall, Accent);
                var f = reactor.Health.Fraction;
                var col = Color.Lerp(new Color(1f, 0.2f, 0.15f), new Color(0.2f, 0.9f, 1f), f);
                Bar(new Rect(cx - 150, 22, 300, 18), f, col);
                Text(new Rect(cx - 150, 22, 300, 18), $"{Mathf.CeilToInt(reactor.Health.Current)} / {Mathf.CeilToInt(reactor.Health.maxHealth)}", labelMono, Color.white);
            }

            Text(new Rect(W - 330, 6, 320, 24), $"SCRAP {gm.Scrap}     KILLS {gm.Kills}     SCORE {gm.Score}", label, new Color(1f, 0.9f, 0.4f));
        }

        void DrawPlayerPanel(float W, float H)
        {
            var p = PlayerController.Instance;
            if (p == null) return;
            var r = new Rect(16, H - 96, 300, 80);
            Box(r, Panel);
            Text(new Rect(r.x + 10, r.y + 4, 200, 20), "COMMANDER", labelSmall, Accent);
            Bar(new Rect(r.x + 10, r.y + 24, 280, 14), p.Health.Fraction, new Color(0.3f, 0.9f, 0.4f));
            string ammo = p.Reloading ? "RELOADING..." : $"AMMO {p.Ammo}/{p.magSize}";
            Text(new Rect(r.x + 10, r.y + 42, 200, 24), ammo, label, p.Ammo <= 5 && !p.Reloading ? new Color(1f, 0.4f, 0.3f) : Color.white);
            if (p.Reloading) Bar(new Rect(r.x + 150, r.y + 50, 140, 8), p.ReloadProgress, Accent);
            if (p.Crouched) Text(new Rect(r.x + 200, r.y + 42, 90, 24), "CROUCH", labelSmall, new Color(1f, 0.9f, 0.4f));
        }

        void DrawPrompt(float W, float H)
        {
            var p = PlayerController.Instance;
            if (p == null || string.IsNullOrEmpty(p.Prompt)) return;
            Text(new Rect(W * 0.5f - 300, H - 130, 600, 28), p.Prompt, labelCenter, Color.white);
        }

        void DrawWaveBanner(float W, float H)
        {
            if (Time.unscaledTime > bannerUntil) return;
            var wm = WaveManager.Instance;
            if (wm == null) return;
            float a = Mathf.Clamp01((bannerUntil - Time.unscaledTime) / 1f);
            Text(new Rect(0, H * 0.22f, W, 60), $"WAVE {wm.CurrentWave}", labelTitle, new Color(1f, 0.85f, 0.3f, a));
            Text(new Rect(0, H * 0.22f + 56, W, 30), wm.CurrentTitle.ToUpper(), labelCenter, new Color(1f, 1f, 1f, a));
        }

        void DrawHelp(float W, float H)
        {
            Text(new Rect(W - 600, H - 48, 590, 40),
                "F1 labels · F2 target lines · F3 AI inspector · V camera · Q shoulder · [ ] sensitivity\nWASD move · Mouse look · LMB fire · RMB aim · R reload · C crouch · Shift sprint",
                labelSmall, new Color(1, 1, 1, 0.6f));
        }

        /// <summary>Presentation mode: the inspector follows <see cref="DemoSelected"/> instead of the mouse / crosshair.</summary>
        public static bool DemoMode;
        public static EnemyBrain DemoSelected;

        void DrawCrosshair()
        {
            Vector2 m = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
            if (!DemoMode && CameraRig.IsThirdPerson)
                m = new Vector2(Screen.width * 0.5f / scale, Screen.height * 0.5f / scale);   // crosshair = screen centre
            if (DemoMode)
            {
                var pl = PlayerController.Instance;
                if (pl == null || cam == null) return;
                Vector3 sp = cam.WorldToScreenPoint(pl.AimPoint);
                if (sp.z < 0f) return;
                m = new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
            }
            var player = PlayerController.Instance;
            float spread = player != null ? player.CurrentSpreadDeg : 1f;
            float gap = 4f + spread * 7f;                        // the cross opens up with movement, bloom and hip-fire
            bool onEnemy = player != null && player.AimingAtEnemy;
            Color c = onEnemy ? new Color(1f, 0.3f, 0.25f, 0.95f) : new Color(0.85f, 1f, 1f, 0.9f);
            if (player != null && player.Sprinting) c.a *= 0.35f;

            const float len = 8f;
            Box(new Rect(m.x - 1, m.y - gap - len, 2, len), c);
            Box(new Rect(m.x - 1, m.y + gap, 2, len), c);
            Box(new Rect(m.x - gap - len, m.y - 1, len, 2), c);
            Box(new Rect(m.x + gap, m.y - 1, len, 2), c);
            Box(new Rect(m.x - 1, m.y - 1, 2, 2), c);

            if (player == null) return;

            // Hit marker (white) and kill marker (red): an X that flashes around the crosshair.
            float sinceHit = Time.time - player.LastHitTime;
            float sinceKill = Time.time - player.LastKillTime;
            if (sinceKill < 0.4f) DrawMarker(m, 11f, 8f, new Color(1f, 0.25f, 0.2f, 1f - sinceKill / 0.4f));
            else if (sinceHit < 0.2f) DrawMarker(m, 8f, 6f, new Color(1f, 1f, 1f, 1f - sinceHit / 0.2f));

            if (player.Reloading)
            {
                var bar = new Rect(m.x - 30f, m.y + gap + len + 12f, 60f, 5f);
                Bar(bar, player.ReloadProgress, Accent);
            }
        }

        void DrawMarker(Vector2 center, float inner, float length, Color c)
        {
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, center);
            Box(new Rect(center.x - 1f, center.y - inner - length, 2f, length), c);
            Box(new Rect(center.x - 1f, center.y + inner, 2f, length), c);
            Box(new Rect(center.x - inner - length, center.y - 1f, length, 2f), c);
            Box(new Rect(center.x + inner, center.y - 1f, length, 2f), c);
            GUI.matrix = old;
        }

        // ---------------------------------------------------------------- damage direction

        struct DamageMark { public Vector3 Position; public float Time; }
        readonly List<DamageMark> damageMarks = new List<DamageMark>();
        float lastDamageSeen = -1f;
        const float DamageMarkLife = 1.6f;

        void TrackDamage(PlayerController player)
        {
            var health = player.Health;
            if (health.LastDamageTime == lastDamageSeen) return;
            lastDamageSeen = health.LastDamageTime;
            var attacker = health.LastAttacker;
            if (attacker == null) return;
            if (damageMarks.Count >= 8) damageMarks.RemoveAt(0);
            damageMarks.Add(new DamageMark { Position = attacker.transform.position, Time = Time.unscaledTime });
        }

        /// <summary>Red wedges around the crosshair pointing at whoever just shot the commander (enemies can be outside the view).</summary>
        void DrawDamageDirections(float W, float H)
        {
            var player = PlayerController.Instance;
            if (player == null || cam == null || damageMarks.Count == 0) return;

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = player.transform.forward;
            forward.Normalize();

            Vector2 center = new Vector2(W * 0.5f, H * 0.5f);
            for (int i = damageMarks.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - damageMarks[i].Time;
                if (age > DamageMarkLife) { damageMarks.RemoveAt(i); continue; }

                Vector3 dir = damageMarks[i].Position - player.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) continue;
                float angle = Vector3.SignedAngle(forward, dir.normalized, Vector3.up);   // + = to the right
                float rad = angle * Mathf.Deg2Rad;
                Vector2 p = center + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * 190f;

                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, p);
                float a = Mathf.Clamp01(1f - age / DamageMarkLife);
                Box(new Rect(p.x - 34f, p.y - 4f, 68f, 8f), new Color(1f, 0.15f, 0.1f, 0.85f * a));
                Box(new Rect(p.x - 18f, p.y - 12f, 36f, 6f), new Color(1f, 0.15f, 0.1f, 0.5f * a));
                GUI.matrix = old;
            }
        }

        CameraRig cameraRigRef;

        void DrawSensitivityToast(float W, float H)
        {
            if (cameraRigRef == null && cam != null) cameraRigRef = cam.GetComponent<CameraRig>();
            if (cameraRigRef == null || Time.unscaledTime > cameraRigRef.SensitivityToastUntil) return;
            Text(new Rect(0, H * 0.62f, W, 28f), $"MOUSE SENSITIVITY  {cameraRigRef.Sensitivity:0.00}      ( [  /  ] )", labelCenter, Accent);
        }


        // ---------------------------------------------------------------- minimap

        const float MapWorld = 124f;
        List<Rect> mapObstacles;

        void BuildMapObstacles()
        {
            mapObstacles = new List<Rect>();
            foreach (var bc in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                if (bc.gameObject.layer != GameLayers.Obstacle) continue;
                var b = bc.bounds;
                if (b.size.x > 100f || b.size.z > 100f) continue; // border walls
                mapObstacles.Add(new Rect(b.min.x, b.min.z, b.size.x, b.size.z));
            }
        }

        Vector2 MapPoint(Rect map, Vector3 world)
            => new Vector2(map.x + (world.x + MapWorld * 0.5f) / MapWorld * map.width,
                           map.y + (1f - (world.z + MapWorld * 0.5f) / MapWorld) * map.height);

        void MapDot(Rect map, Vector3 world, float size, Color c)
        {
            var p = MapPoint(map, world);
            Box(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), c);
        }

        void DrawMinimap()
        {
            if (mapObstacles == null) BuildMapObstacles();

            var map = new Rect(16, 64, 176, 176);
            Box(new Rect(map.x - 2, map.y - 2, map.width + 4, map.height + 4), new Color(0.25f, 0.85f, 1f, 0.5f));
            Box(map, new Color(0.05f, 0.07f, 0.09f, 0.82f));

            foreach (var r in mapObstacles)
            {
                var a = MapPoint(map, new Vector3(r.x, 0f, r.y + r.height));
                float w = r.width / MapWorld * map.width, h = r.height / MapWorld * map.height;
                Box(new Rect(a.x, a.y, Mathf.Max(1.5f, w), Mathf.Max(1.5f, h)), new Color(0.45f, 0.47f, 0.5f, 0.9f));
            }

            foreach (var t in TurretPost.All)
                if (t.IsActive) MapDot(map, t.transform.position, 5f, new Color(0.3f, 0.6f, 1f));

            if (Reactor.Instance != null)
                MapDot(map, Reactor.Instance.transform.position, 9f, new Color(0.2f, 1f, 1f));

            foreach (var e in SquadDirector.Enemies)
                if (e != null && !e.IsDead) MapDot(map, e.Position, 5f, e.Profile.Color);

            var p = PlayerController.Instance;
            if (p != null)
            {
                MapDot(map, p.transform.position, 7f, Color.white);
                var a = MapPoint(map, p.transform.position);
                var b = MapPoint(map, p.transform.position + p.transform.forward * 7f);
                for (int i = 1; i <= 4; i++)
                {
                    var q = Vector2.Lerp(a, b, i / 4f);
                    Box(new Rect(q.x - 1, q.y - 1, 2, 2), Color.white);
                }
            }
        }

        // ---------------------------------------------------------------- world markers

        void DrawWorldMarkers(float H)
        {
            if (cam == null) return;

            var list = SquadDirector.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.IsDead) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.Position + Vector3.up * 2.2f);
                if (sp.z < 0f) continue;
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale;

                Color rc = e.Profile.Color;
                Box(new Rect(x - 22, y - 2, 44, 6), new Color(0, 0, 0, 0.65f));
                Box(new Rect(x - 21, y - 1, 42 * e.Health.Fraction, 4), Color.Lerp(new Color(1f, 0.2f, 0.2f), new Color(0.4f, 1f, 0.4f), e.Health.Fraction));
                Box(new Rect(x - 36, y - 6, 12, 12), rc);
                Text(new Rect(x - 36, y - 8, 12, 14), e.Profile.Letter, labelMono, Color.black);

                if (AIDebug.ShowLabels && e.Current != null)
                {
                    string tgt = e.HasTarget ? e.Target.kind.ToString() : "-";
                    Text(new Rect(x - 60, y - 22, 120, 16), $"{e.Current.Name} → {tgt}", labelCenterSmall(), e.Current.DebugColor);
                }
            }

            // Selection ring marker
            if (selected != null && !selected.IsDead)
            {
                Vector3 sp = cam.WorldToScreenPoint(selected.Position + Vector3.up * 0.9f);
                if (sp.z > 0f)
                {
                    float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
                    Color c = Accent;
                    Box(new Rect(x - 20, y - 20, 40, 2), c); Box(new Rect(x - 20, y + 18, 40, 2), c);
                    Box(new Rect(x - 20, y - 20, 2, 40), c); Box(new Rect(x + 18, y - 20, 2, 40), c);
                }
            }
        }

        GUIStyle centerSmall;
        GUIStyle bigCenter;
        GUIStyle labelBigCenter()
        {
            if (bigCenter == null) bigCenter = new GUIStyle(labelBig) { alignment = TextAnchor.MiddleCenter };
            return bigCenter;
        }

        GUIStyle labelCenterSmall()
        {
            if (centerSmall == null) centerSmall = new GUIStyle(labelSmall) { alignment = TextAnchor.MiddleCenter };
            return centerSmall;
        }

        // ---------------------------------------------------------------- AI inspector

        const float RowH = 16f;

        void DrawInspector(float W, float H)
        {
            PickSelected();

            // Height follows the content (an action can have up to 6 considerations) so nothing is ever clipped.
            float panelH = 56f;
            if (selected != null && !selected.IsDead)
            {
                panelH = 24f + 22f + 18f + 15f + selected.TargetScores.Count * RowH + 6f + 15f + selected.Actions.Count * RowH + 6f + 10f;
                if (selected.Current != null) panelH += 15f + selected.Current.Considerations.Count * RowH;
            }
            var r = new Rect(W - 326, 64, 310, panelH);
            Box(r, Panel);
            Text(new Rect(r.x + 10, r.y + 4, 295, 20), CameraRig.IsThirdPerson ? "UTILITY AI INSPECTOR  (aim at an enemy)" : "UTILITY AI INSPECTOR  (hover an enemy)", labelMono, Accent);

            if (selected == null || selected.IsDead)
            {
                Text(new Rect(r.x + 10, r.y + 28, 260, 40), "No enemy selected.", labelSmall, new Color(1, 1, 1, 0.6f));
                return;
            }

            float y = r.y + 24;
            var e = selected;
            Text(new Rect(r.x + 10, y, 270, 18), $"{e.Profile.Name}   HP {Mathf.CeilToInt(e.Health.Current)}/{Mathf.CeilToInt(e.Health.maxHealth)}", label, e.Profile.Color + new Color(0.2f, 0.2f, 0.2f));
            y += 22;
            Text(new Rect(r.x + 10, y, 295, 16), $"Suppression {e.Suppression:0.00}   LOS {(e.HasLos ? "yes" : "no")}   Allies pinning {e.PinningAllies}", labelMono, Color.white);
            y += 18;

            // Target scores
            Text(new Rect(r.x + 10, y, 270, 16), "TARGET SELECTION", labelMono, Accent);
            y += 15;
            float maxScore = 0.01f;
            foreach (var kv in e.TargetScores) maxScore = Mathf.Max(maxScore, kv.Value);
            string current = e.HasTarget ? e.Target.kind.ToString() : "";
            foreach (var kv in e.TargetScores)
            {
                bool isCur = kv.Key == current;
                Text(new Rect(r.x + 10, y, 70, RowH), kv.Key, labelMono, isCur ? Color.white : new Color(1, 1, 1, 0.6f));
                Bar(new Rect(r.x + 80, y + 3, 170, 9), kv.Value / maxScore, isCur ? Accent : new Color(0.5f, 0.55f, 0.6f));
                y += RowH;
            }
            y += 6;

            // Action scores
            Text(new Rect(r.x + 10, y, 270, 16), "ACTION UTILITY", labelMono, Accent);
            y += 15;
            float maxAction = 0.01f;
            foreach (var a in e.Actions) maxAction = Mathf.Max(maxAction, a.LastScore);
            foreach (var a in e.Actions)
            {
                bool isCur = a == e.Current;
                Text(new Rect(r.x + 10, y, 70, RowH), a.Name, labelMono, isCur ? Color.white : new Color(1, 1, 1, 0.6f));
                Bar(new Rect(r.x + 80, y + 3, 140, 9), a.LastScore / maxAction, a.DebugColor * (isCur ? 1f : 0.55f));
                Text(new Rect(r.x + 226, y, 60, RowH), a.LastScore.ToString("0.00"), labelMono, Color.white);
                y += RowH;
            }
            y += 6;

            // Considerations of the active action
            if (e.Current != null)
            {
                Text(new Rect(r.x + 10, y, 270, 16), $"CONSIDERATIONS · {e.Current.Name}", labelMono, Accent);
                y += 15;
                foreach (var c in e.Current.Considerations)
                {
                    Text(new Rect(r.x + 10, y, 130, RowH), c.Name, labelMono, new Color(1, 1, 1, 0.75f));
                    Bar(new Rect(r.x + 140, y + 3, 80, 9), c.LastScore, new Color(0.9f, 0.8f, 0.3f));
                    Text(new Rect(r.x + 226, y, 80, RowH), $"{c.LastInput:0.00}→{c.LastScore:0.00}", labelMono, Color.white);
                    y += RowH;
                }
            }
        }

        void PickSelected()
        {
            if (DemoMode)
            {
                selected = DemoSelected != null && !DemoSelected.IsDead ? DemoSelected : null;
                return;
            }
            if (cam == null) return;
            Vector2 m = CameraRig.IsThirdPerson ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : (Vector2)Input.mousePosition;
            EnemyBrain best = null;
            float bestD = 70f;
            var list = SquadDirector.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.IsDead) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.Position + Vector3.up * 0.9f);
                if (sp.z < 0f) continue;
                float d = Vector2.Distance(new Vector2(sp.x, sp.y), m);
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best != null) selected = best;
            else if (selected != null && selected.IsDead) selected = null;
        }

        // ---------------------------------------------------------------- full screen states

        void DrawOverlay(float W, float H, string title, string sub, Color c)
        {
            Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
            Text(new Rect(0, H * 0.35f, W, 70), title, labelTitle, c);
            Text(new Rect(0, H * 0.35f + 70, W, 30), sub, labelCenter, Color.white);
        }

        void DrawEnd(float W, float H, GameManager gm, string title, Color c)
        {
            Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.62f));
            Text(new Rect(0, H * 0.28f, W, 70), title, labelTitle, c);
            Text(new Rect(0, H * 0.28f + 66, W, 30), gm.EndReason, labelCenter, Color.white);
            var wm = WaveManager.Instance;
            string stats = $"Waves reached: {(wm != null ? wm.CurrentWave : 0)}/{(wm != null ? wm.TotalWaves : 0)}     Kills: {gm.Kills}     Score: {gm.Score}     Time: {Mathf.FloorToInt(gm.TimePlayed / 60)}:{Mathf.FloorToInt(gm.TimePlayed % 60):00}";
            Text(new Rect(0, H * 0.28f + 104, W, 30), stats, labelCenter, new Color(1f, 0.9f, 0.4f));
            Text(new Rect(0, H * 0.28f + 150, W, 30), "Press R to restart", labelCenter, Accent);
        }

        void DrawStartScreen(float W, float H)
        {
            Box(new Rect(0, 0, W, H), new Color(0.02f, 0.03f, 0.05f, 0.5f));
            Text(new Rect(0, H * 0.10f, W, 60), "TACTICAL OUTPOST DEFENSE", labelTitle, Color.white);
            Text(new Rect(0, H * 0.10f + 58, W, 28), "INTELLIGENT ASSAULT AI  ·  Utility AI · Target Selection · Cover · Flanking", labelCenter, Accent);

            float cx = W * 0.5f;
            Text(new Rect(cx - 330, H * 0.30f, 300, 24), "CONTROLS", label, new Color(1f, 0.9f, 0.4f));
            string controls = "WASD  move  ·  Mouse  aim  ·  LMB  fire  ·  RMB  ADS\nV  camera  ·  Q  shoulder  ·  [ ]  sensitivity\nR  reload   ·   Shift  sprint (no firing)\nC / Ctrl  crouch (hide behind low cover!)\nHold E  repair reactor / turrets (scrap)\nF  build or rebuild turret (scrap)\nN  skip countdown   ·   ESC  pause\nF1 / F2 / F3  AI debug overlays";
            Text(new Rect(cx - 330, H * 0.30f + 28, 330, 220), controls, labelSmall, Color.white);

            Text(new Rect(cx + 20, H * 0.30f, 320, 24), "ENEMY ROLES", label, new Color(1f, 0.9f, 0.4f));
            float y = H * 0.30f + 30;
            DrawRoleLegend(cx + 20, ref y, EnemyRole.Assault, "pushes the objective, bursts, strafes");
            DrawRoleLegend(cx + 20, ref y, EnemyRole.Flanker, "swings round the side when allies pin you");
            DrawRoleLegend(cx + 20, ref y, EnemyRole.Sniper, "long-range overwatch from cover (laser warning)");
            DrawRoleLegend(cx + 20, ref y, EnemyRole.Heavy, "armoured breacher, hunts turrets & reactor");
            DrawRoleLegend(cx + 20, ref y, EnemyRole.Medic, "heals the wounded, retreats to cover");

            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
            Text(new Rect(0, H * 0.86f, W, 36), "Press ENTER or click to deploy", labelBigCenter(), new Color(1f, 1f, 1f, pulse));
        }

        void DrawRoleLegend(float x, ref float y, EnemyRole role, string desc)
        {
            var p = RoleProfile.Get(role);
            Box(new Rect(x, y + 2, 14, 14), p.Color);
            Text(new Rect(x + 22, y, 90, 20), p.Name, labelSmall, Color.white);
            Text(new Rect(x + 100, y, 300, 20), desc, labelSmall, new Color(1, 1, 1, 0.75f));
            y += 24;
        }
    }
}
