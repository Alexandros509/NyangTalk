# Third-party files: where they came from

Downloaded 2026-11-19 with `gh release download` from the official GitHub releases, with the user's permission.

| Component | Version | Release asset | Source | License | SHA-256 of downloaded asset |
|---|---|---|---|---|---|
| BepInEx (Unity Mono, Windows x64) | 5.4.23.6 (published 2026-10-23) | `BepInEx_win_x64_5.4.23.6.zip` | https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6 | LGPL-2.1 | `6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755`, matches the digest GitHub publishes for the asset |
| Pretendard | 1.3.9 | `Pretendard-1.3.9.zip` → `public/static/alternative/Pretendard-{Regular,Bold}.ttf` | https://github.com/orioncactus/pretendard/releases/tag/v1.3.9 | SIL OFL 1.1 | `04be351a74d6bf7d60c480a3087e51d185485d35a52023142af1df19eb8c428a` (no published digest to compare) |
| SUIT | 2.0.5 | `SUIT-ttf.zip` → `SUIT-{Regular,Bold}.ttf` | https://github.com/sun-typeface/SUIT/releases/tag/v2.0.5 | SIL OFL 1.1 | `9e2ac954cb59f0e6cba0cb811b70877eedde9a9364be4cd17181e93878cf133d` (no published digest) |
| Noto Sans KR | Noto Sans CJK 2.004 | `17_NotoSansKR.zip` → `NotoSansKR-{Regular,Bold}.otf` | https://github.com/notofonts/noto-cjk/releases/tag/Sans2.004 | SIL OFL 1.1 | `ac7eeb4e2b0d41de8ff31b2d6e1e2a41caf253fd5cefb380bfa1f40f1747b612` (no published digest) |

Added 2026-11-20 with the user's permission (downloaded with `curl`, each into its own empty folder):

| Component | Version | Downloaded file | Source | License | SHA-256 of downloaded file | Other check |
|---|---|---|---|---|---|---|
| Mono class libraries, full build for Unity 6000.2.8 | 4.6.57.0 (file version) | `6000.2.8.zip`, 5,829,701 bytes, 15 DLLs → `BepInEx/payload/BepInEx/unstripped/` | https://unity.bepinex.dev/corlibs/6000.2.8.zip (BepInEx team) | MIT, text `licenses/Mono-class-libraries-LICENSE.txt` (Unity-Technologies/mono `unity-main` `LICENSE`, SHA-256 `844242559dbdb96cb9cbe103b06c2bc8a349f19ad0b89d5c3cf830a8914333f6`) | `afe4522a3be6d7faa58004bf222d491dca00ec8408e65647d669270ab139be2c` | DLLs are not code-signed ("(c) Various Mono authors"). The page publishes no hash to compare. The Mono LICENSE exceptions (RabbitMQ.Client, SharpZipLib, an Android TimeZoneInfo file) are not among these 15 DLLs (by name). |
| Newtonsoft.Json | 13.0.2 | `newtonsoft.json.13.0.2.nupkg`, 2,441,714 bytes → `lib/net45/Newtonsoft.Json.dll` → same folder | https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.2/ | MIT, text `licenses/Newtonsoft.Json-MIT.txt` (`LICENSE.md` from the package) | `112ca3b7f47bcbd743befcf949fb68ce4f9eff73ad9f7f1b39c2c61a1ebd3add` | nupkg SHA-512 equals the NuGet catalog `packageHash`; DLL Authenticode signature valid (Json.NET (.NET Foundation)). |
| BepInEx source (LGPL-2.1) | tag `v5.4.23.6` | `BepInEx-5.4.23.6-src.zip`, 133,713 bytes → `third_party/sources/` | https://github.com/BepInEx/BepInEx/archive/refs/tags/v5.4.23.6.zip | LGPL-2.1 | `ced5fec76c315e54a775c56f3f04a2a0e62b5c3fb16b2d98f00a2a550ba7197e` | GitHub tag archive; git submodules (HarmonyX etc., MIT) are not part of it. |
| UnityDoorstop source (LGPL-2.1) | tag `v4.6.0` (= `.doorstop_version` 4.6.0) | `UnityDoorstop-4.6.0-src.zip`, 111,849 bytes → `third_party/sources/` | https://github.com/NeighTools/UnityDoorstop/archive/refs/tags/v4.6.0.zip | LGPL-2.1 | `95a3d62f5cb2fc792f1dfc61e6b09f78e685e0966401e6f9e6fa58540249e0ea` | GitHub tag archive. |

