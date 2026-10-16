namespace AVABombJack.Engine;

public sealed partial class GameEngine
{
    public const double Gravity = 1150, JumpSpeed = 600, ThrustTime = 0.5, GlideFall = 75, DropFall = 900, MaxFall = 620;
    public const double RunSpeed = 235, AirSpeed = 215, GlideSpeed = 165;
    public const double StartSeconds = 2.2, DyingSeconds = 2.2, SafeSeconds = 2, ClearSeconds = 4;
    public const int BombPoints = 100, LitPoints = 200, PowerAt = 16, FirstExtraLife = 20000;
    public const int BonusEvery = 10000, MaxMultiplier = 5;

    /// <summary>The points for eating each enemy in turn while the power lasts.</summary>
    public static readonly int[] EatPoints = [100, 200, 300, 500, 800, 1200, 2000];

    /// <summary>The bonus for taking so many bombs lit in a round, from 20 up.</summary>
    public static int FireBonus(int lit) => lit switch { >= 23 => 50000, 22 => 30000, 21 => 20000, 20 => 10000, _ => 0 };

    /// <summary>The round: the five layouts come round again, faster.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Layout Layout { get; private set; } = Layouts.For(1);
    public Hero Hero { get; } = new();
    public List<Bomb> Bombs { get; } = [];
    public List<Enemy> Enemies { get; } = [];
    public List<Coin> Coins { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    /// <summary>The bomb that's lit: the next in order after the last one taken.</summary>
    public Bomb? Lit { get; private set; }
    public int LitTaken { get; private set; }
    /// <summary>Fills as bombs are taken (lit ones count three); full, and a power coin comes out.</summary>
    public int PowerMeter { get; private set; }
    /// <summary>How long the enemies stay coins.</summary>
    public double Power { get; private set; }
    public int Multiplier { get; private set; } = 1;
    public int Bonus { get; private set; }
    /// <summary>For tests: no enemies come, and those there stay put.</summary>
    internal bool Calm { get; set; }

    private int _eaten, _powers, _nextBonus, _nextExtra;
    private double _spawnIn;
    private bool _jumpPrev, _glideArmed;

    public int BombsLeft => Bombs.Count(b => !b.Taken);
    public double RobotSpeed => Math.Min(130, 70 + 6 * (Wave - 1));
    public double FlyerSpeed => Math.Min(185, 105 + 8 * (Wave - 1));
    public double SpawnEvery => Math.Max(3, 7 - 0.5 * (Wave - 1));
    public int MaxEnemies => Math.Min(7, 3 + Wave / 2);
    public const double WispLife = 16;
    public double PowerSeconds => Math.Max(3.5, 7 - 0.4 * (Wave - 1));

    /// <summary>What a robot turns into on this round, once it reaches the floor.</summary>
    public EnemyKind FlyerKind => ((Wave - 1) % 4) switch { 0 => EnemyKind.Hawk, 1 => EnemyKind.Orb, 2 => EnemyKind.Saucer, _ => EnemyKind.Wisp };

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        Multiplier = 1;
        _powers = 0;
        _nextBonus = BonusEvery;
        _nextExtra = FirstExtraLife;
        Wreckage.Clear();
        SetPhase(Phase.Start);
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Layout = Layouts.For(wave);
        Bombs.Clear();
        for (int i = 0; i < Layout.Bombs.Length; i++) Bombs.Add(new Bomb { Order = i, X = Layout.Bombs[i].X, Y = Layout.Bombs[i].Y });
        Lit = null;
        LitTaken = 0;
        PowerMeter = 0;
        Coins.Clear();
        Restart();
        SetPhase(Phase.Start);
        Say($"ROUND {wave}", Layout.Name, StartSeconds + 0.3);
        Play(Sfx.RoundStart);
    }

