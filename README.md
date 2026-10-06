# Tactical Outpost Defense — Intelligent Assault AI

Game top-down tactical defense untuk **Unity 6 (6000.0.83f1)**, **URP** (SSAO, bloom, ACES tonemapping, SMAA), input lama (`UnityEngine.Input`).
Pemain mempertahankan **reactor** dari 6 wave musuh. Setiap musuh punya **role** dan perilaku taktis sendiri yang
digerakkan oleh **Utility AI**, **target selection**, **cover**, dan **flanking**.

Environment: **Desert Military Outpost** (compound berdinding dengan 4 gerbang, sandbag, crate, barrier, bangunan, tenda).
Seluruh aset dibuat secara prosedural → tidak ada dependensi aset eksternal:
* **Karakter** humanoid articulated (`Characters/HumanoidRig.cs`): helm, rompi, perlengkapan, senjata per role, animasi jalan/jongkok/bidik/recoil,
  dan two-bone IK agar kedua tangan selalu memegang senjata. Tinggi jongkok (≤ 1.28 m) selalu di bawah cover 1.3 m (diverifikasi test).
* **Tekstur & normal map** pasir/beton/kayu/aspal/kain dibuat oleh `ProceduralTextures.cs`; SFX dan ambience dibuat di `SfxLibrary.cs`.
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
| `Shift` | Sprint |
| `C` / `Ctrl` | Jongkok (bersembunyi di balik cover rendah – musuh kehilangan line of sight) |
| Tahan `E` | Perbaiki reactor / turret terdekat (memakai scrap) |
| `F` | Bangun / bangun ulang turret di slot kosong (scrap) |
| `N` | Lewati countdown wave |
| `ESC` / `P` | Pause |
| `F1` / `F2` / `F3` | Debug AI: label aksi · garis target · **Utility AI Inspector** |
| `R` (saat menang/kalah) | Restart |

Arahkan mouse ke seorang musuh untuk melihat **Utility AI Inspector** (skor target, skor tiap aksi, nilai tiap consideration).

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
EnemyBrain  ──every 0.3s──►  TargetSelector.Select()     pilih target (weighted scoring)
   │                         perception: LOS, jarak, cover terbaik, rencana flank, sekutu terluka
   │                         UtilityAction.Evaluate()  ×6  →  pilih aksi skor tertinggi (+hysteresis)
   └──every frame──────────►  action.Tick()   (gerak NavMesh, menembak, crouch/peek)
```

### 1. Utility AI (`AI/Utility`)
* `Consideration` = *input ternormalisasi (0‒1)* → `ResponseCurve` (Linear / Polynomial / Logistic) → skor 0‒1.
* `UtilityAction.Evaluate()` = **perkalian semua consideration** + *compensation factor* (agar aksi dengan banyak
  consideration tidak dirugikan) × **bobot role** (`RoleProfile.WAdvance/WAttack/WCover/WFlank/WRetreat/WSupport`).
* Aksi: `Advance`, `Attack`, `Cover`, `Flank`, `Retreat`, `Support`.
* Stabilitas: *commit bonus* ×1.15 untuk aksi aktif, durasi komit minimum 1 s, dan aksi yang selesai (`IsFinished`)
  boleh langsung diganti. Aksi baru juga boleh memotong bila skornya > 1.6× aksi saat ini.
* **Suppression** (0‒1): naik saat terkena tembakan atau tembakan pemain lewat dekat musuh, turun 0.25/s →
  memicu cover/retreat, menurunkan keinginan menyerang.

### 2. Target selection (`AI/TargetSelector.cs`)
Kandidat: Player, Reactor, setiap Turret aktif.
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

## Struktur project

```
Assets/
  Scenes/Outpost_Desert.unity        scene hasil generator
  Materials/                          material hasil generator
  Scripts/
    Core/        GameManager, WaveManager, Health, Targetable, Ballistics, FxManager, SfxLibrary, OutpostLevel
    AI/          EnemyBrain, RoleProfile, TargetSelector, FlankPlanner, SquadDirector, EnemyWeapon, EnemyVisual, EnemyFactory
    AI/Utility/  ResponseCurve, Consideration, UtilityAction + 6 aksi
    Environment/ CoverObject, CoverManager
    Player/      PlayerController, CameraRig
    Structures/  Reactor, TurretPost
    UI/          HUD (IMGUI, termasuk AI Inspector)
    Editor/      OutpostSceneBuilder, LayerSetup
  Tests/PlayMode/ OutpostPlayModeTests    simulasi headless AI
```

Tuning cepat: semua angka role ada di `RoleProfile.cs`, komposisi wave di `WaveManager.DefaultWaves()`,
HP reactor di `Reactor.maxIntegrity`, bobot target di `TargetSelector`.

## Verifikasi (tanpa membuka Editor)

```
Unity.exe -batchmode -nographics -projectPath <folder> -runTests -testPlatform PlayMode -testResults results.xml -logFile test.log
```
Test memuat scene, memeriksa NavMesh/cover/jalur spawn→reactor, lalu mensimulasikan wave 5 (semua role) dan mencatat
waktu yang dihabiskan tiap role pada tiap aksi/target.
