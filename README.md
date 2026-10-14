# Tactical Outpost Defense, Intelligent Assault AI

Game tactical defense (kamera third-person, tampilan atas opsional dengan `V`) untuk **Unity 6 (6000.0.83f1)**, **URP** (SSAO, bloom, ACES tonemapping, SMAA), input lama (`UnityEngine.Input`).
Pemain mempertahankan **reactor** dari 6 wave musuh. Setiap musuh punya **role** dan perilaku taktis sendiri yang
digerakkan oleh **Utility AI**, **perception + memory**, **target selection**, **cover**, **flanking**, dan **koordinasi squad**.

Environment: **Desert Military Outpost** (compound berdinding dengan 4 gerbang, sandbag, crate, barrier, bangunan, tenda).
Gaya visual: **stylized low poly** yang konsisten. Aset 3D berasal dari **Quaternius (CC0)** lewat Poly Pizza dan diimpor dengan paket
`com.unity.cloud.gltfast` (daftar lengkap dan lisensi di `Assets/Art/CREDITS.txt`):
* **Karakter** (`Assets/Resources/Art/Characters`): 3 model beranimasi dari satu keluarga (rig dan klip sama): SWAT, Soldier, dan Operator.
  Role dibedakan lewat warna seragam, postur, dan senjata: Assault (AK, warna gurun), Flanker (SMG, cokelat), Sniper (senapan runduk, hijau zaitun),
  Heavy (minigun, lebih besar), Medic (SMG, abu-hijau terang). `Characters/ModelRig.cs` memutar klip lewat Playables:
  * locomotion idle/walk/run/strafe/mundur dengan **satu fase langkah bersama** dan kecepatan putar yang disetel dari data klip (kaki yang menapak
    bergerak sama cepat dengan badan, jadi tidak meluncur), arah dicampur hanya antar klip bertetangga;
  * **penguncian pinggul ke tanah** (kaki terendah selalu menapak, tidak melayang atau tenggelam);
  * pose senjata di layer tubuh atas, recoil, animasi mati, **jongkok** prosedural, dan **IK tangan kiri** ke genggaman depan senjata.
  Senjata (AK, SMG, sniper, minigun) dipasang otomatis: arah laras dideteksi dari bentuk mesh. Tinggi jongkok (maks. 1.3 m) selalu di bawah cover.
  Tes `Feet_StayPlantedWhenMovingInAnyDirection` mengukur kecepatan kaki yang menapak terhadap kecepatan gerak di 3 kecepatan dan 4 arah.
* **Prop** (`Assets/Art/Props`): sandbag, crate, barel, barikade, tenda, container (disusun menjadi bangunan), menara jaga, lampu jalan, radar,
  turret, tank, truk lapis baja, batu, kaktus. `OutpostSceneBuilder` menaruhnya di atas collider tak terlihat, sehingga cover, NavMesh, dan
  logika AI tidak berubah.
* `Core/ModelMaterials.cs` mengubah material glTF menjadi URP/Lit (tekstur dan warna dasar dipertahankan) agar tidak tampil hitam atau pink.
* **Dibuat sendiri:** tanah, jalan, dinding compound, reactor, efek partikel, tekstur pasir/beton/aspal (`ProceduralTextures.cs`), SFX dan
  ambience (`SfxLibrary.cs`). `HumanoidRig` (karakter prosedural) tersisa sebagai cadangan bila model tidak ditemukan.
* **UI**: HUD IMGUI + minimap + AI Inspector; kamera sinematik di layar judul.

## Cara menjalankan

1. Buka folder ini lewat Unity Hub (Unity **6000.0.83f1**).
2. Buka scene `Assets/Scenes/Outpost_Desert.unity` lalu tekan **Play**.
   (Scene bisa dibangun ulang kapan saja: menu **Tools ▸ Tactical Outpost ▸ Build Outpost Scene**.)

### Kontrol

