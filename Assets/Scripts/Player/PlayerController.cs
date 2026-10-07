using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Third-person (and optional top-down) tactical shooter controls.
    /// WASD move · mouse look · LMB fire · RMB aim down sights · Shift sprint · C/Ctrl crouch · R reload ·
    /// Q swap shoulder · V switch camera · hold E repair · F build turret.
    /// Feel: acceleration, sprint (no firing), ADS (slower, tighter spread, zoom), spread bloom, camera recoil,
    /// hit / kill markers, footsteps, camera shake when hurt.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(Health), typeof(Targetable))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        [Header("Movement")]
        public float moveSpeed = 5.2f;
        public float sprintMultiplier = 1.55f;
        public float crouchMultiplier = 0.5f;
        public float adsMultiplier = 0.55f;
        public float acceleration = 38f;
        public float deceleration = 48f;

        [Header("Weapon")]
        public float damage = 16f;
        public float fireRate = 8.5f;
        [Tooltip("Hip-fire spread when standing still (degrees).")] public float spreadDeg = 0.9f;
        public float moveSpreadDeg = 1.4f;
        public float bloomPerShot = 0.4f;
        public float maxBloomDeg = 3.2f;
        [Range(0.1f, 1f)] public float adsSpreadFactor = 0.35f;
        public float range = 75f;
        public int magSize = 30;
        public float reloadTime = 1.4f;

        [Header("Feel")]
        public float recoilPitch = 0.75f;
        public float recoilYaw = 0.3f;

        [Header("Interaction")]
        public float interactRange = 4.5f;
        public float repairRate = 28f;      // hp per second
        public float scrapPerHp = 0.2f;

        public HumanoidRig Rig { get; private set; }

        public Health Health { get; private set; }
        public Targetable Targetable { get; private set; }
        public int Ammo { get; private set; }
        public bool Reloading { get; private set; }
        public float ReloadProgress => Reloading ? Mathf.Clamp01((Time.time - reloadStart) / reloadTime) : 1f;
        public bool Crouched { get; private set; }
        public bool Sprinting { get; private set; }
        /// <summary>0..1 blend of "aiming down sights".</summary>
        public float AdsBlend { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public bool AimingAtEnemy { get; private set; }
        public float CurrentSpreadDeg { get; private set; }
        public float LastHitTime { get; private set; } = -9f;
        public float LastKillTime { get; private set; } = -9f;
        public string Prompt { get; private set; } = "";
        public Camera Cam { get; set; }

        CharacterController cc;
        float nextShot;
        float reloadStart;
        float repairBudget;
        float crouchBlend;
        float bloom;
        float stepDistance;
        Vector3 velocity;
        float lastShotTime = -9f;
        const float StandHeight = 1.8f;
        const float CrouchHeight = 1.0f;

        // ---- Scripted control (used by tests and presentation tooling; ignored unless Scripted is true) ----
        [System.NonSerialized] public bool Scripted;
        [System.NonSerialized] public Vector2 ScriptedMove;
        [System.NonSerialized] public Vector3 ScriptedAim;
        [System.NonSerialized] public bool ScriptedFire;
        [System.NonSerialized] public bool ScriptedCrouch;
        [System.NonSerialized] public bool ScriptedAds;

        bool FireHeld => Scripted ? ScriptedFire : Input.GetMouseButton(0);
        bool AdsHeld => Scripted ? ScriptedAds : Input.GetMouseButton(1);

        void Awake()
        {
            Instance = this;
            cc = GetComponent<CharacterController>();
            Health = GetComponent<Health>();
            Health.Setup(150f, Team.Defender);
            Targetable = GetComponent<Targetable>();
            Targetable.kind = TargetKind.Player;
            Targetable.aimHeight = 1.1f;
            Targetable.strategicValue = 0.55f;
            Targetable.threat = 0.85f;
            Ammo = magSize;
            cc.height = StandHeight;
            cc.center = new Vector3(0f, StandHeight * 0.5f, 0f);
            cc.radius = 0.4f;
            Health.Died += OnDied;
            Health.Damaged += OnHurt;

            var visual = new GameObject("Model").transform;
            visual.SetParent(transform, false);
            Rig = HumanoidRig.Build(visual, CharacterStyles.ForPlayer());
            GameLayers.SetLayerRecursive(visual.gameObject, GameLayers.Player);
        }

        void Start()
        {
            if (Cam == null) Cam = Camera.main;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var gm = GameManager.Instance;
            bool active = gm != null && gm.IsPlaying && !Health.IsDead;

            if (active)
            {
                HandleCrouch(dt);
                HandleMove(dt);
                HandleAim();
                HandleWeapon(dt);
                HandleInteraction(dt);
                Regenerate(dt);
            }
            else
            {
                AdsBlend = Mathf.MoveTowards(AdsBlend, 0f, dt * 9f);
                Sprinting = false;
            }

            AnimateBody(dt, active);
        }

        // ================================================================== animation

        void AnimateBody(float dt, bool active)
        {
            if (Rig == null) return;
            if (Health.IsDead)
            {
                Rig.Tick(dt, 0f, 0f, 0f);
                return;
            }

            Vector3 hv = new Vector3(cc.velocity.x, 0f, cc.velocity.z);
            float speed = active ? hv.magnitude : 0f;

            // Direction of travel relative to where the body faces: drives strafe / backpedal leg animation.
            float moveAngle = 0f;
            if (speed > 0.3f)
            {
                Vector3 local = Quaternion.Inverse(transform.rotation) * hv;
                moveAngle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            }

            float aimPitch = ThirdPersonView ? cameraRig.Pitch : 0f;
            bool raised = active && !Sprinting && (FireHeld || AdsBlend > 0.3f || Time.time - lastShotTime < 1.2f);
            Rig.Tick(dt, speed, Crouched ? 1f : 0f, raised ? 1f : 0.35f, moveAngle, aimPitch);
        }

        // ================================================================== movement

        void HandleCrouch(float dt)
        {
            Crouched = Scripted ? ScriptedCrouch : (Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl));
            crouchBlend = Mathf.MoveTowards(crouchBlend, Crouched ? 1f : 0f, dt * 8f);
            float h = Mathf.Lerp(StandHeight, CrouchHeight, crouchBlend);
            cc.height = h;
            cc.center = new Vector3(0f, h * 0.5f, 0f);
            Targetable.aimHeight = Mathf.Lerp(1.1f, 0.55f, crouchBlend);
        }

        void HandleMove(float dt)
        {
            Vector3 wish = Scripted
                ? new Vector3(ScriptedMove.x, 0f, ScriptedMove.y)
                : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            // Third person: W/A/S/D are relative to where the camera is looking.
            if (!Scripted && ThirdPersonView) wish = Quaternion.Euler(0f, cameraRig.Yaw, 0f) * wish;

            // Sprint only when running roughly forward; aiming down sights or crouching cancels it.
            Sprinting = !Scripted && Input.GetKey(KeyCode.LeftShift) && !Crouched && !AdsHeld &&
                        wish.sqrMagnitude > 0.01f && Vector3.Dot(wish.normalized, transform.forward) > 0.25f;

            float adsTarget = AdsHeld && !Sprinting ? 1f : 0f;
            AdsBlend = Mathf.MoveTowards(AdsBlend, adsTarget, dt * 9f);

            float speed = moveSpeed;
            if (Crouched) speed *= crouchMultiplier;
            if (Sprinting) speed *= sprintMultiplier;
            speed *= Mathf.Lerp(1f, adsMultiplier, AdsBlend);

            Vector3 target = wish * speed;
            float rate = target.sqrMagnitude > velocity.sqrMagnitude ? acceleration : deceleration;
            velocity = Vector3.MoveTowards(velocity, target, rate * dt);
            cc.SimpleMove(velocity);

            Footsteps(dt);
        }

        void Footsteps(float dt)
        {
            float horizontalSpeed = new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude;
            if (horizontalSpeed < 0.5f) return;

            stepDistance += horizontalSpeed * dt;
            float stride = Crouched ? 1.5f : Sprinting ? 2.7f : 2.1f;
            if (stepDistance < stride) return;

            stepDistance = 0f;
            Fx.Sound(SfxKind.Step, transform.position, Crouched ? 0.12f : Sprinting ? 0.34f : 0.24f, Random.Range(0.9f, 1.1f));
        }

        // ================================================================== aiming

        CameraRig cameraRig;

        /// <summary>True when the over-the-shoulder camera is active and available.</summary>
        bool ThirdPersonView
        {
            get
            {
                if (!CameraRig.IsThirdPerson) return false;
                if (cameraRig == null)
                {
                    if (Cam == null) Cam = Camera.main;
                    if (Cam != null) cameraRig = Cam.GetComponent<CameraRig>();
                }
                return cameraRig != null;
            }
        }

        const int AimMask = GameLayers.ObstacleMask | GameLayers.EnemyMask | GameLayers.GroundMask | GameLayers.StructureMask;

        void HandleAim()
        {
            Vector3 point;
            AimingAtEnemy = false;

            if (Scripted)
            {
                point = ScriptedAim;
            }
            else if (ThirdPersonView)
            {
                // Crosshair = centre of the screen. Fire towards whatever the camera ray hits.
                Ray ray = Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (Physics.Raycast(ray, out RaycastHit hit, 250f, AimMask, QueryTriggerInteraction.Ignore))
                {
                    point = hit.point;
                    if (hit.collider.gameObject.layer == GameLayers.Enemy)
                    {
                        point.y = Mathf.Clamp(point.y, 0.4f, 1.7f);
                        AimingAtEnemy = hit.distance < 70f;
                    }
                    if (Vector3.Distance(Muzzle, point) < 2.5f) point = ray.GetPoint(40f);  // never aim at our own feet / a wall in our face
                }
                else
                {
                    point = ray.GetPoint(120f);
                }
            }
            else
            {
                if (Cam == null) { Cam = Camera.main; if (Cam == null) return; }

                Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
                var plane = new Plane(Vector3.up, new Vector3(0f, 1.2f, 0f));
                point = transform.position + transform.forward * 10f;
                if (plane.Raycast(ray, out float enter)) point = ray.GetPoint(enter);

                // If the cursor is on an enemy, aim at the point of the body under it (lets you shoot over low cover).
                if (Physics.Raycast(ray, out RaycastHit hit, 300f, GameLayers.EnemyMask))
                {
                    point = hit.point;
                    point.y = Mathf.Clamp(point.y, 0.4f, 1.7f);
                    AimingAtEnemy = true;
                }
            }
            AimPoint = point;

            if (!Scripted && ThirdPersonView)
            {
                // The commander always faces where the camera looks.
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, cameraRig.Yaw, 0f), 22f * Time.deltaTime);
                return;
            }

            Vector3 flat = point - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.05f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 20f * Time.deltaTime);
        }

        Vector3 Muzzle => transform.position + Vector3.up * (Crouched ? 0.8f : 1.25f) + transform.forward * 0.7f;

        // ================================================================== weapon

        void HandleWeapon(float dt)
        {
            // Spread = base + movement + bloom from sustained fire, tightened by ADS and crouching.
            float moveFactor = Mathf.Clamp01(new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude / moveSpeed);
            float spread = spreadDeg + moveSpreadDeg * moveFactor + bloom;
            CurrentSpreadDeg = spread * Mathf.Lerp(1f, adsSpreadFactor, AdsBlend) * (Crouched ? 0.7f : 1f);
            bloom = Mathf.MoveTowards(bloom, 0f, 6f * dt);

            if (Reloading)
            {
                if (Time.time - reloadStart >= reloadTime)
                {
                    Reloading = false;
                    Ammo = magSize;
                }
                return;
            }

            if (!Scripted && Input.GetKeyDown(KeyCode.R) && Ammo < magSize) { StartReload(); return; }

            if (FireHeld && !Sprinting && Time.time >= nextShot)
            {
                if (Ammo <= 0) { StartReload(); return; }

                nextShot = Time.time + 1f / fireRate;
                Ammo--;
                var hitHealth = Ballistics.Fire(Muzzle, AimPoint, CurrentSpreadDeg, range, GameLayers.DefenderBulletMask,
                                                damage, gameObject, Team.Defender, new Color(0.5f, 1f, 1f), 1f);
                lastShotTime = Time.time;
                bloom = Mathf.Min(maxBloomDeg, bloom + bloomPerShot);

                Rig?.Kick();
                Fx.MuzzleFlash(Muzzle);
                Fx.Sound(SfxKind.Rifle, Muzzle, 0.6f, Random.Range(0.95f, 1.05f));

                // Camera kick: climbs while holding the trigger, recovers when you let go. Less when aiming down sights.
                if (ThirdPersonView)
                {
                    float steady = Mathf.Lerp(1f, 0.55f, AdsBlend) * (Crouched ? 0.8f : 1f);
                    cameraRig.AddRecoil(recoilPitch * steady * Random.Range(0.8f, 1.2f), Random.Range(-recoilYaw, recoilYaw) * steady);
                }

                if (hitHealth != null)
                {
                    LastHitTime = Time.time;
                    bool killed = hitHealth.IsDead;
                    if (killed) LastKillTime = Time.time;
                    if (Cam != null) Fx.Sound(SfxKind.Hit, Cam.transform.position, killed ? 0.8f : 0.4f, killed ? 0.7f : 1.7f);
                }

                if (Ammo <= 0) StartReload();
            }
        }

        void StartReload()
        {
            Reloading = true;
            reloadStart = Time.time;
            Fx.Sound(SfxKind.Reload, transform.position, 0.6f);
        }

        // ================================================================== interaction / status

        void HandleInteraction(float dt)
        {
            Prompt = "";

            // Nearest repairable structure.
            Health repairTarget = null;
            string repairName = "";
            float best = interactRange;

            var reactor = Reactor.Instance;
            if (reactor != null)
            {
                float d = Vector3.Distance(transform.position, reactor.transform.position) - 3f;
                if (d < best && reactor.Health.Fraction < 1f) { best = d; repairTarget = reactor.Health; repairName = "Reactor"; }
            }
            TurretPost nearestPost = null;
            float bestPost = interactRange;
            for (int i = 0; i < TurretPost.All.Count; i++)
            {
                var post = TurretPost.All[i];
                float d = Vector3.Distance(transform.position, post.transform.position);
                if (post.IsActive && post.Health.Fraction < 1f && d < best)
                {
                    best = d; repairTarget = post.Health; repairName = "Turret";
                }
                if (!post.IsActive && d < bestPost) { bestPost = d; nearestPost = post; }
            }

            var gm = GameManager.Instance;

            if (nearestPost != null)
            {
                Prompt = $"[F] {(nearestPost.Current == TurretPost.State.Destroyed ? "Rebuild" : "Build")} turret  ({nearestPost.Cost} scrap)";
                if (Input.GetKeyDown(KeyCode.F))
                {
                    if (gm.TrySpend(nearestPost.Cost)) nearestPost.Build();
                    else Prompt = "Not enough scrap!";
                }
            }

            if (repairTarget != null)
            {
                Prompt = (Prompt.Length > 0 ? Prompt + "   " : "") + $"[Hold E] Repair {repairName}";
                if (Input.GetKey(KeyCode.E) && gm.Scrap > 0)
                {
                    float hp = repairRate * dt;
                    repairTarget.Heal(hp);
                    repairBudget += hp * scrapPerHp;
                    while (repairBudget >= 1f)
                    {
                        repairBudget -= 1f;
                        if (!gm.TrySpend(1)) break;
                    }
                }
            }
        }

        void Regenerate(float dt)
        {
            if (Time.time - Health.LastDamageTime > 6f && Health.Fraction < 1f)
                Health.Heal(4f * dt);
        }

        public void RestockBetweenWaves()
        {
            Health.Heal(Health.maxHealth * 0.4f);
            Ammo = magSize;
            Reloading = false;
        }

        void OnHurt(Health h, float amount, GameObject source)
        {
            if (ThirdPersonView) cameraRig.Shake(Mathf.Clamp(amount / 22f, 0.2f, 1f));
        }

        void OnDied(Health h, GameObject src)
        {
            Fx.Burst(transform.position + Vector3.up, new Color(0.4f, 0.8f, 1f), 3f, 0.5f);
            GameManager.Instance?.Lose("Commander down.");
        }
    }
}
