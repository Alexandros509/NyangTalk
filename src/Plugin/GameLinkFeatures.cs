using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NyangTalk.Shared;
using UnityEngine;

namespace NyangTalk.GameLink
{
    /// <summary>
    /// Features added by the 2026-11-19 20:28 spec update: chat-filter check (50-SPEC 7장), emote pictures (50-SPEC 2장),
    /// cat portraits (70-SPEC 2.2), automatic chest opening (80-SPEC 1.2). All game access goes through contract IDs.
    /// None of this has been run inside the game yet (docs/manual-checklist.md).
    /// </summary>
    public sealed partial class GameLinkPlugin
    {
        private readonly PictureQueue _emotes = new PictureQueue();
        private readonly PictureQueue _portraits = new PictureQueue();
        private readonly Dictionary<string, string> _emoteNames = new Dictionary<string, string>();
        private readonly Dictionary<string, KeyValuePair<int, int>> _looks = new Dictionary<string, KeyValuePair<int, int>>(); // steam id → (skin, hat)
        private readonly ChestAutomaton _chest = new ChestAutomaton();
        private readonly string _gameSession = Guid.NewGuid().ToString("N");
        private bool _pictureLoop;

        private void StartPictureLoop()
        {
            if (_pictureLoop) return;
            _pictureLoop = true;
            _emotes.FileExists = f => File.Exists(Path.Combine(_dir, f));
            _portraits.FileExists = _emotes.FileExists;
            StartCoroutine(PictureLoop());
        }

        // ------------------------------------------------------------ 50-SPEC 7장: chat filter

        /// <summary>true: the game's filter would block the text; false: passes; null: filter off, not ready or unknown.</summary>
        private bool? FilterBlocks(string text, out bool punctuation)
        {
            punctuation = false;
            if (!_c.Has("settings.chatFilter") || !_c.Has("bwf.contains")) return null;
            var settings = _c.Get("settings.instance", null);
            var toggle = settings == null ? null : _c.Get("settings.chatFilter", settings);
            var on = toggle == null ? null : _c.Get("toggle.value", toggle);
            if (!(on is bool) || !(bool)on) return null;          // filter off or unknown: no check
            var bwf = _c.Get("bwf.instance", null);
            var ready = bwf == null ? null : _c.Get("bwf.ready", bwf);
            if (!(ready is bool) || !(bool)ready) return null;    // not ready: no check
            var all = _c.Get("bwf.maskAll", null);
            var blocked = _c.Call("bwf.contains", bwf, text, all, null);
            if (!(blocked is bool)) return null;
            if ((bool)blocked && _c.Has("bwf.maskPunctuation"))
            {
                var p = _c.Call("bwf.contains", bwf, text, _c.Get("bwf.maskPunctuation", null), null);
                punctuation = p is bool && (bool)p;
            }
            return (bool)blocked;
        }

        // ------------------------------------------------------------ pictures

        private string EmoteName(int id)
        {
            string n;
            var key = id.ToString();
            if (_emoteNames.TryGetValue(key, out n)) return n;
            var item = Item(id);
            n = item == null ? null : _c.Get("item.name", item) as string;
            _emoteNames[key] = n;
            return n;
        }

        private object Item(int id)
        {
            var reg = _c.Get("items.inst", null);
            if (reg == null || !_c.Has("items.tryGet")) return null;
            var args = new object[] { id, null };
            var ok = _c.CallWithArgs("items.tryGet", reg, args);
            return ok is bool && (bool)ok ? args[1] : null;
        }

        private void RequestEmotePicture(int id)
        {
            StartPictureLoop();
            _emotes.Request(id.ToString());
            EmoteName(id);
        }

