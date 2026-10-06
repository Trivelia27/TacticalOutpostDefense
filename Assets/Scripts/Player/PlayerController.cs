using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Top-down tactical shooter controls.
    /// WASD move, mouse aim, LMB fire, R reload, Shift sprint, C / Ctrl crouch (hide behind low cover),
    /// hold E repair the nearest structure (costs scrap), F build / rebuild a turret.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(Health), typeof(Targetable))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        [Header("Movement")]
        public float moveSpeed = 6f;
        public float sprintMultiplier = 1.4f;
        public float crouchMultiplier = 0.5f;

        [Header("Weapon")]
        public float damage = 16f;
        public float fireRate = 8.5f;
        public float spreadDeg = 1.2f;
        public float range = 75f;
        public int magSize = 30;
        public float reloadTime = 1.4f;

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
        public Vector3 AimPoint { get; private set; }
        public string Prompt { get; private set; } = "";
        public Camera Cam { get; set; }

        CharacterController cc;
        float nextShot;
        float reloadStart;
        float repairBudget;
        float crouchBlend;
        const float StandHeight = 1.8f;
        const float CrouchHeight = 1.0f;

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

        float lastShotTime = -9f;

        void Update()
        {
            float dt = Time.deltaTime;
            var gm = GameManager.Instance;
            bool active = gm != null && gm.IsPlaying && !Health.IsDead;

            if (active)
            {
                HandleCrouch(dt);
                HandleMove();
                HandleAim();
                HandleWeapon();
                HandleInteraction(dt);
                Regenerate(dt);
            }

            AnimateBody(dt, active);
        }

        void AnimateBody(float dt, bool active)
        {
            if (Rig == null) return;
            if (Health.IsDead)
            {
                Rig.Tick(dt, 0f, 0f, 0f);
                return;
            }
            float speed = active ? new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude : 0f;
            bool aiming = active && (Input.GetMouseButton(0) || Time.time - lastShotTime < 1.2f);
            Rig.Tick(dt, speed, Crouched ? 1f : 0f, aiming ? 1f : 0.4f);
        }

        void HandleCrouch(float dt)
        {
            Crouched = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl);
            crouchBlend = Mathf.MoveTowards(crouchBlend, Crouched ? 1f : 0f, dt * 8f);
            float h = Mathf.Lerp(StandHeight, CrouchHeight, crouchBlend);
            cc.height = h;
            cc.center = new Vector3(0f, h * 0.5f, 0f);
            Targetable.aimHeight = Mathf.Lerp(1.1f, 0.55f, crouchBlend);
        }

        void HandleMove()
        {
            Vector3 move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 1f) move.Normalize();

            float speed = moveSpeed;
            if (Crouched) speed *= crouchMultiplier;
            else if (Input.GetKey(KeyCode.LeftShift)) speed *= sprintMultiplier;

            cc.SimpleMove(move * speed);
        }

        void HandleAim()
        {
            if (Cam == null) { Cam = Camera.main; if (Cam == null) return; }

            Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, 1.2f, 0f));
            Vector3 point = transform.position + transform.forward * 10f;
            if (plane.Raycast(ray, out float enter)) point = ray.GetPoint(enter);

            // If the cursor is on an enemy, aim at the point of the body under it (lets you shoot over low cover).
            if (Physics.Raycast(ray, out RaycastHit hit, 300f, GameLayers.EnemyMask))
            {
                point = hit.point;
                point.y = Mathf.Clamp(point.y, 0.4f, 1.7f);
            }
            AimPoint = point;

            Vector3 flat = point - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.05f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 20f * Time.deltaTime);
        }

        Vector3 Muzzle => transform.position + Vector3.up * (Crouched ? 0.8f : 1.25f) + transform.forward * 0.7f;

        void HandleWeapon()
        {
            if (Reloading)
            {
                if (Time.time - reloadStart >= reloadTime)
                {
                    Reloading = false;
                    Ammo = magSize;
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.R) && Ammo < magSize) { StartReload(); return; }

            if (Input.GetMouseButton(0) && Time.time >= nextShot)
            {
                if (Ammo <= 0) { StartReload(); return; }

                nextShot = Time.time + 1f / fireRate;
                Ammo--;
                Ballistics.Fire(Muzzle, AimPoint, spreadDeg * (Crouched ? 0.6f : 1f), range, GameLayers.DefenderBulletMask,
                                damage, gameObject, Team.Defender, new Color(0.5f, 1f, 1f), 1f);
                lastShotTime = Time.time;
                Rig?.Kick();
                Fx.MuzzleFlash(Muzzle);
                Fx.Sound(SfxKind.Rifle, Muzzle, 0.6f, Random.Range(0.95f, 1.05f));
                if (Ammo <= 0) StartReload();
            }
        }

        void StartReload()
        {
            Reloading = true;
            reloadStart = Time.time;
            Fx.Sound(SfxKind.Reload, transform.position, 0.6f);
        }

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

        void OnDied(Health h, GameObject src)
        {
            Fx.Burst(transform.position + Vector3.up, new Color(0.4f, 0.8f, 1f), 3f, 0.5f);
            GameManager.Instance?.Lose("Commander down.");
        }
    }
}