Changed by NyangTalk: `BepInEx/payload/doorstop_config.ini`, only `dll_search_path_override = BepInEx\unstripped`
(was empty). Doorstop 4.6.0 resolves the relative path with `GetFullPathName` against the working directory, like its
default `target_assembly` (`src/windows/config.c`, `src/bootstrap.c` in the source archive). Line endings kept (CRLF).

Changed at install time (0.1.8, connector 0.1.5): the installer writes `doorstop_config.ini` with
`ignore_disable_switch = true` (`Payload.Prepare` in `src/Adapters/GameLink/Installer.cs`); the file kept here and
embedded in the exe stays as listed in `BepInEx/hashes.txt`. Reason: Doorstop puts `DOORSTOP_DISABLE=TRUE` into the
game's environment; when the game itself starts Steam, Steam inherits it and every later game start from that Steam
skipped BepInEx (observed 2026-11-26).

## Windows runtime correction (2026-11-23)

The original `6000.2.8.zip` corlib profile above caused `DllNotFoundException: System.Native` in the actual Windows
game before BepInEx could create its log. Managed type/method compatibility alone did not detect this platform error.
The 14 class libraries other than `netstandard.dll` were replaced with the unmodified Windows profile supplied locally
by the user, published at https://github.com/BepInEx/UnityDataMiner/issues/3 as
https://github.com/user-attachments/files/17195456/2022.3.30_Win.zip (Unity 2022.3.30 Windows profile).
The `netstandard.dll` facade from the earlier archive and the verified Newtonsoft.Json 13.0.2 package are retained.
Exact current DLL hashes are in `BepInEx/hashes.txt`; the source archive was later downloaded directly from the URL above and SHA-256 verified as
`a140e6a379f784efe4c2fbd5b7d2a8c2b5ee1eaf2c4a1e829771f4274bf9f61d`. All 14 DLLs were byte-identical
to that archive and were recopied from it, independently of the earlier application folder. Licenses remain Mono MIT and Newtonsoft.Json MIT as listed above.

With this payload the installed Unity 6000.2.8 game started, BepInEx 5.4.23.6 loaded NyangTalk GameLink, its chat hooks
registered, and all six capability areas reported ready. The new distribution check rejects the Unix `Interop+Sys`
profile and requires Windows `Interop+Kernel32` support. Actual room delivery and chest rewards were not exercised.

BepInEx bundles these libraries, shipped unchanged in `BepInEx/payload` (XML doc files left out):

| Library | License text | Source |
|---|---|---|
| UnityDoorstop (`winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`) | `licenses/UnityDoorstop-LGPL-2.1.txt` | https://github.com/NeighTools/UnityDoorstop |
| HarmonyX (`0Harmony.dll`, `0Harmony20.dll`, `BepInEx.Harmony.dll`, `HarmonyXInterop.dll`) | `licenses/HarmonyX-MIT.txt` | https://github.com/BepInEx/HarmonyX |
| MonoMod (`MonoMod.*.dll`) | `licenses/MonoMod-MIT.txt` | https://github.com/MonoMod/MonoMod |
| Mono.Cecil (`Mono.Cecil*.dll`) | `licenses/Mono.Cecil-MIT.txt` | https://github.com/jbevain/cecil |

License texts were taken from each repository's license file through the GitHub API on the same day.

LGPL-2.1 source offer: NyangTalk ships BepInEx and UnityDoorstop unmodified as separate files. Their complete source is attached to every release by the release tool (`ReleaseBuilder.LgplSources`; the release stops if an archive is missing or its hash differs) and kept in `third_party/sources/`.

The `BepInEx/payload` file hashes are checked by `tests/StructureDistTests.cs` against `third_party/BepInEx/hashes.txt`.