| Input | Fungsi |
|---|---|
| `WASD` | Gerak |
| Mouse / `LMB` | Bidik / tembak |
| `R` | Reload |
| `RMB` | Bidik (aim down sights) |
| `Shift` | Sprint |
| `C` / `Ctrl` | Jongkok (bersembunyi di balik cover rendah – musuh kehilangan line of sight) |
| Tahan `E` | Perbaiki reactor / turret terdekat (memakai scrap) |
| `F` | Bangun / bangun ulang turret di slot kosong (scrap) |
| `V` / `Q` / `[` `]` | Ganti kamera (third-person / atas) · tukar bahu · sensitivitas mouse |
| `N` | Lewati countdown wave |
| `ESC` / `P` | Pause |
| `F1` | **AI Debug Mode** on/off (label aksi, inspector, overlay) |
| `F2` / `F3` / `F4` | Garis target · **Utility AI Inspector** · overlay persepsi (FOV, radius, raycast, path, last known position, cover, flank) |
| `F5` / `F6` / `F7` | **AI test lab**: munculkan satu musuh uji di depan pemain (role bergantian; `Shift`+`F5` = 46 m, di luar radius deteksi) · hapus semua musuh · tahan musuh di tempat (tetap mengindra dan berpikir, tidak bergerak/menembak) |
| `R` (saat menang/kalah) | Restart |

Bidik seorang musuh (atau arahkan mouse di tampilan atas) untuk melihat **Utility AI Inspector**: state, target, *can see*, jarak, last known position, destination/path, skor target, skor tiap aksi, dan nilai tiap consideration.

## Role musuh

| Role | Karakter | Perilaku utama (bobot Utility) |
|---|---|---|
| **Assault** (A) | Seimbang, tembakan burst | Mendorong ke objective, strafe saat menembak, sesekali pakai cover |
| **Flanker** (F) | Cepat, HP rendah, SMG | `Flank` bobot tertinggi: memutar ke sisi/belakang target saat rekan menekan dari depan |
| **Sniper** (S) | Jarak jauh, tembakan terkunci (laser merah = peringatan) | Selalu mencari cover dengan peek-shot; memprioritaskan Player & Turret |
| **Heavy** (H) | HP besar, damage ×2.2 ke struktur | Maju terus, hampir tidak pernah cover/retreat; memburu Turret & Reactor |
| **Medic** (M) | Menyembuhkan sekutu | Aksi `Support` (heal beam), mengikuti squad, berlindung saat ditekan |

## Arsitektur AI (`Assets/Scripts/AI`)

```
Environment / Player
      │  sight (jarak + FOV + raycast), close sense, hearing (tembakan), damage awareness
      ▼
EnemySenses  ─►  memory per target: last known position · last seen time · source (Seen/Heard/Alert/Hit)
      │                                  ▲ shared alert ke sekutu dalam 30 m
      ▼  (tiap 0.3 s)
TargetSelector.Select()   pilih target dari yang DIKETAHUI (weighted scoring)
      ▼
UtilityAction.Evaluate() ×7  →  aksi skor tertinggi (+hysteresis):  Advance · Attack · Cover · Flank · Retreat · Support · Search
      ▼  (tiap frame)
action.Tick()  →  Movement/Steering (NavMesh path, Seek, Arrive, Separation, obstacle avoidance) + menembak, crouch/peek
```

### 0. Perception & Memory (`AI/Senses.cs`)
* **Perception** (per role di `RoleProfile`): *detection radius* (`SightRange` 36–60 m), *field of view* (`FovDeg` 90–150°),
  *line of sight* (raycast mata→target terhadap layer Obstacle), *close sense* 360° dalam 6 m, dan **hearing** (setiap tembakan
  pemain/turret didengar dalam `HearingRange` 30–45 m dengan posisi yang tidak presisi). Target yang sudah terlihat tetap dilacak
  walau keluar FOV selama masih dalam jangkauan dan LOS.
* **Memory** (`TargetMemory`): *last known position*, *last seen time*, *source* (Seen / Heard / Alert / Hit / Objective).
  Memori dilupakan 10 s setelah informasi terakhir. Reactor selalu diketahui (objective yang di-briefing ke semua penyerang).
* **Shared alert**: apa yang dilihat satu musuh dibagikan ke sekutu dalam 30 m (sumber `Alert`). Terkena tembakan juga
  memberi tahu musuh arah penembaknya (sumber `Hit`).
* Target selection, cover, flank, dan gerak memakai posisi yang **diyakini** (`EnemyBrain.TargetPos`): posisi asli saat terlihat,
  last known position saat tidak terlihat — musuh tidak "melihat menembus dinding".

### 1. Utility AI (`AI/Utility`)
* `Consideration` = *input ternormalisasi (0‒1)* → `ResponseCurve` (Linear / Polynomial / Logistic) → skor 0‒1.
* `UtilityAction.Evaluate()` = **perkalian semua consideration** + *compensation factor* (agar aksi dengan banyak
  consideration tidak dirugikan) × **bobot role** (`RoleProfile.WAdvance/WAttack/WCover/WFlank/WRetreat/WSupport`).
