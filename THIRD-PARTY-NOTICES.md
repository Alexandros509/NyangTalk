# Third-party notices

NyangTalk's own code is MIT licensed (`LICENSE`). The release `NyangTalk.exe` also carries the following third-party
files, unmodified except `doorstop_config.ini` (two settings, see below). Versions, download sources and hashes: `third_party/SOURCES.md`. Full license texts:
`third_party/licenses/` (also embedded in the exe, shown under Settings → 업데이트·정보 → 라이선스·제3자 고지).

| Component | Files | License | Source |
|---|---|---|---|
| BepInEx 5.4.23.6 | `BepInEx/core/BepInEx*.dll`, `changelog.txt` | LGPL-2.1 | https://github.com/BepInEx/BepInEx (tag v5.4.23.6) |
| UnityDoorstop (part of the BepInEx release) | `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` | LGPL-2.1 | https://github.com/NeighTools/UnityDoorstop |
| HarmonyX (part of the BepInEx release) | `0Harmony*.dll`, `BepInEx.Harmony.dll`, `HarmonyXInterop.dll` | MIT | https://github.com/BepInEx/HarmonyX |
| Mono class libraries, full Windows build (Unity 2022.3.30 profile, used with Unity 6000.2.8) | `BepInEx/unstripped/mscorlib.dll`, `System*.dll`, `Mono.*.dll`, `Microsoft.CSharp.dll` | MIT (Mono `LICENSE`: runtime and class libraries under MIT) | https://github.com/BepInEx/UnityDataMiner/issues/3, https://github.com/user-attachments/files/17195456/2022.3.30_Win.zip |
| Mono netstandard facade for Unity 6000.2.8 | `BepInEx/unstripped/netstandard.dll` | MIT | https://unity.bepinex.dev/corlibs/6000.2.8.zip |
| Newtonsoft.Json 13.0.2 (full build) | `BepInEx/unstripped/Newtonsoft.Json.dll` | MIT | https://www.nuget.org/packages/Newtonsoft.Json/13.0.2 |
| MonoMod (part of the BepInEx release) | `MonoMod.*.dll` | MIT | https://github.com/MonoMod/MonoMod |
| Mono.Cecil (part of the BepInEx release) | `Mono.Cecil*.dll` | MIT | https://github.com/jbevain/cecil |
| Pretendard 1.3.9 | Regular, Bold | SIL OFL 1.1 | https://github.com/orioncactus/pretendard |
| Noto Sans KR (Noto Sans CJK 2.004) | Regular, Bold | SIL OFL 1.1 | https://github.com/notofonts/noto-cjk |
| SUIT 2.0.5 | Regular, Bold | SIL OFL 1.1 | https://github.com/sun-typeface/SUIT |

**LGPL-2.1 components.** BepInEx and UnityDoorstop are licensed under LGPL-2.1. NyangTalk ships them unmodified as
separate files (only two values in `doorstop_config.ini`, a configuration file, differ: `dll_search_path_override` is
set to `BepInEx\unstripped`, and the installer sets `ignore_disable_switch = true` when it writes the file to the game
folder). Their complete source code is attached to every NyangTalk release as
`BepInEx-5.4.23.6-src.zip` and `UnityDoorstop-4.6.0-src.zip` (the official source archives of tags `v5.4.23.6` and
`v4.6.0`, also kept in `third_party/sources/`).

The full class libraries in `BepInEx/unstripped` are there because the game ships stripped system libraries: the loader
is pointed at the full ones first, and the game's own `Managed` folder stays second in the search path.

The BepInEx files are installed into the game folder only when the user installs the connector and BepInEx is not
already there; their license texts go along into `BepInEx/plugins/NyangTalk/licenses/`. LGPL components are shipped as
separate, unmodified files; their complete source is available at the links above. The fonts are used only inside the
app window and are not installed into Windows.

Used at build time only, not distributed: the .NET Framework 4.8 compiler and reference assemblies (part of Windows) and
the Unity engine modules in the user's game folder (compile references of the game component).

The app icon (`assets/app.ico`) was provided by the user and is based on game artwork.
Rights in the underlying artwork remain with their respective owners; the icon is excluded from the MIT license
for NyangTalk's own code (see `docs/design-assets-licenses.md`).

Facts about the game (type and member names in `contract.json`) were read from the user's own game installation for
interoperability. Bongo Cat and its files belong to their owners; NyangTalk is unofficial and not affiliated with them.
