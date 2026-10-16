namespace AVABombJack.Engine;

/// <summary>The controls: left and right, down (to drop faster), and the jump button (hold it to go higher; press it again in the air to glide).</summary>
public readonly record struct Controls(int Dx = 0, bool Down = false, bool Jump = false);

/// <summary>The screen, in pixels: the play area below the score bar, with a frame all round.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 32, Right = 992, Top = 96, Floor = 744;
    public const double CX = (Left + Right) / 2;
}

public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CentreX => X + W / 2;
    public bool Overlaps(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}

/// <summary>A round's backdrop.</summary>
public enum Scene { Dunes, Ruins, Frost, City, Lagoon }

/// <summary>
/// One of the five rounds' layouts: where the platforms are, and the 24 bombs in the order they light.
/// </summary>
public sealed record Layout(string Name, Scene Scene, Box[] Platforms, (double X, double Y)[] Bombs);

public static class Layouts
{
    public const double Thick = 16;
    private static Box P(double x, double y, double w) => new(x, y, w, Thick);

    /// <summary>A row of bombs at one height: on a platform's top (y is the surface), or in the air.</summary>
    private static IEnumerable<(double, double)> Row(double y, params double[] xs) => xs.Select(x => (x, y - 22));

    private static (double, double)[] Bombs(params IEnumerable<(double, double)>[] rows)
    {
        var all = rows.SelectMany(r => r).ToArray();
        if (all.Length != 24) throw new InvalidOperationException($"{all.Length} bombs");
        return all;
    }

    public static readonly Layout[] All =
    [
        new("THE DUNES", Scene.Dunes,
            [P(130, 610, 220), P(674, 610, 220), P(402, 480, 220), P(110, 340, 200), P(714, 340, 200), P(402, 210, 220)],
            Bombs(Row(Field.Floor, 90, 150), Row(610, 170, 240, 310), Row(480, 442, 512, 582), Row(610, 714, 784, 854),
                  Row(Field.Floor, 874, 934), Row(340, 744, 804, 864), Row(152, 700, 780, 860), Row(210, 452, 572), Row(152, 164, 244, 324))),
        new("THE RUINS", Scene.Ruins,
            [P(80, 560, 180), P(764, 560, 180), P(300, 420, 160), P(564, 420, 160), P(80, 280, 180), P(764, 280, 180), P(412, 180, 200)],
            Bombs(Row(560, 120, 170, 220), Row(420, 330, 380, 430), Row(280, 120, 170, 220), Row(152, 150, 230, 310),
                  Row(180, 462, 512, 562), Row(152, 714, 794, 874), Row(280, 804, 854, 904), Row(420, 594, 644, 694))),
        new("THE FROST PEAKS", Scene.Frost,
            [P(100, 620, 160), P(300, 520, 160), P(500, 420, 160), P(700, 320, 160), P(100, 320, 140), P(300, 200, 180), P(760, 600, 180)],
            Bombs(Row(Field.Floor, 200, 280, 360), Row(620, 130, 180, 230), Row(520, 330, 380, 430), Row(420, 530, 580, 630),
                  Row(320, 730, 780, 830), Row(162, 900, 950), Row(600, 800, 860, 920), Row(320, 125, 185), Row(200, 350, 410))),
        new("THE NEON CITY", Scene.City,
            [P(200, 600, 624), P(60, 440, 240), P(724, 440, 240), P(330, 300, 364), P(60, 170, 200), P(764, 170, 200)],
            Bombs(Row(600, 260, 340, 420), Row(600, 604, 684, 764), Row(440, 774, 844, 914), Row(170, 804, 864, 924),
                  Row(300, 400, 470, 554, 624), Row(170, 100, 160, 220), Row(440, 110, 180, 250), Row(Field.Floor, 60, 964))),
        new("THE LAGOON", Scene.Lagoon,
            [P(412, 640, 200), P(160, 520, 160), P(704, 520, 160), P(412, 400, 200), P(100, 280, 160), P(764, 280, 160), P(412, 160, 200)],
            Bombs(Row(Field.Floor, 90, 160), Row(520, 190, 240, 290), Row(280, 130, 180, 230), Row(352, 380, 644),
                  Row(160, 452, 512, 572), Row(280, 794, 844, 894), Row(520, 734, 784, 834), Row(Field.Floor, 864, 934),
                  Row(400, 462, 562), Row(640, 512))),
    ];

    public static Layout For(int round) => All[(round - 1) % All.Length];
}

/// <summary>A bomb waiting to be picked up.</summary>
public sealed class Bomb
{
    public int Order;
    public double X, Y;
    public bool Taken;
    public const double Radius = 12;
}

/// <summary>The jumper.</summary>
public sealed class Hero
{
    public double X = Field.CX, Y = Field.Floor - Height, Vx, Vy;
    public const double Width = 26, Height = 40;
    public bool OnGround = true, Gliding, Alive = true;
    /// <summary>How much longer holding the button keeps it rising.</summary>
    public double Thrust;
    public int Face = 1;
    public double Safe, Spin;
    public Box Box => new(X - Width / 2, Y, Width, Height);
    public double CentreY => Y + Height / 2;
}

public enum EnemyKind
{
    /// <summary>A wind-up robot: drops from the top, walks the platforms, and unfolds into a flyer on the floor.</summary>
    Robot,
    /// <summary>A mechanical hawk, swooping at the jumper.</summary>
    Hawk,
    /// <summary>A saucer that tracks the jumper across the screen.</summary>
    Saucer,
    /// <summary>A spiked orb, bouncing off the walls.</summary>
    Orb,
    /// <summary>A wisp that drifts after the jumper, slowly and then faster.</summary>
    Wisp,
}

public sealed class Enemy
{
    public EnemyKind Kind;
    public double X, Y, Vx, Vy, T;
    public int Dir = 1;
    public bool OnGround, Dead;
    /// <summary>A robot on the floor, unfolding: how long until it flies.</summary>
    public double Unfold;
    /// <summary>Each one a little quicker or slower than the next.</summary>
    public double Pace = 1;
    public const double Radius = 15;
    public bool Flying => Kind != EnemyKind.Robot;
}

public enum CoinKind
{
    /// <summary>Power: every enemy becomes a coin for a while, there to be eaten.</summary>
    Power,
    /// <summary>Bonus: the score multiplier goes up one.</summary>
    Bonus,
    /// <summary>Extra: an extra life.</summary>
    Extra,
}

/// <summary>A lettered coin bouncing round the screen until it's caught, or goes.</summary>
public sealed class Coin
{
    public CoinKind Kind;
    public double X, Y, Vx, Vy, Life = 12;
    public const double Radius = 14;
}

/// <summary>A fleck of something, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
