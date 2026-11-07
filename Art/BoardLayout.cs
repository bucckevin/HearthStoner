using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HearthStoner.Art
{
    /// <summary>
    /// Hearthstone's own gameplay layout (where every board element sits and how big it is, as the game's camera sees it)
    /// and the game's 3D End Turn button, for the replay canvas. Writes in board\:
    ///   layout.json              every bone / zone / actor position and size, in world coords and on the 800x600 canvas
    ///   endturn-btn-yellow.png   End Turn button, your turn with plays left   (cropped, transparent; box in layout.json)
    ///   endturn-btn-green.png    End Turn button, your turn with no plays left
    ///   endturn-btn-grey.png     End Turn button, opponent's turn ("Enemy Turn" side, flipped over)
    ///
    /// The canvas is the board render at 800x600: the BoardCameras/Main Camera (perspective, 4:3) with
    /// px = (vx / (vz*tan(fov/2)*4/3) + 1)/2*800, py = (1 - vy / (vz*tan(fov/2)))/2*600, v = R^T (world - camera).
    /// Where the numbers come from:
    ///   * prefab BoardStandardGame (bones, zones, colliders, mana/history managers), with each TransformOverride's PC
    ///     (ScreenCategory.PC = 4) values applied;
    ///   * the actor prefabs the zones put cards in, placed and scaled like the game's ZonePlay / ZoneHand / ZoneSecret /
    ///     ManaCrystalMgr / HistoryManager code does (formulas in layout.json "formulas");
    ///   * EndTurnButton.prefab at the EndTurnButton bone, with the final pose / _MainTex offset of the state animations
    ///     ENDTURN_YOUR_TURN (yellow), ENDTURN_NO_MORE_PLAYS (green: UV offset y 0.5) and ENDTURN_WAITING (flipped 180
    ///     degrees about X: the grey underside). The END TURN / Enemy Turn words are live text, so their boxes are given.
    /// Prefabs are found through the game's asset catalog (asset_manifest.unity3d) by the GUIDs its code loads them with.
    /// </summary>
    internal static class BoardLayout
    {
        private const int W = 800, H = 600, PC = 4;   // ScreenCategory.PC
        private static readonly Dictionary<string, string> Guid = new Dictionary<string, string>
        {
            ["EndTurnButton"] = "313ebd8bcb770a944be3633ad928096b",
            ["Card_Play_Secret_Mage"] = "ffc78954f637f6f4d8b8bb7ec0b936ca",
        };
        private static readonly (string png, string clip, string what)[] States =
        {
            ("yellow", "ENDTURN_YOUR_TURN", "your turn, plays left (ActorStateType.ENDTURN_YOUR_TURN)"),
            ("green", "ENDTURN_NO_MORE_PLAYS", "your turn, no plays left (ENDTURN_NO_MORE_PLAYS; the game also turns on a green glow plane, GreenHighlightPlane, not drawn here)"),
            ("grey", "ENDTURN_WAITING", "opponent's turn (ENDTURN_WAITING / ENDTURN_NMP_2_WAITING / ENDTURN_WAITING_TIMER)"),
        };
        private static readonly HashSet<string> SkipComps = new HashSet<string> { "ParticleSystem", "AudioSource", "SoundDef" };

        // ---------------------------------------------------------------- numbers
        /// <summary>Python's round(x, n): the double nearest the exactly rounded (half to even) decimal.</summary>
        internal static double Rnd(double x, int n)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            exp -= 1075;
            BigInteger num = man * BigInteger.Pow(10, n), den = BigInteger.One;
            if (exp > 0) num <<= exp; else den <<= -exp;
            var q = BigInteger.DivRem(num, den, out var rem);
            var twice = rem * 2;
            if (twice > den || (twice == den && !q.IsEven)) q += 1;
            double v = (double)q / Math.Pow(10, n);
            return bits < 0 ? -v : v;
        }

        private static JArray R(IEnumerable<double> v, int n = 3) => new JArray(v.Select(x => Rnd(x, n)));

        /// <summary>The camera's view of the 800x600 canvas.</summary>
        private sealed class Canvas
        {
            private readonly Camera _cam;
            public Canvas(CameraInfo c) =>
                _cam = new Camera(new CameraInfo { Position = c.Position, Rotation = c.Rotation, Fov = c.Fov, Near = c.Near, Far = c.Far }, (double)W / H);

            public (double x, double y) Xy(double[] p, int i = 0) => _cam.Screen(p[i], p[i + 1], p[i + 2], W, H);
            public List<(double x, double y)> Xys(double[] pts) => Enumerable.Range(0, pts.Length / 3).Select(i => Xy(pts, i * 3)).ToList();
            public JArray Pt(double[] p) { var s = Xy(p); return new JArray(Rnd(s.x, 1), Rnd(s.y, 1)); }

            public JArray Box(double[] pts)
            {
                var s = Xys(pts);
                double lx = s.Min(e => e.x), ly = s.Min(e => e.y), hx = s.Max(e => e.x), hy = s.Max(e => e.y);
                return new JArray(Rnd(lx, 1), Rnd(ly, 1), Rnd(hx - lx, 1), Rnd(hy - ly, 1));
            }
        }

        /// <summary>Convex hull (monotone chain) of 2D points, counter-clockwise on screen.</summary>
        private static List<(double x, double y)> Hull(IEnumerable<(double x, double y)> s)
        {
            static double R2(double v) => Math.Round(v * 100, MidpointRounding.ToEven) / 100;   // numpy.round(v, 2)
            var p = s.Select(e => (R2(e.x), R2(e.y))).Distinct().OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToList();
            if (p.Count < 3) return p;
            static double Cross((double x, double y) o, (double x, double y) a, (double x, double y) b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var lo = new List<(double x, double y)>();
            var up = new List<(double x, double y)>();
            foreach (var q in p) { while (lo.Count >= 2 && Cross(lo[^2], lo[^1], q) <= 0) lo.RemoveAt(lo.Count - 1); lo.Add(q); }
            for (int i = p.Count - 1; i >= 0; i--) { var q = p[i]; while (up.Count >= 2 && Cross(up[^2], up[^1], q) <= 0) up.RemoveAt(up.Count - 1); up.Add(q); }
            return lo.Take(lo.Count - 1).Concat(up.Take(up.Count - 1)).ToList();
        }

        /// <summary>Drops the vertex that adds the least area until n are left.</summary>
        private static JArray Simplify(List<(double x, double y)> poly, int n)
        {
            poly = poly.ToList();
            while (poly.Count > n)
            {
                int k = poly.Count, best = 0;
                double bestArea = double.MaxValue;
                for (int i = 0; i < k; i++)
                {
                    var a = poly[(i - 1 + k) % k]; var b = poly[i]; var c = poly[(i + 1) % k];
                    double area = Math.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
                    if (area < bestArea) { bestArea = area; best = i; }
                }
                poly.RemoveAt(best);
            }
            return new JArray(poly.Select(p => new JArray(Rnd(p.x, 1), Rnd(p.y, 1))));
        }

        /// <summary>Unity Quaternion.Euler(x, y, z) as a rotation matrix (Z, then X, then Y).</summary>
        internal static double[,] Euler(double x, double y, double z)
        {
            double[,] Rot(char axis, double a)
            {
                double c = Math.Cos(a * (Math.PI / 180.0)), s = Math.Sin(a * (Math.PI / 180.0));
                return axis switch
                {
                    'x' => new double[,] { { 1, 0, 0 }, { 0, c, -s }, { 0, s, c } },
                    'y' => new double[,] { { c, 0, s }, { 0, 1, 0 }, { -s, 0, c } },
                    _ => new double[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } },
                };
            }
            return Mul3(Mul3(Rot('y', y), Rot('x', x)), Rot('z', z));
        }

        private static double[,] Mul3(double[,] a, double[,] b)
        {
            var m = new double[3, 3];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
            return m;
        }

        /// <summary>(V * scale) @ R^T + pos, for x y z triples.</summary>
        private static double[] Place(double[] v, double[] pos, double[] scale, double[,] r = null)
        {
            var o = new double[v.Length];
            for (int i = 0; i < v.Length; i += 3)
            {
                double x = v[i] * scale[0], y = v[i + 1] * scale[1], z = v[i + 2] * scale[2];
                for (int k = 0; k < 3; k++)
                    o[i + k] = (r == null ? (k == 0 ? x : k == 1 ? y : z) : x * r[k, 0] + y * r[k, 1] + z * r[k, 2]) + pos[k];
            }
            return o;
        }

        private static double[] Add(double[] a, params double[] b) => a.Select((v, i) => v + b[i]).ToArray();
        private static double[] Linear(double[] m, double[] v) => new[]
        {
            m[0] * v[0] + m[1] * v[1] + m[2] * v[2], m[4] * v[0] + m[5] * v[1] + m[6] * v[2], m[8] * v[0] + m[9] * v[1] + m[10] * v[2],
        };

        // ---------------------------------------------------------------- prefab trees
        private sealed class Node
        {
            public string Name, Path;
            public int Depth;
            public double[] World, P, Q, S;
            public List<(string name, UObj obj)> Comps;
            public UObj Go;
            public bool Active;
            public UObj Comp(string name) => UnityAssets.Component(Comps, name);
        }

        /// <summary>Flattened hierarchy of a prefab root with PC TransformOverrides applied.</summary>
        private static List<Node> Tree(UnityAssets ua, string root, bool identityRoot = false)
        {
            var (go0, t0) = ua.FindRoot(root);
            var nodes = new List<Node>();
            void Walk(UObj t, UObj go, double[] parent, string path, int depth)
            {
                var cs = ua.Components(go, scripts: true);
                double[] p = UnityAssets.V3(t.F["m_LocalPosition"]), q = UnityAssets.Q4(t.F["m_LocalRotation"]), s = UnityAssets.V3(t.F["m_LocalScale"]);
                foreach (var (n, r) in cs)
                {
                    if (n != "TransformOverride") continue;
                    var tt = r.F;
                    var cats = tt["m_screenCategory"]["Array"].Children.Select(c => c.AsInt).ToList();
                    int i = cats.IndexOf(PC);
                    if (i < 0) continue;
                    p = UnityAssets.V3(tt["m_localPosition"]["Array"][i]);
                    q = UnityAssets.Q4(tt["m_localRotation"]["Array"][i]);
                    s = UnityAssets.V3(tt["m_localScale"]["Array"][i]);
                }
                var m = identityRoot && depth == 0 ? Geo.Identity() : Geo.Mul(parent, Geo.Trs(p, q, s));
                nodes.Add(new Node { Name = go.Name, Path = path, Depth = depth, World = m, P = p, Q = q, S = s, Comps = cs, Go = go, Active = go.F["m_IsActive"].AsBool });
                foreach (var (ct, cgo) in ua.Children(t)) Walk(ct, cgo, m, path + "/" + cgo.Name, depth + 1);
            }
            Walk(t0, go0, Geo.Identity(), root, 0);
            return nodes;
        }

        private static Node Find(List<Node> nodes, string pathEnd) =>
            nodes.FirstOrDefault(n => n.Path.EndsWith(pathEnd, StringComparison.Ordinal)) ?? throw new KeyNotFoundException(pathEnd);

        /// <summary>The mesh of a node's MeshRenderer / SkinnedMeshRenderer and the renderer, else null.</summary>
        private static (MeshData mesh, UObj renderer)? MeshOf(UnityAssets ua, Node node)
        {
            var mr = node.Comp("MeshRenderer") ?? node.Comp("SkinnedMeshRenderer");
            if (mr == null) return null;
            UObj mesh;
            if (ua.TypeName(mr) == "SkinnedMeshRenderer") mesh = ua.Read(mr, mr.F["m_Mesh"]);
            else
            {
                var mf = node.Comp("MeshFilter");
                mesh = mf == null ? null : ua.Read(mf, mf.F["m_Mesh"]);
            }
            return mesh == null ? null : (ua.ReadMesh(mesh), mr);
        }

        private static double[] ToDouble(float[] v) => v.Select(x => (double)x).ToArray();

        private static double[] WorldPoints(UnityAssets ua, Node node, double[] v = null)
        {
            if (v == null)
            {
                var m = MeshOf(ua, node);
                if (m == null) return null;
                v = ToDouble(m.Value.mesh.V);
            }
            return Geo.Apply(node.World, v);
        }

        /// <summary>Vertices of the named mesh in its actor's own space (actor root = identity, as cards set it).</summary>
        private static double[] ActorMesh(UnityAssets ua, string root, string meshName)
        {
            foreach (var n in Tree(ua, root, identityRoot: true))
            {
                if (n.Comp("MeshRenderer") == null && n.Comp("SkinnedMeshRenderer") == null) continue;
                var m = MeshOf(ua, n);
                if (m != null && m.Value.mesh.Name == meshName) return WorldPoints(ua, n, ToDouble(m.Value.mesh.V));
            }
            throw new KeyNotFoundException($"{root}: no mesh {meshName}");
        }

        private static readonly double[] Unit = { -.5, .5 };

        /// <summary>World corners of a node's (first) BoxCollider, and its centre and size.</summary>
        private static (double[] corners, double[] c, double[] s) BoxCollider(Node node)
        {
            var tt = node.Comp("BoxCollider").F;
            double[] c = UnityAssets.V3(tt["m_Center"]), s = UnityAssets.V3(tt["m_Size"]);
            var local = new List<double>();
            foreach (var x in Unit) foreach (var y in Unit) foreach (var z in Unit) { local.Add(x * s[0] + c[0]); local.Add(y * s[1] + c[1]); local.Add(z * s[2] + c[2]); }
            return (Geo.Apply(node.World, local.ToArray()), c, s);
        }

        /// <summary>World corners of an UberText's width x height rectangle (its local XY plane, centred), and its text.</summary>
        private static (double[] rect, string text) TextRect(Node node, double[] world = null)
        {
            var tt = node.Comp("UberText").F;
            double w = tt["m_Width"].AsFloat, h = tt["m_Height"].AsFloat;
            var loc = new[] { -w / 2, -h / 2, 0, w / 2, -h / 2, 0, w / 2, h / 2, 0, -w / 2, h / 2, 0 };
            return (Geo.Apply(world ?? node.World, loc), tt["m_Text"].IsDummy ? null : tt["m_Text"].AsString);
        }

        private static double[] Mean(double[] pts)
        {
            int n = pts.Length / 3;
            return new[] { 0, 1, 2 }.Select(k => Enumerable.Range(0, n).Sum(i => pts[i * 3 + k]) / n).ToArray();
        }

        /// <summary>{clip: (pos, rot, uv_offset)}: last keyframe values of the clip's curves on the animated object itself.</summary>
        private static Dictionary<string, (double[] pos, double[] rot, double[] uv)> ClipFinal(UnityAssets ua, HashSet<string> names)
        {
            var o = new Dictionary<string, (double[], double[], double[])>();
            foreach (var f in ua.Files)
                foreach (var c in ua.Objects(f, AssetClassID.AnimationClip))
                {
                    string n;
                    try { n = ua.PeekName(c); } catch (Exception) { continue; }
                    if (n == null || !names.Contains(n) || o.ContainsKey(n)) continue;
                    var tt = c.F;
                    double[] pos = null, rot = null, uv = null;
                    foreach (var (kind, isRot) in new[] { ("m_RotationCurves", true), ("m_PositionCurves", false) })
                        foreach (var cv in tt[kind]["Array"].Children)
                        {
                            var ks = cv["curve"]["m_Curve"]["Array"].Children;
                            if (cv["path"].AsString != "" || ks.Count == 0) continue;
                            var v = ks[^1]["value"];
                            if (isRot) rot = UnityAssets.Q4(v); else pos = UnityAssets.V3(v);
                        }
                    foreach (var cv in tt["m_FloatCurves"]["Array"].Children)
                    {
                        var ks = cv["curve"]["m_Curve"]["Array"].Children;
                        string attr = cv["attribute"].AsString;
                        if (cv["path"].AsString != "" || ks.Count == 0 || (attr != "_MainTex.offset.x" && attr != "_MainTex.offset.y")) continue;
                        uv ??= new double[2];
                        uv[attr.EndsWith("x") ? 0 : 1] = ks[^1]["value"].AsFloat;
                    }
                    o[n] = (pos, rot, uv);
                }
            return o;
        }

        // ---------------------------------------------------------------- the end turn button
        private static JObject EndTurnButton(UnityAssets ua, double[] bonePos, CameraInfo cam, Canvas cv, string outDir)
        {
            var nodes = Tree(ua, "EndTurnButton");
            Node root = nodes[0], cont = Find(nodes, "/ButtonContainer"), btn = Find(nodes, "/ButtonContainer/ButtonMesh");
            var rootM = Geo.Trs(bonePos, root.Q, root.S);   // Gameplay: transform.position = bone; rotation/scale kept
            var contM = Geo.Trs(cont.P, cont.Q, cont.S);
            var (mesh, renderer) = MeshOf(ua, btn).Value;
            var material = ua.Read(renderer, renderer.F["m_Materials"]["Array"][0]);
            var (tex, env) = ua.MainTexture(material);
            double[] baseSt = { env["m_Scale"]["x"].AsFloat, env["m_Scale"]["y"].AsFloat, env["m_Offset"]["x"].AsFloat, env["m_Offset"]["y"].AsFloat };
            var clips = ClipFinal(ua, States.Select(s => s.clip).ToHashSet());
            var texts = new Dictionary<string, Node>();
            foreach (var n in nodes) if (n.Comp("UberText") != null) texts[n.Name] = n;
            var tri = mesh.Subs.SelectMany(t => t).ToArray();
            var v = mesh.V;
            // face normals in mesh space (outward for this mesh), in single precision like the mesh
            var nrm = new float[tri.Length];
            for (int i = 0; i < tri.Length; i += 3)
            {
                int a = tri[i] * 3, b = tri[i + 1] * 3, c = tri[i + 2] * 3;
                float ux = v[b] - v[a], uy = v[b + 1] - v[a + 1], uz = v[b + 2] - v[a + 2];
                float wx = v[c] - v[a], wy = v[c + 1] - v[a + 1], wz = v[c + 2] - v[a + 2];
                nrm[i] = uy * wz - uz * wy; nrm[i + 1] = uz * wx - ux * wz; nrm[i + 2] = ux * wy - uy * wx;
            }
            var texture = new Tex(ua.ReadTexture(tex));
            var vd = ToDouble(v);
            var result = new JObject
            {
                ["bone_world"] = R(bonePos), ["prefab"] = "EndTurnButton.prefab (" + Guid["EndTurnButton"] + ")",
                ["mesh"] = mesh.Name, ["texture"] = tex.Name, ["root_rotation_quat"] = R(root.Q, 4), ["root_scale"] = R(root.S, 4),
                ["states"] = new JObject(),
            };
            foreach (var (png, clip, what) in States)
            {
                clips.TryGetValue(clip, out var pose);
                var bp = pose.pos ?? btn.P;
                var bq = pose.rot ?? new double[] { 0, 0, 0, 1 };   // NO_MORE_PLAYS has no rotation keys: it keeps YOUR_TURN's (identity)
                var off = pose.uv ?? new double[] { 0, 0 };
                var m = Geo.Mul(Geo.Mul(rootM, contM), Geo.Trs(bp, bq, btn.S));
                var world = Geo.Apply(m, vd);
                var obj = new SceneObject
                {
                    Name = "ButtonMesh", Path = "EndTurnButton/ButtonContainer/ButtonMesh", World = world, Uv = ToDouble(mesh.Uv), Tris = tri,
                    Texture = texture, TextureName = tex.Name, Shader = "Hero/Diffuse/DiffuseColor",
                    St = new[] { baseSt[0], baseSt[1], baseSt[2] + off[0], baseSt[3] + off[1] },
                };
                var img = Rasterizer.Render(new[] { obj }, cam, W, H, 4);
                var a8 = Pixels.Channel(img, 3);
                for (int i = 0; i < a8.P.Length; i++) a8.P[i] = (byte)(a8.P[i] > 8 ? 255 : 0);
                var bb = Pixels.BBox(a8);
                GameArtBuilder.SavePng(Pixels.Crop(img, bb), Path.Combine(outDir, $"endturn-btn-{png}.png"));
                // top face outline: the triangles facing up after this state's pose
                var top = new SortedSet<int>();
                for (int i = 0; i < tri.Length; i += 3)
                {
                    var nw = Linear(m, new double[] { nrm[i], nrm[i + 1], nrm[i + 2] });
                    if (nw[1] / (Math.Sqrt(nw[0] * nw[0] + nw[1] * nw[1] + nw[2] * nw[2]) + 1e-12) > 0.7) { top.Add(tri[i]); top.Add(tri[i + 1]); top.Add(tri[i + 2]); }
                }
                var st = new JObject
                {
                    ["png"] = $"endturn-btn-{png}.png", ["animation"] = clip, ["meaning"] = what,
                    ["box"] = new JArray(bb[0], bb[1], bb[2] - bb[0], bb[3] - bb[1]),
                    ["face_polygon"] = Simplify(Hull(top.Select(i => cv.Xy(world, i * 3))), 16),
                    ["buttonmesh_local_pos"] = R(bp, 4), ["buttonmesh_local_rot"] = R(bq, 4), ["uv_offset"] = R(off, 3),
                };
                string tname = png == "grey" ? "WaitingText" : "MyTurnText";
                if (texts.TryGetValue(tname, out var tn))
                {
                    // the texts are children of ButtonMesh: their world matrix under this state's pose
                    var rel = Geo.Mul(Geo.Inverse(btn.World), tn.World);
                    var (rect, text) = TextRect(tn, Geo.Mul(m, rel));
                    st["text"] = new JObject
                    {
                        ["name"] = tname, ["prefab_text"] = text, ["box"] = cv.Box(rect), ["center"] = cv.Pt(Mean(rect)),
                        ["polygon"] = new JArray(Enumerable.Range(0, 4).Select(i => cv.Pt(rect.Skip(i * 3).Take(3).ToArray()))),
                    };
                }
                ((JObject)result["states"])[png] = st;
            }
            return result;
        }

        // ---------------------------------------------------------------- the layout
        private static JObject Entry(UnityAssets ua, Canvas cv, Node node)
        {
            var o = Geo.Origin(node.World);
            var e = new JObject { ["world"] = R(o), ["canvas"] = cv.Pt(o), ["scale"] = R(Geo.ColumnNorms(node.World), 4) };
            if (!node.Active) e["active"] = false;
            if (node.Comp("BoxCollider") != null) e["collider_box"] = cv.Box(BoxCollider(node).corners);
            if (node.Comp("UberText") != null) e["text_box"] = cv.Box(TextRect(node).rect);
            if (node.Comp("MeshRenderer") != null && WorldPoints(ua, node) is double[] p && p.Length > 0) e["mesh_box"] = cv.Box(p);
            return e;
        }

        private static readonly HashSet<string> Plain = new HashSet<string>
        {
            "Transform", "MeshFilter", "MeshRenderer", "BoxCollider", "MeshCollider", "Animation", "Animator", "TransformOverride",
        };

        /// <summary>Every transform of BoardStandardGame down to depth 2 (and the deck/top-of-deck bones), minus FX/sound.</summary>
        private static JObject Bones(UnityAssets ua, Canvas cv, List<Node> nodes)
        {
            var o = new JObject();
            foreach (var n in nodes)
            {
                var names = n.Comps.Select(c => c.name).ToHashSet();
                bool deepOk = (n.Path.Contains("/TopOfDeckBones/") || n.Path.Contains("/TooltipZones/")) && n.Depth <= 4;
                if (n.Depth == 0 || names.Overlaps(SkipComps) || !(n.Depth <= 2 || deepOk)) continue;
                var key = n.Path.Substring(n.Path.IndexOf('/') + 1);
                var e = Entry(ua, cv, n);
                var mbs = names.Where(c => !Plain.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
                if (mbs.Count > 0) e["components"] = new JArray(mbs);
                o[key] = e;
            }
            return o;
        }

        /// <summary>ZoneHand.GetCardPosition/GetCardRotation for n cards (PC, not enlarged, nothing moused over or drawn).</summary>
        private static JObject HandLayout(Canvas cv, double[] center, double maxw, bool enemy, double[] cardV, int n)
        {
            double cx = center[0], cy = center[1], cz = center[2];
            double spacing = 1.270804 * n <= maxw ? 1.270804 : maxw / n;
            bool scrunched = n > 3;
            var scale = enemy ? new[] { 0.682, 0.125, 0.682 } : new[] { 0.62, 0.125, 0.62 };
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            for (int i = 0; i < cardV.Length; i += 3)
            {
                minX = Math.Min(minX, cardV[i]); maxX = Math.Max(maxX, cardV[i]); minZ = Math.Min(minZ, cardV[i + 2]); maxZ = Math.Max(maxZ, cardV[i + 2]);
            }
            var cards = new JArray();
            for (int i = 0; i < n; i++)
            {
                double half = spacing / 2, x = cx + (i * 2 - (n - 1)) * half, rotY = 0.0;
                if (n > 1 && scrunched)
                {
                    double a = 40.0 + (enemy ? 0 : n * 2), step = a / (n - 1), start = -a / 2;
                    rotY = enemy ? rotY - (step * i + start) : rotY + (step * i + start);
                }
                double arc = scrunched ? Math.Pow(i + 0.5 - n / 2.0, 2) / (6 * n) : 0.0;
                double lift = (enemy && rotY < 0) || (!enemy && rotY > 0) ? Math.Sin(Math.Abs(rotY * (Math.PI / 180.0))) * half : 0.0;
                double z = enemy ? cz + (arc + lift) : cz - (arc + lift);
                var r = Euler(0, rotY, 352.5);
                var pos = new[] { x, cy, z };
                var corners = Place(new[] { minX, 0, minZ, maxX, 0, minZ, maxX, 0, maxZ, minX, 0, maxZ }, pos, scale, r);
                cards.Add(new JObject
                {
                    ["world"] = R(pos), ["canvas"] = cv.Pt(pos), ["rot_y"] = Rnd(rotY, 3), ["rot_z"] = 352.5,
                    ["box"] = cv.Box(Place(cardV, pos, scale, r)),
                    ["quad"] = new JArray(Enumerable.Range(0, 4).Select(k => cv.Pt(corners.Skip(k * 3).Take(3).ToArray()))),
                });
            }
            return new JObject { ["spacing"] = Rnd(spacing, 4), ["cards"] = cards };
        }

        /// <summary>{guid: bundle file} from the game's asset catalog.</summary>
        private static Dictionary<string, string> Catalog(string hs)
        {
            using var ua = new UnityAssets(hs, null);
            ua.Open("asset_manifest.unity3d");
            foreach (var f in ua.Files)
                foreach (var o in ua.Objects(f, AssetClassID.MonoBehaviour))
                {
                    if (ua.PeekName(o) != "base_assets_catalog") continue;
                    var names = o.F["m_bundleNames"]["Array"].Children.Select(c => c.AsString).ToList();
                    var cat = new Dictionary<string, string>();
                    foreach (var a in o.F["m_assets"]["Array"].Children) cat[a["guid"].AsString] = names[a["bundleId"].AsInt];
                    return cat;
                }
            throw new KeyNotFoundException("base_assets_catalog not found");
        }

        public static void Build(string hs, Dictionary<string, string> cabs, List<(string bundle, string root)> boards, CameraInfo cam, string outDir, Action<string, int, int> progress, CancellationToken ct)
        {
            var cat = Catalog(hs);
            var cv = new Canvas(cam);
            using var ua = new UnityAssets(hs, cabs);
            ua.Open(cat[Guid["EndTurnButton"]]);
            var nodes = Tree(ua, "BoardStandardGame");
            var by = new Dictionary<string, Node>();
            foreach (var n in nodes) by[n.Path.Contains('/') ? n.Path.Substring(n.Path.IndexOf('/') + 1) : ""] = n;
            double[] Wp(string path) => Geo.Origin(by[path].World);
            double[] Sc(string path) => Geo.ColumnNorms(by[path].World);
            void Step() { ct.ThrowIfCancellationRequested(); progress("Layout", 0, 1); }
            Step();

            // actor meshes, in actor space
            var ally = ActorMesh(ua, "Card_Play_Ally", "Card_InPlay_Ally_mesh");
            var hero = ActorMesh(ua, "Card_Play_Hero", "Hero_Mesh");
            var hpow = ActorMesh(ua, "Card_Play_HeroPower", "HeroPowerV2");
            var weap = ActorMesh(ua, "Card_Play_Weapon", "Weapon_InPlay_Mesh");
            var hand = ActorMesh(ua, "Card_Hand_Ally", "Card_InHand_Ally_Base_mesh");
            var hidden = ActorMesh(ua, "Card_Hidden", "Card_Hidden");
            var tileFull = ActorMesh(ua, "HistoryTile_Card", "History_Tile_Full_mesh");
            var tileHalf = ActorMesh(ua, "HistoryTile_Attack", "History_Tile_half_mesh");
            double[] secret;
            using (var ua2 = new UnityAssets(hs, cabs))
            {
                ua2.Open(cat[Guid["Card_Play_Secret_Mage"]]);
                secret = ActorMesh(ua2, "Card_Play_Secret_Mage", "Secret_mesh");
            }
            // mana crystal: Resource prefab keeps its own root scale under ManaCrystalMgr
            var resNodes = Tree(ua, "Resource");
            var gemWorld = WorldPoints(ua, Find(resNodes, "/Gem_Mana"));
            var resOrigin = Geo.Origin(resNodes[0].World);
            var gemRel = gemWorld.Select((p, i) => p - resOrigin[i % 3]).ToArray();   // includes the prefab's root scale (0.815)
            double crystalW = Enumerable.Range(0, gemWorld.Length / 3).Max(i => gemWorld[i * 3]) - Enumerable.Range(0, gemWorld.Length / 3).Min(i => gemWorld[i * 3]);
            var tileNodes = new[] { "HistoryTile_Card", "HistoryTile_Attack" }.ToDictionary(k => k, k => Tree(ua, k, identityRoot: true));
            Step();

            var camJson = new JObject
            {
                ["position"] = new JArray(cam.Position), ["rotation_quat"] = new JArray(cam.Rotation), ["fov_deg"] = cam.Fov,
                ["orthographic"] = cam.Ortho, ["ortho_size"] = cam.OrthoSize, ["near"] = cam.Near, ["far"] = cam.Far,
            };
            var L = new JObject
            {
                ["about"] = new JObject
                {
                    ["source"] = "Hearthstone game data (BoardStandardGame prefab, actor prefabs, EndTurnButton prefab); Blizzard's, for personal use only",
                    ["generated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["canvas"] = new JArray(W, H), ["camera"] = camJson,
                    ["projection"] = "v = R^T (world - camera.position); x = (v.x/(v.z*tan(fov/2)*4/3)+1)/2*800; y = (1 - v.y/(v.z*tan(fov/2)))/2*600 (same as the board renders)",
                    ["units"] = "world = Unity units (left-handed, +y up, +z away from the player); canvas = pixels on 800x600; boxes are [x, y, w, h]",
                    ["note"] = "PC values (TransformOverride screen category 4). Boxes are the projected bounds of the named mesh as the game scales it; minions/heroes etc. also have gems/FX that stick out a little.",
                },
            };
            var bones = Bones(ua, cv, nodes);
            Step();
            L["bones"] = bones;
            JToken BoneBox(string key) => bones[key]?["collider_box"];

            // zones per side
            var zones = new JObject();
            foreach (var (side, S) in new[] { ("friendly", "Friendly"), ("opposing", "Opposing") })
            {
                var z = new JObject();
                string zp = $"ZoneMgr/Zone_{S}_Play";
                var (corners, c, s) = BoxCollider(by[zp]);
                var pscale = Sc(zp);
                var center = Add(Linear(by[zp].World, c), Wp(zp));
                double slotW = s[0] * pscale[0] / 7;
                double SlotX(int n, int i) => center[0] - n * slotW / 2 + slotW / 2 + i * slotW;
                var slots = new JObject();
                for (int n = 1; n <= 7; n++)
                    slots[n.ToString()] = new JArray(Enumerable.Range(0, n).Select(i => cv.Pt(new[] { SlotX(n, i), center[1], center[2] })));
                var mid = Place(ally, center, pscale);
                var midBox = cv.Box(mid);
                z["play"] = new JObject
                {
                    ["zone_world"] = R(Wp(zp)), ["center_world"] = R(center), ["center"] = cv.Pt(center),
                    ["collider_world_size"] = R(s.Select((v, i) => v * pscale[i])), ["collider_box"] = cv.Box(corners),
                    ["max_slots"] = 7, ["slot_width_world"] = Rnd(slotW, 4), ["card_scale"] = R(pscale, 3),
                    ["slot_canvas_spacing_at_center"] = Rnd((double)cv.Pt(Add(center, slotW, 0, 0))[0] - (double)cv.Pt(center)[0], 2),
                    ["minion_box_at_center"] = midBox, ["minion_size_at_center"] = new JArray(midBox[2], midBox[3]),
                    ["minion_boxes_7"] = new JArray(Enumerable.Range(0, 7).Select(i => cv.Box(Place(ally, new[] { SlotX(7, i), center[1], center[2] }, pscale)))),
                    ["slots"] = slots,
                };
                string zh = $"ZoneMgr/Zone_{S}_Hero";
                var heroP = Place(hero, Wp(zh), Sc(zh));
                z["hero"] = new JObject
                {
                    ["world"] = R(Wp(zh)), ["canvas"] = cv.Pt(Wp(zh)), ["card_scale"] = R(Sc(zh), 3),
                    ["frame_box"] = cv.Box(heroP), ["frame_polygon"] = Simplify(Hull(cv.Xys(heroP)), 16),
                };
                foreach (var (key, zname, V) in new[] { ("hero_power", $"Zone_{S}_HeroPower", hpow), ("weapon", $"Zone_{S}_Weapon", weap) })
                {
                    string zpath = "ZoneMgr/" + zname;
                    var e = new JObject
                    {
                        ["world"] = R(Wp(zpath)), ["canvas"] = cv.Pt(Wp(zpath)), ["card_scale"] = R(Sc(zpath), 3),
                        ["box"] = cv.Box(Place(V, Wp(zpath), Sc(zpath))),
                    };
                    var alt = by[zpath].Comp("TransformAlternate");
                    if (alt != null)   // used instead while a Battlegrounds quest-reward / trinket sub-zone holds a card
                    {
                        var tt = alt.F;
                        int i = tt["m_screenCategory"]["Array"].Children.Select(x => x.AsInt).ToList().IndexOf(PC);
                        if (i >= 0)
                        {
                            var lp = UnityAssets.V3(tt["m_localPosition"]["Array"][i]);
                            var ls = UnityAssets.V3(tt["m_localScale"]["Array"][i]);
                            var ap = Add(Wp("ZoneMgr"), lp);
                            e["alternate"] = new JObject
                            {
                                ["world"] = R(ap), ["canvas"] = cv.Pt(ap), ["card_scale"] = R(ls, 3), ["box"] = cv.Box(Place(V, ap, ls)),
                                ["note"] = "TransformAlternate: Battlegrounds, when a quest reward/trinket hero power is shown",
                            };
                        }
                    }
                    z[key] = e;
                }
                string za = $"ZoneMgr/Zone_{S}_HeroPower_Additional";
                z["hero_power_additional"] = new JObject
                {
                    ["world"] = R(Wp(za)), ["canvas"] = cv.Pt(Wp(za)), ["card_scale"] = R(Sc(za), 3), ["box"] = cv.Box(Place(hpow, Wp(za), Sc(za))),
                };
                // secrets: ZoneSecret.UpdateLayout_Default around the hero's mesh bounds
                string zs = $"ZoneMgr/Zone_{S}_Secret";
                var ext = new[] { 0, 1, 2 }.Select(k => (Enumerable.Range(0, heroP.Length / 3).Max(i => heroP[i * 3 + k]) - Enumerable.Range(0, heroP.Length / 3).Min(i => heroP[i * 3 + k])) / 2).ToArray();
                double vx = ext[0], vy = ext[2] * 0.9;
                var secrets = new JArray();
                for (int i = 0; i < 7; i++)
                {
                    var p = Wp(zs);
                    int n3 = (i + 1) >> 1;
                    double n5 = n3 > 2 ? 1.0 : n3 == 1 ? 0.6 : n3 / 2.0;
                    p[0] += (i & 1) == 0 ? vx * n5 : -vx * n5;
                    p[2] -= vy * n5 * n5;
                    if (n3 > 2) p[2] -= 0.6 * vy * (n3 - 2);
                    secrets.Add(new JObject { ["index"] = i, ["world"] = R(p), ["canvas"] = cv.Pt(p), ["box"] = cv.Box(Place(secret, p, Sc(zs))) });
                }
                z["secrets"] = new JObject
                {
                    ["zone_world"] = R(Wp(zs)), ["zone_canvas"] = cv.Pt(Wp(zs)), ["hero_extents_xz"] = R(new[] { vx, ext[2] }, 4),
                    ["icon_scale"] = R(Sc(zs), 3), ["positions"] = secrets,
                };
                // hand
                string zhp = $"ZoneMgr/Zone_{S}_Hand";
                var (hc, hcC, hcS) = BoxCollider(by[zhp]);
                var handCenter = Add(Linear(by[zhp].World, hcC), Wp(zhp));
                var hzCenter = handCenter.Select(v => Rnd(v, 3)).ToArray();
                double maxWidth = hcS[0] * Sc(zhp)[0];
                bool enemy = side == "opposing";
                var layouts = new JObject();
                for (int n = 1; n <= 10; n++) layouts[n.ToString()] = HandLayout(cv, hzCenter, maxWidth, enemy, enemy ? hidden : hand, n);
                z["hand"] = new JObject
                {
                    ["zone_world"] = R(Wp(zhp)), ["center_world"] = R(handCenter), ["center"] = cv.Pt(handCenter),
                    ["max_width_world"] = Rnd(maxWidth, 4), ["default_spacing_world"] = 1.270804,
                    ["card_scale"] = enemy ? new JArray(0.682, 0.125, 0.682) : new JArray(0.62, 0.125, 0.62),
                    ["card_mesh"] = enemy ? "Card_Hidden (card back)" : "Card_InHand_Ally_Base_mesh",
                    ["collider_box"] = cv.Box(hc), ["layouts"] = layouts,
                };
                // deck
                var deckMesh = nodes.First(n => n.Path.EndsWith($"Zone_{S}_Deck/DeckStates/Deck_Full/RootObject/Mesh", StringComparison.Ordinal));
                string dz = $"ZoneMgr/Zone_{S}_Deck";
                z["deck"] = new JObject
                {
                    ["world"] = R(Wp(dz)), ["canvas"] = cv.Pt(Wp(dz)), ["full_deck_box"] = cv.Box(WorldPoints(ua, deckMesh)),
                    ["tooltip_zone_box"] = BoneBox("TooltipZones/" + (enemy ? "EnemyDeck" : "Deck")),
                };
                string mc = $"ManaCounter_{S}";
                z["mana_counter"] = new JObject
                {
                    ["world"] = R(Wp(mc)), ["canvas"] = cv.Pt(Wp(mc)), ["text_box"] = cv.Box(TextRect(by[mc]).rect),
                    ["tooltip_zone_box"] = BoneBox("TooltipZones/" + (enemy ? "EnemyManaRack" : "MyManaRack")),
                };
                zones[side] = z;
            }

            // mana crystals (friendly only: the opponent just has the counter)
            var mgr = Wp("ManaCrystalMgr");
            var crystals = new JArray();
            for (int k = 0; k < 10; k++)
            {
                var p = Add(mgr, k * crystalW, 0, 0);
                crystals.Add(new JObject { ["index"] = k, ["world"] = R(p), ["canvas"] = cv.Pt(p), ["box"] = cv.Box(gemRel.Select((g, i) => g + p[i % 3]).ToArray()) });
            }
            ((JObject)zones["friendly"])["mana_crystals"] = new JObject
            {
                ["start_world"] = R(mgr), ["crystal_width_world"] = Rnd(crystalW, 4), ["crystals"] = crystals,
            };
            L["zones"] = zones;

            // history
            var hm = by["HistoryManager"];
            var bcs = hm.Comps.Where(e => e.name == "BoxCollider").Select(e => e.obj.F).ToList();
            var baseBc = bcs.Aggregate((a, b) => b["m_Size"]["z"].AsFloat > a["m_Size"]["z"].AsFloat ? b : a);
            var hmS = Sc("HistoryManager");
            var hCenter = UnityAssets.V3(baseBc["m_Center"]);
            var hSize = UnityAssets.V3(baseBc["m_Size"]);
            var topTile = Add(Add(Wp("HistoryManager"), hCenter), 0, -0.32, hSize[2] / 2 / hmS[2]);
            double TileSize(List<Node> tn)
            {
                var (cs, _, _) = BoxCollider(tn.First(n => n.Name == "Collider"));
                var zs = Enumerable.Range(0, cs.Length / 3).Select(i => cs[i * 3 + 2]).ToList();
                return (zs.Max() - zs.Min()) * hmS[2];
            }
            double fullSz = TileSize(tileNodes["HistoryTile_Card"]), halfSz = TileSize(tileNodes["HistoryTile_Attack"]);
            var tiles = new JArray();
            double acc = 0.0;
            for (int k = 0; k < 12; k++)
            {
                var p = Add(topTile, 0, 0, -acc);
                tiles.Add(new JObject
                {
                    ["index"] = k, ["world"] = R(p), ["canvas"] = cv.Pt(p), ["box"] = cv.Box(Place(tileFull, p, hmS)),
                    ["kept"] = acc + fullSz <= hSize[2] * hmS[2] + 1e-6,
                });
                acc += fullSz + 0.15;
            }
            var hLocal = new List<double>();
            foreach (var x in Unit) foreach (var y in Unit) foreach (var zz in Unit) { hLocal.Add(x * hSize[0] + hCenter[0]); hLocal.Add(y * hSize[1] + hCenter[1]); hLocal.Add(zz * hSize[2] + hCenter[2]); }
            L["history"] = new JObject
            {
                ["manager_world"] = R(Wp("HistoryManager")), ["manager_scale"] = R(hmS, 3),
                ["collider_box"] = cv.Box(Geo.Apply(hm.World, hLocal.ToArray())),
                ["column_length_world"] = Rnd(hSize[2] * hmS[2], 4), ["top_tile_world"] = R(topTile), ["top_tile"] = cv.Pt(topTile),
                ["full_tile_size_world"] = Rnd(fullSz, 4), ["half_tile_size_world"] = Rnd(halfSz, 4), ["space_between_tiles_world"] = 0.15,
                ["full_tile_box_at_top"] = cv.Box(Place(tileFull, topTile, hmS)), ["half_tile_box_at_top"] = cv.Box(Place(tileHalf, topTile, hmS)),
                ["full_tiles"] = tiles,
            };

            // big card (played-card preview), rope
            var bc = Wp("Bones/BigCardPosition");
            L["big_card"] = new JObject
            {
                ["world"] = R(bc), ["canvas"] = cv.Pt(bc), ["card_scale"] = 1.03, ["box"] = cv.Box(Place(hand, bc, new[] { 1.03, 1.03, 1.03 })),
                ["spell_or_hero_power_scale"] = 0.6,
                ["note"] = "minions/weapons/heroes show at BigCardPosition (HistoryCard.ShowBigCard scale 1.03); spells, hero powers and metadata big cards stay at the card's own position at scale 0.6",
            };
            var tbone = Wp("Bones/TurnTimerBone");
            var sp = new Dictionary<string, double[]>();
            foreach (var n in Tree(ua, "TurnTimer"))
                if (n.Name is "SparksStartBone" or "SparksFinishBone" or "CountdownText") sp[n.Name] = Add(Geo.Origin(n.World), tbone);
            L["turn_timer"] = new JObject
            {
                ["rope_start_world"] = R(sp["SparksStartBone"]), ["rope_start"] = cv.Pt(sp["SparksStartBone"]),
                ["rope_end_world"] = R(sp["SparksFinishBone"]), ["rope_end"] = cv.Pt(sp["SparksFinishBone"]),
                ["countdown_text"] = cv.Pt(sp["CountdownText"]), ["rope_seconds"] = 20,
                ["note"] = "the spark runs from rope_start (20 s left) to rope_end (0 s) linearly (TurnTimer.m_RopeCapSeconds = 20; position = lerp(end, start, remaining/20))",
            };
            Step();
            L["end_turn_button"] = EndTurnButton(ua, Wp("Bones/EndTurnButton"), cam, cv, outDir);
            L["formulas"] = new JObject
            {
                ["minion_slots"] = "ZonePlay: slot_width = collider.bounds.size.x / 7 (= 12 * 0.92 / 7 world); with n minions minion i (0-based) is at x = center.x - n*slot_width/2 + slot_width/2 + i*slot_width, y/z = collider center; card scale = zone localScale (0.92). A card being dragged over the row counts as one more slot.",
                ["hand"] = "ZoneHand: spacing = 1.270804, or max_width/n when n*1.270804 > max_width; x = center.x + (2i-(n-1))*spacing/2; scrunched when n > 3: fan angle a = 40 (+2n for your hand), rot_y = i*a/(n-1) - a/2 (negated for the opponent), z -= (i+0.5-n/2)^2/(6n) + (rot_y>0 ? sin(|rot_y|)*spacing/2 : 0) (+= for the opponent); rotation Euler(0, rot_y, 352.5); scale (0.62,0.125,0.62) yours, (0.682,0.125,0.682) theirs. Hovered card: scale 1.5, y = center.y+1, z = MouseOverCardHeight.z+0.15.",
                ["secrets"] = "ZoneSecret (PC): with hero mesh half-extents ex, ez' = ez*0.9: secret i, k=(i+1)>>1, f = 0 (k=0), 0.6 (k=1), 1 (k>=2); x = zone.x + (i even ? +1 : -1)*ex*f; z = zone.z - ez'*f*f - (k>2 ? 0.6*ez'*(k-2) : 0)",
                ["mana_crystals"] = "ManaCrystalMgr: crystal k at ManaCrystalMgr.position + (k*width, 0, 0), width = Gem_Mana renderer bounds.size.x; the opponent has no crystals on PC, only ManaCounter_Opposing text",
                ["history"] = "HistoryManager: top = position + collider.center + (0, -0.32, collider.size.z/2/localScale.z); tiles from newest (top) down: z = top.z - sum(previous tile sizes + 0.15) (+ size/2 for half tiles); tiles that make the column longer than collider.bounds.size.z drop off",
                ["weapon_hero_power"] = "Card sits at the zone position with the zone's localScale (ZoneWeapon tweens from +1.5 y)",
            };
            L["board_tints"] = BoardTints(hs, boards, progress, ct);
            L["board_tints_note"] = "Gameplay multiplies every End Turn button material colour by its board's m_EndTurnButtonColor; the PNGs are drawn untinted (white, like Stormwind)";

            var path = Path.Combine(outDir, "layout.json");
            using (var sw = new StreamWriter(path + ".tmp"))
            using (var w = new JsonTextWriter(sw) { Formatting = Formatting.Indented, Indentation = 1 })
                L.WriteTo(w);
            File.Move(path + ".tmp", path, true);
            progress("Layout", 1, 1);
        }

        /// <summary>Board.m_EndTurnButtonColor (multiplied into the button's material) / deck / history tile colours per board.</summary>
        private static JObject BoardTints(string hs, List<(string bundle, string root)> boards, Action<string, int, int> progress, CancellationToken ct)
        {
            var o = new JObject();
            foreach (var (bundle, root) in boards)
            {
                ct.ThrowIfCancellationRequested();
                progress("Layout", 0, 1);
                try
                {
                    using var ua = new UnityAssets(hs, null);
                    ua.Open(bundle);
                    var (go, _) = ua.FindRoot(root);
                    foreach (var (_, r) in ua.Components(go))
                    {
                        AssetTypeValueField tt;
                        try { tt = r.F; } catch (Exception) { continue; }
                        if (tt["m_EndTurnButtonColor"].IsDummy) continue;
                        JArray Col(AssetTypeValueField c) => R(new double[] { c["r"].AsFloat, c["g"].AsFloat, c["b"].AsFloat, c["a"].AsFloat });
                        o[root] = new JObject
                        {
                            ["end_turn_button"] = Col(tt["m_EndTurnButtonColor"]), ["deck"] = Col(tt["m_DeckColor"]), ["history_tile"] = Col(tt["m_HistoryTileColor"]),
                        };
                        break;
                    }
                }
                catch (Exception e) { GameArtBuilder.Log?.Invoke($"tint of {root}: {e.Message}"); }
            }
            return o;
        }
    }
}
