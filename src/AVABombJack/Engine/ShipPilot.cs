namespace AVABombJack.Engine;

/// <summary>
/// The computer jumper, for the demonstration (and the tests). It goes for the lit bomb (or, with none lit, the
/// nearest), a coin when one comes near, and, while the power lasts, the enemies. To get up to something it
/// finds a way up past the platforms between, jumps, and steers over once it's high enough; to get down it walks
/// off the nearest edge. When an enemy comes too close it tries each way it could move for the one that keeps
/// it furthest away.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>Closer than this, an enemy is worth dodging.</summary>
    public const double Wary = 78;
    /// <summary>The most it trusts a jump to climb, feet to feet.</summary>
    public const double Climb = 420;

    private bool _held;
    private double _col;
    /// <summary>A platform it's climbing to on the way up, remembered until it lands.</summary>
    private (double X, double Y)? _step;

    public Controls Decide(GameEngine g)
    {
        var h = g.Hero;
        if (g.Phase != Phase.Play || !h.Alive) { _held = false; Doing = "waiting"; return default; }
        var (tx, ty, what) = Target(g);
        var c = Go(g, tx, ty);
        Doing = what;
        if (g.Power <= 0.3 && h.Safe <= 0.2 && Dodge(g, c) is { } dodge) { c = dodge; Doing = "dodging"; }
        _held = c.Jump;
        return c;
    }

    /// <summary>What to go for: the enemies while they're coins, a coin close by, the lit bomb, or the nearest.</summary>
    private static (double X, double Y, string What) Target(GameEngine g)
    {
        var h = g.Hero;
        double D(double x, double y) => Math.Abs(x - h.X) + Math.Abs(y - h.CentreY) * 1.3;
        if (g.Power > 1 && g.Enemies.Count > 0)
        {
            var e = g.Enemies.OrderBy(e => D(e.X, e.Y)).First();
            if (D(e.X, e.Y) < 420) return (e.X, e.Y, "eating");
        }
        var coin = g.Coins.Where(c => D(c.X, c.Y) < 260).OrderBy(c => D(c.X, c.Y)).FirstOrDefault();
        if (coin != null) return (coin.X, coin.Y, "coin");
        var bomb = g.Lit is { Taken: false } lit && D(lit.X, lit.Y) < 700 ? lit : g.Bombs.Where(b => !b.Taken).OrderBy(b => D(b.X, b.Y)).FirstOrDefault();
        return bomb == null ? (h.X, h.CentreY, "idle") : (bomb.X, bomb.Y, bomb == g.Lit ? "lit bomb" : "bomb");
    }

    /// <summary>Steering for a point: up, across or down.</summary>
    private Controls Go(GameEngine g, double tx, double ty)
    {
        var h = g.Hero;
        double cy = h.CentreY;
        if (h.OnGround) _step = null;
        else if (_step is { } st) (tx, ty) = st;
        int toward = Math.Abs(tx - h.X) < 6 ? 0 : Math.Sign(tx - h.X);
        if (ty < cy - 28)
        {
            // Up. On the ground: to a spot with nothing overhead all the way up, then jump.
            if (h.OnGround)
            {
                // No way straight up from here: up onto a platform first, the one that gets it nearest.
                if (Column(g, h, tx, ty) is not { } col)
                {
                    var step = Stepping(g, h, tx, ty);
                    if (step == null) return new Controls(toward);
                    (tx, ty) = step.Value;
                    _step = step;
                    toward = Math.Abs(tx - h.X) < 6 ? 0 : Math.Sign(tx - h.X);
                    col = Column(g, h, tx, ty) ?? tx;
                }
                // A spot just off the edge of the platform it's on: jump from the edge and drift out on the way up.
                double from = Platform(g, h) is { } stand ? Math.Clamp(col, stand.X + 6, stand.Right - 6) : col;
                if (Math.Abs(from - h.X) > 7) return new Controls(Math.Sign(from - h.X));
                _col = col;
                return new Controls(0, false, !_held);
            }
            // Rising: keep the button held until high enough, then over.
            bool high = h.Y + Hero.Height < ty + 22;
            bool rising = h.Vy < 0 && !high;
            // Still climbing: hold over the spot it jumped from, clear of the platforms, until it's high enough.
            int dx = high ? toward : Math.Abs(_col - h.X) > 4 ? Math.Sign(_col - h.X) : 0;
            if (h.Vy >= 0 && !high && !Under(g, h, ty)) dx = toward;
            // Falling short: glide while getting across.
            bool glide = h.Vy > 0 && Math.Abs(tx - h.X) > 30 && h.Y + Hero.Height < ty + 60;
            return new Controls(dx, false, rising || glide);
        }
        if (ty > cy + 28)
        {
            // Down: off the nearest edge towards it, steering on the way.
            if (h.OnGround && Platform(g, h) is { } p && tx > p.X - 6 && tx < p.Right + 6)
            {
                double left = p.X - Hero.Width, right = p.Right + Hero.Width;
                double edge = Math.Abs(left - h.X) + Math.Abs(left - tx) < Math.Abs(right - h.X) + Math.Abs(right - tx) ? left : right;
                return new Controls(Math.Sign(edge - h.X));
            }
            return new Controls(toward, !h.OnGround && Math.Abs(tx - h.X) < 40 && ty - cy > 160);
        }
        // Across. A gap between here and there on a platform: hop it.
        if (h.OnGround && toward != 0 && Platform(g, h) is { } on)
        {
            bool gap = (toward > 0 && tx > on.Right + 4) || (toward < 0 && tx < on.X - 4);
            if (gap && (toward > 0 ? on.Right - h.X : h.X - on.X) < 30) return new Controls(toward, false, !_held);
        }
        bool keep = !h.OnGround && h.Vy < 0 && _held && h.Y + Hero.Height > ty + 30;
        return new Controls(toward, false, keep);
    }

    /// <summary>The platform under the jumper's feet, if any.</summary>
    private static Box? Platform(GameEngine g, Hero h)
    {
        var feet = new Box(h.X - Hero.Width / 2, h.Y + Hero.Height, Hero.Width, 2);
        foreach (var p in g.Layout.Platforms) if (feet.Overlaps(p)) return p;
        return null;
    }

    /// <summary>Still under a platform it would bump into before reaching the height it wants.</summary>
    private static bool Under(GameEngine g, Hero h, double ty) =>
        g.Layout.Platforms.Any(p => p.Bottom <= h.Y + 4 && p.Y > ty - 10 && h.X + Hero.Width / 2 + 2 > p.X && h.X - Hero.Width / 2 - 2 < p.Right);

    /// <summary>The nearest place to jump from with nothing in the way between here and the target's height.</summary>
    private static double? Column(GameEngine g, Hero h, double tx, double ty)
    {
        if (h.Y + Hero.Height - (ty + 22) > Climb) return null;
        var blocking = g.Layout.Platforms.Where(p => p.Bottom <= h.Y + 4 && p.Y > ty - 10).ToList();
        bool Clear(double x) => x > Field.Left + 20 && x < Field.Right - 20 &&
            !blocking.Any(p => x + Hero.Width / 2 + 4 > p.X && x - Hero.Width / 2 - 4 < p.Right);
        var on = Platform(g, h);
        // How far off the platform it's on a spot is: a little way is fine (it can drift out), too far isn't.
        double Off(double x) => on is not { } p ? 0 : Math.Max(0, Math.Max(p.X + 6 - x, x - (p.Right - 6)));
        var choices = new List<double> { tx, h.X };
        foreach (var p in blocking) { choices.Add(p.X - Hero.Width / 2 - 8); choices.Add(p.Right + Hero.Width / 2 + 8); }
        double? best = null;
        double cost = double.MaxValue;
        foreach (var x in choices)
        {
            if (!Clear(x)) continue;
            double off = Off(x);
            if (off > 50) continue;
            double c = Math.Abs(x - h.X) + Math.Abs(x - tx) * 0.6 + off * 2;
            if (c < cost) { cost = c; best = x; }
        }
        return best;
    }

    /// <summary>A platform above to climb onto on the way up, with a clear way up to it: the one nearest the target.</summary>
    private static (double X, double Y)? Stepping(GameEngine g, Hero h, double tx, double ty)
    {
        (double, double)? best = null;
        double cost = double.MaxValue;
        foreach (var p in g.Layout.Platforms)
        {
            if (p.Y >= h.Y + Hero.Height - 10 || p.Y < ty - 30) continue;
            double x = Math.Clamp(tx, p.X + 16, p.Right - 16), y = p.Y - 22;
            if (Column(g, h, x, y) is null) continue;
            double c = Math.Abs(x - tx) + Math.Abs(y - ty) * 0.5 + Math.Abs(x - h.X) * 0.3;
            if (c < cost) { cost = c; best = (x, y); }
        }
        return best;
    }

    /// <summary>
    /// With an enemy too close, every way it could go (left, right or still; jumping, dropping or neither), looked
    /// ahead a little, for the one that keeps it furthest from them all. Nothing if it's not in danger.
    /// </summary>
    private Controls? Dodge(GameEngine g, Controls plan)
    {
        var h = g.Hero;
        if (!g.Enemies.Any(e => Near(e, h) < Wary * 1.6)) return null;
        double Worst(Controls c)
        {
            double worst = double.MaxValue;
            double vy = h.OnGround ? (c.Jump ? -GameEngine.JumpSpeed : 0) : h.Vy;
            foreach (double t in new[] { 0.12, 0.25, 0.4 })
            {
                double x = Math.Clamp(h.X + c.Dx * (h.OnGround ? GameEngine.RunSpeed : GameEngine.AirSpeed) * t, Field.Left + 14, Field.Right - 14);
                double y = h.CentreY + (c.Down ? Math.Max(vy, 400) : vy) * t + (h.OnGround && !c.Jump ? 0 : 0.5 * GameEngine.Gravity * t * t * (c.Jump && h.OnGround ? 0.1 : 1));
                y = Math.Clamp(y, Field.Top + 20, Field.Floor - 20);
                foreach (var e in g.Enemies)
                {
                    double ex = e.X + e.Vx * t, ey = e.Y + e.Vy * t;
                    if (e.Kind == EnemyKind.Robot && e.OnGround && e.Unfold <= 0) ex = e.X + e.Dir * g.RobotSpeed * t;
                    worst = Math.Min(worst, Math.Sqrt((x - ex) * (x - ex) + (y - ey) * (y - ey) * 0.8));
                }
            }
            return worst;
        }
        double now = Worst(plan);
        if (now > Wary) return null;
        Controls best = plan;
        double score = now;
        foreach (int dx in new[] { -1, 0, 1 })
            foreach (var (jump, down) in new[] { (false, false), (true, false), (false, true) })
            {
                if (jump && !h.OnGround && !_held) continue;
                var c = new Controls(dx, down, jump && (h.OnGround ? !_held : _held));
                double s = Worst(c);
                if (s > score + 6) { score = s; best = c; }
            }
        return best;
    }

    private static double Near(Enemy e, Hero h) => Math.Sqrt((e.X - h.X) * (e.X - h.X) + (e.Y - h.CentreY) * (e.Y - h.CentreY));
}