* Aksi: `Advance`, `Attack`, `Cover`, `Flank`, `Retreat`, `Support`, **`Search`**.
* **Search** (`AI/Utility/SearchAction.cs`): target pemain/turret diketahui tetapi hilang dari pandangan → menuju last known position,
  menyapu pandangan kiri-kanan, memeriksa 3 titik di sekitarnya, lalu melupakan target dan kembali ke objective. Melihat target lagi
  langsung menghentikan pencarian (skor Attack/Cover mengambil alih).
* Stabilitas: *commit bonus* ×1.15 untuk aksi aktif, durasi komit minimum 1 s, dan aksi yang selesai (`IsFinished`)
  boleh langsung diganti. Aksi baru juga boleh memotong bila skornya > 1.6× aksi saat ini.
* **Suppression** (0‒1): naik saat terkena tembakan atau tembakan pemain lewat dekat musuh, turun 0.25/s →
  memicu cover/retreat, menurunkan keinginan menyerang.

### 2. Target selection (`AI/TargetSelector.cs`)
Kandidat: target yang **diketahui** musuh itu (punya memori): Reactor selalu; Player dan Turret setelah terlihat, terdengar, dilaporkan sekutu, atau menembaknya.
`skor = (0.30·kedekatan + 0.15·visibilitas + 0.25·ancaman + 0.10·kerentanan + 0.20·nilai objektif) × preferensi role × penalti kerumunan × stickiness(1.2)`
* Ancaman bertambah bila target itu baru menembak musuh ("balas dendam").
* Penalti kerumunan menyebar tembakan agar tidak semua menumpuk ke satu target non-objective.
* Preferensi per role (mis. Heavy×1.6 ke Turret, Sniper×1.3 ke Player, Flanker×1.25 ke Player).
* Cooldown pergantian target 1.5 s agar tidak plin-plan.

### 3. Cover (`Environment/CoverManager.cs`, `AI/Utility/CoverAction.cs`)
* Saat level mulai, titik cover dibuat di sekeliling tiap `CoverObject` dan divalidasi ke NavMesh.
* Cover rendah (1.3 m): unit **jongkok** → tersembunyi (collider & line of sight), **berdiri untuk peek & menembak** lalu jongkok lagi.
* Kualitas cover = terlindungi dari target (2 linecast: kaki & badan) × jarak × kecocokan jarak tembak × *peek-shot valid*.
* Satu titik hanya boleh diklaim satu unit; cover dibuang bila target sudah bisa melihatnya (terkena flank).
* Pemain juga bisa jongkok di balik cover — musuh kehilangan LOS dan harus bereaksi (flank!).

### 4. Flanking (`AI/FlankPlanner.cs`, `AI/Utility/FlankAction.cs`)
* "Sisi depan" serangan = arah rata-rata sekutu yang sudah menembaki target (`SquadDirector.FrontDirection`).
* Kandidat posisi pada sudut ±55°…±130° dari sisi depan: harus punya LOS ke target, divalidasi NavMesh path,
  diberi skor (sudut flank, panjang rute, jarak rute dari garis tembak, jarak dari klaim flanker lain).
* Bila rute lurus lewat dekat target, ditambah **waypoint busur** supaya memutar lebar.
* `Flank` baru bernilai tinggi saat **ada sekutu yang menekan** (`AlliesPinning`) dan flank belum dilakukan baru-baru ini.

### 5. Squad coordination (`AI/SquadDirector.cs`)
Blackboard bersama: hitung sekutu yang menembak target, arah "depan" serangan, klaim posisi flank, medic terdekat, sekutu terluka.

### 6. Movement, steering, navigation
Navigasi memakai **Unity NavMesh** (dibangun saat level mulai, `NavMeshAgent`). Di atasnya `EnemyBrain.ApplySteering`:
**Seek** (mengikuti path ke tujuan), **Arrive** (melambat halus dalam 3.5 m dari tujuan), **Separation** (menjauhi sekutu yang terlalu dekat)
dan *obstacle avoidance* bawaan agent. Label steering tampil di inspector.

### 7. AI Debug Mode (`F1`)
* **Inspector** (`F3`): State, Target, Steering, *Can see* (YES/NO + alasan: di luar jangkauan / di luar FOV / terhalang), jarak ke Player,
  *Last known position* + umur + sumbernya, *Destination* dan status *Path*; lalu skor Target Selection, skor tiap aksi (Utility), dan consideration aksi aktif.