    /// <summary>The jumper back in the middle of the floor, and the enemies cleared away to come again.</summary>
    private void Restart()
    {
        Hero.X = Field.CX;
        Hero.Y = Field.Floor - Hero.Height;
        Hero.Vx = Hero.Vy = 0;
        Hero.OnGround = true;
        Hero.Gliding = false;
        Hero.Thrust = 0;
        Hero.Alive = true;
        Hero.Spin = 0;
        Enemies.Clear();
        Power = 0;
        _spawnIn = 1.2;
        _glideArmed = false;
    }

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds)
                {
                    SetPhase(Phase.Play);
                    Music(true);
                    // Two robots to start with, dropping from the top.
                    if (!Calm) { Drop(Field.Left + 160); Drop(Field.Right - 160); }
                }
                break;
            case Phase.Play:
                Hero.Safe = Math.Max(0, Hero.Safe - dt);
                Move(dt, c);
                Collect();
                Menace(dt);
                MoveCoins(dt);
                Catch();
                if (BombsLeft == 0) Clear();
                break;
            case Phase.Dying:
                Hero.Spin += dt * 12;
                Hero.Vy = Math.Min(MaxFall, Hero.Vy + Gravity * dt);
                Hero.Y = Math.Min(Field.Floor - Hero.Height, Hero.Y + Hero.Vy * dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Restart();
                    Hero.Safe = SafeSeconds;
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Clear:
                Move(dt, default);
                if (PhaseTime >= 1 && PhaseTime - dt < 1 && Bonus > 0)
                {
                    Score += Bonus;
                    Play(Sfx.FireBonus);
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the jumper ─────────────────────────────

    /// <summary>
    /// Running and jumping. A press on the ground jumps, and holding it keeps the jumper rising for a while; let go and it
    /// falls. A fresh press in the air, on the way down, glides it gently down for as long as it's held. Down drops it fast.
    /// </summary>
    private void Move(double dt, Controls c)
    {
        var h = Hero;
        bool pressed = c.Jump && !_jumpPrev;
        _jumpPrev = c.Jump;
        if (c.Dx != 0) h.Face = c.Dx;
        if (h.OnGround)
        {
            h.Vx = c.Dx * RunSpeed;
            if (pressed)
            {
                h.Vy = -JumpSpeed;
                h.Thrust = ThrustTime;
                h.OnGround = false;
                _glideArmed = false;
                Play(Sfx.Jump);
            }
        }
        else
        {
            if (!c.Jump) { h.Thrust = 0; _glideArmed = true; }
            if (h.Thrust > 0)
            {
                h.Thrust -= dt;
                h.Vy = -JumpSpeed;
            }
            else h.Vy += Gravity * dt;
            if (pressed && _glideArmed && h.Vy > -100 && !h.Gliding) Play(Sfx.Glide);
            h.Gliding = c.Jump && _glideArmed && h.Thrust <= 0 && !c.Down && h.Vy > -100;
            if (h.Gliding) h.Vy = Math.Min(h.Vy, GlideFall);
            if (c.Down) { h.Vy = Math.Max(h.Vy, DropFall * 0.5); h.Thrust = 0; }
            h.Vy = Math.Min(h.Vy, c.Down ? DropFall : MaxFall);
            h.Vx = c.Dx * (h.Gliding ? GlideSpeed : AirSpeed);
        }
        // Across, then up or down, each stopped by the platforms and the frame.
        h.X += h.Vx * dt;
        foreach (var p in Layout.Platforms)
        {
            var b = h.Box;
            if (!b.Overlaps(p)) continue;
            h.X = h.Vx > 0 ? p.X - Hero.Width / 2 : p.Right + Hero.Width / 2;
        }
        h.X = Math.Clamp(h.X, Field.Left + Hero.Width / 2, Field.Right - Hero.Width / 2);
        h.Y += h.Vy * dt;
        bool landed = false;
        foreach (var p in Layout.Platforms)
        {
            var b = h.Box;
            if (!b.Overlaps(p)) continue;
            if (h.Vy >= 0) { h.Y = p.Y - Hero.Height; landed = true; }
            else { h.Y = p.Bottom; h.Vy = 0; h.Thrust = 0; }
        }
        if (h.Y >= Field.Floor - Hero.Height) { h.Y = Field.Floor - Hero.Height; landed = true; }
        if (h.Y < Field.Top) { h.Y = Field.Top; h.Vy = Math.Max(0, h.Vy); h.Thrust = 0; }
        if (landed && h.Vy >= 0)
        {
            h.Vy = 0;
            h.OnGround = true;
            h.Gliding = false;
            h.Thrust = 0;
        }
        else if (h.OnGround && !Standing(h)) h.OnGround = false;
    }

    /// <summary>Something under the jumper's feet.</summary>
    private bool Standing(Hero h)
    {
        if (h.Y >= Field.Floor - Hero.Height - 0.5) return true;
        var feet = new Box(h.X - Hero.Width / 2, h.Y + Hero.Height, Hero.Width, 1);
        return Layout.Platforms.Any(p => feet.Overlaps(p));
    }

    // ───────────────────────────── bombs and coins ─────────────────────────────

    private void Collect()
    {
        var h = Hero;
        foreach (var b in Bombs.Where(b => !b.Taken))
        {
            double dx = b.X - h.X, dy = b.Y - h.CentreY;
            if (Math.Abs(dx) > Hero.Width / 2 + Bomb.Radius - 2 || Math.Abs(dy) > Hero.Height / 2 + Bomb.Radius - 2) continue;
            Take(b);
        }
    }

    /// <summary>A bomb taken: double if it's the lit one, and the next in order lights.</summary>
    private void Take(Bomb b)
    {
        b.Taken = true;
        bool lit = b == Lit;
        int points = (lit ? LitPoints : BombPoints) * Multiplier;
        if (lit) LitTaken++;
        Award(points, b.X, b.Y - 14, lit ? 0xFFFF8A3A : 0xFFFFE14A);
        Play(lit ? Sfx.LitBomb : Sfx.Bomb);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            Wreckage.Add(new Debris { X = b.X, Y = b.Y, Vx = Math.Cos(a) * 120, Vy = Math.Sin(a) * 120, Life = 0.35, Colour = lit ? 0xFFFFB04A : 0xFFFFF0A0 });
        }
        Lit = Bombs.Where(o => !o.Taken).OrderBy(o => (o.Order - b.Order + Bombs.Count) % Bombs.Count).FirstOrDefault();
        PowerMeter += lit ? 3 : 1;
        if (PowerMeter >= PowerAt && !Coins.Any(c => c.Kind == CoinKind.Power) && BombsLeft > 0)
        {
            PowerMeter = 0;
            Release(CoinKind.Power);
        }
    }

    /// <summary>A coin comes out at the top of the screen and starts bouncing about.</summary>
    private void Release(CoinKind kind)
    {
        double a = Math.PI * (0.2 + _rng.NextDouble() * 0.6);
        Coins.Add(new Coin { Kind = kind, X = Field.Left + 80 + _rng.NextDouble() * (Field.Right - Field.Left - 160), Y = Field.Top + 30, Vx = Math.Cos(a) * 150, Vy = Math.Sin(a) * 150 });
    }

    private void MoveCoins(double dt)
    {
        foreach (var c in Coins)
        {
            c.Life -= dt;
            c.X += c.Vx * dt;
            c.Y += c.Vy * dt;
            if (c.X < Field.Left + Coin.Radius || c.X > Field.Right - Coin.Radius) c.Vx = -c.Vx;
            if (c.Y < Field.Top + Coin.Radius || c.Y > Field.Floor - Coin.Radius) c.Vy = -c.Vy;
            c.X = Math.Clamp(c.X, Field.Left + Coin.Radius, Field.Right - Coin.Radius);
            c.Y = Math.Clamp(c.Y, Field.Top + Coin.Radius, Field.Floor - Coin.Radius);
        }
        Coins.RemoveAll(c => c.Life <= 0);
        var h = Hero;
        foreach (var c in Coins.ToList())
        {
            if (Math.Abs(c.X - h.X) > Hero.Width / 2 + Coin.Radius || Math.Abs(c.Y - h.CentreY) > Hero.Height / 2 + Coin.Radius) continue;
            Coins.Remove(c);
            switch (c.Kind)
            {
                case CoinKind.Power:
                    Power = PowerSeconds;
                    _eaten = 0;
                    _powers++;
                    Play(Sfx.Power);
                    Award(1000 * Multiplier, c.X, c.Y - 14, 0xFF6AE0FF);
                    if (_powers % 3 == 0) Release(CoinKind.Extra);
                    break;
                case CoinKind.Bonus:
                    Multiplier = Math.Min(MaxMultiplier, Multiplier + 1);
                    Play(Sfx.Bonus);
                    Effects.Add(new Effect { Kind = EffectKind.Word, X = c.X, Y = c.Y - 20, Text = $"x{Multiplier}", Life = 1.2, Colour = 0xFFFF6AE0 });
                    break;
                case CoinKind.Extra:
                    Lives++;
                    Play(Sfx.ExtraLife);
                    Effects.Add(new Effect { Kind = EffectKind.Word, X = c.X, Y = c.Y - 20, Text = "1 UP", Life = 1.2, Colour = 0xFF6AFF8A });
                    break;
            }
        }
    }

    // ───────────────────────────── the enemies ─────────────────────────────

    /// <summary>A robot drops in from the top of the screen.</summary>
    private void Drop(double x)
    {
        Enemies.Add(new Enemy { Kind = EnemyKind.Robot, X = x, Y = Field.Top + 16, Dir = x < Hero.X ? 1 : -1, Pace = 0.85 + _rng.NextDouble() * 0.3 });
        Play(Sfx.Drop);
    }

    private void Menace(double dt)
    {
        if (Power > 0) Power = Math.Max(0, Power - dt);
        if (Calm) return;
        _spawnIn -= dt;
        if (_spawnIn <= 0)
        {
            _spawnIn = SpawnEvery;
            if (Enemies.Count < MaxEnemies)
            {
                // Away from the jumper.
                double x = Hero.X < Field.CX ? Field.Right - 120 - _rng.NextDouble() * 260 : Field.Left + 120 + _rng.NextDouble() * 260;
                Drop(x);
            }
        }
        double slow = Power > 0 ? 0.35 : 1;
        foreach (var e in Enemies)
        {
            e.T += dt;
            if (e.Kind == EnemyKind.Robot) Walk(e, dt * slow);
            else Fly(e, dt * slow);
        }
        Enemies.RemoveAll(e => e.Dead);
    }

    /// <summary>A robot walks along until it walks off; on the floor it stops, unfolds, and takes to the air.</summary>
    private void Walk(Enemy e, double dt)
    {
        if (e.Unfold > 0)
        {
            e.Unfold -= dt;
            if (e.Unfold <= 0)
            {
                e.Kind = FlyerKind;
                e.T = 0;
                e.Vy = -FlyerSpeed * 0.6;
                e.Vx = e.Dir * FlyerSpeed * 0.6;
                Play(Sfx.Transform);
            }
            return;
        }
        if (e.OnGround) e.X += e.Dir * RobotSpeed * dt;
        else
        {
            e.Vy = Math.Min(MaxFall * 0.6, e.Vy + Gravity * 0.7 * dt);
            e.Y += e.Vy * dt;
        }
        if (e.X < Field.Left + 14 || e.X > Field.Right - 14) { e.Dir = -e.Dir; e.X = Math.Clamp(e.X, Field.Left + 14, Field.Right - 14); }
        // Its feet: a platform under them, or the floor.
        double feet = e.Y + 16;
        e.OnGround = false;
        if (feet >= Field.Floor)
        {
            e.Y = Field.Floor - 16;
            e.Vy = 0;
            e.OnGround = true;
            e.Unfold = 0.8;
            return;
        }
        foreach (var p in Layout.Platforms)
        {
            if (e.X < p.X || e.X > p.Right) continue;
            if (feet >= p.Y && feet <= p.Y + 12 && e.Vy >= 0)
            {
                e.Y = p.Y - 16;
                e.Vy = 0;
                e.OnGround = true;
            }
        }
    }

    /// <summary>A flyer, after its own fashion; all of them go through the platforms and bounce off the frame.</summary>
    private void Fly(Enemy e, double dt)
    {
        var h = Hero;
        double tx = h.X - e.X, ty = h.CentreY - e.Y, d = Math.Max(1, Math.Sqrt(tx * tx + ty * ty)), speed = FlyerSpeed * e.Pace;
        switch (e.Kind)
        {
            case EnemyKind.Hawk:
            {
                // Swoops: steers for the jumper, turning only so fast.
                double want = Math.Atan2(ty, tx), have = Math.Atan2(e.Vy, e.Vx);
                have += Math.Clamp(Math.IEEERemainder(want - have, Math.PI * 2), -1.6 * dt, 1.6 * dt);
                e.Vx = Math.Cos(have) * speed;
                e.Vy = Math.Sin(have) * speed;
                break;
            }
            case EnemyKind.Saucer:
                // Slides across to get over the jumper, and bobs down towards it.
                e.Vx += Math.Sign(tx) * speed * 2 * dt;
                e.Vx = Math.Clamp(e.Vx, -speed, speed);
                e.Vy = Math.Sin(e.T * 2) * speed * 0.5 + Math.Sign(ty) * speed * 0.25;
                break;
            case EnemyKind.Orb:
                // Straight lines and bounces, a little faster.
                if (Math.Abs(e.Vx) < 1 || Math.Abs(e.Vy) < 1) { e.Vx = speed * 0.75; e.Vy = speed * 0.75; }
                double l = Math.Sqrt(e.Vx * e.Vx + e.Vy * e.Vy);
                e.Vx *= speed * 1.1 / l;
                e.Vy *= speed * 1.1 / l;
                break;
            case EnemyKind.Wisp:
            {
                // Slow to start, gathering speed the longer it's out, until it burns itself out.
                if (e.T > WispLife) { e.Dead = true; Burst(e.X, e.Y, 0xFF6AA0FF, 10); break; }
                double k = Math.Min(0.9, 0.35 + e.T * 0.04);
                e.Vx += (tx / d * speed * k - e.Vx) * Math.Min(1, 1.5 * dt);
                e.Vy += (ty / d * speed * k - e.Vy) * Math.Min(1, 1.5 * dt);
                break;
            }
        }
        // Flyers keep apart, so they don't bunch into one.
        foreach (var o in Enemies)
        {
            if (o == e || !o.Flying) continue;
            double ox = e.X - o.X, oy = e.Y - o.Y, od = ox * ox + oy * oy;
            if (od > 60 * 60 || od < 0.01) continue;
            od = Math.Sqrt(od);
            e.X += ox / od * 40 * dt;
            e.Y += oy / od * 40 * dt;
        }
        e.X += e.Vx * dt;
        e.Y += e.Vy * dt;
        if (e.X < Field.Left + 16 || e.X > Field.Right - 16) { e.Vx = -e.Vx; e.X = Math.Clamp(e.X, Field.Left + 16, Field.Right - 16); }
        if (e.Y < Field.Top + 16 || e.Y > Field.Floor - 16) { e.Vy = -e.Vy; e.Y = Math.Clamp(e.Y, Field.Top + 16, Field.Floor - 16); }
    }

    /// <summary>The jumper and the enemies: caught, or, while the power lasts, eating them.</summary>
    private void Catch()
    {
        var h = Hero;
        foreach (var e in Enemies)
        {
            if (Math.Abs(e.X - h.X) > Hero.Width / 2 + Enemy.Radius - 4 || Math.Abs(e.Y - h.CentreY) > Hero.Height / 2 + Enemy.Radius - 4) continue;
            if (Power > 0)
            {
                e.Dead = true;
                int points = EatPoints[Math.Min(_eaten, EatPoints.Length - 1)] * Multiplier;
                _eaten++;
                Award(points, e.X, e.Y - 14, 0xFF6AE0FF);
                Play(Sfx.Eat);
                continue;
            }
            if (h.Safe > 0) continue;
            Die(e.Kind);
            return;
        }
        Enemies.RemoveAll(e => e.Dead);
    }

    private void Die(EnemyKind by)
    {
        Hero.Alive = false;
        Hero.Vy = -380;
        Cause = by.ToString().ToLowerInvariant();
        Lives--;
        Music(false);
        Play(Sfx.Death);
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    // ───────────────────────────── a round won ─────────────────────────────

    private void Clear()
    {
        Bonus = FireBonus(LitTaken);
        Enemies.Clear();
        Coins.Clear();
        Power = 0;
        Music(false);
        Play(Sfx.RoundClear);
        Say("ROUND CLEAR", Bonus > 0 ? $"{LitTaken} LIT - BONUS {Bonus}" : $"{LitTaken} LIT", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y, uint colour)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Life = 0.8, Colour = colour });
        CheckExtra();
    }

    /// <summary>A bonus coin every 10000 points (while the multiplier can still go up), and an extra life at 20000.</summary>
    private void CheckExtra()
    {
        while (Score >= _nextBonus)
        {
            _nextBonus += BonusEvery;
            if (Multiplier + Coins.Count(c => c.Kind == CoinKind.Bonus) < MaxMultiplier && Phase == Phase.Play) Release(CoinKind.Bonus);
        }
        if (Score >= _nextExtra)
        {
            _nextExtra = int.MaxValue;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Burst(double x, double y, uint colour, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 140;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 0.4 + _rng.NextDouble() * 0.4, Colour = colour });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a round, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal Enemy PutEnemy(EnemyKind kind, double x, double y)
    {
        var e = new Enemy { Kind = kind, X = x, Y = y };
        Enemies.Add(e);
        return e;
    }

    internal Coin PutCoin(CoinKind kind, double x, double y)
    {
        var c = new Coin { Kind = kind, X = x, Y = y };
        Coins.Add(c);
        return c;
    }

    internal void PutHero(double x, double y) { Hero.X = x; Hero.Y = y; Hero.Vx = Hero.Vy = 0; Hero.OnGround = false; Hero.Thrust = 0; }
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
    internal void TakeBomb(int order) => Take(Bombs[order]);
}
