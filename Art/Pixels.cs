using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HearthStoner.Art
{
    /// <summary>An 8-bit picture: 1 (a mask), 3 (RGB) or 4 (RGBA, straight alpha) bytes per pixel, top row first.</summary>
    internal sealed class Img
    {
        public readonly int W, H, N;
        public readonly byte[] P;

        public Img(int w, int h, int n, byte fill = 0)
        {
            W = w; H = h; N = n; P = new byte[w * h * n];
            if (fill != 0) Array.Fill(P, fill);
        }

        public Img(int w, int h, int n, byte[] p) { W = w; H = h; N = n; P = p; }

        public Img Clone() => new Img(W, H, N, (byte[])P.Clone());
    }

    /// <summary>
    /// The Pillow 13 operations the game-art steps use, ported bit for bit (blur, flood fill, shapes, Lanczos resize,
    /// HSV), so the art comes out as it did from the old Python tools.
    /// </summary>
    internal static class Pixels
    {
        // ------------------------------------------------------------------ plain helpers
        /// <summary>The box (x0, y0, x1, y1), with zeros where it reaches past the picture (as Pillow's crop).</summary>
        public static Img Crop(Img im, int x0, int y0, int x1, int y1)
        {
            var o = new Img(x1 - x0, y1 - y0, im.N);
            for (int y = Math.Max(0, -y0); y < o.H && y + y0 < im.H; y++)
            {
                int sx = Math.Max(0, -x0), ex = Math.Min(o.W, im.W - x0);
                if (ex > sx) Buffer.BlockCopy(im.P, ((y + y0) * im.W + sx + x0) * im.N, o.P, (y * o.W + sx) * o.N, (ex - sx) * im.N);
            }
            return o;
        }

        public static Img Crop(Img im, int[] box) => Crop(im, box[0], box[1], box[2], box[3]);

        /// <summary>Bounds (x0, y0, x1, y1) of the non-zero alpha (or mask) pixels, as Pillow's getbbox; null if none.</summary>
        public static int[] BBox(Img im)
        {
            int x0 = im.W, y0 = im.H, x1 = 0, y1 = 0;
            for (int y = 0; y < im.H; y++)
                for (int x = 0; x < im.W; x++)
                {
                    int i = (y * im.W + x) * im.N;
                    bool any = im.N == 4 ? im.P[i + 3] != 0 : im.N == 3 ? (im.P[i] | im.P[i + 1] | im.P[i + 2]) != 0 : im.P[i] != 0;
                    if (!any) continue;
                    if (x < x0) x0 = x;
                    if (x >= x1) x1 = x + 1;
                    if (y < y0) y0 = y;
                    y1 = y + 1;
                }
            return x1 == 0 ? null : new[] { x0, y0, x1, y1 };
        }

        public static Img PutAlpha(Img rgba, Img mask)
        {
            var o = rgba.Clone();
            for (int i = 0; i < mask.P.Length; i++) o.P[i * 4 + 3] = mask.P[i];
            return o;
        }

        public static Img Channel(Img im, int c)
        {
            var o = new Img(im.W, im.H, 1);
            for (int i = 0; i < o.P.Length; i++) o.P[i] = im.P[i * im.N + c];
            return o;
        }

        public static Img ToRgb(Img rgba)
        {
            var o = new Img(rgba.W, rgba.H, 3);
            for (int i = 0, n = rgba.W * rgba.H; i < n; i++)
            {
                o.P[i * 3] = rgba.P[i * 4]; o.P[i * 3 + 1] = rgba.P[i * 4 + 1]; o.P[i * 3 + 2] = rgba.P[i * 4 + 2];
            }
            return o;
        }

        public static Img ToRgba(Img rgb)
        {
            var o = new Img(rgb.W, rgb.H, 4, 255);
            for (int i = 0, n = rgb.W * rgb.H; i < n; i++)
            {
                o.P[i * 4] = rgb.P[i * 3]; o.P[i * 4 + 1] = rgb.P[i * 3 + 1]; o.P[i * 4 + 2] = rgb.P[i * 3 + 2];
            }
            return o;
        }

        // RGBA <-> RGBa (premultiplied), as Pillow converts around a resize.
        public static Img Premultiply(Img im)
        {
            var o = im.Clone();
            for (int i = 0; i < o.P.Length; i += 4)
            {
                int a = o.P[i + 3];
                for (int c = 0; c < 3; c++) { int t = o.P[i + c] * a + 128; o.P[i + c] = (byte)(((t >> 8) + t) >> 8); }
            }
            return o;
        }

        public static Img Unpremultiply(Img im)
        {
            var o = im.Clone();
            for (int i = 0; i < o.P.Length; i += 4)
            {
                int a = o.P[i + 3];
                if (a == 255 || a == 0) continue;
                for (int c = 0; c < 3; c++) o.P[i + c] = (byte)Math.Min(255, 255 * o.P[i + c] / a);
            }
            return o;
        }

        /// <summary>Pillow's alpha_composite of a straight-alpha RGBA picture over opaque black, as RGB.</summary>
        public static Img OverBlack(Img rgba)
        {
            var o = new Img(rgba.W, rgba.H, 3);
            for (int i = 0, n = rgba.W * rgba.H; i < n; i++)
            {
                uint sa = rgba.P[i * 4 + 3];
                if (sa == 0) continue;
                uint blend = 255 * (255 - sa), outa255 = sa * 255 + blend;
                uint coef1 = sa * 255 * 255 * 128 / outa255;
                for (int c = 0; c < 3; c++)
                {
                    uint t = rgba.P[i * 4 + c] * coef1 + (0x80u << 7);
                    o.P[i * 3 + c] = (byte)((((t >> 8) + t) >> 8) >> 7);
                }
            }
            return o;
        }

        // ------------------------------------------------------------------ board-art cut-outs
        public static Img CropToContent(Img im) => BBox(im) is int[] b ? Crop(im, b) : im.Clone();

        /// <summary>Cuts a shape off a flat background: the background is flood-filled in from the corners, and only the
        /// part connected to (kx, ky) stays (so other pieces on the same sheet are dropped).</summary>
        public static Img Cutout(Img im, int thresh, int kx, int ky, float feather = 0.8f)
        {
            var mag = new byte[] { 255, 0, 255 };
            var w = ToRgb(im);
            foreach (var (x, y) in new[] { (1, 1), (w.W - 2, 1), (1, w.H - 2), (w.W - 2, w.H - 2) }) FloodFill(w, x, y, mag, thresh);
            var fg = new Img(w.W, w.H, 1);
            for (int i = 0; i < fg.P.Length; i++)
                fg.P[i] = (byte)(w.P[i * 3] != 255 || w.P[i * 3 + 1] != 0 || w.P[i * 3 + 2] != 255 ? 255 : 0);
            FloodFill(fg, kx, ky, new byte[] { 128 }, 0);
            for (int i = 0; i < fg.P.Length; i++) fg.P[i] = (byte)(fg.P[i] == 128 ? 255 : 0);
            return CropToContent(PutAlpha(im, Blur(MinFilter3(fg), feather)));
        }

        /// <summary>Hue turned from red to the mana blue (the facets' shading is kept); saturation and brightness scaled.</summary>
        public static Img Recolour(Img gem, double sat, double val)
        {
            var hsv = RgbToHsv(ToRgb(gem));
            for (int i = 0; i < hsv.P.Length; i += 3)
            {
                hsv.P[i] = (byte)((hsv.P[i] + 154) % 256);
                hsv.P[i + 1] = (byte)(int)(hsv.P[i + 1] * sat);
                hsv.P[i + 2] = (byte)Math.Min(255, (int)(hsv.P[i + 2] * val));
            }
            return PutAlpha(ToRgba(HsvToRgb(hsv)), Channel(gem, 3));
        }

        // ------------------------------------------------------------------ HSV (Convert.c, "following colorsys.py")
        public static Img RgbToHsv(Img rgb)
        {
            var o = new Img(rgb.W, rgb.H, 3);
            for (int i = 0; i < rgb.P.Length; i += 3)
            {
                byte r = rgb.P[i], g = rgb.P[i + 1], b = rgb.P[i + 2];
                byte maxc = Math.Max(r, Math.Max(g, b)), minc = Math.Min(r, Math.Min(g, b)), uh = 0, us = 0;
                if (minc != maxc)
                {
                    float cr = maxc - minc, s = cr / maxc;
                    float rc = (maxc - r) / cr, gc = (maxc - g) / cr, bc = (maxc - b) / cr, h;
                    if (r == maxc) h = bc - gc;
                    else if (g == maxc) h = (float)(2.0 + rc - bc);
                    else h = (float)(4.0 + gc - rc);
                    h = (float)CMod(h / 6.0 + 1.0, 1.0);
                    uh = Clip8((int)(h * 255.0));
                    us = Clip8((int)(s * 255.0));
                }
                o.P[i] = uh; o.P[i + 1] = us; o.P[i + 2] = maxc;
            }
            return o;
        }

        public static Img HsvToRgb(Img hsv)
        {
            var o = new Img(hsv.W, hsv.H, 3);
            for (int j = 0; j < hsv.P.Length; j += 3)
            {
                byte h = hsv.P[j], s = hsv.P[j + 1], v = hsv.P[j + 2];
                if (s == 0) { o.P[j] = o.P[j + 1] = o.P[j + 2] = v; continue; }
                int i = (int)Math.Floor(h * 6.0 / 255.0);
                float f = (float)(h * 6.0 / 255.0 - i), fs = (float)(s / 255.0);
                byte p = Clip8((int)Math.Round(v * (1.0 - fs), MidpointRounding.AwayFromZero));
                byte q = Clip8((int)Math.Round(v * (1.0 - fs * f), MidpointRounding.AwayFromZero));
                byte t = Clip8((int)Math.Round(v * (1.0 - fs * (1.0 - f)), MidpointRounding.AwayFromZero));
                (byte r, byte g, byte b) = (i % 6) switch
                {
                    0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q),
                };
                o.P[j] = r; o.P[j + 1] = g; o.P[j + 2] = b;
            }
            return o;
        }

        private static byte Clip8(int v) => (byte)(v <= 0 ? 0 : v < 256 ? v : 255);

        // C's fmod.
        private static double CMod(double x, double y) => x - y * Math.Truncate(x / y);

        // ------------------------------------------------------------------ Gaussian blur (BoxBlur.c: 3 extended box passes)
        public static Img Blur(Img im, float radius)
        {
            float r = BoxRadius(radius, 3);
            var o = new Img(im.W, im.H, im.N);
            HBlur(im, o, r);
            for (int i = 1; i < 3; i++) HBlur(o, o, r);
            var t = Transpose(o);
            for (int i = 0; i < 3; i++) HBlur(t, t, r);
            return Transpose(t);
        }

        internal static float BoxRadius(float radius, int passes)
        {
            float sigma2 = radius * radius / passes;
            float L = (float)Math.Sqrt(12.0 * sigma2 + 1.0);
            float l = (float)Math.Floor((L - 1.0) / 2.0);
            float a = (2 * l + 1) * (l * (l + 1) - 3 * sigma2);
            a /= 6 * (sigma2 - (l + 1) * (l + 1));
            return l + a;
        }

        private static Img Transpose(Img im)
        {
            var o = new Img(im.H, im.W, im.N);
            for (int y = 0; y < im.H; y++)
                for (int x = 0; x < im.W; x++)
                    for (int c = 0; c < im.N; c++) o.P[(x * im.H + y) * im.N + c] = im.P[(y * im.W + x) * im.N + c];
            return o;
        }

        private static void HBlur(Img src, Img dst, float floatRadius)
        {
            int radius = (int)floatRadius, xs = src.W, n = src.N, lastx = xs - 1;
            uint ww = (uint)(16777216f / (floatRadius * 2 + 1));
            uint fw = ((1u << 24) - (uint)(radius * 2 + 1) * ww) / 2;
            int edgeA = Math.Min(radius + 1, xs), edgeB = Math.Max(xs - radius - 1, 0);
            Parallel.For(0, src.H, y =>
            {
                var line = new int[xs];
                var outp = new byte[xs];
                for (int c = 0; c < n; c++)
                {
                    for (int x = 0; x < xs; x++) line[x] = src.P[(y * xs + x) * n + c];
                    uint acc = (uint)(line[0] * (radius + 1));
                    for (int x = 0; x < edgeA - 1; x++) acc += (uint)line[x];
                    acc += (uint)(line[lastx] * (radius - edgeA + 1));
                    void Step(int x, int sub, int add, int left, int right)
                    {
                        acc += (uint)(line[add] - line[sub]);
                        uint bulk = acc * ww + (uint)(line[left] + line[right]) * fw;
                        outp[x] = (byte)((bulk + (1u << 23)) >> 24);
                    }
                    if (edgeA <= edgeB)
                    {
                        for (int x = 0; x < edgeA; x++) Step(x, 0, x + radius, 0, x + radius + 1);
                        for (int x = edgeA; x < edgeB; x++) Step(x, x - radius - 1, x + radius, x - radius - 1, x + radius + 1);
                        for (int x = edgeB; x <= lastx; x++) Step(x, x - radius - 1, lastx, x - radius - 1, lastx);
                    }
                    else
                    {
                        for (int x = 0; x < edgeB; x++) Step(x, 0, x + radius, 0, x + radius + 1);
                        for (int x = edgeB; x < edgeA; x++) Step(x, 0, lastx, 0, lastx);
                        for (int x = edgeA; x <= lastx; x++) Step(x, x - radius - 1, lastx, x - radius - 1, lastx);
                    }
                    for (int x = 0; x < xs; x++) dst.P[(y * xs + x) * n + c] = outp[x];
                }
            });
        }

        /// <summary>MinFilter(3) of a mask: the darkest pixel of each 3x3 window, edges repeated.</summary>
        public static Img MinFilter3(Img m)
        {
            var o = new Img(m.W, m.H, 1);
            for (int y = 0; y < m.H; y++)
                for (int x = 0; x < m.W; x++)
                {
                    byte v = 255;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            byte p = m.P[Math.Clamp(y + dy, 0, m.H - 1) * m.W + Math.Clamp(x + dx, 0, m.W - 1)];
                            if (p < v) v = p;
                        }
                    o.P[y * m.W + x] = v;
                }
            return o;
        }

        /// <summary>ImageDraw.floodfill: the 4-connected region around (x, y) within `thresh` (summed channel
        /// difference) of the seed's colour becomes `value`.</summary>
        public static void FloodFill(Img im, int x, int y, byte[] value, int thresh)
        {
            if (x < 0 || y < 0 || x >= im.W || y >= im.H) return;
            int n = im.N;
            var bg = new byte[n];
            Array.Copy(im.P, (y * im.W + x) * n, bg, 0, n);
            int DiffAt(int i)
            {
                int d = 0;
                for (int c = 0; c < n; c++) d += Math.Abs(im.P[i * n + c] - bg[c]);
                return d;
            }
            int seedDiff = 0;
            for (int c = 0; c < n; c++) seedDiff += Math.Abs(value[c] - bg[c]);
            if (seedDiff <= thresh) return;
            var seen = new bool[im.W * im.H];
            var queue = new Queue<int>();
            int s0 = y * im.W + x;
            Array.Copy(value, 0, im.P, s0 * n, n);
            seen[s0] = true;
            queue.Enqueue(s0);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), px = i % im.W, py = i / im.W;
                for (int k = 0; k < 4; k++)
                {
                    int sx = px + (k == 0 ? 1 : k == 1 ? -1 : 0), sy = py + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (sx < 0 || sy < 0 || sx >= im.W || sy >= im.H) continue;
                    int j = sy * im.W + sx;
                    if (seen[j]) continue;
                    seen[j] = true;
                    if (DiffAt(j) > thresh) continue;
                    Array.Copy(value, 0, im.P, j * n, n);
                    queue.Enqueue(j);
                }
            }
        }

        // ------------------------------------------------------------------ shapes (Draw.c), on masks
        private static void HLine(Img m, int x0, int y0, int x1, byte ink)
        {
            if (y0 < 0 || y0 >= m.H) return;
            if (x0 < 0) x0 = 0; else if (x0 >= m.W) return;
            if (x1 < 0) return; else if (x1 >= m.W) x1 = m.W - 1;
            for (int x = x0; x <= x1; x++) m.P[y0 * m.W + x] = ink;
        }

        public static void Rectangle(Img m, int x0, int y0, int x1, int y1, byte ink)
        {
            if (y0 > y1) (y0, y1) = (y1, y0);
            if (y0 < 0) y0 = 0; else if (y0 >= m.H) return;
            if (y1 < 0) return; else if (y1 > m.H) y1 = m.H;
            for (int y = y0; y <= y1; y++) HLine(m, x0, y, x1, ink);
        }

        private sealed class Quarter
        {
            readonly int cxEnd, cyEnd;
            readonly long a2, b2, a2b2;
            int cx, cy;
            public bool Finished;

            public Quarter(int a, int b)
            {
                if (a < 0 || b < 0) { Finished = true; return; }
                cx = a; cy = b % 2; cxEnd = a % 2; cyEnd = b;
                a2 = (long)a * a; b2 = (long)b * b; a2b2 = a2 * b2;
            }

            long Delta(long x, long y) => Math.Abs(a2 * y * y + b2 * x * x - a2b2);

            public bool Next(out int x, out int y)
            {
                x = y = 0;
                if (Finished) return false;
                x = cx; y = cy;
                if (cx == cxEnd && cy == cyEnd) Finished = true;
                else
                {
                    int nx = cx, ny = cy + 2;
                    long nd = Delta(nx, ny);
                    if (nx > 1)
                    {
                        long d = Delta(cx - 2, cy + 2);
                        if (nd > d) { nx = cx - 2; ny = cy + 2; nd = d; }
                        d = Delta(cx - 2, cy);
                        if (nd > d) { nx = cx - 2; ny = cy; }
                    }
                    cx = nx; cy = ny;
                }
                return true;
            }
        }

        // The horizontal runs of an ellipse of thickness w, in doubled coordinates around its centre (ellipse_next).
        private static IEnumerable<(int x0, int y, int x1)> EllipseRuns(int a, int b, int w)
        {
            int leftmost = a % 2;
            var o = new Quarter(a, b);
            if (w < 1 || !o.Next(out int pr, out int py)) yield break;
            var inner = new Quarter(a - 2 * (w - 1), b - 2 * (w - 1));
            int pl = leftmost;
            bool finished = false;
            var buf = new List<(int, int, int)>(4);
            while (!finished)
            {
                int y = py, l = pl, r = pr;
                bool more;
                int cx, cy;
                while ((more = o.Next(out cx, out cy)) && cy <= y) { }
                if (!more) finished = true; else { pr = cx; py = cy; }
                while ((more = inner.Next(out cx, out cy)) && cy <= y) l = cx;
                pl = more ? cx : leftmost;
                buf.Clear();
                if ((l > 0 || l < r) && y > 0) buf.Add((l == 0 ? 2 : l, y, r));
                if (y > 0) buf.Add((-r, y, -l));
                if (l > 0 || l < r) buf.Add((l == 0 ? 2 : l, -y, r));
                buf.Add((-r, -y, -l));
                for (int i = buf.Count - 1; i >= 0; i--) yield return buf[i];
            }
        }

        public static void Ellipse(Img m, int x0, int y0, int x1, int y1, byte ink)
        {
            int a = x1 - x0, b = y1 - y0;
            if (a < 0 || b < 0) return;
            foreach (var (X0, Y, X1) in EllipseRuns(a, b, a + b))
                HLine(m, x0 + (X0 + a) / 2, y0 + (Y + b) / 2, x0 + (X1 + a) / 2, ink);
        }

        // A filled pie slice (pie_init: the ellipse clipped by the two side half-planes, AND below 180 degrees).
        private static void Pieslice(Img m, int x0, int y0, int x1, int y1, float start, float end, byte ink)
        {
            int a = x1 - x0, b = y1 - y0;
            if (a < 0 || b < 0) return;
            if (end - start >= 360) { start = 0; end = 360; }
            else
            {
                start = (float)CMod(start < 0 ? 360 - CMod(-start, 360) : start, 360);
                end = start + (float)CMod(end < start ? 360 - CMod(start - end, 360) : end - start, 360);
            }
            if (start + 360 == end) { Ellipse(m, x0, y0, x1, y1, ink); return; }
            if (start == end) return;
            double xl = a * Math.Cos(start * Math.PI / 180.0), xr = a * Math.Cos(end * Math.PI / 180.0);
            double yl = b * Math.Sin(start * Math.PI / 180.0), yr = b * Math.Sin(end * Math.PI / 180.0);
            var lc = (-yl, xl, 0.0);
            var rc = (yr, -xr, 0.0);
            bool and = end - start < 180, spike = end - start < 90;
            var sc = ((xl + xr) / 2.0, (yl + yr) / 2.0, 0.0);
            foreach (var (X0, Y, X1) in EllipseRuns(a, b, x1 + y1 - x0 - y0))
            {
                var segs = Combine(and, Clip(lc, X0, Y, X1), Clip(rc, X0, Y, X1));
                if (spike) segs = Combine(true, segs, Clip(sc, X0, Y, X1));
                for (int i = 0; i + 1 < segs.Count; i += 2)
                    HLine(m, x0 + (segs[i].x + a) / 2, y0 + (Y + b) / 2, x0 + (segs[i + 1].x + a) / 2, ink);
            }
        }

        private static List<(int x, int type)> Clip((double A, double B, double C) n, int x0, int y, int x1)
        {
            const double eps = 1e-9;
            if (Math.Abs(n.A) < eps) { if (n.B * y + n.C < -eps) { x0 = 1; x1 = 0; } }
            else
            {
                double ix = -(n.B * y + n.C) / n.A;
                if (n.A * x0 + n.B * y + n.C < eps) x0 = (int)Math.Round(Math.Max(x0, ix), MidpointRounding.AwayFromZero);
                if (n.A * x1 + n.B * y + n.C < eps) x1 = (int)Math.Round(Math.Min(x1, ix), MidpointRounding.AwayFromZero);
            }
            return x0 <= x1 ? new List<(int, int)> { (x0, 1), (x1, -1) } : new List<(int, int)>();
        }

        // clip_tree_do_clip's AND / OR merge of two sorted open/close event lists.
        private static List<(int x, int type)> Combine(bool and, List<(int x, int type)> l1, List<(int x, int type)> l2)
        {
            var ret = new List<(int x, int type)>();
            int i1 = 0, i2 = 0, k1 = 0, k2 = 0;
            while (i1 < l1.Count || i2 < l2.Count)
            {
                (int x, int type) t;
                if (i2 >= l2.Count || (i1 < l1.Count && (l1[i1].x < l2[i2].x || (l1[i1].x == l2[i2].x && l1[i1].type > l2[i2].type))))
                { t = l1[i1++]; k1 += t.type; }
                else { t = l2[i2++]; k2 += t.type; }
                bool tailOpen = ret.Count > 0 && ret[^1].type == 1;
                bool keep = and
                    ? (t.type == 1 && !tailOpen && k1 > 0 && k2 > 0) || (t.type == -1 && tailOpen && (k1 == 0 || k2 == 0))
                    : (t.type == 1 && !tailOpen) || (t.type == -1 && k1 == 0 && k2 == 0);
                if (keep) ret.Add(t);
            }
            return ret;
        }

        /// <summary>ImageDraw.rounded_rectangle, filled, all four corners round.</summary>
        public static void RoundedRectangle(Img m, int x0, int y0, int x1, int y1, int radius, byte ink)
        {
            int d = Math.Min(Math.Min(x1 - x0, y1 - y0), radius * 2);
            bool fullX = d >= x1 - x0 - 1;
            if (fullX) d = x1 - x0;
            bool fullY = d >= y1 - y0 - 1;
            if (fullY) d = y1 - y0;
            if (fullX && fullY) { Ellipse(m, x0, y0, x1, y1, ink); return; }
            if (d == 0) { Rectangle(m, x0, y0, x1, y1, ink); return; }
            int r = d / 2;
            if (fullX) { Pieslice(m, x0, y0, x0 + d, y0 + d, 180, 360, ink); Pieslice(m, x0, y1 - d, x0 + d, y1, 0, 180, ink); }
            else if (fullY) { Pieslice(m, x0, y0, x0 + d, y0 + d, 90, 270, ink); Pieslice(m, x1 - d, y0, x1, y0 + d, 270, 90, ink); }
            else
            {
                Pieslice(m, x0, y0, x0 + d, y0 + d, 180, 270, ink);
                Pieslice(m, x1 - d, y0, x1, y0 + d, 270, 360, ink);
                Pieslice(m, x1 - d, y1 - d, x1, y1, 0, 90, ink);
                Pieslice(m, x0, y1 - d, x0 + d, y1, 90, 180, ink);
            }
            if (fullX) Rectangle(m, x0, y0 + r + 1, x1, y1 - r - 1, ink);
            else if (x1 - r - 1 >= x0 + r + 1) Rectangle(m, x0 + r + 1, y0, x1 - r - 1, y1, ink);
            if (!fullX && !fullY)
            {
                Rectangle(m, x0, y0 + r + 1, x0 + r, y1 - r - 1, ink);
                Rectangle(m, x1 - r, y0 + r + 1, x1, y1 - r - 1, ink);
            }
        }

        private struct Edge { public int X0, Y0, XMin, YMin, XMax, YMax; public float Dx; }

        private static Edge MakeEdge(int x0, int y0, int x1, int y1) => new Edge
        {
            XMin = Math.Min(x0, x1), XMax = Math.Max(x0, x1), YMin = Math.Min(y0, y1), YMax = Math.Max(y0, y1),
            X0 = x0, Y0 = y0, Dx = y0 == y1 ? 0f : (float)(x1 - x0) / (y1 - y0),
        };

        private static int RoundUp(float f) => (int)(f >= 0.0 ? Math.Floor(f + 0.5f) : -Math.Floor(Math.Abs(f) + 0.5f));
        private static int RoundDown(float f) => (int)(f >= 0.0 ? Math.Ceiling(f - 0.5f) : -Math.Ceiling(Math.Abs(f) - 0.5f));

        /// <summary>ImageDraw.polygon, filled (polygon_generic's scan lines).</summary>
        public static void Polygon(Img m, (int x, int y)[] p, byte ink)
        {
            var e = new List<Edge>();
            int i;
            for (i = 0; i < p.Length - 1; i++)
            {
                int x0 = p[i].x, y0 = p[i].y, x1 = p[i + 1].x, y1 = p[i + 1].y;
                if (y0 == y1 && i != 0 && y0 == p[i - 1].y)
                {
                    var last = e[^1];
                    if (x1 > x0 && x0 > p[i - 1].x) { last.XMax = x1; e[^1] = last; continue; }
                    if (x1 < x0 && x0 < p[i - 1].x) { last.XMin = x1; e[^1] = last; continue; }
                }
                e.Add(MakeEdge(x0, y0, x1, y1));
            }
            if (p[i].x != p[0].x || p[i].y != p[0].y) e.Add(MakeEdge(p[i].x, p[i].y, p[0].x, p[0].y));

            var table = new List<Edge>();
            int ymin = m.H - 1, ymax = 0;
            foreach (var ed in e)
            {
                if (ymin > ed.YMin) ymin = ed.YMin;
                if (ymax < ed.YMax) ymax = ed.YMax;
                if (ed.YMin == ed.YMax) { HLine(m, ed.XMin, ed.YMin, ed.XMax, ink); continue; }
                table.Add(ed);
            }
            if (ymin < 0) ymin = 0;
            if (ymax > m.H) ymax = m.H;
            var xx = new float[table.Count * 2];
            for (; ymin <= ymax; ymin++)
            {
                int j = 0;
                for (i = 0; i < table.Count; i++)
                {
                    var cur = table[i];
                    if (ymin < cur.YMin || ymin > cur.YMax) continue;
                    xx[j++] = (ymin - cur.Y0) * cur.Dx + cur.X0;
                    if (ymin == cur.YMax && ymin < ymax) { xx[j] = xx[j - 1]; j++; }
                    else if ((ymin == cur.YMin || ymin == cur.YMax) && cur.Dx != 0)
                    {
                        for (int k = 0; k < i; k++)
                        {
                            var other = table[k];
                            if ((ymin != other.YMin && ymin != other.YMax) || other.Dx == 0) continue;
                            if (MathF.Round(xx[j - 1], MidpointRounding.AwayFromZero) !=
                                MathF.Round((ymin - other.Y0) * other.Dx + other.X0, MidpointRounding.AwayFromZero)) continue;
                            int offset = ymin == cur.YMax ? -1 : 1;
                            float adj = (ymin + offset - cur.Y0) * cur.Dx + cur.X0;
                            if (ymin + offset >= other.YMin && ymin + offset <= other.YMax)
                            {
                                float adjOther = (ymin + offset - other.Y0) * other.Dx + other.X0;
                                if (xx[j - 1] > adj + 1 && xx[j - 1] > adjOther + 1)
                                    xx[j - 1] = MathF.Round(Math.Max(adj, adjOther), MidpointRounding.AwayFromZero) + 1;
                                else if (xx[j - 1] < adj - 1 && xx[j - 1] < adjOther - 1)
                                    xx[j - 1] = MathF.Round(Math.Min(adj, adjOther), MidpointRounding.AwayFromZero) - 1;
                                break;
                            }
                        }
                    }
                }
                Array.Sort(xx, 0, j);
                for (i = 1; i < j; i += 2) HLine(m, RoundUp(xx[i - 1]), ymin, RoundDown(xx[i]), ink);
            }
        }

        // ------------------------------------------------------------------ Lanczos resize (Resample.c, 8 bits per channel)
        /// <summary>Image.resize(LANCZOS) of the bands as they are (premultiply RGBA first, as Pillow does).</summary>
        public static Img Resize(Img im, int w, int h)
        {
            bool needH = w != im.W, needV = h != im.H;
            bool hFirst = !((im.H - h) > 0 && (im.H - h) > (im.W - w) * 2);
            int[] bv = null, kv = null, bh = null, kh = null;
            int ksv = 0, ksh = 0, yFirst = 0, yLast = im.H;
            if ((needH && hFirst) || needV) ksv = Coeffs(im.H, h, out bv, out kv);
            if (needH)
            {
                if (hFirst)
                {
                    yFirst = bv[0];
                    yLast = bv[h * 2 - 2] + bv[h * 2 - 1];
                    for (int i = 0; i < h; i++) bv[i * 2] -= yFirst;
                }
                ksh = Coeffs(im.W, w, out bh, out kh);
            }
            var cur = im;
            if (hFirst)
            {
                if (needH) cur = PassH(cur, w, yLast - yFirst, yFirst, ksh, bh, kh);
                if (needV) cur = PassV(cur, h, ksv, bv, kv);
            }
            else
            {
                if (needV) cur = PassV(cur, h, ksv, bv, kv);
                if (needH) cur = PassH(cur, w, h, 0, ksh, bh, kh);
            }
            return cur == im ? im.Clone() : cur;
        }

        private static double Sinc(double x)
        {
            if (x == 0.0) return 1.0;
            x *= Math.PI;
            return Math.Sin(x) / x;
        }

        private static double Lanczos(double x) => -3.0 <= x && x < 3.0 ? Sinc(x) * Sinc(x / 3) : 0.0;

        private const int Prec = 32 - 8 - 2;

        private static int Coeffs(int inSize, int outSize, out int[] bounds, out int[] kk)
        {
            double scale = (double)inSize / outSize, filterscale = Math.Max(scale, 1.0);
            double support = 3.0 * filterscale;
            int ksize = (int)Math.Ceiling(support) * 2 + 1;
            bounds = new int[outSize * 2];
            kk = new int[outSize * ksize];
            var k = new double[ksize];
            double inv = 1.0 / filterscale;
            for (int xx = 0; xx < outSize; xx++)
            {
                double center = (xx + 0.5) * scale, ww = 0.0;
                int xmin = (int)(center - support + 0.5);
                if (xmin < 0) xmin = 0;
                int xmax = (int)(center + support + 0.5);
                if (xmax > inSize) xmax = inSize;
                xmax -= xmin;
                Array.Clear(k);
                for (int x = 0; x < xmax; x++) { double w = Lanczos((x + xmin - center + 0.5) * inv); k[x] = w; ww += w; }
                if (ww != 0.0) for (int x = 0; x < xmax; x++) k[x] /= ww;
                for (int x = 0; x < ksize; x++)
                    kk[xx * ksize + x] = k[x] < 0 ? (int)(-0.5 + k[x] * (1 << Prec)) : (int)(0.5 + k[x] * (1 << Prec));
                bounds[xx * 2] = xmin;
                bounds[xx * 2 + 1] = xmax;
            }
            return ksize;
        }

        private static byte ClipPrec(int ss) => (byte)Math.Clamp(ss >> Prec, 0, 255);

        private static Img PassH(Img src, int w, int h, int offset, int ksize, int[] bounds, int[] kk)
        {
            var o = new Img(w, h, src.N);
            int n = src.N;
            Parallel.For(0, h, yy =>
            {
                int row = (yy + offset) * src.W;
                for (int xx = 0; xx < w; xx++)
                {
                    int xmin = bounds[xx * 2], xmax = bounds[xx * 2 + 1], kb = xx * ksize;
                    for (int c = 0; c < n; c++)
                    {
                        int ss = 1 << (Prec - 1);
                        for (int x = 0; x < xmax; x++) ss += src.P[(row + x + xmin) * n + c] * kk[kb + x];
                        o.P[(yy * w + xx) * n + c] = ClipPrec(ss);
                    }
                }
            });
            return o;
        }

        private static Img PassV(Img src, int h, int ksize, int[] bounds, int[] kk)
        {
            var o = new Img(src.W, h, src.N);
            int n = src.N, w = src.W;
            Parallel.For(0, h, yy =>
            {
                int ymin = bounds[yy * 2], ymax = bounds[yy * 2 + 1], kb = yy * ksize;
                for (int xx = 0; xx < w; xx++)
                    for (int c = 0; c < n; c++)
                    {
                        int ss = 1 << (Prec - 1);
                        for (int y = 0; y < ymax; y++) ss += src.P[((y + ymin) * w + xx) * n + c] * kk[kb + y];
                        o.P[(yy * w + xx) * n + c] = ClipPrec(ss);
                    }
            });
            return o;
        }
    }
}