* **Overlay dunia** (`F4`, `AI/AIDebugDraw.cs`, untuk musuh yang dibidik): *field of view*, *detection radius* (close sense) dan radius dengar,
  *raycast* ke target (hijau = bersih, merah = terhalang), *path* dan *destination*, *last known position*, *cover point*, *flank position*.
  Digambar dengan LineRenderer sehingga terlihat di Game view dan build; Scene view memakai Gizmos yang sama.
* `F2` garis dari setiap musuh ke target yang diyakininya (warna = aksi). **AI test lab**: `F5` memunculkan satu musuh uji di depan pemain,
  `F6` menghapusnya, `F7` menahan musuh di tempat — dipakai untuk mendemokan perception (di luar radius / FOV / balik obstacle), memory, dan search tanpa menunggu wave.

## Pemetaan requirement UTS

| Requirement | Di project ini |
|---|---|
| Perception | Jarak, FOV, raycast LOS, close sense, hearing, damage awareness (`AI/Senses.cs`) |
| Memory | Last known position, last seen time, source, shared alert, expiry 10 s |
| Movement / Steering | Seek, Arrive, Separation, obstacle avoidance (`EnemyBrain.ApplySteering`) |
| Navigation | Unity NavMesh runtime + `NavMeshAgent` |
| Decision making | Utility AI, 7 aksi, response curve, hysteresis |
| Tactical behavior | Cover (peek), flanking, target selection, retreat, shared alert, role assignment (5 role), koordinasi squad |
| Win / Lose | Menang: wave 6 selesai. Kalah: reactor hancur atau pemain mati |
| AI Debug Mode | `F1`–`F6` (lihat di atas) |
| Pengujian | 15 PlayMode + 2 EditMode test, termasuk perception, memory, hearing, shared alert, debug overlay, pemeriksaan aset, dan kaki yang tidak meluncur |
| Aset non-default | Karakter beranimasi, senjata, dan prop berupa model CC0 (Quaternius), lihat `Assets/Art/CREDITS.txt` |

## Struktur project

```
Assets/
  Scenes/Outpost_Desert.unity        scene hasil generator
  Materials/                          material hasil generator
  Art/                                prop 3D (Props/*.glb), material hasil konversi, CREDITS.txt (lisensi)
  Resources/Art/                      karakter dan senjata (dimuat saat runtime)
  Scripts/
    Core/        GameManager, WaveManager, Health, Targetable, Ballistics, FxManager, SfxLibrary, OutpostLevel, ModelMaterials
    AI/          EnemyBrain, Senses (perception + memory), RoleProfile, TargetSelector, FlankPlanner, SquadDirector, EnemyWeapon,
                 EnemyVisual, EnemyFactory, AIDebugDraw, AIDebugTools
    AI/Utility/  ResponseCurve, Consideration, UtilityAction + 7 aksi (Advance, Attack, Cover, Flank, Retreat, Support, Search)
    Environment/ CoverObject, CoverManager
    Characters/  CharacterRig, ModelRig (animasi model), CharacterCatalog (role -> model), HumanoidRig (cadangan), CharacterStyles
    Player/      PlayerController, CameraRig
    Structures/  Reactor, TurretPost
    UI/          HUD (IMGUI, termasuk AI Inspector)
    Editor/      OutpostSceneBuilder, LayerSetup
  Tests/PlayMode/ OutpostPlayModeTests (level + simulasi AI) · PerceptionMemoryTests · ThirdPersonTests · AssetTests · ScreenshotTests
  Tests/EditMode/ SceneAssetTests (semua cover dan mayoritas prop adalah model impor, material URP)
```

Tuning cepat: semua angka role ada di `RoleProfile.cs`, komposisi wave di `WaveManager.DefaultWaves()`,
HP reactor di `Reactor.maxIntegrity`, bobot target di `TargetSelector`.

## Verifikasi (tanpa membuka Editor)

```
Unity.exe -batchmode -nographics -projectPath <folder> -runTests -testPlatform PlayMode -testResults results.xml -logFile test.log
```
Test memuat scene, memeriksa NavMesh/cover/jalur spawn→reactor, mensimulasikan wave 5 (semua role) dan mencatat waktu tiap role pada tiap aksi/target,
serta menguji satu per satu: jarak, FOV, line of sight, close sense, hearing, last known position → Search → lupa, shared alert, dan overlay debug.

## Rencana Pengembangan

Pengembangan selanjutnya berfokus pada tuning balance lewat playtest, koordinasi squad yang lebih dalam, serta penggantian reactor dan dinding dengan model 3D dan penambahan environment baru.
