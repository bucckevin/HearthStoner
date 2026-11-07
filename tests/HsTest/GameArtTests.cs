using System;
using HearthStoner.Art;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>The pure pieces of the game-art builder: blend math, the mana hue shift, the camera projection,
    /// the rasterizer and the Pillow ports (expected values from Pillow 13 itself).</summary>
    public class GameArtTests
    {
        [Fact]
        public void BlendModes()
        {
            var rgb = new float[] { 0.5f, 0.5f, 0.5f };
            var a = new float[] { 1f };
            Rasterizer.BlendPixel(rgb, a, 0, new double[] { 1, 0, 0, 0.5 }, Blend.Alpha);   // half red over grey
            Assert.Equal(0.75f, rgb[0], 6);
            Assert.Equal(0.25f, rgb[1], 6);
            Assert.Equal(1f, a[0], 6);

            rgb = new float[] { 0.2f, 0.2f, 0.2f };
            Rasterizer.BlendPixel(rgb, a, 0, new double[] { 0.5, 0, 1, 0.5 }, Blend.Add);    // glow: adds colour * alpha
            Assert.Equal(0.45f, rgb[0], 6);
            Assert.Equal(0.2f, rgb[1], 6);
            Assert.Equal(0.7f, rgb[2], 6);

            rgb = new float[] { 0.8f, 0.8f, 0.8f };
            Rasterizer.BlendPixel(rgb, a, 0, new double[] { 1, 0, 0.5, 0.5 }, Blend.Mul);    // shadow: white leaves it, black halves it
            Assert.Equal(0.8f, rgb[0], 6);
            Assert.Equal(0.4f, rgb[1], 6);
            Assert.Equal(0.6f, rgb[2], 6);
        }

        [Fact]
        public void ManaHueShift()
        {
            var red = new Img(2, 1, 3, new byte[] { 255, 0, 0, 120, 120, 120 });
            var hsv = Pixels.RgbToHsv(red);
            Assert.Equal(new byte[] { 0, 255, 255, 0, 0, 120 }, hsv.P);
            hsv.P[0] = (byte)((hsv.P[0] + 154) % 256);   // the shift GameArtBuilder.Recolour applies
            var rgb = Pixels.HsvToRgb(hsv);
            Assert.Equal(new byte[] { 0, 96, 255, 120, 120, 120 }, rgb.P);   // Pillow gives the same: red turns mana blue, grey stays
        }

        [Fact]
        public void CameraProjection()
        {
            // looking straight down from 10 up (Unity Euler(90, 0, 0)): world +z is up on the picture, +x is right
            double s = Math.Sqrt(0.5);
            var cam = new Camera(new CameraInfo { Position = new double[] { 0, 10, 0 }, Rotation = new[] { s, 0, 0, s }, Fov = 90, Near = 0.3, Far = 100 }, 4.0 / 3);
            var (x, y) = cam.Screen(0, 0, 0, 800, 600);
            Assert.Equal(400, x, 6);
            Assert.Equal(300, y, 6);
            (x, y) = cam.Screen(0, 0, 5, 800, 600);   // tan(45) = 1: 5 up at distance 10 is half way to the top edge
            Assert.Equal(400, x, 6);
            Assert.Equal(150, y, 6);
            (x, y) = cam.Screen(10 * 4.0 / 3, 0, 0, 800, 600);
            Assert.Equal(800, x, 6);
        }

        [Fact]
        public void RendersATexturedQuadWithDepth()
        {
            var cam = new CameraInfo { Position = new double[] { 0, 0, -10 }, Rotation = new double[] { 0, 0, 0, 1 }, Fov = 60, Near = 0.3, Far = 100 };
            SceneObject Quad(double z, float[] tint) => new SceneObject
            {
                World = new double[] { -3, -3, z, 3, -3, z, 3, 3, z, -3, 3, z }, Uv = new double[8], Tris = new[] { 0, 1, 2, 0, 2, 3 }, Tint = tint,
            };
            // the red quad is nearer the camera (at z -10), so it hides the green one whatever the draw order; the corners stay empty
            var img = Rasterizer.Render(new[] { Quad(-1, new[] { 1f, 0, 0, 1 }), Quad(0, new[] { 0, 1f, 0, 1 }) }, cam, 40, 30, 2);
            int c = (15 * 40 + 20) * 4;
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.P[c..(c + 4)]);
            Assert.Equal(0, img.P[3]);
        }

        [Fact]
        public void NearClippingSplitsTriangles()
        {
            // one corner behind the near plane: two triangles; two corners behind: one
            var view = new double[] { 0, 0, 5, 1, 0, 5, 0, 1, -1 };
            var uv = new double[6];
            Assert.Equal(30, Rasterizer.ClipNear(view, uv, new[] { 0, 1, 2 }, 0.3).Count);
            view = new double[] { 0, 0, 5, 1, 0, -1, 0, 1, -1 };
            Assert.Equal(15, Rasterizer.ClipNear(view, uv, new[] { 0, 1, 2 }, 0.3).Count);
        }

        [Fact]
        public void PillowShapesAndBlur()
        {
            var m = new Img(10, 10, 1);
            Pixels.Ellipse(m, 2, 2, 7, 7, 255);
            Assert.Equal(PillowEllipse, Rows(m));
            var b = Pixels.Blur(m, 1);
            Assert.Equal(PillowBlurRow5, b.P[50..60]);
        }

        private static string Rows(Img m)
        {
            var s = new System.Text.StringBuilder();
            for (int y = 0; y < m.H; y++)
            {
                for (int x = 0; x < m.W; x++) s.Append(m.P[y * m.W + x] > 0 ? '#' : '.');
                s.Append('\n');
            }
            return s.ToString();
        }

        // Image.new("L", (10, 10)); ImageDraw.Draw(m).ellipse((2, 2, 7, 7), fill=255), and GaussianBlur(1) of it (row 5).
        private const string PillowEllipse = "..........\n..........\n....##....\n...####...\n..######..\n..######..\n...####...\n....##....\n..........\n..........\n";
        private static readonly byte[] PillowBlurRow5 = { 10, 53, 138, 211, 242, 242, 211, 138, 53, 10 };
    }
}
