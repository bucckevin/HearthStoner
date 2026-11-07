using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetsTools.NET.Extra;
using HearthStoner.Art;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HearthStoner
{
    /// <summary>
    /// Pulls the replay board's art out of your own Hearthstone install (it only reads the game's files):
    ///   board\*.png            the playing field, frames, gems, icons, end-turn button and effects (textures, cut out)
    ///   board\layout.json      where every board element sits on the 800x600 canvas (see <see cref="BoardLayout"/>)
    ///   board\endturn-btn-*.png  the game's 3D End Turn button, drawn in its three states
    ///   boards\&lt;ROOT&gt;.jpg      every game board (STW, ULD, ...) rendered empty through the gameplay camera, 1600x1200
    ///   boards\boards.txt      one line per board: ROOT, a tab, the name of its play-area texture
    /// The bundle index (which bundle holds which internal file, and the boards) is cached in boards\.cache and rebuilt
    /// after a Hearthstone patch. That art is Blizzard's: for your own use on this PC.
    /// </summary>
    public static class GameArtBuilder
    {
        /// <summary>Optional detail lines (skipped boards, timings); the CLI prints them.</summary>
        internal static Action<string> Log;

        public static void Run(string hearthstoneDir, string outDir, Action<string, int, int> progress, CancellationToken ct)
        {
            progress ??= (_, _, _) => { };
            if (!Directory.Exists(Path.Combine(hearthstoneDir ?? "", "Data", "Win")))
                throw new DirectoryNotFoundException("No Hearthstone install at " + hearthstoneDir);
            string board = Path.Combine(outDir, "board"), boards = Path.Combine(outDir, "boards");
            Directory.CreateDirectory(board);
            Directory.CreateDirectory(boards);
            var errors = new List<string>();
            void Step(string what, Action a)
            {
                ct.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                try { a(); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { errors.Add($"{what}: {e.Message}"); Log?.Invoke($"{what} failed: {e}"); }
                Log?.Invoke($"{what}: {sw.Elapsed.TotalSeconds:0.0}s");
            }
            Step("Textures", () => BoardArt(hearthstoneDir, board, progress, ct));
            Index idx = null;
            Step("Index", () => idx = LoadIndex(hearthstoneDir, Path.Combine(boards, ".cache", "index.json"), progress, ct));
            if (idx != null)
            {
                Step("Boards", () => Boards(hearthstoneDir, idx, boards, progress, ct));
                Step("Layout", () =>
                {
                    progress("Layout", 0, 1);
                    BoardLayout.Build(hearthstoneDir, idx.Cabs, idx.Boards, ReadCamera(hearthstoneDir, idx), board, progress, ct);
                });
            }
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        }

        /// <summary>The installed game build, e.g. "37.0.0.255790" (from the launcher's .product.db or build config).</summary>
        public static string GameVersion(string hearthstoneDir)
        {
            try
            {
                var db = File.ReadAllBytes(Path.Combine(hearthstoneDir, ".product.db"));
                var m = Regex.Match(Encoding.ASCII.GetString(db), @"\b\d+\.\d+\.\d+\.\d{4,}\b");
                if (m.Success) return m.Value;
            }
            catch (Exception) { }
            try
            {
                foreach (var f in new DirectoryInfo(Path.Combine(hearthstoneDir, "Data", "config")).EnumerateFiles("*", SearchOption.AllDirectories)
                             .Where(f => f.Length < 64 * 1024).OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    var m = Regex.Match(File.ReadAllText(f.FullName), @"build-name = (\d+)_([\d.]+)");
                    if (m.Success) return m.Groups[2].Value + "." + m.Groups[1].Value;
                }
            }
            catch (Exception) { }
            try
            {
                // else when the game data last changed
                return "data-" + new DirectoryInfo(Path.Combine(hearthstoneDir, "Data", "Win")).EnumerateFiles("*.unity3d")
                    .Max(f => f.LastWriteTimeUtc).ToString("yyyyMMddHHmmss");
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ textures (board art)
        private static readonly string[] Want =
        {
            "GenFX_Card_InPlay_AllyFrame", "HeroFrame_Silver", "GameplayArea_D", "Board_EndTurnButton", "Armor_D",
            "DivineShield_Bubble2", "FrozenFrost_D", "Gem_Attack_D_Fake", "Gem_Health_D_Fake", "GenFX_ally_taunt_d",
            "Hero_Power_D", "Hero_Power_Frame", "GenFX_Card_Back_Default_D", "HistoryTileIcons", "Card_InHand_Legendary",
            "GenFX_Deathrattle_Skull", "Trigger", "Lifesteal_Icon", "Poisonous", "History_Tiles_D", "Gem_Mana_Red",
            "Gem_LockMana_D", "Secret_D", "Sigil_DemonHunter",
        };

        private static string[] Bundles(string hs) =>
            Directory.GetFiles(Path.Combine(hs, "Data", "Win"), "*.unity3d").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray();

        /// <summary>Finds the wanted textures in the game's essential/initial texture bundles (first one of each name).</summary>
        private static Dictionary<string, Img> Extract(string hs, Action<string, int, int> progress, CancellationToken ct)
        {
            var found = new Dictionary<string, Img>();
            var want = new HashSet<string>(Want);
            var bundles = Bundles(hs).Where(b => (b.StartsWith("essential_base") || b.StartsWith("initial_base")) && (b.Contains("texture") || b.Contains("qualityhigh"))).ToList();
            for (int i = 0; i < bundles.Count && found.Count < want.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress("Textures", i, bundles.Count);
                using var ua = new UnityAssets(hs, null);
                try { ua.Open(bundles[i]); }
                catch (Exception) { continue; }
                foreach (var f in ua.Files)
                    foreach (var o in ua.Objects(f, AssetClassID.Texture2D))
                    {
                        try
                        {
                            var name = ua.PeekName(o);
                            if (name != null && want.Contains(name) && !found.ContainsKey(name)) found[name] = ua.ReadTexture(o);
                        }
                        catch (Exception) { }
                    }
            }
            return found;
        }

        private static void BoardArt(string hs, string outDir, Action<string, int, int> progress, CancellationToken ct)
        {
            var images = Extract(hs, progress, ct);
            var missing = Want.Where(n => !images.ContainsKey(n)).ToList();
            if (missing.Count > 0) Log?.Invoke("Not found (renamed by a patch?): " + string.Join(", ", missing));
            Img Load(string n) => images.TryGetValue(n, out var im) ? im : throw new FileNotFoundException("texture not found in the game: " + n);
            void Save(Img im, string n) => SavePng(im, Path.Combine(outDir, n + ".png"));

            // Minion frame: the stone oval, ring only (the sheet also holds other pieces).
            var fr = Load("GenFX_Card_InPlay_AllyFrame");
            var outer = Ellipse(fr, 63, 2, 463, 498); var inner = Ellipse(fr, 125, 50, 405, 470);
            Save(Pixels.Crop(WithAlpha(fr, i => Math.Min(outer.P[i], 255 - inner.P[i])), 58, 0, 468, 504), "minion-frame");

            // Hero frame: white around and inside the arch becomes see-through.
            fr = Load("HeroFrame_Silver");
            var hero = WithAlpha(fr, i =>
            {
                int x = i % fr.W, y = i / fr.W;
                if (y < 150 && x < 120) return 0;   // a stray piece of another texture in the corner
                double white = Math.Min(fr.P[i * 4], Math.Min(fr.P[i * 4 + 1], fr.P[i * 4 + 2]));   // high only where all channels are
                return (232 - white) * 255 / 52;    // 180 and below: opaque; 232 and up: clear (the white glow too)
            });
            Save(Pixels.CropToContent(hero), "hero-frame");

            // Board: the classic Stormwind playing field.
            Save(Load("GameplayArea_D"), "board");

            // End turn button: yellow (moves left) and green (nothing left), and the plain one for the opponent's turn.
            var btn = Load("Board_EndTurnButton");
            void OvalCut(int x0, int y0, int x1, int y1, string key)
            {
                var a = Ellipse(btn, x0, y0, x1, y1, 1.5f);
                Save(Pixels.Crop(WithAlpha(btn, i => a.P[i]), x0, y0, x1, y1), key);
            }
            int w = btn.W, h = btn.H;
            OvalCut((int)(w * 0.03), (int)(h * 0.27), (int)(w * 0.52), (int)(h * 0.48), "endturn-yellow");
            OvalCut((int)(w * 0.03), (int)(h * 0.62), (int)(w * 0.52), (int)(h * 0.83), "endturn-green");
            OvalCut((int)(w * 0.53), (int)(h * 0.27), (int)(w * 0.98), (int)(h * 0.48), "endturn-grey");

            // Armor: the whole shield on the left of its sheet; grey background becomes clear.
            var ar = Load("Armor_D");
            int bg = (3 * ar.W + 3) * 4;
            Save(Pixels.Crop(WithAlpha(ar, i => 6.0 * (Math.Abs(ar.P[i * 4] - ar.P[bg]) + Math.Abs(ar.P[i * 4 + 1] - ar.P[bg + 1]) + Math.Abs(ar.P[i * 4 + 2] - ar.P[bg + 2]))),
                12, 8, 98, 122), "armor");

            // Minion and hero stats: the gold attack gem with its sword, and the blood-drop health gem (cut to its shape,
            // since a stone strip from another texture sits right next to it).
            Save(Pixels.Cutout(Load("Gem_Attack_D_Fake"), 18, 80, 72), "attack");
            var hp = Load("Gem_Health_D_Fake");
            var m = new Img(hp.W, hp.H, 1);
            Pixels.Ellipse(m, 27, 49, 102, 123, 255);
            Pixels.Polygon(m, new[] { (64, 6), (30, 80), (98, 80) }, 255);
            Save(Pixels.CropToContent(Pixels.PutAlpha(hp, Pixels.Blur(m, 0.8f))), "health");
            Save(Pixels.Cutout(Load("GenFX_ally_taunt_d"), 8, 128, 140), "taunt");
            // Hero power: the round frame (its hole shows the power's picture) and the coin shown once it's used.
            fr = Load("Hero_Power_Frame");
            outer = Ellipse(fr, 8, 6, 248, 240, 3); inner = Ellipse(fr, 46, 60, 200, 210, 1.2f);
            Save(Pixels.Crop(WithAlpha(fr, i => Math.Min(outer.P[i], 255 - inner.P[i])), 6, 4, 250, 242), "heropower-frame");
            hp = Load("Hero_Power_D");
            var used = Ellipse(hp, 16, 30, 226, 228, 1.5f);
            Save(Pixels.Crop(WithAlpha(hp, i => used.P[i]), 14, 28, 228, 230), "heropower-used");
            // Card back (the classic one) with rounded corners, and the play-history icons (a bolt for plays, a sword for attacks).
            var cb = Pixels.Crop(Load("GenFX_Card_Back_Default_D"), 96, 2, 422, 508);
            m = new Img(cb.W, cb.H, 1);
            Pixels.RoundedRectangle(m, 0, 0, cb.W - 1, cb.H - 1, 22, 255);
            Save(Pixels.PutAlpha(cb, m), "card-back");
            var icons = Load("HistoryTileIcons");
            Save(Pixels.Cutout(icons, 25, 70, 70), "history-play");
            Save(Pixels.Cutout(icons, 25, 180, 90), "history-attack");

            // The legendary dragon the game wraps over a legendary minion's frame (the top half of its sheet; the bottom is its shadow).
            Save(Pixels.Cutout(Pixels.Crop(Load("Card_InHand_Legendary"), 0, 0, 256, 126), 30, 130, 30), "legendary");
            // Keyword icons shown at the bottom of a minion.
            Save(Pixels.Cutout(Load("GenFX_Deathrattle_Skull"), 14, 128, 128), "deathrattle");
            Save(Pixels.Cutout(Load("Poisonous"), 12, 128, 140), "poisonous");
            Save(Pixels.CropToContent(Load("Lifesteal_Icon")), "lifesteal");
            var tr = Pixels.Unpremultiply(Pixels.Resize(Pixels.Premultiply(Load("Trigger")), 128, 128));
            var ring = Ellipse(tr, 14, 18, 110, 114, 1.5f);
            Save(WithAlpha(tr, i => Math.Max(ring.P[i], tr.P[i * 4] > 120 && tr.P[i * 4 + 2] < 90 ? 255 : 0)), "trigger");   // + the bolt
            // Play-history tile frame: its middle (the white square) is where the card's art shows.
            var ht = Load("History_Tiles_D");
            m = new Img(ht.W, ht.H, 1, 255);
            Pixels.RoundedRectangle(m, 64, 52, 202, 204, 12, 0);
            Save(Pixels.PutAlpha(ht, Pixels.Blur(m, 1)), "history-tile");
            // Mana crystals: the game's crystal turned to its mana blue, a dim one for spent mana, and the overload lock.
            var gem = Pixels.Cutout(Load("Gem_Mana_Red"), 14, 64, 64);
            Save(Pixels.Recolour(gem, 1.0, 1.1), "mana");
            Save(Pixels.Recolour(gem, 0.35, 0.4), "mana-empty");
            Save(Pixels.Cutout(Load("Gem_LockMana_D"), 14, 64, 64), "mana-locked");
            // Secret gems, one per class colour (the sheet holds four).
            var sd = Load("Secret_D");
            foreach (var (cx, cy, key) in new[] { (32, 32, "secret-mage"), (96, 32, "secret-hunter"), (32, 96, "secret-paladin"), (96, 96, "secret-rogue") })
            {
                var a = Ellipse(sd, cx - 30, cy - 30, cx + 30, cy + 30, 1);
                Save(Pixels.Crop(WithAlpha(sd, i => a.P[i]), cx - 32, cy - 32, cx + 32, cy + 32), key);
            }
            // Aura icon (lasting effects like Paladin auras): the yellow bolt in its stone ring, shown over the hero like secrets.
            var au = Load("Sigil_DemonHunter");
            var auA = Ellipse(au, 9, 9, 120, 120, 1);
            Save(Pixels.Crop(WithAlpha(au, i => auA.P[i]), 8, 8, 121, 121), "aura");
            // Effects that already have transparency.
            Save(Load("DivineShield_Bubble2"), "divine-shield");
            Save(Load("FrozenFrost_D"), "frozen");
        }

        /// <summary>A soft-edged oval mask the size of `im` (Pillow ellipse, then Gaussian blur).</summary>
        private static Img Ellipse(Img im, int x0, int y0, int x1, int y1, float feather = 2f)
        {
            var m = new Img(im.W, im.H, 1);
            Pixels.Ellipse(m, x0, y0, x1, y1, 255);
            return Pixels.Blur(m, feather);
        }

        /// <summary>The picture with its alpha replaced (per pixel index; clipped to 0..255 and truncated, as numpy does).</summary>
        private static Img WithAlpha(Img im, Func<int, double> alpha)
        {
            var o = im.Clone();
            for (int i = 0; i < im.W * im.H; i++) o.P[i * 4 + 3] = (byte)Math.Clamp(alpha(i), 0, 255);
            return o;
        }

        // ------------------------------------------------------------------ saving (WPF imaging)
        internal static void SavePng(Img rgba, string path)
        {
            var bgra = new byte[rgba.P.Length];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                bgra[i] = rgba.P[i + 2]; bgra[i + 1] = rgba.P[i + 1]; bgra[i + 2] = rgba.P[i]; bgra[i + 3] = rgba.P[i + 3];
            }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(rgba.W, rgba.H, 96, 96, PixelFormats.Bgra32, null, bgra, rgba.W * 4)));
            WriteAtomic(path, enc);
        }

        private static void SaveJpeg(Img rgb, string path, int quality)
        {
            var bgr = new byte[rgb.P.Length];
            for (int i = 0; i < bgr.Length; i += 3) { bgr[i] = rgb.P[i + 2]; bgr[i + 1] = rgb.P[i + 1]; bgr[i + 2] = rgb.P[i]; }
            var enc = new JpegBitmapEncoder { QualityLevel = quality };
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(rgb.W, rgb.H, 96, 96, PixelFormats.Bgr24, null, bgr, rgb.W * 3)));
            WriteAtomic(path, enc);
        }

        // Written next to the target and moved over it, so the app never reads a half-written picture.
        private static void WriteAtomic(string path, BitmapEncoder enc)
        {
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp)) enc.Save(fs);
            File.Move(tmp, path, true);
        }

        // ------------------------------------------------------------------ bundle index (cached)
        internal sealed class Index
        {
            public string Stamp, Camera;
            public Dictionary<string, string> Cabs = new Dictionary<string, string>();
            public List<(string bundle, string root)> Boards = new List<(string, string)>();
        }

        private static readonly Regex RootName = new Regex(@"^[A-Za-z]{2,5}\d?$");   // STW, ULD2, BotB, STWOG; not Board_STW
        private static readonly HashSet<string> Exclude = new HashSet<string> { "Bacon", "NCE", "LET" };   // Battlegrounds, Mercenaries

        /// <summary>Which bundle holds each internal (CAB) file, the boards (bundle, root) and the camera's bundle.
        /// Each constructed board is a prefab in a qualityhigh bundle whose root (STW, ULD, ...) has PlaySurface1 under it.</summary>
        private static Index LoadIndex(string hs, string cache, Action<string, int, int> progress, CancellationToken ct)
        {
            var win = Path.Combine(hs, "Data", "Win");
            var files = Bundles(hs);
            var newest = files.Length == 0 ? 0 : files.Max(f => File.GetLastWriteTimeUtc(Path.Combine(win, f)).Ticks);
            var stamp = $"{Path.GetFullPath(hs)}|{files.Length}|{newest}";
            try
            {
                var j = JObject.Parse(File.ReadAllText(cache));
                if ((string)j["stamp"] == stamp)
                    return new Index
                    {
                        Stamp = stamp, Camera = (string)j["camera"],
                        Cabs = j["cabs"].ToObject<Dictionary<string, string>>(),
                        Boards = j["boards"].Select(b => ((string)b[0], (string)b[1])).ToList(),
                    };
            }
            catch (Exception) { }

            var idx = new Index { Stamp = stamp };
            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i % 10 == 0) progress("Indexing game files", i, files.Length);
                string name = files[i];
                bool high = name.Contains("qualityhigh");
                using var ua = new UnityAssets(hs, null);
                try
                {
                    var bun = ua.Am.LoadBundleFile(Path.Combine(win, name), unpackIfPacked: high);
                    foreach (var d in bun.file.BlockAndDirInfo.DirectoryInfos) idx.Cabs[d.Name.ToLowerInvariant()] = name;
                    if (!high) continue;
                    ua.Open(name);
                    var gos = new Dictionary<string, List<UObj>>();
                    var order = new List<string>();
                    foreach (var f in ua.Files)
                        foreach (var o in ua.Objects(f, AssetClassID.GameObject))
                        {
                            try
                            {
                                var n = o.Name;
                                if (!gos.TryGetValue(n, out var l)) { gos[n] = l = new List<UObj>(); order.Add(n); }
                                l.Add(o);
                            }
                            catch (Exception) { }
                        }
                    if (gos.ContainsKey("BoardCameras")) idx.Camera = name;
                    if (!gos.ContainsKey("PlaySurface1")) continue;
                    foreach (var root in order)
                    {
                        if (!RootName.IsMatch(root) || Exclude.Contains(root)) continue;
                        foreach (var o in gos[root])
                        {
                            var t = ua.TransformOf(o);
                            if (t != null && t.F["m_Father"]["m_PathID"].AsLong == 0 && HasChild(ua, t, "PlaySurface1")) idx.Boards.Add((name, root));
                        }
                    }
                }
                catch (Exception e) { Log?.Invoke($"can't read {name}: {e.Message}"); }
            }
            progress("Indexing game files", files.Length, files.Length);
            Directory.CreateDirectory(Path.GetDirectoryName(cache));
            File.WriteAllText(cache, new JObject
            {
                ["stamp"] = stamp, ["camera"] = idx.Camera, ["cabs"] = JObject.FromObject(idx.Cabs),
                ["boards"] = new JArray(idx.Boards.Select(b => new JArray(b.bundle, b.root))),
            }.ToString(Formatting.None));
            return idx;
        }

        private static bool HasChild(UnityAssets ua, UObj t, string name)
        {
            var stack = new Stack<UObj>();
            stack.Push(t);
            while (stack.Count > 0)
                foreach (var (ct, go) in ua.Children(stack.Pop()))
                {
                    if (go.Name == name) return true;
                    stack.Push(ct);
                }
            return false;
        }

        // ------------------------------------------------------------------ boards
        /// <summary>The gameplay camera (BoardCameras/Main Camera; the BoardCameras root sits at the origin).</summary>
        internal static CameraInfo ReadCamera(string hs, Index idx)
        {
            using var ua = new UnityAssets(hs, idx.Cabs);
            ua.Open(idx.Camera ?? throw new InvalidOperationException("no BoardCameras prefab found"));
            var (_, t) = ua.FindRoot("BoardCameras");
            foreach (var (ct, cgo) in ua.Children(t))
            {
                var cam = UnityAssets.Component(ua.Components(cgo), "Camera");
                if (cam == null) continue;
                var c = cam.F;
                var m = Geo.Mul(UnityAssets.Trs(t), UnityAssets.Trs(ct));
                return new CameraInfo
                {
                    Position = Geo.Origin(m), Rotation = UnityAssets.Q4(ct.F["m_LocalRotation"]),
                    Fov = c["field of view"].AsFloat, Ortho = c["orthographic"].AsBool, OrthoSize = c["orthographic size"].AsFloat,
                    Near = c["near clip plane"].AsFloat, Far = c["far clip plane"].AsFloat,
                };
            }
            throw new InvalidOperationException("no camera under BoardCameras");
        }

        private static void Boards(string hs, Index idx, string outDir, Action<string, int, int> progress, CancellationToken ct)
        {
            var cam = ReadCamera(hs, idx);
            var listing = Path.Combine(outDir, "boards.txt");
            var done = new Dictionary<string, string>();
            if (File.Exists(listing))
                foreach (var line in File.ReadAllLines(listing))
                    if (line.Split('\t', 2) is { Length: 2 } p) done[p[0]] = p[1];
            for (int i = 0; i < idx.Boards.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (bundle, root) = idx.Boards[i];
                progress("Boards", i, idx.Boards.Count);
                var sw = Stopwatch.StartNew();
                try
                {
                    using var ua = new UnityAssets(hs, idx.Cabs);
                    ua.Open(bundle);
                    var objects = Export(ua, root, out var play);
                    progress("Boards", i, idx.Boards.Count);   // often: the caller checks for a starting game here
                    if (play == null) throw new InvalidDataException("no visible play area");
                    var img = Rasterizer.Render(objects, cam, 1600, 1200, 2);
                    SaveJpeg(Pixels.OverBlack(img), Path.Combine(outDir, root + ".jpg"), 90);
                    done[root] = play;
                    Log?.Invoke($"[{i + 1}/{idx.Boards.Count}] {root,-6} {play}  {sw.Elapsed.TotalSeconds:0.0}s");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { Log?.Invoke($"[{i + 1}/{idx.Boards.Count}] {root,-6} skipped: {e.Message}"); }
            }
            progress("Boards", idx.Boards.Count, idx.Boards.Count);
            File.WriteAllLines(listing, done.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .Where(r => File.Exists(Path.Combine(outDir, r + ".jpg"))).Select(r => r + "\t" + done[r]));
        }

        private static readonly Regex SeeThrough = new Regex("transparent|additive|alpha|blend|particle|/add|multiply|glow", RegexOptions.IgnoreCase);
        private static readonly Regex Opaque = new Regex(@"^Hero/Diffuse/(?!.*transparent)", RegexOptions.IgnoreCase);   // alpha is a mask there

        /// <summary>
        /// The board prefab's drawable pieces (one per submesh, in hierarchy order) with their transforms, textures and
        /// material settings; `play` is the name of its play-area texture (the most used one on PlaySurface pieces,
        /// preferring names with "play").
        /// </summary>
        internal static List<SceneObject> Export(UnityAssets ua, string root, out string play)
        {
            var (go0, t0) = ua.FindRoot(root);
            var objects = new List<SceneObject>();
            var meshes = new Dictionary<(string, long), MeshData>();
            var textures = new Dictionary<(string, long), Tex>();

            void Walk(UObj t, UObj go, double[] parent, string path, bool active)
            {
                var m = Geo.Mul(parent, UnityAssets.Trs(t));
                active = active && go.F["m_IsActive"].AsBool;
                var comps = ua.Components(go);
                var renderer = UnityAssets.Component(comps, "MeshRenderer") ?? UnityAssets.Component(comps, "SkinnedMeshRenderer");
                if (renderer != null && UnityAssets.Component(comps, "ParticleSystemRenderer") == null) AddRenderer(renderer, comps, go, m, path, active);
                foreach (var (ct, cgo) in ua.Children(t)) Walk(ct, cgo, m, path + "/" + cgo.Name, active);
            }

            void AddRenderer(UObj renderer, List<(string name, UObj obj)> comps, UObj go, double[] m, string path, bool active)
            {
                var r = renderer.F;
                UObj mesh;
                if (ua.TypeName(renderer) == "SkinnedMeshRenderer") mesh = ua.Read(renderer, r["m_Mesh"]);   // drawn in bind pose
                else
                {
                    var mf = UnityAssets.Component(comps, "MeshFilter");
                    mesh = mf == null ? null : ua.Read(mf, mf.F["m_Mesh"]);
                }
                if (mesh == null) return;
                if (!meshes.TryGetValue(mesh.Key, out var md))
                {
                    try { md = ua.ReadMesh(mesh); } catch (Exception) { md = null; }
                    meshes[mesh.Key] = md;
                }
                if (md == null) return;
                var mats = r["m_Materials"]["Array"].Children.Select(p => ua.Read(renderer, p)).ToList();
                var world = Geo.Apply(m, md.V.Select(v => (double)v).ToArray());
                var uv = md.Uv.Select(v => (double)v).ToArray();
                for (int si = 0; si < md.Subs.Count; si++)
                {
                    var o = new SceneObject
                    {
                        Name = go.Name, Path = path, World = world, Uv = uv, Tris = md.Subs[si],
                        Active = active, Enabled = r["m_Enabled"].AsBool,
                    };
                    var mat = mats.Count > 0 ? mats[Math.Min(si, mats.Count - 1)] : null;
                    if (mat != null) Material(o, mat);
                    objects.Add(o);
                }
            }

            void Material(SceneObject o, UObj mat)
            {
                var sh = ua.Read(mat, mat.F["m_Shader"]);
                var parsed = sh?.F["m_ParsedForm"];
                o.Shader = parsed != null && !parsed.IsDummy ? parsed["m_Name"].AsString : sh == null || sh.F["m_Name"].IsDummy ? "?" : sh.F["m_Name"].AsString;
                int rq = mat.F["m_CustomRenderQueue"].AsInt;
                o.RenderQueue = rq >= 0 ? rq : null;
                var cols = UnityAssets.Props(mat, "m_Colors");
                foreach (var k in new[] { "_Color", "_BaseColor", "_TintColor", "_MainColor" })
                {
                    var c = cols.FirstOrDefault(e => e.key == k).value;
                    if (c == null) continue;
                    o.Tint = new[] { c["r"].AsFloat, c["g"].AsFloat, c["b"].AsFloat, c["a"].AsFloat };
                    break;
                }
                var (tex, env) = ua.MainTexture(mat);
                if (tex != null)
                {
                    if (!textures.TryGetValue(tex.Key, out var t))
                    {
                        try { t = new Tex(ua.ReadTexture(tex)); } catch (Exception) { t = null; }
                        textures[tex.Key] = t;
                    }
                    o.Texture = t;
                    o.TextureName = tex.Name;
                    o.St = new double[] { env["m_Scale"]["x"].AsFloat, env["m_Scale"]["y"].AsFloat, env["m_Offset"]["x"].AsFloat, env["m_Offset"]["y"].AsFloat };
                }
                o.Transparent = (o.RenderQueue ?? 0) >= 2500 || (SeeThrough.IsMatch(o.Shader) && !Opaque.IsMatch(o.Shader));
            }

            Walk(t0, go0, Geo.Identity(), root, true);
            var counts = new List<(string name, int n)>();
            foreach (var o in objects)
            {
                if (!o.Active || !o.Enabled || string.IsNullOrEmpty(o.TextureName) || !o.Path.Contains("/PlaySurface")) continue;
                int i = counts.FindIndex(c => c.name == o.TextureName);
                if (i < 0) counts.Add((o.TextureName, 1)); else counts[i] = (o.TextureName, counts[i].n + 1);
            }
            play = null;
            (bool, int) best = (false, -1);
            foreach (var (name, n) in counts)
            {
                var key = (Regex.IsMatch(name, "play", RegexOptions.IgnoreCase), n);
                if (play == null || key.Item1.CompareTo(best.Item1) > 0 || (key.Item1 == best.Item1 && key.n > best.Item2)) { play = name; best = key; }
            }
            return objects;
        }
    }
}
