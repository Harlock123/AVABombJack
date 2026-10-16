using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVABombJack.Engine;
using Effect = AVABombJack.Engine.Effect;

namespace AVABombJack.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVABombJack/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>A colour part of the way to another.</summary>
    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return Ch(24) << 24 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>A round glow: the stops run from the middle (0) to the edge (1).</summary>
    private static IBrush Radial(Point c, double r, Point? origin, params (double At, uint Colour)[] stops)
    {
        var b = new RadialGradientBrush
        {
            Center = new RelativePoint(c, RelativeUnit.Absolute),
            GradientOrigin = new RelativePoint(origin ?? c, RelativeUnit.Absolute),
            RadiusX = new RelativeScalar(r, RelativeUnit.Absolute),
            RadiusY = new RelativeScalar(r, RelativeUnit.Absolute),
        };
        foreach (var (at, col) in stops) b.GradientStops.Add(new GradientStop(Color.FromUInt32(col), at));
        return b;
    }


    /// <summary>A vertical sweep from one colour at the top to another at the bottom.</summary>
    private static IBrush Sky(double top, double bottom, params (double At, uint Colour)[] stops)
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, top, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, bottom, RelativeUnit.Absolute),
        };
        foreach (var (at, col) in stops) b.GradientStops.Add(new GradientStop(Color.FromUInt32(col), at));
        return b;
    }

    private const uint Suit = 0xFFFF8A2A, Scarf = 0xFFF8F8F0, Goggle = 0xFF6AE0FF, CoinGold = 0xFFFFD040;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF06060E), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            var area = new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Floor - Field.Top);
            using (dc.PushClip(area))
            {
                DrawScene(dc, g.Layout.Scene, area);
                foreach (var p in g.Layout.Platforms) DrawPlatform(dc, p, g.Layout.Scene);
                foreach (var b in g.Bombs.Where(b => !b.Taken)) DrawBomb(dc, b.X, b.Y, b == g.Lit, 1.2);
                foreach (var c in g.Coins) DrawCoin(dc, c.Kind, c.X, c.Y, 1, c.Life < 2.5 && (int)(_clock * 8) % 2 == 0);
                foreach (var e in g.Enemies) DrawEnemy(dc, e, g.Power);
                var h = g.Hero;
                if (h.Alive ? h.Safe <= 0 || (int)(_clock * 10) % 2 == 0 : true) DrawHero(dc, h.X, h.Y, h.Face, h.OnGround, h.Gliding, h.Vy, h.Alive ? 0 : h.Spin, 1);
                foreach (var d in g.Wreckage)
                {
                    double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                    dc.FillRectangle(Brush(Alpha(d.Colour, a)), new Rect(d.X - 2, d.Y - 2, 4, 4));
                }
            }
            DrawFrame(dc, g.Layout.Scene);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 10), 24, Yellow, center: true);
    }

    // ───────────────────────────── the backdrops ─────────────────────────────

    /// <summary>The round's painted backdrop, kept dim so everything in front of it stands out.</summary>
    private void DrawScene(DrawingContext dc, Scene scene, Rect a)
    {
        double top = a.Y, bottom = a.Bottom, l = a.X, r = a.Right;
        switch (scene)
        {
            case Scene.Dunes:
            {
                dc.FillRectangle(Sky(top, bottom, (0, 0xFF2A1450), (0.45, 0xFFC0503A), (0.75, 0xFFF0A050), (1, 0xFFF0C070)), a);
                var sun = new Point(700, 470);
                dc.DrawEllipse(Radial(sun, 160, null, (0, 0xFFFFF0B0), (0.35, 0xFFFFD070), (0.4, 0x80FFB050), (1, 0x00FF8040)), null, sun, 160, 160);
                // Rock arches far off, then the dunes in layers.
                Poly(dc, 0xFF6A3048, null, 0, new Point(150, 560), new Point(170, 470), new Point(230, 450), new Point(290, 470), new Point(310, 560), new Point(280, 560), new Point(265, 500), new Point(195, 500), new Point(180, 560));
                Dunes(dc, l, r, 600, 40, 0.012, 1.1, 0xFF8A4A3A, bottom);
                Dunes(dc, l, r, 660, 30, 0.017, 2.3, 0xFFB06A40, bottom);
                Dunes(dc, l, r, 715, 22, 0.022, 0.4, 0xFFD08A4A, bottom);
                break;
            }
            case Scene.Ruins:
            {
                dc.FillRectangle(Sky(top, bottom, (0, 0xFF0E1A40), (0.6, 0xFF3A4A8A), (1, 0xFF8A7AA0)), a);
                Stars(dc, a, 60, 0.6);
                dc.DrawEllipse(Brush(0xFFF0F0E0), null, new Point(820, 190), 34, 34);
                dc.DrawEllipse(Brush(0xFF3A4A8A), null, new Point(806, 182), 30, 30);
                Poly(dc, 0xFF2A2A4A, null, 0, new Point(l, 650), new Point(200, 600), new Point(420, 620), new Point(700, 580), new Point(r, 630), new Point(r, bottom), new Point(l, bottom));
                // A broken colonnade with an arch, and dark cypresses.
                uint stone = 0xFF4A4A6A;
                foreach (var (x, hgt) in new[] { (380.0, 230.0), (450.0, 260.0), (520.0, 260.0), (590.0, 200.0), (660.0, 120.0) })
                {
                    dc.FillRectangle(Brush(stone), new Rect(x, 600 - hgt, 26, hgt));
                    dc.FillRectangle(Brush(0xFF5A5A7A), new Rect(x - 6, 600 - hgt, 38, 10));
                }
                dc.FillRectangle(Brush(stone), new Rect(374, 334, 248, 22));
                foreach (double x in new[] { 120.0, 170.0, 860.0, 910.0 })
                    Poly(dc, 0xFF14203A, null, 0, new Point(x, 380 + (x % 50)), new Point(x + 18, 620), new Point(x - 18, 620));
                break;
            }
            case Scene.Frost:
            {
                dc.FillRectangle(Sky(top, bottom, (0, 0xFF040818), (0.7, 0xFF10204A), (1, 0xFF2A4A7A)), a);
                Stars(dc, a, 120, 0.9);
                // The aurora: wavering bands.
                for (int band = 0; band < 3; band++)
                {
                    uint col = band == 1 ? 0x384AFFB0u : 0x286AE0FFu;
                    var upper = new List<Point>();
                    var lower = new List<Point>();
                    for (double x = l; x <= r + 24; x += 24)
                    {
                        double y = 200 + band * 40 + Math.Sin(x * 0.01 + _clock * 0.4 + band) * 40;
                        upper.Add(new Point(x, y - 14 + band * 3));
                        lower.Add(new Point(x, y + 30 + Math.Sin(x * 0.03 + _clock) * 8));
                    }
                    lower.Reverse();
                    Poly(dc, col, null, 0, upper.Concat(lower).ToArray());
                }
                Poly(dc, 0xFF2A3A5A, null, 0, new Point(l, 640), new Point(180, 380), new Point(330, 560), new Point(520, 300), new Point(720, 580), new Point(860, 420), new Point(r, 600), new Point(r, bottom), new Point(l, bottom));
                Poly(dc, 0xFFE0ECFF, null, 0, new Point(180, 380), new Point(215, 445), new Point(195, 435), new Point(170, 450), new Point(150, 430));
                Poly(dc, 0xFFE0ECFF, null, 0, new Point(520, 300), new Point(570, 375), new Point(540, 365), new Point(515, 385), new Point(480, 360));
                Poly(dc, 0xFFE0ECFF, null, 0, new Point(860, 420), new Point(890, 470), new Point(860, 460), new Point(835, 465));
                dc.FillRectangle(Brush(0xFFC8D8F0), new Rect(l, 700, r - l, bottom - 700));
                break;
            }
            case Scene.City:
            {
                dc.FillRectangle(Sky(top, bottom, (0, 0xFF0A0420), (0.7, 0xFF2A0A40), (1, 0xFF4A1A50)), a);
                Stars(dc, a, 40, 0.5);
                dc.DrawEllipse(Brush(0xFFF8E8C0), null, new Point(180, 180), 40, 40);
                // Towers, far and near, with their windows lit here and there.
                for (int layer = 0; layer < 2; layer++)
                {
                    uint body = layer == 0 ? 0xFF1A1030 : 0xFF0E0A1A;
                    for (int i = 0; i < 14; i++)
                    {
                        uint h = Hash(i, layer, 9);
                        double w = 50 + h % 50, x = l + i * 72 + (h >> 8) % 30 - (layer * 30), hgt = (layer == 0 ? 260 : 160) + (h >> 12) % 200;
                        var tower = new Rect(x, bottom - hgt, w, hgt);
                        dc.FillRectangle(Brush(body), tower);
                        for (double wy = tower.Y + 12; wy < bottom - 10; wy += 18)
                            for (double wx = x + 8; wx < x + w - 8; wx += 14)
                                if (Hash((int)wx, (int)wy, layer) % 5 == 0) dc.FillRectangle(Brush(layer == 0 ? 0x80FFD870 : 0xC0FFE08A), new Rect(wx, wy, 6, 8));
                    }
                }
                // Neon.
                dc.DrawLine(Pen(Alpha(0xFFFF4AD0, 0.5 + 0.3 * Math.Sin(_clock * 3)), 4), new Point(560, 380), new Point(680, 380));
                dc.DrawLine(Pen(Alpha(0xFF4AE0FF, 0.6), 4), new Point(250, 470), new Point(330, 470));
                break;
            }
            case Scene.Lagoon:
            {
                dc.FillRectangle(Sky(top, bottom, (0, 0xFF061030), (0.55, 0xFF1A3A6A), (0.62, 0xFF0A2A4A), (1, 0xFF04182A)), a);
                Stars(dc, a, 70, 0.7);
                var moon = new Point(640, 220);
                dc.DrawEllipse(Radial(moon, 110, null, (0, 0xFFFFFFE0), (0.3, 0xFFF0F0D0), (0.34, 0x40F0F0D0), (1, 0x00F0F0D0)), null, moon, 110, 110);
                // The moon's path on the water.
                for (int i = 0; i < 14; i++)
                {
                    double y = 470 + i * 19, w = 40 + i * 12 + Math.Sin(_clock * 2 + i) * 10;
                    dc.FillRectangle(Brush(Alpha(0xFFF0F0D0, 0.35 - i * 0.02)), new Rect(640 - w / 2, y, w, 3));
                }
                // An island with two palms.
                Poly(dc, 0xFF0A1A20, null, 0, new Point(80, 520), new Point(200, 470), new Point(340, 520));
                foreach (var (bx, lean) in new[] { (190.0, -0.3), (250.0, 0.25) })
                {
                    var top0 = new Point(bx + lean * 100, 330);
                    dc.DrawLine(Pen(0xFF0A1A20, 7), new Point(bx, 500), top0);
                    for (int f = 0; f < 6; f++)
                    {
                        double ang = f * Math.PI / 3 + 0.3;
                        Poly(dc, 0xFF0A1A20, null, 0, top0, new Point(top0.X + Math.Cos(ang) * 70, top0.Y + Math.Sin(ang) * 30 + 18), new Point(top0.X + Math.Cos(ang + 0.2) * 40, top0.Y + Math.Sin(ang + 0.2) * 20));
                    }
                }
                break;
            }
        }
        dc.FillRectangle(Brush(0x58000010), a);
    }

    /// <summary>A rolling line of sand from one side to the other, filled to the bottom.</summary>
    private static void Dunes(DrawingContext dc, double l, double r, double y, double amp, double freq, double phase, uint colour, double bottom)
    {
        var pts = new List<Point>();
        for (double x = l; x <= r + 1; x += 16) pts.Add(new Point(x, y + Math.Sin(x * freq + phase) * amp + Math.Sin(x * freq * 2.7 + phase) * amp * 0.3));
        pts.Add(new Point(r, bottom));
        pts.Add(new Point(l, bottom));
        Poly(dc, colour, null, 0, pts.ToArray());
    }

    private void Stars(DrawingContext dc, Rect a, int n, double bright)
    {
        for (int i = 0; i < n; i++)
        {
            uint h = Hash(i, (int)a.Width, 4);
            double x = a.X + h % (uint)a.Width, y = a.Y + (h >> 11) % (uint)(a.Height * 0.6);
            double tw = 0.5 + 0.5 * Math.Abs(Math.Sin(_clock * 1.3 + i));
            dc.DrawEllipse(Brush(Alpha(0xFFF0F0FF, bright * tw)), null, new Point(x, y), 1.1, 1.1);
        }
    }

    private static uint SceneColour(Scene s) => s switch
    {
        Scene.Dunes => 0xFFE08A3A, Scene.Ruins => 0xFFB0A0E0, Scene.Frost => 0xFF8AD0FF, Scene.City => 0xFFFF5AD0, _ => 0xFF4AE0B0,
    };

    /// <summary>A platform: a lit top edge, a body in the round's colour, and a shadow below.</summary>
    private static void DrawPlatform(DrawingContext dc, Box p, Scene scene)
    {
        uint col = SceneColour(scene);
        dc.FillRectangle(Brush(0x60000000), new Rect(p.X + 4, p.Bottom, p.W, 6));
        dc.FillRectangle(Brush(Mix(col, 0xFF000000, 0.45)), new Rect(p.X, p.Y, p.W, p.H));
        dc.FillRectangle(Brush(Mix(col, 0xFFFFFFFF, 0.35)), new Rect(p.X, p.Y, p.W, 4));
        for (double x = p.X + 12; x < p.Right - 4; x += 24) dc.FillRectangle(Brush(Mix(col, 0xFF000000, 0.25)), new Rect(x, p.Y + 6, 10, 6));
    }

    private static void DrawFrame(DrawingContext dc, Scene scene)
    {
        uint col = SceneColour(scene);
        dc.DrawRectangle(null, Pen(Mix(col, 0xFF000000, 0.3), 6), new Rect(Field.Left - 3, Field.Top - 3, Field.Right - Field.Left + 6, Field.Floor - Field.Top + 6));
        dc.DrawRectangle(null, Pen(Alpha(col, 0.6), 1.5), new Rect(Field.Left - 7, Field.Top - 7, Field.Right - Field.Left + 14, Field.Floor - Field.Top + 14));
    }

    // ───────────────────────────── the things in it ─────────────────────────────

    /// <summary>A bomb: a round black shell with a shine and a fuse; the lit one glows, its fuse spitting sparks.</summary>
    private void DrawBomb(DrawingContext dc, double x, double y, bool lit, double k)
    {
        var c = new Point(x, y + 2 * k);
        if (lit) dc.DrawEllipse(Radial(c, 26 * k, null, (0, 0x90FF6A2A), (1, 0x00FF6A2A)), null, c, 26 * k, 26 * k);
        dc.DrawEllipse(Brush(lit ? 0xFFB02A1A : 0xFF2A3466), Pen(lit ? 0xFFFF8A4A : 0xFF8A9AD0, 1.6), c, 11 * k, 11 * k);
        dc.DrawEllipse(Brush(0x90FFFFFF), null, new Point(x - 4 * k, y - 3 * k), 3 * k, 2.4 * k);
        dc.FillRectangle(Brush(0xFF8A8AA0), new Rect(x - 3 * k, y - 11 * k, 6 * k, 4 * k));
        dc.DrawLine(Pen(0xFFD0B080, 1.6 * k), new Point(x, y - 11 * k), new Point(x + 5 * k, y - 17 * k));
        if (lit)
        {
            var tip = new Point(x + 5 * k, y - 17 * k);
            for (int i = 0; i < 5; i++)
            {
                double a = _clock * 17 + i * 1.3, len = (4 + (i * 3 + (int)(_clock * 30)) % 5) * k;
                dc.DrawLine(Pen(i % 2 == 0 ? Yellow : 0xFFFFFFFF, 1.4), tip, new Point(tip.X + Math.Cos(a) * len, tip.Y + Math.Sin(a) * len));
            }
        }
    }

    /// <summary>A lettered coin, spinning: P for power, B for bonus, E for an extra life.</summary>
    private void DrawCoin(DrawingContext dc, CoinKind kind, double x, double y, double k, bool fade, bool spin = true)
    {
        uint col = kind switch { CoinKind.Power => 0xFF4AD0FF, CoinKind.Bonus => 0xFFFF5AD0, _ => 0xFF6AFF7A };
        double w = spin ? Math.Max(0.45, Math.Abs(Math.Cos(_clock * 2.5))) : 1;
        uint face = fade ? Alpha(col, 0.4) : col;
        dc.DrawEllipse(Brush(Mix(face, 0xFF000000, 0.35)), Pen(Mix(face, 0xFFFFFFFF, 0.5), 2), new Point(x, y), 14 * k * w, 14 * k);
        dc.DrawEllipse(Brush(face), null, new Point(x, y), 10 * k * w, 10.5 * k);
        if (w > 0.55) Text(dc, kind switch { CoinKind.Power => "P", CoinKind.Bonus => "B", _ => "E" }, new Point(x, y - 1), 22 * k, 0xFF101020, center: true, shadow: false);
    }

    /// <summary>
    /// The jumper: an aviator's cap and goggles, an orange flying suit and a long white scarf. On the ground it runs,
    /// in the air it tucks its knees, gliding it spreads its arms, and caught it spins.
    /// </summary>
    private void DrawHero(DrawingContext dc, double x, double top, int face, bool ground, bool gliding, double vy, double spin, double k)
    {
        double cy = top + Hero.Height / 2 * k;
        using var _ = dc.PushTransform(Matrix.CreateTranslation(-x, -cy) * Matrix.CreateRotation(spin) * Matrix.CreateScale(face < 0 ? -1 : 1, 1) * Matrix.CreateTranslation(x, cy));
        double run = ground ? Math.Sin(_clock * 18) : 0;
        // The scarf, streaming behind.
        double flow = gliding ? 1.4 : ground ? 0.6 + 0.2 * Math.Abs(run) : 1;
        var sc = new Point(x - 4 * k, top + 13 * k);
        Poly(dc, Scarf, null, 0, sc, new Point(sc.X - 20 * k * flow, sc.Y + (4 + Math.Sin(_clock * 14) * 3) * k + (vy < 0 ? 6 : -4) * k), new Point(sc.X - 18 * k * flow, sc.Y + (9 + Math.Sin(_clock * 14 + 1) * 3) * k + (vy < 0 ? 6 : -4) * k), new Point(sc.X + 2 * k, sc.Y + 5 * k));
        // Legs and boots.
        if (ground)
        {
            dc.DrawLine(Pen(Suit, 6 * k), new Point(x - 3 * k, top + 28 * k), new Point(x - 3 * k + run * 6 * k, top + 37 * k));
            dc.DrawLine(Pen(Suit, 6 * k), new Point(x + 3 * k, top + 28 * k), new Point(x + 3 * k - run * 6 * k, top + 37 * k));
            dc.FillRectangle(Brush(0xFF5A3A2A), new Rect(x - 7 * k + run * 6 * k, top + 36 * k, 8 * k, 4 * k));
            dc.FillRectangle(Brush(0xFF5A3A2A), new Rect(x - 1 * k - run * 6 * k, top + 36 * k, 8 * k, 4 * k));
        }
        else
        {
            dc.DrawLine(Pen(Suit, 6 * k), new Point(x - 2 * k, top + 28 * k), new Point(x + 6 * k, top + 33 * k));
            dc.DrawLine(Pen(Suit, 6 * k), new Point(x + 2 * k, top + 28 * k), new Point(x - 4 * k, top + 35 * k));
            dc.FillRectangle(Brush(0xFF5A3A2A), new Rect(x + 4 * k, top + 31 * k, 6 * k, 5 * k));
            dc.FillRectangle(Brush(0xFF5A3A2A), new Rect(x - 8 * k, top + 33 * k, 6 * k, 5 * k));
        }
        // Body, with a belt.
        dc.DrawRectangle(Brush(Suit), Pen(0xFF8A3A0A, 1), new Rect(x - 8 * k, top + 13 * k, 16 * k, 17 * k), 4 * k, 4 * k);
        dc.FillRectangle(Brush(0xFF5A3A2A), new Rect(x - 8 * k, top + 24 * k, 16 * k, 3 * k));
        // Arms: out wide when gliding, up when rising, swinging when running.
        if (gliding)
        {
            dc.DrawLine(Pen(Suit, 5 * k), new Point(x - 6 * k, top + 16 * k), new Point(x - 19 * k, top + 12 * k));
            dc.DrawLine(Pen(Suit, 5 * k), new Point(x + 6 * k, top + 16 * k), new Point(x + 19 * k, top + 12 * k));
        }
        else if (!ground && vy < 0)
        {
            dc.DrawLine(Pen(Suit, 5 * k), new Point(x + 5 * k, top + 16 * k), new Point(x + 9 * k, top + 2 * k));
            dc.DrawLine(Pen(Suit, 5 * k), new Point(x - 5 * k, top + 16 * k), new Point(x - 8 * k, top + 24 * k));
        }
        else
        {
            dc.DrawLine(Pen(Suit, 5 * k), new Point(x + 5 * k, top + 16 * k), new Point(x + 7 * k - run * 5 * k, top + 26 * k));
            dc.DrawLine(Pen(Suit, 5 * k), new Point(x - 5 * k, top + 16 * k), new Point(x - 7 * k + run * 5 * k, top + 26 * k));
        }
        // Head: the face, a brown leather cap, and goggles with two blue lenses.
        dc.DrawEllipse(Brush(0xFFFFD0A0), Pen(0xFFB08060, 1), new Point(x, top + 7 * k), 8 * k, 8 * k);
        Poly(dc, 0xFF7A4A2A, null, 0, new Point(x - 9 * k, top + 7 * k), new Point(x - 8 * k, top - 1 * k), new Point(x, top - 3 * k), new Point(x + 8 * k, top - 1 * k), new Point(x + 9 * k, top + 4 * k), new Point(x - 2 * k, top + 3 * k), new Point(x - 6 * k, top + 12 * k));
        dc.FillRectangle(Brush(0xFF3A2A2A), new Rect(x - 6 * k, top + 3.5 * k, 15 * k, 3 * k));
        dc.DrawEllipse(Brush(Goggle), Pen(0xFF2A2A3A, 1.4), new Point(x + 2 * k, top + 5 * k), 3.2 * k, 3.2 * k);
        dc.DrawEllipse(Brush(Goggle), Pen(0xFF2A2A3A, 1.4), new Point(x + 7.5 * k, top + 5 * k), 2.6 * k, 3 * k);
        dc.DrawLine(Pen(0xFF8A3A2A, 1.4), new Point(x + 2 * k, top + 11 * k), new Point(x + 6 * k, top + 11 * k));
    }

    /// <summary>An enemy, or, while the power lasts, the gold coin it has become (flashing as the power runs out).</summary>
    private void DrawEnemy(DrawingContext dc, Enemy e, double power)
    {
        if (power > 0)
        {
            bool flash = power < 1.5 && (int)(_clock * 8) % 2 == 0;
            double w = Math.Max(0.3, Math.Abs(Math.Cos(_clock * 5 + e.X)));
            uint col = flash ? 0xFFFFFFFF : CoinGold;
            dc.DrawEllipse(Brush(Mix(col, 0xFF000000, 0.3)), Pen(Mix(col, 0xFFFFFFFF, 0.5), 2), new Point(e.X, e.Y), 14 * w, 14);
            dc.DrawEllipse(Brush(col), null, new Point(e.X, e.Y), 9 * w, 9.5);
            return;
        }
        DrawFoe(dc, e.Kind, e.X, e.Y, e.T, e.Dir, e.Vx, e.Unfold, 1.25);
    }

    private void DrawFoe(DrawingContext dc, EnemyKind kind, double x, double y, double t, int dir, double vx, double unfold, double k)
    {
        switch (kind)
        {
            case EnemyKind.Robot:
            {
                // A tin robot: a box with rivets, a dome head with one eye, legs stepping, a winding key turning on its back.
                double step = Math.Sin(t * 10);
                dc.DrawLine(Pen(0xFF8A8AA0, 4 * k), new Point(x - 5 * k, y + 6 * k), new Point(x - 5 * k + step * 3 * k, y + 16 * k));
                dc.DrawLine(Pen(0xFF8A8AA0, 4 * k), new Point(x + 5 * k, y + 6 * k), new Point(x + 5 * k - step * 3 * k, y + 16 * k));
                dc.DrawRectangle(Brush(0xFFD03A3A), Pen(0xFF6A1A1A, 1.2), new Rect(x - 11 * k, y - 8 * k, 22 * k, 16 * k), 2, 2);
                foreach (int sx in new[] { -7, 7 }) dc.DrawEllipse(Brush(0xFFFFD0A0), null, new Point(x + sx * k, y - 4 * k), 1.4 * k, 1.4 * k);
                dc.DrawEllipse(Brush(0xFFC0C8D8), Pen(0xFF6A7080, 1), new Point(x, y - 12 * k), 8 * k, 7 * k);
                dc.DrawEllipse(Brush(Yellow), null, new Point(x + dir * 3 * k, y - 13 * k), 2.6 * k, 2.6 * k);
                double key = Math.Cos(t * 8);
                dc.DrawLine(Pen(0xFFE0C060, 2 * k), new Point(x - dir * 11 * k, y), new Point(x - dir * 16 * k, y));
                dc.DrawEllipse(null, Pen(0xFFE0C060, 2 * k), new Point(x - dir * 18 * k, y - 4 * k * key), 3 * k * Math.Abs(key) + 0.5, 3 * k);
                dc.DrawEllipse(null, Pen(0xFFE0C060, 2 * k), new Point(x - dir * 18 * k, y + 4 * k * key), 3 * k * Math.Abs(key) + 0.5, 3 * k);
                if (unfold > 0)
                {
                    // Wings coming out.
                    double w = (0.8 - unfold) / 0.8;
                    Poly(dc, 0xC0E0E8F0, null, 0, new Point(x - 8 * k, y - 4 * k), new Point(x - (10 + 18 * w) * k, y - (6 + 14 * w) * k), new Point(x - 10 * k, y + 2 * k));
                    Poly(dc, 0xC0E0E8F0, null, 0, new Point(x + 8 * k, y - 4 * k), new Point(x + (10 + 18 * w) * k, y - (6 + 14 * w) * k), new Point(x + 10 * k, y + 2 * k));
                }
                break;
            }
            case EnemyKind.Hawk:
            {
                // A brass hawk, its wings beating.
                double flap = Math.Sin(t * 14) * 12 * k, f = vx >= 0 ? 1 : -1;
                Poly(dc, 0xFFC08A3A, 0xFF6A4A1A, 1, new Point(x - 4 * k, y), new Point(x - 22 * k, y - flap), new Point(x - 10 * k, y + 4 * k));
                Poly(dc, 0xFFC08A3A, 0xFF6A4A1A, 1, new Point(x + 4 * k, y), new Point(x + 22 * k, y - flap), new Point(x + 10 * k, y + 4 * k));
                dc.DrawEllipse(Brush(0xFFE0A84A), Pen(0xFF6A4A1A, 1), new Point(x, y + 2 * k), 9 * k, 6 * k);
                dc.DrawEllipse(Brush(0xFFE0A84A), Pen(0xFF6A4A1A, 1), new Point(x + f * 8 * k, y - 3 * k), 5 * k, 5 * k);
                Poly(dc, Yellow, null, 0, new Point(x + f * 12 * k, y - 4 * k), new Point(x + f * 18 * k, y - 1 * k), new Point(x + f * 12 * k, y));
                dc.DrawEllipse(Brush(Red), null, new Point(x + f * 9 * k, y - 4 * k), 1.6 * k, 1.6 * k);
                break;
            }
            case EnemyKind.Saucer:
                dc.DrawEllipse(Brush(0x806AE0FF), null, new Point(x, y - 6 * k), 8 * k, 7 * k);
                dc.DrawEllipse(Brush(0xFF7A8AA0), Pen(0xFFC0D0E0, 1.5), new Point(x, y + 1 * k), 18 * k, 6 * k);
                for (int i = 0; i < 4; i++)
                {
                    double a = t * 6 + i * Math.PI / 2;
                    if (Math.Sin(a) > 0) dc.DrawEllipse(Brush((int)(t * 6 + i) % 2 == 0 ? Yellow : Red), null, new Point(x + Math.Cos(a) * 14 * k, y + 2 * k), 2 * k, 2 * k);
                }
                break;
            case EnemyKind.Orb:
            {
                double a0 = t * 4;
                for (int i = 0; i < 8; i++)
                {
                    double a = a0 + i * Math.PI / 4;
                    Poly(dc, 0xFF9AFF4A, null, 0, new Point(x + Math.Cos(a - 0.25) * 10 * k, y + Math.Sin(a - 0.25) * 10 * k), new Point(x + Math.Cos(a) * 18 * k, y + Math.Sin(a) * 18 * k), new Point(x + Math.Cos(a + 0.25) * 10 * k, y + Math.Sin(a + 0.25) * 10 * k));
                }
                dc.DrawEllipse(Brush(0xFF3A8A1A), Pen(0xFF9AFF4A, 1.5), new Point(x, y), 11 * k, 11 * k);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x - 3 * k, y - 2 * k), 3 * k, 3 * k);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x + 4 * k, y - 2 * k), 3 * k, 3 * k);
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(x - 3 * k, y - 2 * k), 1.4 * k, 1.4 * k);
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(x + 4 * k, y - 2 * k), 1.4 * k, 1.4 * k);
                break;
            }
            case EnemyKind.Wisp:
            {
                double fl = Math.Sin(t * 20) * 3 * k;
                dc.DrawEllipse(Radial(new Point(x, y), 22 * k, null, (0, 0x806AA0FF), (1, 0x006AA0FF)), null, new Point(x, y), 22 * k, 22 * k);
                Poly(dc, 0xFF6AA0FF, null, 0, new Point(x - 11 * k, y + 6 * k), new Point(x - 6 * k + fl, y - 18 * k), new Point(x, y - 8 * k), new Point(x + 5 * k - fl, y - 22 * k), new Point(x + 11 * k, y + 6 * k), new Point(x, y + 13 * k));
                dc.DrawEllipse(Brush(0xFFD0E8FF), null, new Point(x, y + 2 * k), 7 * k, 8 * k);
                dc.DrawEllipse(Brush(0xFF1A2A5A), null, new Point(x - 3 * k, y), 1.8 * k, 2.4 * k);
                dc.DrawEllipse(Brush(0xFF1A2A5A), null, new Point(x + 3 * k, y), 1.8 * k, 2.4 * k);
                break;
            }
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"ROUND {g.Wave}", new Point(512, 16), 26, SceneColour(g.Layout.Scene), center: true);
        // The power meter, filling towards the next P coin.
        double fill = Math.Min(1, g.PowerMeter / (double)GameEngine.PowerAt);
        dc.DrawRectangle(null, Pen(Dim, 1.5), new Rect(432, 40, 160, 12), 3, 3);
        dc.FillRectangle(Brush(g.Power > 0 ? 0xFF4AD0FF : 0xFF2A80C0), new Rect(434, 42, 156 * (g.Power > 0 ? g.Power / g.PowerSeconds : fill), 8));
        Text(dc, g.Power > 0 ? "POWER!" : "POWER", new Point(512, 66), 18, g.Power > 0 ? 0xFF4AD0FF : Dim, center: true);
        Text(dc, $"x{g.Multiplier}", new Point(640, 46), 32, g.Multiplier > 1 ? 0xFFFF5AD0 : Dim, center: true);
        DrawBomb(dc, 728, 48, true, 0.8);
        Text(dc, g.LitTaken.ToString(), new Point(748, 46), 28, 0xFFFF8A4A);
        int spare = Math.Max(0, g.Lives - (g.Hero.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawHero(dc, 210 + i * 30, 26, 1, true, false, 0, 0, 0.75);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 380;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0080A20), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA BOMB JACK", new Point(cx, 104), 108, 0xFFFF8A3A, center: true);
        Text(dc, "AFTER THE 1984 ARCADE CLASSIC", new Point(cx, 168), 28, Dim, center: true);
        DrawHero(dc, cx, 196 + Math.Sin(_clock * 2) * 8, 1, false, true, 40, 0, 2.2);
        for (int i = 0; i < 6; i++) DrawBomb(dc, cx - 330 + i * 42 + (i > 2 ? 400 : 0), 250, i == 3, 1.4);
        Text(dc, "TAKE ALL 24 BOMBS - THE LIT ONE IS WORTH DOUBLE, AND LIGHTS THE NEXT", new Point(cx, 322), 24, White, center: true);
        Text(dc, $"TAKE 20 OR MORE LIT IN A ROUND FOR A BONUS OF UP TO {GameEngine.FireBonus(23)}", new Point(cx, 350), 24, White, center: true);
        EnemyKind[] kinds = [EnemyKind.Robot, EnemyKind.Hawk, EnemyKind.Orb, EnemyKind.Saucer, EnemyKind.Wisp];
        for (int i = 0; i < kinds.Length; i++)
        {
            double x = cx - 300 + i * 150;
            DrawFoe(dc, kinds[i], x, 418, _clock + i, 1, 1, 0, 1.6);
            Text(dc, kinds[i].ToString().ToUpperInvariant(), new Point(x, 460), 20, Dim, center: true);
        }
        Text(dc, "ROBOTS DROP IN AND WALK; ON THE FLOOR THEY TAKE TO THE AIR", new Point(cx, 492), 22, Dim, center: true);
        (CoinKind Kind, string Words)[] coins = [(CoinKind.Power, "ENEMIES TO COINS"), (CoinKind.Bonus, "MULTIPLIER UP"), (CoinKind.Extra, "EXTRA LIFE")];
        for (int i = 0; i < coins.Length; i++)
        {
            double x = cx - 290 + i * 290;
            DrawCoin(dc, coins[i].Kind, x - 70, 540, 1.2, false, spin: false);
            Text(dc, coins[i].Words, new Point(x - 46, 540), 22, White);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 616), 46, Yellow, center: true);
        Text(dc, "ARROWS: RUN    SPACE/UP: JUMP (HOLD TO GO HIGHER, PRESS AGAIN TO GLIDE)    DOWN: DROP", new Point(cx, 672), 22, Dim, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA LIFE AT {GameEngine.FirstExtraLife}", new Point(cx, 702), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "BEST JUMPERS", new Point(cx, 140), 68, 0xFFFF8A3A, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
