using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace HearthStoner.Art
{
    /// <summary>One object in a loaded asset file (read lazily).</summary>
    internal sealed class UObj
    {
        public readonly UnityAssets Owner;
        public readonly AssetsFileInstance File;
        public readonly AssetFileInfo Info;
        private AssetTypeValueField _f;

        public UObj(UnityAssets owner, AssetsFileInstance file, AssetFileInfo info) { Owner = owner; File = file; Info = info; }

        /// <summary>The object's fields (from its type tree).</summary>
        public AssetTypeValueField F => _f ??= Owner.Read(File, Info);
        public int TypeId => Info.TypeId;
        public (string, long) Key => (File.name, Info.PathId);
        public UObj Deref(AssetTypeValueField pptr) => Owner.Deref(File, pptr);
        public string Name => F["m_Name"].AsString;
    }

    /// <summary>A mesh's positions (3 per vertex), first UVs (2 per vertex) and one triangle list per submesh.</summary>
    internal sealed class MeshData
    {
        public string Name;
        public float[] V, Uv;
        public List<int[]> Subs = new List<int[]>();
    }

    /// <summary>
    /// Reads Hearthstone's Unity asset bundles with AssetsTools.NET. References into other bundles are followed through
    /// a map of the bundles' internal CAB files; Unity's built-in resources (stored without type trees) are read with
    /// the type trees of the same classes from the bundles, which come from the same Unity build.
    /// Textures (DXT1/5, BC4 and the plain formats Hearthstone uses) and meshes are decoded here.
    /// </summary>
    internal sealed class UnityAssets : IDisposable
    {
        public readonly AssetsManager Am = new AssetsManager { UseTemplateFieldCache = true, UseMonoTemplateFieldCache = true };
        private readonly string _hs, _win;
        private readonly IReadOnlyDictionary<string, string> _cabs;

        public UnityAssets(string hearthstoneDir, IReadOnlyDictionary<string, string> cabs)
        {
            _hs = hearthstoneDir;
            _win = Path.Combine(hearthstoneDir, "Data", "Win");
            _cabs = cabs ?? new Dictionary<string, string>();
        }

        public void Dispose() => Am.UnloadAll(true);

        /// <summary>Loads a bundle of Data\Win (and its serialized files); returns its first serialized file.</summary>
        public AssetsFileInstance Open(string bundle)
        {
            var bun = Am.LoadBundleFile(Path.Combine(_win, bundle));
            AssetsFileInstance first = null;
            for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                if (bun.file.IsAssetsFile(i)) first ??= Am.LoadAssetsFileFromBundle(bun, i);
            return first;
        }

        /// <summary>Every loaded serialized file, in load order.</summary>
        public IEnumerable<AssetsFileInstance> Files => Am.Files.ToList();

        public IEnumerable<UObj> Objects(AssetsFileInstance file, AssetClassID type) =>
            file.file.Metadata.GetAssetsOfType(type).Select(i => new UObj(this, file, i));

        public UObj Deref(AssetsFileInstance from, AssetTypeValueField pptr) =>
            pptr == null || pptr.IsDummy ? null : Get(from, pptr["m_FileID"].AsInt, pptr["m_PathID"].AsLong);

        public UObj Get(AssetsFileInstance from, int fileId, long pathId)
        {
            if (fileId == 0 && pathId == 0) return null;
            try
            {
                var file = fileId == 0 ? from : Dependency(from, fileId - 1);
                var info = file?.file.GetAssetInfo(pathId);
                return info == null ? null : new UObj(this, file, info);
            }
            catch (Exception) { return null; }
        }

        private AssetsFileInstance Dependency(AssetsFileInstance from, int i)
        {
            var ext = from.file.Metadata.Externals;
            if (i < 0 || i >= ext.Count) return null;
            string path = ext[i].PathName, key = path.Substring(path.LastIndexOf('/') + 1).ToLowerInvariant();
            if (!Am.FileLookup.ContainsKey(key))
            {
                var builtin = Path.Combine(_hs, "Hearthstone_Data", "Resources", key);   // Unity's own Quad etc.
                if ((key == "unity default resources" || key == "unity_builtin_extra") && System.IO.File.Exists(builtin)) Am.LoadAssetsFile(builtin);
                else if (_cabs.TryGetValue(key, out var bundle)) Open(bundle);
            }
            return from.GetDependency(Am, i);
        }

        public AssetTypeValueField Read(AssetsFileInstance file, AssetFileInfo info)
        {
            try { return Am.GetBaseField(file, info); }
            catch (Exception) when (!file.file.Metadata.TypeTreeEnabled && Borrow(info.TypeId)) { return Am.GetBaseField(file, info); }
        }

        // Caches the class's template from a bundle that has its type tree (the template cache is per class id).
        private bool Borrow(int typeId)
        {
            foreach (var f in Am.Files)
                if (f.file.Metadata.TypeTreeEnabled && f.file.Metadata.FindTypeTreeTypeByID(typeId) != null)
                {
                    try { Am.GetTemplateBaseField(f, f.file.Reader, 0, typeId, 0xffff, AssetReadFlags.None); return true; }
                    catch (Exception) { }
                }
            return false;
        }

        /// <summary>Unity's class name of an object (MonoBehaviours give "MonoBehaviour").</summary>
        public string TypeName(UObj o)
        {
            try { return Am.GetTemplateBaseField(o.File, o.Info).Type; }
            catch (Exception) { return ((AssetClassID)o.TypeId).ToString(); }
        }

        /// <summary>The script class of a MonoBehaviour, else the Unity class name.</summary>
        public string ClassName(UObj o)
        {
            var n = TypeName(o);
            if (n != "MonoBehaviour") return n;
            return o.Deref(o.F["m_Script"])?.F["m_ClassName"].AsString ?? n;
        }

        /// <summary>The name of a named object without reading it all (m_Name comes first; after the
        /// GameObject / enabled / script fields for a MonoBehaviour).</summary>
        public string PeekName(UObj o)
        {
            if (o.TypeId == (int)AssetClassID.GameObject) return o.Name;
            long at = o.Info.GetAbsoluteByteOffset(o.File.file) + (o.TypeId == (int)AssetClassID.MonoBehaviour ? 28 : 0);
            lock (o.File.LockReader)
            {
                var r = o.File.file.Reader;
                r.Position = at;
                int len = r.ReadInt32();
                if (len < 0 || len > 4096 || len > o.Info.ByteSize) return null;
                return Encoding.UTF8.GetString(r.ReadBytes(len));
            }
        }

        /// <summary>Bytes of a streamed resource ("archive:/CAB-.../CAB-....resS") in a bundle.</summary>
        public byte[] Resource(AssetsFileInstance from, string path, long offset, long size)
        {
            string name = path.Substring(path.LastIndexOf('/') + 1);
            var bun = from.parentBundle;
            int index = bun?.file.GetFileIndex(name) ?? -1;
            if (index < 0 && _cabs.TryGetValue(name.ToLowerInvariant(), out var other))
            {
                Open(other);
                bun = Am.LoadBundleFile(Path.Combine(_win, other));
                index = bun.file.GetFileIndex(name);
            }
            if (index < 0) throw new FileNotFoundException("resource not found", path);
            bun.file.GetFileRange(index, out long start, out _);
            lock (bun.file.DataReader)
            {
                var r = bun.file.DataReader;
                r.Position = start + offset;
                return r.ReadBytes((int)size);
            }
        }

        private byte[] StreamedOr(UObj o, byte[] inline)
        {
            var sd = o.F["m_StreamData"];
            if (sd.IsDummy) return inline;
            var path = sd["path"].AsString;
            return string.IsNullOrEmpty(path) ? inline : Resource(o.File, path, sd["offset"].AsLong, sd["size"].AsLong);
        }

        // ------------------------------------------------------------------ prefabs
        /// <summary>Follows a reference and reads the object; null if it's empty or broken.</summary>
        public UObj Read(UObj from, AssetTypeValueField pptr)
        {
            try
            {
                var o = from.Deref(pptr);
                return o?.F == null ? null : o;
            }
            catch (Exception) { return null; }
        }

        /// <summary>A GameObject's components (class name, or script class for MonoBehaviours when `scripts`).</summary>
        public List<(string name, UObj obj)> Components(UObj go, bool scripts = false)
        {
            var list = new List<(string, UObj)>();
            foreach (var c in go.F["m_Component"]["Array"].Children)
            {
                try
                {
                    var o = go.Deref(c["component"]);
                    if (o != null) list.Add((scripts ? ClassName(o) : TypeName(o), o));
                }
                catch (Exception) { }
            }
            return list;
        }

        public static UObj Component(List<(string name, UObj obj)> comps, string name) =>
            comps.FirstOrDefault(c => c.name == name).obj;

        public UObj TransformOf(UObj go)
        {
            var comps = Components(go);
            return Component(comps, "Transform") ?? Component(comps, "RectTransform");
        }

        /// <summary>The (Transform, GameObject) pairs under a Transform.</summary>
        public IEnumerable<(UObj t, UObj go)> Children(UObj t)
        {
            foreach (var ch in t.F["m_Children"]["Array"].Children.ToList())
            {
                var ct = Read(t, ch);
                var go = ct == null ? null : Read(ct, ct.F["m_GameObject"]);
                if (go != null) yield return (ct, go);
            }
        }

        /// <summary>The first GameObject called `name` (in any loaded file) whose Transform has no parent.</summary>
        public (UObj go, UObj t) FindRoot(string name)
        {
            foreach (var f in Files)
                foreach (var o in Objects(f, AssetClassID.GameObject))
                {
                    try
                    {
                        if (o.Name != name) continue;
                        var t = TransformOf(o);
                        if (t != null && t.F["m_Father"]["m_PathID"].AsLong == 0) return (o, t);
                    }
                    catch (Exception) { }
                }
            throw new KeyNotFoundException($"root {name} not found");
        }

        public static double[] V3(AssetTypeValueField v) => new double[] { v["x"].AsFloat, v["y"].AsFloat, v["z"].AsFloat };
        public static double[] Q4(AssetTypeValueField v) => new double[] { v["x"].AsFloat, v["y"].AsFloat, v["z"].AsFloat, v["w"].AsFloat };

        /// <summary>A Transform's local matrix.</summary>
        public static double[] Trs(UObj t) => Geo.Trs(V3(t.F["m_LocalPosition"]), Q4(t.F["m_LocalRotation"]), V3(t.F["m_LocalScale"]));

        /// <summary>A material's saved properties of one kind (m_TexEnvs, m_Colors, m_Floats) by name, in file order.</summary>
        public static List<(string key, AssetTypeValueField value)> Props(UObj mat, string kind)
        {
            var list = new List<(string, AssetTypeValueField)>();
            foreach (var p in mat.F["m_SavedProperties"][kind]["Array"].Children)
            {
                string key = p["first"].AsString;
                int i = list.FindIndex(e => e.Item1 == key);
                if (i >= 0) list[i] = (key, p["second"]); else list.Add((key, p["second"]));
            }
            return list;
        }

        private static readonly string[] MainKeys = { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo", "_Diffuse", "_MainTexture" };

        /// <summary>The material's main Texture2D (else the first one it has) and its tex-env (scale / offset).</summary>
        public (UObj tex, AssetTypeValueField env) MainTexture(UObj mat)
        {
            var envs = Props(mat, "m_TexEnvs");
            foreach (var key in MainKeys.Where(k => envs.Any(e => e.key == k)).Concat(envs.Select(e => e.key).Where(k => !MainKeys.Contains(k))))
            {
                var env = envs.First(e => e.key == key).value;
                var tex = Read(mat, env["m_Texture"]);
                if (tex != null && tex.TypeId == (int)AssetClassID.Texture2D) return (tex, env);
            }
            return (null, null);
        }

        // ------------------------------------------------------------------ meshes
        private static readonly int[] FormatSize = { 4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };

        private static float Component(byte[] d, int at, int format) => format switch
        {
            0 => BitConverter.ToSingle(d, at),
            1 => (float)BitConverter.ToHalf(d, at),
            2 or 6 => d[at],
            3 or 7 => (sbyte)d[at],
            4 or 8 => BitConverter.ToUInt16(d, at),
            5 or 9 => BitConverter.ToInt16(d, at),
            10 => BitConverter.ToUInt32(d, at),
            _ => BitConverter.ToInt32(d, at),
        };

        public MeshData ReadMesh(UObj mesh)
        {
            var f = mesh.F;
            var m = new MeshData { Name = f["m_Name"].AsString };
            var vd = f["m_VertexData"];
            int count = vd["m_VertexCount"].AsInt;
            var chans = vd["m_Channels"]["Array"].Children
                .Select(c => (stream: c["stream"].AsInt, offset: c["offset"].AsInt, format: c["format"].AsInt, dim: c["dimension"].AsInt)).ToList();
            var data = StreamedOr(mesh, vd["m_DataSize"].AsByteArray);

            // stream layout: channels packed per stream, each stream 16-byte aligned
            int streams = chans.Count == 0 ? 0 : chans.Max(c => c.stream) + 1;
            var sOffset = new int[streams];
            var sStride = new int[streams];
            for (int s = 0, off = 0; s < streams; s++)
            {
                foreach (var c in chans) if (c.stream == s && c.dim > 0) sStride[s] += (c.dim & 0xF) * FormatSize[c.format];
                sOffset[s] = off;
                off = (off + count * sStride[s] + 15) & ~15;
            }
            float[] Channel(int chn, int keep)
            {
                if (chn >= chans.Count || chans[chn].dim == 0 || data == null || data.Length == 0) return null;
                var c = chans[chn];
                int dim = c.dim & 0xF, size = FormatSize[c.format];
                var o = new float[count * keep];
                for (int v = 0; v < count; v++)
                {
                    int at = sOffset[c.stream] + c.offset + v * sStride[c.stream];
                    for (int k = 0; k < Math.Min(keep, dim); k++) o[v * keep + k] = Component(data, at + k * size, c.format);
                }
                return o;
            }
            m.V = Channel(0, 3) ?? Array.Empty<float>();
            m.Uv = Channel(4, 2) ?? new float[m.V.Length / 3 * 2];

            var ib = f["m_IndexBuffer"]["Array"].AsByteArray ?? Array.Empty<byte>();
            bool use16 = f["m_IndexFormat"].IsDummy || f["m_IndexFormat"].AsInt == 0;
            int nIdx = ib.Length / (use16 ? 2 : 4);
            int Index(int i) => use16 ? BitConverter.ToUInt16(ib, i * 2) : (int)BitConverter.ToUInt32(ib, i * 4);
            foreach (var sm in f["m_SubMeshes"]["Array"].Children)
            {
                int first = sm["firstByte"].AsInt / (use16 ? 2 : 4), n = sm["indexCount"].AsInt, topology = sm["topology"].AsInt;
                int baseVertex = sm["baseVertex"].IsDummy ? 0 : sm["baseVertex"].AsInt;
                var t = new List<int>();
                if (nIdx == 0) { m.Subs.Add(Array.Empty<int>()); continue; }
                if (topology == 0)
                    for (int i = first; i < first + n; i += 3) { t.Add(Index(i)); t.Add(Index(i + 1)); t.Add(Index(i + 2)); }
                else if (topology == 1)   // strip
                    for (int i = first; i < first + n - 2; i++)
                    {
                        int a = Index(i), b = Index(i + 1), c = Index(i + 2);
                        if (a == b || a == c || b == c) continue;
                        if (((i - first) & 1) != 0) { t.Add(b); t.Add(a); t.Add(c); } else { t.Add(a); t.Add(b); t.Add(c); }
                    }
                else if (topology == 2)   // quads
                    for (int i = first; i < first + n; i += 4)
                    {
                        int a = Index(i), b = Index(i + 1), c = Index(i + 2), d = Index(i + 3);
                        t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d);
                    }
                else throw new InvalidDataException("lines or points mesh");
                m.Subs.Add(t.Select(i => i + baseVertex).ToArray());
            }
            return m;
        }

        // ------------------------------------------------------------------ textures
        /// <summary>A Texture2D's first mip as RGBA, top row first (decoded as Pillow does, so the result matches).</summary>
        public Img ReadTexture(UObj tex)
        {
            var f = tex.F;
            int w = f["m_Width"].AsInt, h = f["m_Height"].AsInt, format = f["m_TextureFormat"].AsInt;
            var data = StreamedOr(tex, f["image data"].AsByteArray);
            if (w == 0 || h == 0) return new Img(0, 0, 4);
            if (data == null || data.Length == 0) throw new InvalidDataException("Texture2D has no image data");
            var img = Decode(data, w, h, format);
            // Unity stores the bottom row first
            var o = new Img(w, h, 4);
            for (int y = 0; y < h; y++) Buffer.BlockCopy(img.P, (h - 1 - y) * w * 4, o.P, y * w * 4, w * 4);
            return o;
        }

        internal static Img Decode(byte[] d, int w, int h, int format)
        {
            var o = new Img(w, h, 4);
            switch (format)
            {
                case 3:   // RGB24
                    for (int i = 0; i < w * h; i++) { o.P[i * 4] = d[i * 3]; o.P[i * 4 + 1] = d[i * 3 + 1]; o.P[i * 4 + 2] = d[i * 3 + 2]; o.P[i * 4 + 3] = 255; }
                    return o;
                case 4:   // RGBA32
                    Buffer.BlockCopy(d, 0, o.P, 0, w * h * 4);
                    return o;
                case 5:   // ARGB32
                    for (int i = 0; i < w * h; i++) { o.P[i * 4] = d[i * 4 + 1]; o.P[i * 4 + 1] = d[i * 4 + 2]; o.P[i * 4 + 2] = d[i * 4 + 3]; o.P[i * 4 + 3] = d[i * 4]; }
                    return o;
                case 14:  // BGRA32
                    for (int i = 0; i < w * h; i++) { o.P[i * 4] = d[i * 4 + 2]; o.P[i * 4 + 1] = d[i * 4 + 1]; o.P[i * 4 + 2] = d[i * 4]; o.P[i * 4 + 3] = d[i * 4 + 3]; }
                    return o;
                case 7:   // RGB565
                    for (int i = 0; i < w * h; i++)
                    {
                        int p = d[i * 2] + (d[i * 2 + 1] << 8);
                        o.P[i * 4 + 2] = (byte)((p & 31) * 255 / 31);
                        o.P[i * 4 + 1] = (byte)(((p >> 5) & 63) * 255 / 63);
                        o.P[i * 4] = (byte)(((p >> 11) & 31) * 255 / 31);
                        o.P[i * 4 + 3] = 255;
                    }
                    return o;
                case 10: case 12: case 26:   // DXT1, DXT5, BC4
                {
                    int bw = (w + 3) / 4, bh = (h + 3) / 4, bytes = format == 10 || format == 26 ? 8 : 16;
                    var px = new byte[64];
                    for (int by = 0; by < bh; by++)
                        for (int bx = 0; bx < bw; bx++)
                        {
                            int at = (by * bw + bx) * bytes;
                            if (format == 10) Bc1(d, at, px, false);
                            else if (format == 12) { Bc1(d, at + 8, px, true); Bc3Alpha(d, at, px, 3); }
                            else { Bc3Alpha(d, at, px, 0); for (int i = 0; i < 16; i++) { px[i * 4 + 1] = px[i * 4 + 2] = px[i * 4]; px[i * 4 + 3] = 255; } }
                            for (int n = 0; n < 4; n++)
                                for (int k = 0; k < 4; k++)
                                {
                                    int x = bx * 4 + k, y = by * 4 + n;
                                    if (x < w && y < h) Buffer.BlockCopy(px, (n * 4 + k) * 4, o.P, (y * w + x) * 4, 4);
                                }
                        }
                    return o;
                }
                default:
                    throw new NotSupportedException("texture format " + format);
            }
        }

        // Pillow's BcnDecode.c: BC1 colour block (BC2/3 always use the 4-colour mode).
        private static void Bc1(byte[] d, int at, byte[] px, bool separateAlpha)
        {
            int c0 = d[at] | (d[at + 1] << 8), c1 = d[at + 2] | (d[at + 3] << 8);
            var p = new int[4, 4];
            void Rgb565(int x, int i)
            {
                int r = (x & 0xf800) >> 8; r |= r >> 5;
                int g = (x & 0x7e0) >> 3; g |= g >> 6;
                int b = (x & 0x1f) << 3; b |= b >> 5;
                p[i, 0] = r; p[i, 1] = g; p[i, 2] = b; p[i, 3] = 255;
            }
            Rgb565(c0, 0); Rgb565(c1, 1);
            for (int k = 0; k < 3; k++)
                if (c0 > c1 || separateAlpha) { p[2, k] = (2 * p[0, k] + p[1, k]) / 3; p[3, k] = (p[0, k] + 2 * p[1, k]) / 3; }
                else { p[2, k] = (p[0, k] + p[1, k]) / 2; p[3, k] = 0; }
            p[2, 3] = 255;
            p[3, 3] = c0 > c1 || separateAlpha ? 255 : 0;
            for (int n = 0; n < 4; n++)
                for (int o = 0; o < 4; o++)
                {
                    int cw = 3 & (d[at + 4 + n] >> (2 * o));
                    for (int k = 0; k < 4; k++) px[(n * 4 + o) * 4 + k] = (byte)p[cw, k];
                }
        }

        // BC3 alpha / BC4 block into channel `channel` of the 16 pixels.
        private static void Bc3Alpha(byte[] d, int at, byte[] px, int channel)
        {
            int a0 = d[at], a1 = d[at + 1];
            int lut1 = d[at + 2] | (d[at + 3] << 8) | (d[at + 4] << 16), lut2 = d[at + 5] | (d[at + 6] << 8) | (d[at + 7] << 16);
            var a = new int[8];
            a[0] = a0; a[1] = a1;
            if (a0 > a1) for (int i = 2; i < 8; i++) a[i] = ((8 - i) * a0 + (i - 1) * a1) / 7;
            else { for (int i = 2; i < 6; i++) a[i] = ((6 - i) * a0 + (i - 1) * a1) / 5; a[6] = 0; a[7] = 255; }
            for (int n = 0; n < 8; n++) px[n * 4 + channel] = (byte)a[7 & (lut1 >> (3 * n))];
            for (int n = 0; n < 8; n++) px[(8 + n) * 4 + channel] = (byte)a[7 & (lut2 >> (3 * n))];
        }
    }
}