        private IEnumerator PictureLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                // one picture per frame, emotes first (they are small)
                var key = _emotes.Next();
                bool emote = key != null;
                if (key == null) key = _portraits.Next();
                if (key == null) continue;
                try
                {
                    var file = emote ? MakeEmotePicture(int.Parse(key)) : MakePortrait(key);
                    if (file != null) (emote ? _emotes : _portraits).Succeeded(key, file);
                    else (emote ? _emotes : _portraits).Failed(key);
                }
                catch (Exception ex)
                {
                    (emote ? _emotes : _portraits).Failed(key);
                    Logger.LogWarning("picture " + key + ": " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private string MakeEmotePicture(int id)
        {
            var item = Item(id);
            var icon = item == null ? null : _c.Get("item.icon", item) as Sprite;
            if (icon == null) return null;
            var rel = Exchange.EmoteDir + "/" + id + ".png";
            var png = Render(new List<KeyValuePair<Sprite, Rect>> { new KeyValuePair<Sprite, Rect>(icon, new Rect(0, 0, 1, 1)) }, 64);
            if (png == null) return null;
            Save(rel, png);
            return rel;
        }

        /// <summary>70-SPEC 2.2.2: the person's cat in the scene if found, otherwise the item pictures.</summary>
        private string MakePortrait(string key)
        {
            bool face = PortraitFiles.IsFaceKey(key);
            var parts = PortraitFiles.FullKey(key).Split('_');
            int skin = int.Parse(parts[0]), hat = int.Parse(parts[1]);
            var layers = SceneCatLayers(skin, hat, !face) ?? ItemLayers(skin, face ? 0 : hat);
            if (layers == null || layers.Count == 0) return null;
            var rel = Exchange.PortraitDir + "/" + key + ".png";
            var png = Render(layers, 128);
            if (png == null) return null;
            Save(rel, png);
            return rel;
        }

        private List<KeyValuePair<Sprite, Rect>> ItemLayers(int skin, int hat)
        {
            var r = new List<KeyValuePair<Sprite, Rect>>();
            foreach (var id in new[] { skin, hat })
            {
                if (id <= 0) continue; // no hat / default
                var item = Item(id);
                var full = item == null ? null : _c.Get("item.fullImage", item) as Sprite;
                if (full != null) r.Add(new KeyValuePair<Sprite, Rect>(full, new Rect(0, 0, 1, 1)));
            }
            return r.Count == 0 ? null : r;
        }

        /// <summary>Finds a scene cat with this look and lays out body → paw → hat in the body's coordinate space.</summary>
        private List<KeyValuePair<Sprite, Rect>> SceneCatLayers(int skin, int hat, bool includeHat = true)
        {
            object cat = null;
            foreach (var kv in _looks)
            {
                if (kv.Value.Key != skin || kv.Value.Value != hat) continue;
                cat = SceneCatOf(kv.Key);
                if (cat != null) break;
            }
            if (cat == null) return null;
            var spritesAndImages = new List<KeyValuePair<Sprite, Component>>();
            Action<string, string> add = (spriteId, imageId) =>
            {
                var sp = _c.Call(spriteId, cat) as Sprite;
                var img = _c.Get(imageId, cat) as Component;
                if (sp != null && img != null && img.gameObject.activeInHierarchy) spritesAndImages.Add(new KeyValuePair<Sprite, Component>(sp, img));
            };
            add("cat.baseSprite", "cat.bodyImage");
            add("cat.frontSprite", "cat.pawImage");
            if (includeHat && hat > 0) add("cat.hatSprite", "cat.hatImage");
            if (spritesAndImages.Count == 0) return null;
            // bounds of all layers in the body's local space
            var body = spritesAndImages[0].Value.transform;
            var corners = new Vector3[4];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var rects = new List<Rect>();
            foreach (var p in spritesAndImages)
            {
                var rt = p.Value.transform as RectTransform;
                if (rt == null) return null;
                rt.GetWorldCorners(corners);
                var a = body.InverseTransformPoint(corners[0]);
                var b = body.InverseTransformPoint(corners[2]);
                var r = Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
                rects.Add(r);
                minX = Math.Min(minX, r.xMin); minY = Math.Min(minY, r.yMin); maxX = Math.Max(maxX, r.xMax); maxY = Math.Max(maxY, r.yMax);
            }
            float w = maxX - minX, h = maxY - minY, side = Math.Max(w, h);
            if (side <= 0) return null;
            float ox = (side - w) / 2, oy = (side - h) / 2;
            var layers = new List<KeyValuePair<Sprite, Rect>>();
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                // output space is y-down, normalised to the square
                layers.Add(new KeyValuePair<Sprite, Rect>(spritesAndImages[i].Key,
                    new Rect((r.xMin - minX + ox) / side, (maxY - r.yMax + oy) / side, r.width / side, r.height / side)));
            }
            return layers;
        }

        private object SceneCatOf(string steamId)
        {
            if (steamId == MyId().ToString() && _c.Has("cat.instance")) return _c.Get("cat.instance", null);
            var t = _c.TypeOf("memberInstance.type");
            if (t == null) return null;
            foreach (var o in Resources.FindObjectsOfTypeAll(t))
            {
                if (!InLoadedScene(o)) continue;
                var sud = _c.Get("memberInstance.user", o);
                var data = sud == null ? null : _c.Get("steamUserData.data", sud);
                var id = data == null ? null : _c.Get("user.steamId", data);
                if (id is ulong && ((ulong)id).ToString() == steamId) return _c.Get("memberInstance.cat", o);
            }
            return null;
        }

        private static Material _spriteMaterial;
        private static int _mainTex;

        /// <summary>
        /// Draws sprite layers into a temporary render texture with immediate-mode quads and returns PNG bytes. Uses only
        /// engine members present in the game's stripped modules (no DrawTexture/PushMatrix there). Runs at end of frame;
        /// the render texture and the read-back texture are destroyed before returning.
        /// </summary>
        private static byte[] Render(List<KeyValuePair<Sprite, Rect>> layers, int size)
        {
            if (_spriteMaterial == null)
            {
                var shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
                if (shader == null) return null;
                _spriteMaterial = new Material(shader);
                _mainTex = Shader.PropertyToID("_MainTex");
            }
            var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D outTex = null;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0, 0, 0, 0));
                GL.LoadPixelMatrix(0, size, size, 0);
                foreach (var l in layers)
                {
                    _spriteMaterial.SetTexture(_mainTex, l.Key.texture);
                    _spriteMaterial.SetPass(0);
                    // sprite rectangle inside its texture, as 0..1 coordinates (v grows upward)
                    var tex = l.Key.texture; var sr = l.Key.rect;
                    var uv = new Vector4(sr.xMin / tex.width, sr.yMin / tex.height, sr.xMax / tex.width, sr.yMax / tex.height);
                    float x0 = l.Value.x * size, y0 = l.Value.y * size, x1 = (l.Value.x + l.Value.width) * size, y1 = (l.Value.y + l.Value.height) * size;
                    GL.Begin(7); // QUADS
                    GL.TexCoord2(uv.x, uv.w); GL.Vertex3(x0, y0, 0);
                    GL.TexCoord2(uv.z, uv.w); GL.Vertex3(x1, y0, 0);
                    GL.TexCoord2(uv.z, uv.y); GL.Vertex3(x1, y1, 0);
                    GL.TexCoord2(uv.x, uv.y); GL.Vertex3(x0, y1, 0);
                    GL.End();
                }
                outTex = new Texture2D(size, size, TextureFormat.ARGB32, false);
                outTex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                outTex.Apply();
                return ImageConversion.EncodeToPNG(outTex);
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(rt);
                if (outTex != null) UnityEngine.Object.Destroy(outTex);
            }
        }

        private void Save(string rel, byte[] png)
        {
            var path = Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { fs.Write(png, 0, png.Length); fs.Flush(); }
        }

        /// <summary>Portrait file for a look, asking for one if missing.</summary>
        private string PortraitFor(string steamId, int skin, int hat)
        {
            _looks[steamId] = new KeyValuePair<int, int>(skin, hat);
            StartPictureLoop();
            var key = skin + "_" + hat;
            return PortraitFiles.RequestPair(_portraits, key); // same queue: still only one render per frame
        }

        private Dictionary<string, object> EmoteStatus()
        {
            var d = new Dictionary<string, object>();
            foreach (var kv in _emotes.Done) d[kv.Key] = new Dictionary<string, object> { { "file", kv.Value }, { "name", EmoteNameOrNull(kv.Key) } };
            foreach (var kv in _emoteNames)
                if (!d.ContainsKey(kv.Key)) d[kv.Key] = new Dictionary<string, object> { { "file", null }, { "name", kv.Value }, { "given_up", _emotes.GivenUp(kv.Key) } };
            return d;
        }

        private string EmoteNameOrNull(string key) { string n; return _emoteNames.TryGetValue(key, out n) ? n : null; }

        // ------------------------------------------------------------ 80-SPEC 1.2: chests

        private sealed class Shop : IChestShop
        {
            private readonly ContractRuntime _c; private readonly object _s;
            public Shop(ContractRuntime c, object s) { _c = c; _s = s; }
            private bool B(string id) { var v = _c.Get(id, _s); return v is bool && (bool)v; }
            public string Kind { get { var v = _c.Get("shop.chestType", _s); return v == null ? "" : v.ToString(); } }
            public bool Ready { get { return B("shop.ready"); } }
            public bool CanGet { get { return B("shop.canGet"); } }
            public bool CanAfford { get { return B("shop.canAfford"); } }
            public bool Opening { get { return B("shop.opening"); } }
            public void Click() { _c.Call("shop.click", _s); }
        }

        private sealed class Gift : IGiftChest
        {
            private readonly ContractRuntime _c; private readonly Component _o; private readonly string _member;
            public Gift(ContractRuntime c, Component o, string member) { _c = c; _o = o; _member = member; }
            public string Member { get { return _member; } }
            public string Kind { get { var v = _c.Get("gift.chestType", _o); return v == null ? "" : v.ToString(); } }
            public bool Ready
            {
                get
                {
                    var clicked = _c.Get("gift.clicked", _o);
                    return _o != null && _o.gameObject.activeInHierarchy && clicked is bool && !(bool)clicked;
                }
            }
            public bool CanAfford
            {
                get
                {
                    var shop = _c.Get("gift.shop", _o);
                    var v = shop == null ? null : _c.Get("shop.canAfford", shop);
                    return v is bool && (bool)v;
                }
            }
            public void Click() { _c.Call("gift.click", _o); }
        }

        /// <summary>Other members' chest buttons that are in the loaded scene.</summary>
        private List<IGiftChest> GiftChests()
        {
            var r = new List<IGiftChest>();
            var t = _c.TypeOf("gift.type");
            if (t == null || !_c.Has("gift.click")) return r;
            foreach (var o in Resources.FindObjectsOfTypeAll(t))
            {
                if (!InLoadedScene(o)) continue;
                var sud = _c.Get("gift.target", o);
                var data = sud == null ? null : _c.Get("steamUserData.data", sud);
                var id = data == null ? null : _c.Get("user.steamId", data);
                if (id is ulong) r.Add(new Gift(_c, (Component)o, ((ulong)id).ToString()));
            }
            return r;
        }

        private ChestPermit ReadPermit()
        {
            var p = Path.Combine(_dir, Exchange.PermitFile);
            byte[] bytes;
            try { bytes = ExchangeFiles.ReadAllBytes(p); }
            catch (FileNotFoundException) { return null; }
            object o;
            if (!Json.TryParse(Encoding.UTF8.GetString(bytes), out o)) return null;
            var d = Json.Obj(o);
            var chest = Json.Obj(d != null && d.ContainsKey("chest") ? d["chest"] : null);
            DateTime w;
            if (chest == null || !Exchange.TryIso(Json.Str(d, "written_utc"), out w)) return null;
            var permit = new ChestPermit
            {
                Enabled = Json.Bool(chest, "enabled", false), Gifts = Json.Bool(chest, "gifts", false), DelaySeconds = (int)Json.Long(chest, "delay", 3),
                MaxPerHour = (int)Json.Long(chest, "max_per_hour", 20), WrittenUtc = w, Session = Json.Str(d, "session")
            };
            foreach (var k in Json.List(chest, "kinds") ?? new List<object>()) { var s = k as string; if (s != null) permit.Kinds.Add(s); }
            // the permit is only for this lobby and this game run
            if (permit.Session != _lobbyId + ":" + _gameSession) { permit.Enabled = false; permit.Gifts = false; }
            return permit;
        }

        private void ChestTick()
        {
            if (!_c.Areas.ContainsKey("Chest") || !_c.Areas["Chest"]) { _chest.State = "unsupported"; return; }
            var shops = new List<IChestShop>();
            foreach (var id in new[] { "shop.normal", "shop.emote" })
            {
                var s = _c.Get(id, null);
                if (s != null) shops.Add(new Shop(_c, s));
            }
            var ex = _c.Get("chest.exchanging", null);
            int before = _chest.Events.Count;
            _chest.State = "paused"; _chest.Reason = "permit";
            var permit = ReadPermit();
            _chest.Tick(DateTime.UtcNow, permit, shops, ex is bool && (bool)ex,
                permit != null && permit.Gifts ? GiftChests() : null, MyId().ToString(), _currentMembers);
            for (int i = before; i < _chest.Events.Count; i++)
            {
                var e = _chest.Events[i];
                var line = Json.Write(new Dictionary<string, object> { { "schema", Exchange.Schema }, { "time_utc", Exchange.Iso(e.TimeUtc) }, { "kind", e.Kind }, { "member", e.Member }, { "called", e.Called }, { "reason", e.Reason } }) + "\n";
                var bytes = new UTF8Encoding(false).GetBytes(line);
                using (var fs = new FileStream(Path.Combine(_dir, Exchange.ChestLogFile), FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) fs.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
