using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HearthStoner.Art
{
    /// <summary>4x4 transforms as row-major double[16] (column vectors, as Unity and numpy use them).</summary>
    internal static class Geo
    {
        public static double[] Identity() => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        /// <summary>Rotation matrix of a quaternion (x, y, z, w), not normalized (as Unity's TRS reads it).</summary>
        public static double[,] Rotation(double x, double y, double z, double w) => new double[,]
        {
            { 1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w) },
            { 2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w) },
            { 2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y) },
        };

        /// <summary>Translation * rotation * scale.</summary>
        public static double[] Trs(double[] p, double[] q, double[] s)
        {
            var r = Rotation(q[0], q[1], q[2], q[3]);
            var m = Identity();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) m[i * 4 + j] = r[i, j] * s[j];
                m[i * 4 + 3] = p[i];
            }
            return m;
        }

        public static double[] Mul(double[] a, double[] b)
        {
            var m = new double[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    m[i * 4 + j] = a[i * 4] * b[j] + a[i * 4 + 1] * b[4 + j] + a[i * 4 + 2] * b[8 + j] + a[i * 4 + 3] * b[12 + j];
            return m;
        }

        public static double[] Origin(double[] m) => new[] { m[3], m[7], m[11] };

        /// <summary>Points (x, y, z triples) through the matrix: P @ M[:3,:3].T + M[:3,3].</summary>
        public static double[] Apply(double[] m, double[] pts)
        {
            var o = new double[pts.Length];
            for (int i = 0; i < pts.Length; i += 3)
                for (int r = 0; r < 3; r++)
                    o[i + r] = pts[i] * m[r * 4] + pts[i + 1] * m[r * 4 + 1] + pts[i + 2] * m[r * 4 + 2] + m[r * 4 + 3];
            return o;
        }

        /// <summary>Length of each of the first three columns (the world scale along each local axis).</summary>
        public static double[] ColumnNorms(double[] m) =>
            Enumerable.Range(0, 3).Select(j => Math.Sqrt(m[j] * m[j] + m[4 + j] * m[4 + j] + m[8 + j] * m[8 + j])).ToArray();

        public static double[] Inverse(double[] m)
        {
            var a = new double[4, 8];
            for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) a[i, j] = m[i * 4 + j]; a[i, 4 + i] = 1; }
            for (int c = 0; c < 4; c++)
            {
                int p = c;
                for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                for (int j = 0; j < 8; j++) (a[c, j], a[p, j]) = (a[p, j], a[c, j]);
                double d = a[c, c];
                for (int j = 0; j < 8; j++) a[c, j] /= d;
                for (int r = 0; r < 4; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c];
                    for (int j = 0; j < 8; j++) a[r, j] -= f * a[c, j];
                }
            }
            var o = new double[16];
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) o[i * 4 + j] = a[i, 4 + j];
            return o;
        }
    }

    /// <summary>The camera a board is seen through, as the game's BoardCameras prefab sets it up.</summary>
    internal sealed class CameraInfo
    {
        public double[] Position, Rotation; // rotation as a quaternion (x, y, z, w)
        public double Fov, OrthoSize, Near, Far;
        public bool Ortho;
    }

    /// <summary>
    /// Unity camera: view = R^T (world - position), R from the rotation (columns = right, up, forward);
    /// perspective x = vx / (vz tan(fov/2) aspect), y = vy / (vz tan(fov/2)); pixel = ((x+1)/2 W, (1-y)/2 H).
    /// </summary>
    internal sealed class Camera
    {
        public readonly double[] Pos;
        public readonly double[,] R;
        public readonly bool Ortho;
        public readonly double Tan, Size, Far, Aspect;
        public double Near;

        public Camera(CameraInfo c, double aspect)
        {
            Pos = c.Position;
            var q = c.Rotation;
            double n = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            R = Geo.Rotation(q[0] / n, q[1] / n, q[2] / n, q[3] / n);
            Ortho = c.Ortho;
            Tan = Math.Tan((c.Fov == 0 ? 60.0 : c.Fov) * (Math.PI / 180.0) / 2);
            Size = c.OrthoSize == 0 ? 5.0 : c.OrthoSize;
            Near = c.Near == 0 ? 0.3 : c.Near;
            Far = c.Far == 0 ? 1000 : c.Far;
            Aspect = aspect;
        }

        public void ToView(double x, double y, double z, out double vx, out double vy, out double vz)
        {
            x -= Pos[0]; y -= Pos[1]; z -= Pos[2];
            vx = x * R[0, 0] + y * R[1, 0] + z * R[2, 0];
            vy = x * R[0, 1] + y * R[1, 1] + z * R[2, 1];
            vz = x * R[0, 2] + y * R[1, 2] + z * R[2, 2];
        }

        /// <summary>View point to screen x, y, a screen-linear depth key and 1/w.</summary>
        public void Project(double vx, double vy, double vz, int w, int h, out double sx, out double sy, out double zkey, out double q)
        {
            double nx, ny;
            if (Ortho) { nx = vx / (Size * Aspect); ny = vy / Size; zkey = vz; q = 1; }
            else { nx = vx / (vz * Tan * Aspect); ny = vy / (vz * Tan); q = 1.0 / vz; zkey = -q; } // -1/z grows with distance
            sx = (nx + 1) * 0.5 * w;
            sy = (1 - ny) * 0.5 * h;
        }

        /// <summary>A world point on a w x h picture.</summary>
        public (double x, double y) Screen(double x, double y, double z, int w, int h)
        {
            ToView(x, y, z, out var vx, out var vy, out var vz);
            Project(vx, vy, vz, w, h, out var sx, out var sy, out _, out _);
            return (sx, sy);
        }
    }

    /// <summary>A texture as RGBA floats 0..1, top row first.</summary>
    internal sealed class Tex
    {
        public readonly int W, H;
        public readonly float[] Px;
        private static readonly float[] Unit = Enumerable.Range(0, 256).Select(i => (float)i / 255f).ToArray();

        public Tex(Img rgba)
        {
            W = rgba.W; H = rgba.H; Px = new float[rgba.P.Length];
            for (int i = 0; i < Px.Length; i++) Px[i] = Unit[rgba.P[i]];
        }

        /// <summary>Bilinear, repeating. Unity's v = 0 is the bottom row.</summary>
        public void Sample(double u, double v, double[] c)
        {
            double x = u * W - 0.5, y = (1.0 - v) * H - 0.5;
            if (double.IsNaN(x) || double.IsNaN(y)) { x = y = 0; }
            double x0 = Math.Floor(x), y0 = Math.Floor(y), fx = x - x0, fy = y - y0;
            int ix0 = Mod(x0, W), iy0 = Mod(y0, H), ix1 = (ix0 + 1) % W, iy1 = (iy0 + 1) % H;
            int a = (iy0 * W + ix0) * 4, b = (iy0 * W + ix1) * 4, d = (iy1 * W + ix0) * 4, e = (iy1 * W + ix1) * 4;
            for (int k = 0; k < 4; k++)
                c[k] = ((double)Px[a + k] * (1 - fx) + (double)Px[b + k] * fx) * (1 - fy) + ((double)Px[d + k] * (1 - fx) + (double)Px[e + k] * fx) * fy;
        }

        private static int Mod(double v, int n)
        {
            long r = (long)v % n;
            return (int)(r < 0 ? r + n : r);
        }
    }

    /// <summary>One submesh of a board piece, placed in the world, with its material's look.</summary>
    internal sealed class SceneObject
    {
        public string Name, Path, Shader, TextureName;
        public double[] World;   // x, y, z per vertex, world space
        public double[] Uv;      // u, v per vertex
        public int[] Tris;       // 3 vertex indices per triangle
        public Tex Texture;
        public float[] Tint;     // material colour (RGBA), or null
        public double[] St;      // texture scale x, y and offset x, y, or null
        public int? RenderQueue;
        public bool Transparent, Active = true, Enabled = true;
    }

    internal enum Blend { Alpha, Add, Mul }

    /// <summary>
    /// The small software rasterizer the boards are drawn with: z-buffered opaque pieces, then see-through ones
    /// back to front (alpha blended, added for glows, multiplied for fake shadows). Perspective-correct texturing,
    /// no lighting (the game's board textures carry their lighting). Rows are split into bands drawn in parallel.
    /// </summary>
    internal sealed class Rasterizer
    {
        private const int Band = 16;
        public readonly int W, H;
        private readonly Camera _cam;
        private readonly float[] _rgb, _a, _z;   // premultiplied colour, alpha, depth key
        // Depth of what this draw call wrote, unrounded: one piece's own fragments are compared at full precision
        // (as the numpy original resolved them together), other pieces' against the single-precision buffer.
        private readonly double[] _zDraw;
        private readonly int[] _zStamp;
        private int _draw;

        public Rasterizer(int w, int h, Camera cam)
        {
            W = w; H = h; _cam = cam;
            _rgb = new float[w * h * 3];
            _a = new float[w * h];
            _z = new float[w * h];
            Array.Fill(_z, float.PositiveInfinity);
            _zDraw = new double[w * h];
            _zStamp = new int[w * h];
        }

        private struct Tri
        {
            public double X0, Y0, X1, Y1, X2, Y2, Z0, Z1, Z2, Q0, Q1, Q2;
            public double U0, V0, U1, V1, U2, V2;
            public int Bx, By, Bw, Bh;
        }

        /// <summary>Draws one piece; returns its triangle count after near clipping.</summary>
        public int Draw(double[] world, double[] uv, int[] tris, Tex tex, float[] tint, double[] st, bool transparent, bool cutout, Blend mode)
        {
            int draw = ++_draw;
            int nv = world.Length / 3;
            var view = new double[nv * 3];
            for (int i = 0; i < nv; i++)
            {
                _cam.ToView(world[i * 3], world[i * 3 + 1], world[i * 3 + 2], out var x, out var y, out var z);
                view[i * 3] = x; view[i * 3 + 1] = y; view[i * 3 + 2] = z;
            }
            var corners = ClipNear(view, uv, tris, _cam.Near);   // 15 numbers per triangle: x y z u v per corner
            int n = corners.Count / 15;
            var list = new List<(Tri t, double meanZ)>(n);
            for (int k = 0; k < n; k++)
            {
                var t = new Tri();
                var c = corners;
                int o = k * 15;
                _cam.Project(c[o], c[o + 1], c[o + 2], W, H, out t.X0, out t.Y0, out t.Z0, out t.Q0);
                _cam.Project(c[o + 5], c[o + 6], c[o + 7], W, H, out t.X1, out t.Y1, out t.Z1, out t.Q1);
                _cam.Project(c[o + 10], c[o + 11], c[o + 12], W, H, out t.X2, out t.Y2, out t.Z2, out t.Q2);
                t.U0 = c[o + 3]; t.V0 = c[o + 4]; t.U1 = c[o + 8]; t.V1 = c[o + 9]; t.U2 = c[o + 13]; t.V2 = c[o + 14];
                double mnx = Math.Min(t.X0, Math.Min(t.X1, t.X2)), mxx = Math.Max(t.X0, Math.Max(t.X1, t.X2));
                double mny = Math.Min(t.Y0, Math.Min(t.Y1, t.Y2)), mxy = Math.Max(t.Y0, Math.Max(t.Y1, t.Y2));
                t.Bx = (int)Math.Clamp(Math.Ceiling(mnx - 0.5), 0, W);
                t.By = (int)Math.Clamp(Math.Ceiling(mny - 0.5), 0, H);
                t.Bw = (int)Math.Clamp(Math.Floor(mxx - 0.5), -1, W - 1) - t.Bx + 1;
                t.Bh = (int)Math.Clamp(Math.Floor(mxy - 0.5), -1, H - 1) - t.By + 1;
                double a2 = (t.X1 - t.X0) * (t.Y2 - t.Y0) - (t.Y1 - t.Y0) * (t.X2 - t.X0);
                double minZ = Math.Min(c[o + 2], Math.Min(c[o + 7], c[o + 12]));
                if (t.Bw > 0 && t.Bh > 0 && Math.Abs(a2) > 1e-12 && minZ <= _cam.Far)
                    list.Add((t, (c[o + 2] + c[o + 7] + c[o + 12]) / 3));
            }
            // see-through pieces: triangles back to front inside the piece too
            // opaque ones in the order the numpy original resolved equal depths: small triangles first (by bbox
            // size bucket 2, 4, 8, 16, 32, 64+ px), so coplanar pieces come out the same
            var ordered = transparent ? list.Select((e, i) => (e, i)).OrderBy(p => -p.e.meanZ).ThenBy(p => p.i).Select(p => p.e.t).ToList()
                                      : list.Select((e, i) => (e, i)).OrderBy(p => SizeBucket(Math.Max(p.e.t.Bw, p.e.t.Bh))).ThenBy(p => p.i).Select(p => p.e.t).ToList();
            int bands = (H + Band - 1) / Band;
            var perBand = new List<int>[bands];
            for (int b = 0; b < bands; b++) perBand[b] = new List<int>();
            for (int i = 0; i < ordered.Count; i++)
                for (int b = ordered[i].By / Band; b <= (ordered[i].By + ordered[i].Bh - 1) / Band; b++) perBand[b].Add(i);
            Parallel.For(0, bands, b =>
            {
                var col = new double[4];
                int ylo = b * Band, yhi = Math.Min(H, ylo + Band);
                foreach (int i in perBand[b])
                {
                    var t = ordered[i];
                    double area = (t.X1 - t.X0) * (t.Y2 - t.Y0) - (t.Y1 - t.Y0) * (t.X2 - t.X0);
                    for (int py = Math.Max(t.By, ylo); py < Math.Min(t.By + t.Bh, yhi); py++)
                    {
                        double cy = py + 0.5;
                        for (int px = t.Bx; px < t.Bx + t.Bw; px++)
                        {
                            double cx = px + 0.5;
                            double l0 = ((t.X1 - cx) * (t.Y2 - cy) - (t.Y1 - cy) * (t.X2 - cx)) / area;
                            double l1 = ((t.X2 - cx) * (t.Y0 - cy) - (t.Y2 - cy) * (t.X0 - cx)) / area;
                            double l2 = 1.0 - l0 - l1;
                            if (!(l0 >= 0 && l1 >= 0 && l2 >= 0)) continue;   // both windings drawn (no culling)
                            int pix = py * W + px;
                            double zk = l0 * t.Z0 + l1 * t.Z1 + l2 * t.Z2;
                            if (!transparent)
                            {
                                if (cutout) { Shade(t, l0, l1, l2, tex, tint, st, col); if (!(col[3] >= 0.5)) continue; }
                                if (!(zk < (_zStamp[pix] == draw ? _zDraw[pix] : _z[pix]))) continue;
                                if (!cutout) Shade(t, l0, l1, l2, tex, tint, st, col);
                                _z[pix] = (float)zk;
                                _zDraw[pix] = zk;
                                _zStamp[pix] = draw;
                                _rgb[pix * 3] = (float)col[0]; _rgb[pix * 3 + 1] = (float)col[1]; _rgb[pix * 3 + 2] = (float)col[2];
                                _a[pix] = 1f;
                            }
                            else
                            {
                                if (!(zk < _z[pix])) continue;   // depth-tested, not written
                                Shade(t, l0, l1, l2, tex, tint, st, col);
                                BlendInto(pix, col, mode);
                            }
                        }
                    }
                }
            });
            return n;
        }

        private static int SizeBucket(int size)
        {
            int b = 0;
            for (int s = 2; s < Math.Min(size, 64); s *= 2) b++;
            return b;
        }

        private static void Shade(in Tri t, double l0, double l1, double l2, Tex tex, float[] tint, double[] st, double[] col)
        {
            double q0 = l0 * t.Q0, q1 = l1 * t.Q1, q2 = l2 * t.Q2, sum = q0 + q1 + q2;   // perspective-correct weights
            double b0 = q0 / sum, b1 = q1 / sum, b2 = q2 / sum;
            double u = b0 * t.U0 + b1 * t.U1 + b2 * t.U2, v = b0 * t.V0 + b1 * t.V1 + b2 * t.V2;
            if (st != null) { u = u * st[0] + st[2]; v = v * st[1] + st[3]; }   // Unity _ST: uv * scale + offset
            if (tex != null) tex.Sample(u, v, col);
            else col[0] = col[1] = col[2] = col[3] = 1;
            if (tint != null) for (int k = 0; k < 4; k++) col[k] *= tint[k];
        }

        // Blend math on one pixel; public for tests.
        internal static void BlendPixel(float[] rgb, float[] a, int pix, double[] c, Blend mode)
        {
            double al = c[3];
            for (int k = 0; k < 3; k++)
            {
                double d = rgb[pix * 3 + k];
                rgb[pix * 3 + k] = (float)(mode switch
                {
                    Blend.Add => d + c[k] * al,                 // glows: black adds nothing
                    Blend.Mul => d * (1 - al + c[k] * al),      // fake shadows: darken what's below
                    _ => c[k] * al + d * (1 - al),              // SrcAlpha, OneMinusSrcAlpha
                });
            }
            if (mode == Blend.Alpha) a[pix] = (float)(al + a[pix] * (1 - al));
        }

        private void BlendInto(int pix, double[] c, Blend mode) => BlendPixel(_rgb, _a, pix, c, mode);

        /// <summary>The picture as premultiplied RGBA bytes (Pillow's "RGBa").</summary>
        public Img Image()
        {
            var im = new Img(W, H, 4);
            Parallel.For(0, H, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    int p = y * W + x;
                    for (int k = 0; k < 3; k++) im.P[p * 4 + k] = ToByte(_rgb[p * 3 + k]);
                    im.P[p * 4 + 3] = ToByte(_a[p]);
                }
            });
            return im;
        }

        private static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

        /// <summary>
        /// Clips triangles (view-space corners) against z >= near: 1 corner behind gives 2 triangles, 2 behind give 1,
        /// 3 behind none. Returns x y z u v per corner, unclipped triangles first (in order), then the clipped ones.
        /// </summary>
        internal static List<double> ClipNear(double[] view, double[] uv, int[] tris, double near)
        {
            var keep = new List<double>(tris.Length * 5);
            var one1 = new List<double>(); var one2 = new List<double>(); var two = new List<double>();
            var a = new double[3][];
            for (int t = 0; t < tris.Length; t += 3)
            {
                int nb = 0;
                for (int k = 0; k < 3; k++) if (view[tris[t + k] * 3 + 2] < near) nb++;
                if (nb == 3) continue;
                if (nb == 0)
                {
                    for (int k = 0; k < 3; k++) AddCorner(keep, view, uv, tris[t + k]);
                    continue;
                }
                // the corner on its own side goes first
                int odd = 0;
                for (int k = 0; k < 3; k++) if ((view[tris[t + k] * 3 + 2] < near) == (nb == 1)) { odd = k; break; }
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[t + (odd + k) % 3];
                    a[k] = new[] { view[vi * 3], view[vi * 3 + 1], view[vi * 3 + 2], uv[vi * 2], uv[vi * 2 + 1] };
                }
                double[] Cut(int i)
                {
                    double s = (near - a[0][2]) / (a[i][2] - a[0][2]);
                    var p = new double[5];
                    for (int j = 0; j < 5; j++) p[j] = a[0][j] + s * (a[i][j] - a[0][j]);
                    return p;
                }
                var p1 = Cut(1); var p2 = Cut(2);
                if (nb == 1) { one1.AddRange(p1); one1.AddRange(a[1]); one1.AddRange(a[2]); one2.AddRange(p1); one2.AddRange(a[2]); one2.AddRange(p2); }
                else { two.AddRange(a[0]); two.AddRange(p1); two.AddRange(p2); }
            }
            keep.AddRange(one1); keep.AddRange(one2); keep.AddRange(two);
            return keep;
        }

        private static void AddCorner(List<double> l, double[] view, double[] uv, int vi)
        {
            l.Add(view[vi * 3]); l.Add(view[vi * 3 + 1]); l.Add(view[vi * 3 + 2]); l.Add(uv[vi * 2]); l.Add(uv[vi * 2 + 1]);
        }

        private static readonly Regex Cutout = new Regex("cutout|alphatest|clip", RegexOptions.IgnoreCase);
        private static readonly Regex Additive = new Regex("add|commonfx|glow|(?<!alpha)distortion", RegexOptions.IgnoreCase);
        private static readonly Regex Multiply = new Regex("multiply", RegexOptions.IgnoreCase);

        /// <summary>
        /// Draws the pieces through the camera at (width * ss) x (height * ss) and scales down (Lanczos);
        /// returns straight-alpha RGBA. Order: opaque (queue, near to far), then see-through (queue, far to near).
        /// </summary>
        public static Img Render(IEnumerable<SceneObject> objects, CameraInfo camera, int width, int height, int ss)
        {
            int w = width * ss, h = height * ss;
            var cam = new Camera(camera, (double)w / h);
            if (!cam.Ortho) cam.Near = Math.Max(cam.Near, 1e-4);
            var objs = objects.Where(o => o.Active && o.Enabled && o.World != null && o.World.Length > 0).ToList();
            if (objs.Count == 0) throw new InvalidOperationException("nothing to render");
            var keyed = objs.Select((o, i) =>
            {
                bool transp = o.Transparent || (o.RenderQueue ?? 0) > 2500;
                int queue = o.RenderQueue is int q && q != 0 ? q : transp ? 3000 : 2000;
                double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue }, hi = { double.MinValue, double.MinValue, double.MinValue };
                for (int k = 0; k < o.World.Length; k++) { lo[k % 3] = Math.Min(lo[k % 3], o.World[k]); hi[k % 3] = Math.Max(hi[k % 3], o.World[k]); }
                cam.ToView((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2, out _, out _, out var dist);
                return (o, transp, queue, key: transp ? -dist : dist, i);
            }).OrderBy(e => e.transp).ThenBy(e => e.queue).ThenBy(e => e.key).ThenBy(e => e.i).ToList();

            var r = new Rasterizer(w, h, cam);
            foreach (var (o, transp, _, _, _) in keyed)
            {
                string sh = o.Shader ?? "";
                var mode = Additive.IsMatch(sh) ? Blend.Add : Multiply.IsMatch(sh) ? Blend.Mul : Blend.Alpha;
                // fake shadows are dark blobs on white: white must leave the board as is, so no tint
                var tint = mode == Blend.Mul ? null : o.Tint;
                r.Draw(o.World, o.Uv, o.Tris, o.Texture, tint, o.St, transp || mode != Blend.Alpha, Cutout.IsMatch(sh), mode);
            }
            var img = r.Image();
            if (ss > 1) img = Pixels.Resize(img, width, height);
            return Pixels.Unpremultiply(img);
        }
    }
}
