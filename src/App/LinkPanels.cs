using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NyangTalk.Adapters.GameLink;
using NyangTalk.Adapters.Metadata;
using NyangTalk.Adapters.Storage;
using NyangTalk.Adapters.Update;
using NyangTalk.App.Controls;
using NyangTalk.Application;
using NyangTalk.Domain.Automation;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.Preferences;
using NyangTalk.Shared;
using LinkState = NyangTalk.Domain.GameLink.LinkState;

namespace NyangTalk.App
{
    /// <summary>Game link, automation and about sections of the settings window, and the startup game check.</summary>
    public sealed class LinkServices
    {
        private readonly AppHost _host;
        internal readonly MainForm _form;
        public readonly GameLinkService Check;
        public readonly Installer Installer;
        public readonly ChestPolicy Chest = new ChestPolicy();
        public readonly GameLocationService Location;
        public string Status = "";
        /// <summary>The game process for "다시 설치" (closing and restarting it); a fake in checks.</summary>
        public IGameControl Game { get { return _game; } set { _game = value; _gameRunningAt = DateTime.MinValue; } }
        private IGameControl _game;
        /// <summary>The start-up list and the watcher start (95-SPEC 2.1); fakes in checks.</summary>
        public IRunKey RunKey = new RealRunKey();
        public Action<string> StartWatcher = fixedExe =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fixedExe, AutoStart.WatchArg) { UseShellExecute = false }); }
            catch (System.ComponentModel.Win32Exception) { }
        };
        /// <summary>"다른 모드 잠시 끄기 / 다시 켜기" (2026-11-20 user request).</summary>
        public ModParking Parking;
        /// <summary>The question before moving another program's files; replaced in checks.</summary>

        private readonly NoticeDeduper _notices = new NoticeDeduper();

        public LinkServices(AppHost host, MainForm form)
        {
            _host = host; _form = form;
            Check = new GameLinkService(host.Docs, ContractJson(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            Installer = new Installer(host.Docs, Path.Combine(host.DataRoot, "install"));
            Installer.Precheck = g => GameCheck.Run(g, DoorstopOverride(g), ContractJson(), null);
            ConfirmRestart = owner => MessageBox.Show(owner, host.Prefs.L("install.restartConfirm"), "NyangTalk", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.OK;
            Parking = new ModParking(host.Docs, Path.Combine(host.DataRoot, "parked-mods")) { GameRunning = () => Game.IsRunning };
            Parking.Owned = () => { try { var rec = Installer.LoadRecord(); return rec == null ? Enumerable.Empty<string>() : rec.Files.Select(f => f.RelativePath).ToList(); } catch (InvalidDataException) { return null; } };   // unknown: nothing moves
            Game = new RealGameControl { ExePath = System.Windows.Forms.Application.ExecutablePath, DataFolder = host.DataRoot };
            Location = new GameLocationService(new GameLocator(new RealLocatorEnv()), () => host.Prefs.Settings.Get("link.gameDir"), v => host.Prefs.Settings.Set("link.gameDir", v));
            Location.Found += p => form.ShowNotice("loc-found", string.Format(host.Prefs.L("link.locFound"), p));
            Location.Relocated += p => form.ShowNotice("loc-changed:" + p, string.Format(host.Prefs.L("link.locChanged"), p));
            Location.NotFound += () => form.ShowNotice("loc-missing", host.Prefs.L("link.locNotFound"));
            Location.ChooseAmong += list => form.BeginInvoke((Action)(() => ChooseAmong(list)));
            string broken;
            var seen = host.Docs.Load("notices", out broken);
            if (seen != null) _notices.Load(seen.Keys);
        }

        public static string ContractJson()
        {
            var asm = typeof(LinkServices).Assembly;
            using (var s = asm.GetManifestResourceStream("contract.json"))
            {
                if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
            var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "contract.json");
            return File.Exists(file) ? File.ReadAllText(file) : "{\"contract_version\":0,\"entries\":[]}";
        }

        /// <summary>The full-profile folder the loader is configured to prefer, read from the loader's own config file.</summary>
        public static string DoorstopOverride(string gameDir)
        {
            var ini = Path.Combine(gameDir, "doorstop_config.ini");
            if (!File.Exists(ini)) return null;
            foreach (var line in File.ReadAllLines(ini))
            {
                var t = line.Trim();
                int eq = t.IndexOf('=');
                if (eq < 0 || t.StartsWith("#") || t.StartsWith(";")) continue;
                var key = t.Substring(0, eq).Trim().ToLowerInvariant();
                if (key != "dllsearchpathoverride" && key != "dll_search_path_override") continue;
                var v = t.Substring(eq + 1).Trim();
                if (v.Length == 0) return null;
                return Path.IsPathRooted(v) ? v : Path.Combine(gameDir, v);
            }
            return null;
        }

        /// <summary>The saved game folder (40-SPEC 5.2: used as-is, not checked at start-up).</summary>
        public string GameDir { get { return Location.Saved; } }

        private void ChooseAmong(List<string> list)
        {
            using (var f = new Form { Text = _host.Prefs.L("link.locChooseAmong"), ClientSize = new Size(520, 200), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog })
            {
                var pal = Dialogs.Themed(f, _host.Prefs);
                var lb = new ThemedList { Dock = DockStyle.Fill };
                lb.Pal = pal;
                lb.Items.AddRange(list); lb.EndUpdate();
                Action pick = () => { if (lb.SelectedIndex >= 0 && Location.Choose(lb.Items[lb.SelectedIndex])) f.Close(); };
                lb.DoubleClick += (s, e) => pick();
                lb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) pick(); };
                f.Controls.Add(lb);
                Placement.Over(f, _form);
                f.ShowDialog(_form);
            }
        }

        public void PickFolder(IWin32Window owner)
        {
            using (var dlg = new FolderBrowserDialog { Description = _host.Prefs.L("link.locNotFound") })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                if (!Location.Choose(dlg.SelectedPath)) _form.ShowNotice("not-game-folder:" + DateTime.UtcNow.Ticks, _host.Prefs.L("link.notGameFolder"));
            }
        }

        public static void OpenInSteam()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://rungameid/" + GameLocator.AppId) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private DateTime _lastProcCheck = DateTime.MinValue;

        /// <summary>40-SPEC 5.2 conditions 1 and 2, checked every 10 s while the app runs.</summary>
        public void Monitor()
        {
            if (DateTime.UtcNow - _lastProcCheck < TimeSpan.FromSeconds(10)) return;
            _lastProcCheck = DateTime.UtcNow;
            var exe = new RealLocatorEnv().RunningGameExe();
            if (exe == null) return;
            Location.OnGameProcess(exe);
            var st = _host.Chat.Status;
            var silent = st == null ? TimeSpan.MaxValue : DateTime.UtcNow - st.WrittenUtc;
            Location.OnNoStatus(true, silent);
        }

        /// <summary>Updates <see cref="ChatService.Installed"/> and foreign-mod state from the current game folder.</summary>
        public void RefreshInstallState(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { _host.Chat.Installed = false; return; }
            _host.Chat.Installed = Installer.IsInstalled(dir);
            if (!_host.Chat.Installed)
            {
                List<string> notMovable;
                if (ModParking.Candidates(dir, out notMovable).Count > 0 && notMovable.Count == 0) ForeignModsBlocked = true;
            }
        }

        /// <summary>Startup and folder-change check in the background; the UI never waits for it (10-SPEC 6장).</summary>
        public void StartCheck()
        {
            var dir = Location.OnStart();
            if (dir == null) { _host.Chat.Installed = false; Status = _host.Prefs.L("link.notInstalled"); return; }
            RefreshInstallState(dir);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ran; CheckReport report = null; string err = null; bool? typeLoad = false; List<string> drift = null;
                try
                {
                    ran = Check.Refresh(dir, DoorstopOverride(dir), null);
                    report = Check.LastReport;
                    typeLoad = Installer.IsDisabled(dir) ? (bool?)false : GameLinkService.LogHasTypeLoadProblem(dir);
                    drift = Installer.Drift(dir);
                }
                catch (Exception ex) { ran = false; err = ex.GetType().Name + ": " + ex.Message; }
                if (_form.IsDisposed || !_form.IsHandleCreated) return;   // window closed meanwhile
                try { _form.BeginInvoke((Action)(() => ApplyCheck(report, ran, err, typeLoad, drift))); }
                catch (InvalidOperationException) { }
            });
        }

        private void ApplyCheck(CheckReport report, bool ran, string err, bool? typeLoad, List<string> drift)
        {
            var fp = Check.LastFingerprint == null ? "-" : Check.LastFingerprint.Id;
            if (err != null) { Status = err; _host.AddLog("check: " + err); return; }
            if (report != null)
            {
                foreach (var kv in report.Areas)
                {
                    FeatureArea a;
                    if (Enum.TryParse(kv.Key, out a)) _host.Chat.Areas.SetStatic(a, kv.Value);
                }
                var broken = report.Areas.Where(kv => !kv.Value).Select(kv => _host.Prefs.L("area." + kv.Key)).ToList();
                Status = (broken.Count == 0 ? "OK" : string.Join(", ", broken)) + " · " + report.ElapsedMs + " ms" + (report.Mix.Count > 0 ? " · mixing " + report.Mix.Count : "");
                if (ran)
                {
                    Notice(fp, "rechecked", _host.Prefs.L("link.gameUpdated"));
                    // 50-SPEC 2장 / 70-SPEC 2.2: pictures belong to one game build; the component makes them again
                    foreach (var dir in new[] { Exchange.EmoteDir, Exchange.PortraitDir })
                    {
                        var full = Path.Combine(_host.Exchange.Folder, dir);
                        try { if (Directory.Exists(full)) Directory.Delete(full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
                bool keptLoader = Installer.ForeignLoaderKept(Location.Saved ?? "");
                if (keptLoader && report.Mix.Count > 0) { KeptSetupProblem = true; Notice(fp, "kept-mix", string.Format(_host.Prefs.L("install.keptSetupMix"), report.Mix.Count)); }
                else if (report.Mix.Any(m => m.Fixable)) Notice(fp, "fixable", _host.Prefs.L("link.fixable"));
                else if (report.Mix.Count > 0) Notice(fp, "unfixable", _host.Prefs.L("link.updateNeeded"));
            }
            if (typeLoad == true)
            {
                bool keptLoader = Installer.ForeignLoaderKept(Location.Saved ?? "");
                if (keptLoader) KeptSetupProblem = true;
                Notice(fp, "typeload", _host.Prefs.L(keptLoader ? "link.typeLoadKept" : "link.typeLoadWarning"));
            }
            if (typeLoad == null) Notice(fp, "logunreadable", _host.Prefs.L("link.logUnreadable"));
            if (drift != null && drift.Count > 0) Notice(fp, "drift", _host.Prefs.L("install.drift"));
        }

        private void Notice(string fp, string key, string text)
        {
            if (!_notices.ShouldShow(fp, key)) return;
            _host.Docs.Save("notices", _notices.Shown.ToDictionary(k => k, k => (object)true));
            _form.ShowNotice(fp + key, text);
        }

        /// <summary>Status line for the sidebar: what the game component reports, never "working" without a report.</summary>
        public string AutomationStatus()
        {
            if (!_host.Prefs.Settings.GetBool("auto.chest") && !_host.Prefs.Settings.GetBool("auto.gifts")) return null;
            var L = (Func<string, string>)_host.Prefs.L;
            var st = _host.Chat.Status;
            var paused = AutomationPauseReason();
            string text;
            if (!_host.Chat.Areas.IsAvailable(FeatureArea.Chest) || (st != null && st.ChestState == "unsupported")) text = L("auto.status.Unsupported");
            else if (paused != null) text = string.Format(L("auto.status.Paused"), paused);
            else if (st.ChestState == null) text = string.Format(L("auto.status.Paused"), L("auto.reason.permit"));
            else switch (st.ChestState)
            {
                case "waiting": text = L("auto.status.Waiting"); break;
                case "opening": text = L("auto.status.Opening"); break;
                case "paused": text = string.Format(L("auto.status.Paused"), st.ChestReason == null ? "?" : L("auto.reason." + st.ChestReason)); break;
                default: text = string.Format(L("auto.status.Paused"), L("auto.reason.permit")); break;
            }
            return L("automation.activeTitle") + " · " + text;
        }

        private DateTime _lastPermit = DateTime.MinValue;
        private bool _permitOn;
        private string _lastPermitContent;

        private bool AutomationConnected()
        {
            var st = _host.Chat.Status;
            return _host.Chat.Link == LinkState.Connected && st != null && st.Ready && !string.IsNullOrEmpty(st.GameSession);
        }

        private bool AutomationConsented(string key)
        {
            return ChestPolicy.HasValidConsent(LoadConsent(key == "auto.gifts" ? "automation-consent-gifts" : "automation-consent"), DeviceIdIfAny());
        }

        private string AutomationPauseReason(string key = null)
        {
            if (!AutomationConnected()) return L("link.disconnected");
            if (key == null)
            {
                // Either enabled feature may be active; a blocked gift switch must not hide my own chest's report.
                var own = _host.Prefs.Settings.GetBool("auto.chest") ? AutomationPauseReason("auto.chest") : null;
                var gifts = _host.Prefs.Settings.GetBool("auto.gifts") ? AutomationPauseReason("auto.gifts") : null;
                if ((_host.Prefs.Settings.GetBool("auto.chest") && own == null) || (_host.Prefs.Settings.GetBool("auto.gifts") && gifts == null)) return null;
                return own ?? gifts;
            }
            if (!AutomationConsented(key)) return L("auto.reason.permit");
            if (key == "auto.gifts" && _host.Chat.Status.Lobby == null) return L("room.noLobby");
            return null;
        }

        /// <summary>
        /// Own chests need a fresh permit for this game run, including outside a lobby. Gifts additionally need a lobby.
        /// A null lobby keeps the existing component's session spelling (":" + game run). Any change revokes or rebinds immediately.
        /// </summary>
        public void PermitTick()
        {
            var now = _host.Clock.UtcNow;
            var st = _host.Chat.Status;
            bool connected = AutomationConnected() && _host.Chat.Areas.IsAvailable(FeatureArea.Chest) && st.ChestState != "unsupported";
            bool own = _host.Prefs.Settings.GetBool("auto.chest") && AutomationConsented("auto.chest") && connected;
            // 80-SPEC 2장: separate switch and separate consent ("my points are spent, they get it")
            bool gifts = _host.Prefs.Settings.GetBool("auto.gifts") && AutomationConsented("auto.gifts") && connected && st.Lobby != null;
            bool want = own || gifts;
            foreach (var row in _host.Exchange.ReadChestLog())
                Chest.Record(row.Member == null ? row.Kind : string.Format(_host.Prefs.L("auto.giftLog"), NameOf(row.Member)) + " · " + row.Kind, row.Called, row.Reason, row.TimeUtc);
            if (!want && !_permitOn) return;
            var kinds = new List<object>();
            var sel = _host.Prefs.Settings.Get("auto.chestTypes");
            if (sel != "emote") kinds.Add("Cosmetic");
            if (sel != "cosmetic") kinds.Add("Emote");
            var chest = new Dictionary<string, object>
            {
                { "enabled", own }, { "gifts", gifts }, { "kinds", kinds }, { "delay", (long)_host.Prefs.Settings.GetInt("auto.chestDelay") },
                { "max_per_hour", (long)_host.Prefs.Settings.GetInt("auto.chestMaxPerHour") }
            };
            var permit = new Dictionary<string, object> { { "chest", chest }, { "session", want ? (st.Lobby == null ? "" : st.Lobby.LobbyId) + ":" + st.GameSession : null } };
            var content = Json.Write(permit);
            if (want && content == _lastPermitContent && now - _lastPermit < TimeSpan.FromSeconds(2)) return;
            try
            {
                _host.Exchange.WritePermit(permit, now);
                _permitOn = want;
                _lastPermit = now;
                _lastPermitContent = content;
            }
            catch (IOException ex) { _host.AddLog("permit: " + ex.Message); }
        }

        private string DeviceIdIfAny()
        {
            string broken;
            var d = _host.Docs.Load("device", out broken);
            return d == null ? null : Json.Str(new Dictionary<string, object>(d), "id");
        }

        // ---------------------------------------------------------------- consent (80-SPEC 0장)

        private string DeviceId()
        {
            string broken;
            var d = _host.Docs.Load("device", out broken);
            var id = d == null ? null : Json.Str(new Dictionary<string, object>(d), "id");
            if (id != null) return id;
            id = Guid.NewGuid().ToString("N");
            _host.Docs.Save("device", new Dictionary<string, object> { { "id", id } });
            return id;
        }

        private string NameOf(string steamId)
        {
            var p = _host.Chat.PeopleList().FirstOrDefault(x => x.SteamId == steamId);
            return p == null ? steamId : NyangTalk.Domain.People.DisplayName.Format(p.Name, _host.Prefs.Aliases.Get(steamId), _host.Prefs.NameMode);
        }

        public ConsentRecord LoadConsent(string doc = "automation-consent")
        {
            string broken;
            var d = _host.Docs.Load(doc, out broken);
            if (d == null) return null;
            return new ConsentRecord { DeviceId = Json.Str(new Dictionary<string, object>(d), "device"), FeatureVersion = (int)Json.Long(new Dictionary<string, object>(d), "feature_version", 0) };
        }

        /// <summary>
        /// Turning an automation switch on is the consent (2026-11-20 user decision: no pop-up). The risk notice stays
        /// above the switches on the settings page; the record (device, feature version, time) is still kept, and the
        /// game component only acts while it exists (80-SPEC 0장 기록).
        /// </summary>
        public bool RecordConsent(string doc)
        {
            var dev = DeviceId();
            if (ChestPolicy.HasValidConsent(LoadConsent(doc), dev)) return true;
            bool saved = _host.Docs.Save(doc, new Dictionary<string, object> { { "device", dev }, { "feature_version", (long)ChestPolicy.FeatureVersion }, { "accepted_utc", Exchange.Iso(_host.Clock.UtcNow) }, { "how", "switch turned on with the notice shown" } });
            if (!saved) _form.ShowNotice("consent-save", L("auto.consentSaveFailed"));   // the switch stays off, and says why
            return saved;
        }

        // ---------------------------------------------------------------- settings pages (90-SPEC 1.4 분류별)

        /// <summary>Builds the connection, automation and update pages; false for the other categories.</summary>
        public bool BuildPage(string cat, SettingsPage page)
        {
            switch (cat)
            {
                case "link": LinkPage(page); return true;
                case "auto": AutoPage(page); return true;
                case "about": AboutPage(page); return true;
            }
            return false;
        }

        internal string L(string k) { return _host.Prefs.L(k); }

        private static bool Failed(InstallResult r) { return r.Outcome != InstallOutcome.Done && r.Outcome != InstallOutcome.NothingToDo; }

        /// <summary>Game folder for "잠시 끄기/다시 켜기"; null when unknown.</summary>
        private bool? LinkDisabled()
        {
            var dir = GameDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            try { return Installer.IsDisabled(dir); }
            catch (InvalidDataException) { return null; }
            catch (IOException) { return null; }
        }

        /// <summary>① status ② everyday actions ③ management, 완전 제거 last in the warning colour.</summary>
        private void LinkPage(SettingsPage page)
        {
            page.Heading(L("group.linkStatus"));
            var dot = new StatusDot { Text = _form.LinkText() + (Status.Length > 0 ? " · " + Status : ""), State = _host.Chat.Link == LinkState.Connected ? 2 : (_host.Chat.Link == LinkState.Waiting || _host.Chat.Link == LinkState.Disconnected ? 1 : 0) };
            page.Custom(dot, w => dot.HeightFor(w));
            page.Note(L("set.link.gameDir") + ": " + (GameDir ?? "—"), false);

            page.Heading(L("group.linkDaily"));
            var dailyResult = (Label)null;
            page.Buttons(
                page.Button(L("link.relocate"), () => { Location.Search(RecheckReason.UserAsked); dailyResult.Text = L("link.relocateStarted"); page.SetNoteWarn(dailyResult, false); page.Relayout(); }, "tip.link.relocate"),
                page.Button(L("link.chooseFolder"), () => PickFolder(_form), "tip.link.chooseFolder"),
                page.Button(L("link.openSteam"), OpenInSteam, "tip.link.openSteam"),
                page.Button(L("diag.copy"), () =>
                {
                    bool ok;
                    try { Clipboard.SetText(Diagnostics()); ok = true; } catch (System.Runtime.InteropServices.ExternalException) { ok = false; }
                    dailyResult.Text = L(ok ? "diag.copied" : "diag.copyFailed"); page.SetNoteWarn(dailyResult, !ok); page.Relayout();
                }, "tip.diag.copy"));
            dailyResult = page.Note("", false);

            page.Group("link", "group.autoStart");

            page.Heading(L("group.linkManage"));
            var manageResult = (Label)null;
            Action<Label, Func<string, InstallResult>> op = (target, f) =>
            {
                var dir = Location.ForFileOperation();   // 40-SPEC 5.2 condition 3
                if (dir == null) { target.Text = L("link.locNotFound"); page.SetNoteWarn(target, true); page.Relayout(); return; }
                var r = f(dir);
                target.Text = Describe(r); page.SetNoteWarn(target, Failed(r)); page.Relayout();
            };
            var disabled = LinkDisabled();
            var toggle = page.Button(L(disabled == true ? "install.enable" : "install.disable"), null, disabled == true ? "tip.install.enable" : "tip.install.disable");
            toggle.Click += (s, e) =>
            {
                bool turnOn = LinkDisabled() == true;
                op(manageResult, d => Installer.SetEnabled(d, turnOn));
                var now = LinkDisabled();
                toggle.Text = L(now == true ? "install.enable" : "install.disable");   // only the one that fits the state
                page.Relayout();
            };
            toggle.Visible = disabled.HasValue;
            page.Buttons(page.Button(L("install.reinstall"), () =>
            {
                manageResult.Text = L("install.working"); page.SetNoteWarn(manageResult, false); page.Relayout();
                ReinstallAsync(Form.ActiveForm ?? _form, (text, failed) => { if (manageResult.IsDisposed) return; manageResult.Text = text; page.SetNoteWarn(manageResult, failed); page.Relayout(); });
            }, "tip.install.reinstall"), toggle);
            manageResult = page.Note("", false);

            // an earlier mod's BepInEx setup (40-SPEC 1.1·6장): move it out of the game folder and install fresh, or put it back
            var parkResult = (Label)null;
            var gameDir = GameDir;
            bool haveDir = !string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir);
            List<string> notMovable = new List<string>();
            var others = haveDir ? ModParking.Candidates(gameDir, out notMovable) : new List<string>();
            bool kept = haveDir && Installer.ForeignLoaderKept(gameDir);
            bool hasForeignFiles = false;
            if (haveDir) { try { hasForeignFiles = Parking.Foreign(gameDir).Count > 0; } catch (InvalidDataException) { } }
            // 95-SPEC 0장: no dialog — the first button says what will happen right below and shows the button that does it
            var parkGo = (Button)null; var restoreGo = (Button)null; var park = (Button)null; var restore = (Button)null;
            Action<string, bool> showDone = (text, failed) =>
            {
                if (parkResult.IsDisposed) return;
                var curDir = GameDir;
                bool curHave = !string.IsNullOrEmpty(curDir) && Directory.Exists(curDir);
                List<string> nm;
                var curOthers = curHave ? ModParking.Candidates(curDir, out nm) : new List<string>();
                bool curKept = curHave && Installer.ForeignLoaderKept(curDir);
                bool curForeign = false;
                if (curHave) { try { curForeign = Parking.Foreign(curDir).Count > 0; } catch (InvalidDataException) { } }
                park.Visible = curOthers.Count > 0 || curKept || curForeign;
                restore.Visible = Parking.HasParked;
                var curDis = LinkDisabled();
                toggle.Visible = curDis.HasValue;
                if (curDis.HasValue) toggle.Text = L(curDis == true ? "install.enable" : "install.disable");
                parkResult.Text = text; page.SetNoteWarn(parkResult, failed); page.Relayout();
            };
            park = page.Button(L("mods.park"), () =>
            {
                parkResult.Text = L("mods.parkConfirm") + (GameRunningCached() ? " " + L("mods.gameWillRestart") : ""); page.SetNoteWarn(parkResult, false);
                parkGo.Visible = true; restoreGo.Visible = false; page.Relayout();
            }, "tip.mods.park");
            restore = page.Button(L("mods.restore"), () =>
            {
                parkResult.Text = L("mods.restoreConfirm") + (GameRunningCached() ? " " + L("mods.gameWillRestart") : ""); page.SetNoteWarn(parkResult, false);
                restoreGo.Visible = true; parkGo.Visible = false; page.Relayout();
            }, "tip.mods.restore");
            parkGo = page.Button(L("mods.parkGo"), () =>
            {
                parkGo.Visible = false; parkResult.Text = L("install.working"); page.Relayout();
                ParkAndInstallAsync(Form.ActiveForm ?? _form, showDone);
            }, "tip.mods.park");
            restoreGo = page.Button(L("mods.restoreGo"), () =>
            {
                restoreGo.Visible = false; parkResult.Text = L("install.working"); page.Relayout();
                RestoreAsync(Form.ActiveForm ?? _form, showDone);
            }, "tip.mods.restore");
            parkGo.Visible = false; restoreGo.Visible = false;
            park.Visible = others.Count > 0 || kept || hasForeignFiles;
            restore.Visible = Parking.HasParked;
            if (park.Visible || restore.Visible)
            {
                page.Heading(L("group.otherMods"));
                if (kept) page.Note(KeptSetupProblem ? L("mods.keptProblem") : L("mods.kept"), KeptSetupProblem);
                if (others.Count > 0) page.Note(string.Format(L("mods.found"), string.Join(", ", others)), false);
                if (notMovable.Count > 0) page.Note(string.Format(L("mods.notMovable"), string.Join(", ", notMovable)), true);
                page.Buttons(park, restore);
                parkResult = page.Note("", false);
                page.Buttons(parkGo, restoreGo);
            }

            page.Heading(L("group.danger"));
            page.Note(L("install.uninstallNote"), false);
            var removeResult = (Label)null;
            var remove = page.Button(L("install.uninstall"), () =>
            {
                if (MessageBox.Show((IWin32Window)Form.ActiveForm ?? _form, L("install.uninstallConfirm"), L("install.uninstall"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.OK)
                    op(removeResult, d => { var res = Installer.Uninstall(d); RefreshInstallState(d); return res; });
            }, "tip.install.uninstall");
            remove.Tag = "warn";
            page.Buttons(remove);
            removeResult = page.Note("", false);
        }

        // ---------------------------------------------------------------- 40-SPEC 6장: reinstall with game restart

        public ReinstallReport LastReinstall;
        /// <summary>The one question before closing the game; replaced in checks.</summary>
        public Func<IWin32Window, bool> ConfirmRestart;

        public static bool IsFailure(ReinstallReport r)
        {
            return r.Outcome == ReinstallOutcome.Failed || r.Outcome == ReinstallOutcome.GameDidNotClose || r.Outcome == ReinstallOutcome.RelaunchFailed;
        }

        /// <summary>
        /// "연결 다시 설치": the one question on the UI thread, then close the game, install and ask Steam to start it on a
        /// worker thread (closing can take up to 20 s, the administrator prompt longer). <paramref name="done"/> gets the
        /// sentence to show and whether it is a failure, back on the UI thread.
        /// </summary>
        public void ReinstallAsync(IWin32Window owner, Action<string, bool> done)
        {
            var dir = Location.ForFileOperation();   // 40-SPEC 5.2 condition 3
            if (dir == null) { LastReinstall = new ReinstallReport { Outcome = ReinstallOutcome.Failed, Why = "no-game-folder" }; done(L("link.locNotFound"), true); return; }
            bool running = Game.IsRunning;
            if (running && !ConfirmRestart(owner)) { LastReinstall = new ReinstallReport { Outcome = ReinstallOutcome.Cancelled }; done(L("install.cancelled"), false); return; }
            var flow = new ReinstallFlow { Game = Game, Install = d => Installer.Install(d, LoadPayload()), ConfirmRestart = () => true };
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ReinstallReport r;
                try { r = flow.Run(dir); }
                catch (Exception ex) { r = new ReinstallReport { Outcome = ReinstallOutcome.Failed, Why = ex.GetType().Name + ": " + ex.Message }; }
                if (_form.IsDisposed || !_form.IsHandleCreated) return;
                try { _form.BeginInvoke((Action)(() => { LastReinstall = r; RefreshInstallState(dir); done(ReinstallText(r), IsFailure(r)); })); }
                catch (InvalidOperationException) { }
            });
        }

        /// <summary>The sentence for a finished reinstall; every failure is also written to the log.</summary>
        public string ReinstallText(ReinstallReport r)
        {
            if (IsFailure(r) || r.Why != null) _host.AddLog("reinstall: " + r.Outcome + " " + (r.Why ?? "") + (r.Install != null ? " install " + r.Install.Outcome : ""));
            string text;
            switch (r.Outcome)
            {
                case ReinstallOutcome.Cancelled: return L("install.cancelled");
                case ReinstallOutcome.GameDidNotClose: return string.Format(L("install.gameDidNotClose"), WhyText(r.Why));
                case ReinstallOutcome.Relaunched:
                    return L(r.Install != null && r.Install.Outcome == InstallOutcome.NothingToDo ? "install.relaunchedNothing" : "install.relaunched");
                case ReinstallOutcome.RelaunchFailed: return string.Format(L("install.relaunchFailed"), r.Why ?? "?");
                case ReinstallOutcome.Failed:
                    if (r.Elevated && (r.Install == null || r.Install.Outcome == InstallOutcome.NeedsElevation)) text = string.Format(L("install.elevationFailed"), WhyText(r.Why));
                    else if (r.Install != null) text = Describe(r.Install);
                    else text = string.Format(L("install.failedWhy"), WhyText(r.Why));
                    return r.GameClosed ? text + " " + L("install.gameLeftClosed") : text;
                default: return Describe(r.Install);
            }
        }

        private string WhyText(string why)
        {
            if (why == null) return "?";
            if (why == "declined") return L("install.why.declined");
            if (why.StartsWith("timeout:")) return string.Format(L("install.why.timeout"), why.Substring(8));
            if (why == "no-window") return L("install.why.noWindow");
            if (why == "still-running") return L("install.why.stillRunning");
            if (why == "helper-timeout") return L("install.why.helperTimeout");
            if (why.StartsWith("not-verified:")) return L("install.why.notVerified");
            return why;
        }

        // ---------------------------------------------------------------- 95-SPEC 2.6: the one notice line

        public enum NoticeActionKind { None, Install, ReinstallRestart, Park, OpenSteam, Reconnect }

        /// <summary>The one action the notice line offers for the connection (null-like None when nothing is wrong).</summary>
        public static NoticeActionKind ConnectionAction(LinkState state, bool gameRunning, bool modsProblem)
        {
            if (modsProblem) return NoticeActionKind.Park;
            switch (state)
            {
                case LinkState.Connected: return NoticeActionKind.None;
                case LinkState.NotInstalled: return NoticeActionKind.Install;
                case LinkState.NeedsComponentUpdate: case LinkState.NeedsReinstall: return gameRunning ? NoticeActionKind.ReinstallRestart : NoticeActionKind.Install;
                default: return gameRunning ? NoticeActionKind.Reconnect : NoticeActionKind.OpenSteam;
            }
        }

        public static string ActionKey(NoticeActionKind k)
        {
            switch (k)
            {
                case NoticeActionKind.Install: return "notice.btn.install";
                case NoticeActionKind.ReinstallRestart: return "notice.btn.reinstallRestart";
                case NoticeActionKind.Park: return "notice.btn.park";
                case NoticeActionKind.OpenSteam: return "notice.btn.openSteam";
                case NoticeActionKind.Reconnect: return "notice.btn.reconnect";
                default: return null;
            }
        }

        /// <summary>The last install was refused because of other mods (the line then offers to move them).</summary>
        public bool ForeignModsBlocked;
        /// <summary>A flow (install, move, restore) is running; the update restart waits for it.</summary>
        public bool Busy;
        private DateTime _confirmUntil = DateTime.MinValue;
        private bool _gameRunningCache;
        private DateTime _gameRunningAt = DateTime.MinValue;
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;

        private bool GameRunningCached()
        {
            var now = UtcNow();
            if (now - _gameRunningAt > TimeSpan.FromSeconds(5)) { _gameRunningCache = Game.IsRunning; _gameRunningAt = now; }
            return _gameRunningCache;
        }

        /// <summary>Called every tick: puts the connection and update states on the notice line.</summary>
        public void UpdateNotices()
        {
            var state = _host.Chat.Link;
            if (UtcNow() < _confirmUntil) { UpdateCheckTick(); UpdateUpdateNotice(); return; }   // the line is asking; leave it for a minute
            var kind = ConnectionAction(state, GameRunningCached(), ForeignModsBlocked || KeptSetupProblem);
            if (kind == NoticeActionKind.None) _form.Notices.Clear(NoticeKind.Connection);
            else
            {
                var text = kind == NoticeActionKind.Park ? L(ForeignModsBlocked ? "notice.foreignMods" : "notice.keptProblem") : _form.LinkText();
                _form.Notices.Set(new NoticeEntry { Key = "link:" + kind + ":" + state, Kind = NoticeKind.Connection, Text = text, ActionText = Busy ? null : L(ActionKey(kind)), Action = () => RunNoticeAction(kind) });
            }
            UpdateCheckTick();
            UpdateUpdateNotice();
            UpdateHealthNotice();
        }

        /// <summary>95-SPEC 2.4: ⚠ and "다시 연결" when the game component got chat events the app never read.</summary>
        private void UpdateHealthNotice()
        {
            var w = _host.Chat.Receive;
            if (!w.Warning) { _form.Notices.Clear(NoticeKind.Health); return; }
            if (!_healthLogged) { _healthLogged = true; _host.AddLog("health: component reported " + w.Missing + " chat events the app did not read in " + (int)ReceiveWatch.Window.TotalSeconds + " s"); }
            _form.Notices.Set(new NoticeEntry { Key = "health:receive", Kind = NoticeKind.Health, Text = L("health.receiveStuck"), ActionText = L("notice.btn.reconnect"), Action = () => { _healthLogged = false; _form.Reconnect(); } });
        }

        private bool _healthLogged;

        private void RunNoticeAction(NoticeActionKind kind)
        {
            var owner = Form.ActiveForm ?? _form;
            Action<string, bool> done = (text, failed) =>
            {
                Busy = false;
                _form.ShowNotice("result:" + UtcNow().Ticks, text);
                // Keep the result visible on the top notice line itself (otherwise the priority-0 Connection notice hides the Info result)
                _confirmUntil = UtcNow() + TimeSpan.FromSeconds(15);
                var nextKind = ConnectionAction(_host.Chat.Link, GameRunningCached(), ForeignModsBlocked || KeptSetupProblem);
                _form.Notices.Set(new NoticeEntry
                {
                    Key = "link-result:" + UtcNow().Ticks,
                    Kind = NoticeKind.Connection,
                    Text = text,
                    ActionText = failed && nextKind != NoticeActionKind.None ? L(ActionKey(nextKind)) : null,
                    Action = failed && nextKind != NoticeActionKind.None ? (Action)(() => { _confirmUntil = DateTime.MinValue; RunNoticeAction(nextKind); }) : null
                });
            };
            switch (kind)
            {
                case NoticeActionKind.Install: case NoticeActionKind.ReinstallRestart: Busy = true; ReinstallAsync(owner, done); break;
                case NoticeActionKind.Park:
                    _confirmUntil = UtcNow() + TimeSpan.FromSeconds(60);
                    _form.Notices.Set(new NoticeEntry
                    {
                        Key = "confirm-park:" + UtcNow().Ticks, Kind = NoticeKind.Connection,
                        Text = L("mods.parkConfirm") + (GameRunningCached() ? " " + L("mods.gameWillRestart") : ""), ActionText = L("mods.parkGo"),
                        Action = () => { _confirmUntil = DateTime.MinValue; Busy = true; ParkAndInstallAsync(owner, done); }
                    });
                    break;
                case NoticeActionKind.OpenSteam:
                    string why;
                    if (Game.LaunchViaSteam(out why)) _form.ShowNotice("launch:" + UtcNow().Ticks, L("install.askedSteam"));
                    else _form.ShowNotice("launch:" + UtcNow().Ticks, string.Format(L("install.relaunchFailed"), why));
                    break;
                case NoticeActionKind.Reconnect: _form.Reconnect(); break;
            }
        }

        // ---------------------------------------------------------------- 95-SPEC 2.2: automatic update check

        public UpdateSchedule Schedule;
        private bool _checking;
        public string ReadyVersion;           // downloaded and staged, waiting for "지금 재시작"
        public string AvailableVersion;       // found, but the user chose "알림만"
        public Action<Action> RunInBackground = work => ThreadPool.QueueUserWorkItem(_ => work());
        public Action Restart;                // replaced in checks

        private void UpdateCheckTick()
        {
            if (Schedule == null) Schedule = new UpdateSchedule(UtcNow());
            if (_checking || !Schedule.Due(UtcNow())) return;
            _checking = true;
            RunInBackground(() =>
            {
                string ready = null, available = null;
                try
                {
                    var r = RunUpdateCheck();
                    if (r != null && (r.Status == UpdateStatus.UpdateAvailable) && r.Info != null)
                    {
                        if (_host.Prefs.Settings.Get("update.mode") == "auto" && _lastService != null)
                        {
                            var d = _lastService.Download(r);
                            if (d.Status == UpdateStatus.Downloaded) ready = r.Info.Version.ToString();
                            else _host.AddLog("update download: " + d.Status + " " + d.Detail);
                        }
                        else available = r.Info.Version.ToString();
                    }
                }
                catch (Exception ex) { _host.AddLog("update: " + ex.GetType().Name + ": " + ex.Message); }
                Action apply = () => { _checking = false; if (ready != null) ReadyVersion = ready; if (available != null) AvailableVersion = available; UpdateUpdateNotice(); };
                try { if (!_form.IsDisposed && _form.IsHandleCreated) _form.BeginInvoke(apply); else apply(); }
                catch (InvalidOperationException) { _checking = false; }
            });
        }

        /// <summary>Why "지금 재시작" waits (95-SPEC 2.2), or null.</summary>
        public string RestartBlock()
        {
            if (_form.Input.Text.Trim().Length > 0) return "update.defer.draft";
            if (_host.Chat.Outbox.Pending.Any(m => m.State == NyangTalk.Domain.Sending.OutgoingState.Sending)) return "update.defer.sending";
            if (Busy) return "update.defer.install";
            return null;
        }

        private void UpdateUpdateNotice()
        {
            if (ReadyVersion != null)
            {
                var block = RestartBlock();
                var key = "update-ready:" + ReadyVersion;
                if (DismissedUpdate() == ReadyVersion) { _form.Notices.Clear(NoticeKind.Update); return; }
                _form.Notices.Set(new NoticeEntry
                {
                    Key = key, Kind = NoticeKind.Update,
                    Text = string.Format(L("update.readyLine"), ReadyVersion) + (block != null ? " · " + L(block) : ""),
                    ActionText = block == null ? L("update.restartNow") : null,
                    Action = () => { if (RestartBlock() == null) { var h = Restart; if (h != null) h(); else RestartNow(); } }
                });
            }
            else if (AvailableVersion != null && DismissedUpdate() != AvailableVersion)
                _form.Notices.Set(new NoticeEntry { Key = "update-available:" + AvailableVersion, Kind = NoticeKind.Update, Text = string.Format(L("update.availableLine"), AvailableVersion), ActionText = L("update.openSettings"), Action = () => _form.OpenSettings("about") });
            else _form.Notices.Clear(NoticeKind.Update);
        }

        /// <summary>The version whose notice the user closed; it is not shown again (the settings page still shows it).</summary>
        public string DismissedUpdate()
        {
            string broken;
            var d = _host.Docs.Load("update-dismissed", out broken);
            return d == null ? null : Json.Str(new Dictionary<string, object>(d), "version");
        }

        public void DismissUpdate(string version) { _host.Docs.Save("update-dismissed", new Dictionary<string, object> { { "version", version } }); }

        /// <summary>Starts this app again (it hands over to the staged version and waits for this one to end) and closes.</summary>
        public void RestartNow()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--wait-single") { UseShellExecute = false }); }
            catch (System.ComponentModel.Win32Exception ex) { _form.ShowNotice("restart:" + UtcNow().Ticks, string.Format(L("update.restartFailed"), ex.Message)); return; }
            _form.Close();
        }

        /// <summary>
        /// 95-SPEC 2.1: on → the fixed copy in the app data, one start-up entry, the watcher started now; off → the entry is
        /// removed (the watcher then ends by itself). A failure keeps the switch where it was and says why in the notice line.
        /// </summary>
        public bool SetStartWithGame(bool on)
        {
            var auto = new AutoStart(RunKey);
            string why;
            if (!on)
            {
                if (auto.TurnOff(out why)) return true;
                _form.ShowNotice("autostart", string.Format(L("autoStart.offFailed"), why));
                return false;
            }
            var fixedExe = FixedLaunchPath.Ensure(_host.DataRoot, System.Windows.Forms.Application.ExecutablePath, out why);
            if (fixedExe == null || !auto.TurnOn(fixedExe, out why)) { _form.ShowNotice("autostart", string.Format(L("autoStart.failed"), why ?? "?")); return false; }
            StartWatcher(fixedExe);
            return true;
        }

        /// <summary>Set by the start-up check when the kept (earlier) BepInEx setup shows mixing problems or type-load errors.</summary>
        public bool KeptSetupProblem;

        /// <summary>
        /// "이전 모드 옮기고 새로 설치": one plain question, then (closing the game if it runs) move the earlier BepInEx setup to
        /// the app data, install NyangTalk's own BepInEx bundle, and ask Steam to start the game again.
        /// </summary>
        public void ParkAndInstallAsync(IWin32Window owner, Action<string, bool> done)
        {
            var dir = Location.ForFileOperation();
            if (dir == null) { done(L("link.locNotFound"), true); return; }
            ParkResult parked = null;
            RunFlow(dir, d =>
            {
                parked = Parking.ParkAll(d);
                if (parked.Outcome == ParkOutcome.Failed || parked.Outcome == ParkOutcome.GameRunning)
                    return new InstallResult { Outcome = InstallOutcome.Failed, Reasons = { parked.Why ?? parked.Outcome.ToString() } };
                return Installer.Install(d, LoadPayload());
            }, r =>
            {
                if (parked != null && parked.Why != null) _host.AddLog("park: " + parked.Outcome + " " + parked.Why);
                if (parked != null && parked.Outcome == ParkOutcome.Failed) return string.Format(L("mods.failed"), parked.Moved.Count, parked.Why ?? "?") + (parked.Folder != null ? " " + string.Format(L("mods.keptAt"), parked.Folder) : "");
                if (IsFailure(r)) return ReinstallText(r) + (parked != null && parked.Moved.Count > 0 ? " " + L("mods.stillParked") : "");
                KeptSetupProblem = false; ForeignModsBlocked = false;
                return string.Format(L("mods.parkedInstalled"), parked == null ? 0 : parked.Moved.Count) + (r.Outcome == ReinstallOutcome.Relaunched ? " " + L("install.askedSteam") : "");
            }, done);
        }

        /// <summary>"이전 모드 되돌리기": remove NyangTalk's connector, then put the parked files back (hash-checked).</summary>
        public void RestoreAsync(IWin32Window owner, Action<string, bool> done)
        {
            var dir = Location.ForFileOperation();
            if (dir == null) { done(L("link.locNotFound"), true); return; }
            ParkResult back = null;
            RunFlow(dir, d =>
            {
                var u = Installer.Uninstall(d);
                if (u.Outcome != InstallOutcome.Done && u.Outcome != InstallOutcome.NothingToDo) return u;
                back = Parking.RestoreAll(d);
                return back.Outcome == ParkOutcome.Done || back.Outcome == ParkOutcome.NothingToPark ? new InstallResult { Outcome = InstallOutcome.Done } : new InstallResult { Outcome = InstallOutcome.Failed, Reasons = { back.Why ?? string.Join(", ", back.Skipped) } };
            }, r =>
            {
                if (back != null && (back.Why != null || back.Skipped.Count > 0)) _host.AddLog("restore: " + back.Outcome + " " + back.Why + " " + string.Join(", ", back.Skipped));
                if (back == null) return ReinstallText(r);   // the connector could not be removed
                if (back.Outcome != ParkOutcome.Done && back.Outcome != ParkOutcome.NothingToPark)
                    return string.Format(L("mods.restoreFailed"), back.Moved.Count, back.Why ?? string.Join(", ", back.Skipped.Take(3)) + (back.Skipped.Count > 3 ? " …" : "")) + (back.Folder != null ? " " + string.Format(L("mods.keptAt"), back.Folder) : "");
                return string.Format(L("mods.restored"), back.Moved.Count) + (r.Outcome == ReinstallOutcome.Relaunched ? " " + L("install.askedSteam") : "");
            }, done);
        }

        /// <summary>Runs a close-game → work → relaunch flow on a worker thread (no second question) and reports back on the UI thread.</summary>
        private void RunFlow(string dir, Func<string, InstallResult> work, Func<ReinstallReport, string> text, Action<string, bool> done)
        {
            var flow = new ReinstallFlow { Game = Game, Install = work, ConfirmRestart = () => true };
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ReinstallReport r;
                try { r = flow.Run(dir); }
                catch (Exception ex) { r = new ReinstallReport { Outcome = ReinstallOutcome.Failed, Why = ex.GetType().Name + ": " + ex.Message }; }
                if (_form.IsDisposed || !_form.IsHandleCreated) return;
                try
                {
                    _form.BeginInvoke((Action)(() =>
                    {
                        LastReinstall = r;
                        RefreshInstallState(dir);
                        string t;
                        try { t = text(r); } catch (Exception ex) { t = ex.Message; }
                        done(t, IsFailure(r) || t.StartsWith(L("mods.failedPrefix")));
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        /// <summary>Status line under an automation switch (90-SPEC 1.4): what the game component reports, never guessed.</summary>
        public string FeatureStatus(string key)
        {
            if (key == "char.scope" && _host.Prefs.Settings.GetBool("char.show") && _host.Prefs.Settings.Get(key) == "face")
            {
                NyangTalk.Domain.Update.SemVersion producer;
                var current = _host.Chat.Status;
                if (current != null && NyangTalk.Domain.Update.SemVersion.TryParse(current.ProducerVersion, out producer)
                    && producer.CompareTo(NyangTalk.Domain.Update.SemVersion.Parse("0.1.1")) < 0) return L("char.faceNeedsComponent");
            }
            if (key != "auto.chest" && key != "auto.gifts") return null;
            if (!_host.Prefs.Settings.GetBool(key)) return L("auto.status.Off");
            var st = _host.Chat.Status;
            if (!_host.Chat.Areas.IsAvailable(FeatureArea.Chest) || (st != null && st.ChestState == "unsupported")) return L("auto.status.Unsupported");
            var paused = AutomationPauseReason(key);
            if (paused != null) return string.Format(L("auto.status.Paused"), paused);
            if (st.ChestState == null) return string.Format(L("auto.status.Paused"), L("auto.reason.permit"));
            switch (st.ChestState)
            {
                case "waiting": return L("auto.status.Waiting");
                case "opening": return L("auto.status.Opening");
                case "paused": return string.Format(L("auto.status.Paused"), ReasonText(st.ChestReason));
                default: return string.Format(L("auto.status.Paused"), L("auto.reason.permit"));
            }
        }

        private string ReasonText(string reason)
        {
            return reason != null && Strings.Table.ContainsKey("auto.reason." + reason) ? L("auto.reason." + reason) : L("auto.reason.other");
        }

        /// <summary>A recent-log line in words ("열었어요", "포인트 부족으로 건너뜀"), never a raw code.</summary>
        public string LogLine(ChestLogEntry e)
        {
            var what = e.Success ? L("auto.log.opened") : string.Format(L("auto.log.skipped"), ReasonText(e.Reason));
            return e.TimeUtc.ToLocalTime().ToString("MM-dd HH:mm:ss") + " · " + KindText(e.ChestType) + " · " + what;
        }

        private string KindText(string kind)
        {
            if (kind == null) return "";
            // gift lines are "<name>'s chest · <kind>"
            int dot = kind.LastIndexOf(" · ", StringComparison.Ordinal);
            var raw = dot >= 0 ? kind.Substring(dot + 3) : kind;
            var name = raw == "Cosmetic" ? L("val.cosmetic") : raw == "Emote" ? L("val.emote") : raw;
            return dot >= 0 ? kind.Substring(0, dot) + " · " + name : name;
        }

        private void AutoPage(SettingsPage page)
        {
            page.Note(L("auto.risk"), true);            // fixed notice instead of a pop-up (2026-11-20 user decision)
            page.Group("auto", "group.myChest");
            page.Note(L("auto.giftsNotice"), true);
            page.Group("auto", "group.gifts");
            page.Heading(L("auto.logTitle"));
            var entries = Chest.Log.Reverse().Take(10).ToList();
            if (entries.Count == 0) page.Note(L("auto.logEmpty"), false);
            foreach (var e in entries) page.Note(LogLine(e), false);
        }

        // ---------------------------------------------------------------- updates (20-SPEC 2장, 4장)

        public UpdateSource UpdateSource;
        public string LastUpdateText, LastUpdateBand;
        /// <summary>Network and keys of the update check; replaced in checks so they never depend on the network or the app's key.</summary>
        public IHttp UpdateHttp = new RealHttp();
        public IList<byte[]> UpdateKeys = AppHost.UpdateKeys;

        /// <summary>One check now (button "지금 확인"). Runs synchronously; the button calls it on a worker thread.</summary>
        private UpdateService _lastService;

        public UpdateCheck RunUpdateCheck()
        {
            try { return RunUpdateCheckCore(); }
            catch (Exception ex)   // never an unhandled exception on the worker thread; never "최신" after a failure
            {
                _host.AddLog("update: " + ex.GetType().Name + ": " + ex.Message);
                LastUpdateBand = null;
                LastUpdateText = string.Format(L("update.failed"), _host.Clock.UtcNow.ToLocalTime().ToString("HH:mm"));
                return null;
            }
        }

        private UpdateCheck RunUpdateCheckCore()
        {
            var src = UpdateSource ?? (UpdateSource = UpdateSource.Resolve(_host.DataRoot));
            var svc = new UpdateService(UpdateHttp, _host.Docs, AppHost.Version, UpdateKeys, _host.DataRoot);
            var channel = _host.Prefs.Settings.Get("update.channel");
            svc.InfoUrl = src.InfoUrl(channel);
            svc.Channel = channel;
            _lastService = svc;
            var now = _host.Clock.UtcNow;
            UpdateCheck r;
            try { r = svc.Check(now); }
            catch (Exception ex) { _host.AddLog("update: " + ex.GetType().Name + ": " + ex.Message); r = new UpdateCheck { Status = UpdateStatus.CheckFailed }; }
            var when = now.ToLocalTime().ToString("HH:mm");
            LastUpdateBand = null;
            if (r.Status != UpdateStatus.NoUpdate && r.Status != UpdateStatus.UpdateAvailable)
                _host.AddLog("update: " + r.Status + (r.Detail != null ? " " + r.Detail : "") + " (" + svc.InfoUrl + ")");   // the reason goes into 진단 내용
            switch (r.Status)
            {
                case UpdateStatus.NotConfigured: LastUpdateText = string.Format(L("update.failed"), when) + " · " + L("update.noKey"); break;
                case UpdateStatus.NoUpdate: LastUpdateText = string.Format(L("update.upToDate"), when); break;
                case UpdateStatus.UpdateAvailable: case UpdateStatus.ManualOnly:
                    LastUpdateText = string.Format(L("update.checkedAt"), when);
                    LastUpdateBand = string.Format(L("update.available"), r.Info == null ? "?" : r.Info.Version.ToString()); break;
                case UpdateStatus.BadSignature: LastUpdateText = L("update.badSignature") + " · " + when; break;
                case UpdateStatus.Refused: case UpdateStatus.BadInfo: case UpdateStatus.StateNotSaved:
                    LastUpdateText = string.Format(L("update.refused"), r.Detail ?? r.Status.ToString()) + " · " + when; break;
                default: LastUpdateText = string.Format(L("update.failed"), when); break;   // 첫 릴리스 전에도 여기: "최신"이라고 말하지 않음
            }
            return r;
        }

        /// <summary>Version → 지금 확인 → result (band when new) → mode/channel → source (read-only) → license, privacy, risk.</summary>
        private void AboutPage(SettingsPage page)
        {
            var src = UpdateSource ?? (UpdateSource = UpdateSource.Resolve(_host.DataRoot));
            page.Heading(L("group.version"));
            page.Note(string.Format(L("update.current"), AppVersion.Text), false);
            var band = page.Band(LastUpdateBand ?? "");
            var result = (Label)null;
            var check = page.Button(L("update.checkNow"), null, "tip.update.checkNow");
            check.Click += (s, e) =>
            {
                check.Enabled = false;
                result.Text = L("update.checking"); page.Relayout();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    RunUpdateCheck();
                    if (check.IsDisposed) return;
                    if (!check.IsHandleCreated) return;
                    try
                    {
                        check.BeginInvoke((Action)(() =>
                        {
                            if (check.IsDisposed) return;
                            check.Enabled = true; result.Text = LastUpdateText; band.Text = LastUpdateBand ?? ""; page.Relayout();
                        }));
                    }
                    catch (InvalidOperationException) { }   // window closed meanwhile; the result stays in LastUpdateText
                });
            };
            page.Buttons(check);
            result = page.Note(LastUpdateText ?? "", false);
            page.Group("about", "group.updates");
            var link = new LinkLabel { Text = string.Format(L("update.source"), UpdateSource.DisplayName), AutoSize = false, UseMnemonic = false };
            link.LinkClicked += (s, e) => OpenReleases();
            page.Tips.Set(link, L("tip.update.source"));
            page.Custom(link, w => TextRenderer.MeasureText(link.Text, link.Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height + 4);
            if (src.Overridden) page.Note(string.Format(L("update.overrideInUse"), src.OverrideUrl), true);
            else if (src.IgnoredOverride != null) page.Note(L("update.overrideIgnored"), true);
            page.Heading(L("group.aboutInfo"));
            page.Buttons(page.Button(L("about.license"), ShowLicenses, "tip.about.license"));
            page.Note(L("about.privacy"), false);
            page.Note(L("about.modRisk"), false);
        }

        public static void OpenReleases()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateSource.ReleasesPage) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { var f = Shell.Link; if (f != null) f._form.ShowNotice("releases:" + DateTime.UtcNow.Ticks, string.Format(f.L("update.releasesFailed"), UpdateSource.ReleasesPage, ex.Message)); }
        }

        private void ShowLicenses()
        {
            var asm = typeof(LinkServices).Assembly;
            var sb = new StringBuilder();
            foreach (var name in new[] { "LICENSE", "THIRD-PARTY-NOTICES.md" }.Concat(asm.GetManifestResourceNames().Where(n => n.StartsWith("licenses/")).OrderBy(n => n)))
                using (var s = asm.GetManifestResourceStream(name))
                    if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) sb.AppendLine("==== " + name + " ====").AppendLine(r.ReadToEnd());
            var text = sb.Length > 0 ? sb.ToString() : "LICENSE / THIRD-PARTY-NOTICES.md";
            using (var f = new Form { Text = L("about.license"), ClientSize = new Size(640, 520), StartPosition = FormStartPosition.CenterParent })
            {
                Dialogs.Themed(f, _host.Prefs);
                f.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = text.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n") });
                var owner = Form.ActiveForm ?? _form;
                Placement.Over(f, owner);
                f.ShowDialog(owner);
            }
        }

        public Payload LoadPayload()
        {
            return Payload.Load(typeof(LinkServices).Assembly, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload"), GameLinkVersion);
        }

        public const string GameLinkVersion = "0.1.4";

        public string Describe(InstallResult r)
        {
            var L = (Func<string, string>)_host.Prefs.L;
            switch (r.Outcome)
            {
                case InstallOutcome.Done: return L("install.done");
                case InstallOutcome.NothingToDo: return L("install.nothing");
                case InstallOutcome.PrecheckFailed: return r.KeptForeignLoader ? string.Format(L("install.keptSetupMix"), r.Reasons.Count) : L("install.precheckFailed");
                case InstallOutcome.LoaderMissing: return L("install.payloadMissing");
                case InstallOutcome.RecordUnreadable: return L("install.recordUnreadable");
                case InstallOutcome.Blocked:
                    if (r.Block == PlanBlock.GameRunning) return L("install.gameRunning");
                    if (r.Block == PlanBlock.ForeignLoader) { ForeignModsBlocked = true; return string.Format(L("install.foreign"), string.Join(", ", r.Reasons)); }
                    return string.Format(L("install.modified"), string.Join(", ", r.Reasons));
                default: return r.ToString();
            }
        }

        /// <summary>No message bodies, names or full Steam IDs (90-SPEC 5장).</summary>
        public string Diagnostics()
        {
            var up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sb = new StringBuilder();
            sb.AppendLine("NyangTalk " + AppVersion.Text);
            var st = _host.Chat.Status;
            sb.AppendLine("component: " + (st == null ? "-" : st.ProducerVersion) + " schema: " + (st == null ? "-" : st.Schema));
            sb.AppendLine("link: " + _host.Chat.Link + " " + Status);
            sb.AppendLine("fingerprint: " + (Check.LastFingerprint == null ? "-" : Check.LastFingerprint.Id) + " unity: " + (Check.LastFingerprint == null ? "-" : Check.LastFingerprint.UnityPlayerVersion));
            sb.AppendLine("areas broken: " + string.Join(",", _host.Chat.Areas.Broken));
            sb.AppendLine("my id: " + (st == null ? "-" : DiagnosticsSanitizer.MaskSteamIds(st.MySteamId ?? "")));
            sb.AppendLine("recent log:");
            foreach (var l in _host.RecentLog(20)) sb.AppendLine("  " + DiagnosticsSanitizer.Line(l, up));
            return sb.ToString();
        }

        /// <summary>Turning an automation switch on records the consent (no dialog); if the record cannot be saved the switch stays off.</summary>
        public bool BeforeChange(IWin32Window owner, string key, string value)
        {
            if (key == "link.startWithGame") return SetStartWithGame(value == "true");
            if (key == "auto.chest" && value == "true") return RecordConsent("automation-consent");
            if (key == "auto.gifts" && value == "true") return RecordConsent("automation-consent-gifts");
            return true;
        }
    }
}
