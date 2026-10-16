using AVABombJack.Engine;

namespace AVABombJack.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avabombjack-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A round in play, calm: no enemies come, and any put there stay put.</summary>
    private static GameEngine Game(int round = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(round);
        return g;
    }

    /// <summary>A real game, with plenty of lives.</summary>
    private static GameEngine Real(int round = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(round);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls JumpButton = new(Jump: true);

    /// <summary>The highest the jumper gets (the least Y) with the button held for so many ticks after take-off.</summary>
    private static double Apex(GameEngine g, int hold)
    {
        double top = g.Hero.Y;
        g.Run(hold, JumpButton);
        for (int i = 0; i < 120 && !g.Hero.OnGround; i++)
        {
            g.Tick(default);
            top = Math.Min(top, g.Hero.Y);
            if (i > 0 && g.Hero.Vy > 0 && g.Hero.Y > top + 50) break;
        }
        return Math.Min(top, g.Hero.Y);
    }

    /// <summary>Clears the bombs out of the way, keeping only these (or just the first, so the round isn't over).</summary>
    private static void Keep(GameEngine g, params int[] orders)
    {
        if (orders.Length == 0) orders = [0];
        foreach (var b in g.Bombs.Where(b => !orders.Contains(b.Order))) b.Taken = true;
    }

    // ───────────────────────────── the jumper ─────────────────────────────

    [Fact]
    public void The_jumper_runs_and_the_frame_stops_it()
    {
        var g = Game();
        Keep(g);
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(Field.CX + GameEngine.RunSpeed * 0.5, g.Hero.X, 1);
        g.Run(300, new Controls(Dx: 1));
        Assert.Equal(Field.Right - Hero.Width / 2, g.Hero.X);
    }

    [Fact]
    public void Holding_the_button_jumps_higher()
    {
        var a = Game(2);
        Keep(a);
        double tap = Apex(a, 2);
        var b = Game(2);
        Keep(b);
        double held = Apex(b, 60);
        double floor = Field.Floor - Hero.Height;
        Assert.True(floor - tap > 100, $"{floor - tap}");
        Assert.True(floor - held > 2.4 * (floor - tap), $"{floor - held} against {floor - tap}");
    }

    [Fact]
    public void Pressing_again_in_the_air_glides_and_down_drops()
    {
        var g = Game(3);
        Keep(g);
        g.Run(10, JumpButton);
        g.Idle(40);
        Assert.True(g.Hero.Vy > 0);
        g.Run(20, JumpButton);
        Assert.True(g.Hero.Gliding);
        Assert.True(g.Hero.Vy <= GameEngine.GlideFall);
        g.Run(10, new Controls(Down: true));
        Assert.False(g.Hero.Gliding);
        Assert.True(g.Hero.Vy > GameEngine.MaxFall * 0.6);
    }

    [Fact]
    public void Platforms_hold_the_jumper_up_and_stop_it_from_below()
    {
        var g = Game();
        Keep(g);
        var p = g.Layout.Platforms[2];
        g.PutHero(p.CentreX, p.Y - Hero.Height - 30);
        g.Idle(60);
        Assert.True(g.Hero.OnGround);
        Assert.Equal(p.Y - Hero.Height, g.Hero.Y, 3);
        // From below: the head stops at its underside.
        g.PutHero(p.CentreX, p.Bottom + 60);
        g.Run(30, JumpButton);
        Assert.True(g.Hero.Y >= p.Bottom - 0.01);
    }

    [Fact]
    public void Off_the_end_of_a_platform_and_down()
    {
        var g = Game();
        Keep(g);
        var p = g.Layout.Platforms[0];
        g.PutHero(p.Right - 20, p.Y - Hero.Height);
        g.Idle(2);
        Assert.True(g.Hero.OnGround);
        g.Run(30, new Controls(Dx: 1));
        g.Idle(90);
        Assert.Equal(Field.Floor - Hero.Height, g.Hero.Y, 3);
    }

    // ───────────────────────────── the bombs ─────────────────────────────

    [Fact]
    public void A_bomb_taken_lights_the_next_in_order_and_the_lit_one_is_worth_double()
    {
        var g = Game();
        Assert.Null(g.Lit);
        g.TakeBomb(4);
        Assert.Equal(100, g.Score);
        Assert.Equal(5, g.Lit!.Order);
        g.TakeBomb(5);
        Assert.Equal(300, g.Score);
        Assert.Equal(6, g.Lit!.Order);
        Assert.Equal(1, g.LitTaken);
        // An unlit one moves the light on from there.
        g.TakeBomb(20);
        Assert.Equal(400, g.Score);
        Assert.Equal(21, g.Lit!.Order);
        // Past the end, it comes round to the first not yet taken.
        g.TakeBomb(21); g.TakeBomb(22); g.TakeBomb(23);
        Assert.Equal(0, g.Lit!.Order);
    }

    [Fact]
    public void Touching_a_bomb_takes_it()
    {
        var g = Game();
        var b = g.Bombs[0];
        g.PutHero(b.X, b.Y - Hero.Height / 2);
        g.Idle(1);
        Assert.True(b.Taken);
        Assert.Equal(23, g.BombsLeft);
    }

    [Fact]
    public void The_last_bomb_clears_the_round_with_a_bonus_for_twenty_or_more_lit()
    {
        var g = Game();
        g.TakeBomb(0);
        for (int i = 1; i <= 20; i++) g.TakeBomb(i);
        Assert.Equal(20, g.LitTaken);
        g.TakeBomb(22);
        g.TakeBomb(21);
        g.TakeBomb(23);
        g.Idle(1);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(21, g.LitTaken);
        Assert.Equal(GameEngine.FireBonus(21), g.Bonus);
        int before = g.Score;
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(before + 20000, g.Score);
        Assert.Equal(2, g.Wave);
        Assert.Equal(Layouts.All[1], g.Layout);
        Assert.Equal(24, g.BombsLeft);
        Assert.Equal(new[] { 0, 0, 0, 0, 10000, 20000, 30000, 50000 }, new[] { 0, 10, 15, 19, 20, 21, 22, 23 }.Select(GameEngine.FireBonus));
    }

    [Fact]
    public void Every_layout_has_24_bombs_clear_of_its_platforms()
    {
        foreach (var l in Layouts.All)
        {
            Assert.Equal(24, l.Bombs.Length);
            foreach (var (x, y) in l.Bombs)
            {
                Assert.InRange(x, Field.Left + 12, Field.Right - 12);
                Assert.InRange(y, Field.Top + 12, Field.Floor - 12);
                var box = new Box(x - 11, y - 11, 22, 22);
                Assert.DoesNotContain(l.Platforms, p => p.Overlaps(box));
            }
            // And no two on top of each other.
            for (int i = 0; i < 24; i++)
                for (int j = i + 1; j < 24; j++)
                    Assert.True(Math.Abs(l.Bombs[i].X - l.Bombs[j].X) + Math.Abs(l.Bombs[i].Y - l.Bombs[j].Y) > 30, $"{l.Name} {i} {j}");
        }
    }

    // ───────────────────────────── coins ─────────────────────────────

    [Fact]
    public void Enough_bombs_bring_out_a_power_coin()
    {
        var g = Game();
        for (int i = 0; i < 6 && g.Coins.Count == 0; i++) g.TakeBomb(i);
        // One unlit (1) and five lit (3 each) is 16.
        var coin = Assert.Single(g.Coins);
        Assert.Equal(CoinKind.Power, coin.Kind);
        Assert.Equal(0, g.PowerMeter);
    }

    [Fact]
    public void Power_turns_the_enemies_to_coins_to_eat_for_more_each_time()
    {
        var g = Game();
        Keep(g, 0);
        var h = g.Hero;
        g.PutCoin(CoinKind.Power, h.X, h.CentreY);
        g.Idle(1);
        Assert.True(g.Power > 0);
        int before = g.Score;
        for (int i = 0; i < 3; i++)
        {
            g.PutEnemy(EnemyKind.Hawk, h.X, h.CentreY);
            g.Idle(1);
        }
        Assert.Empty(g.Enemies);
        Assert.Equal(100 + 200 + 300, g.Score - before);
        g.Idle((int)(g.PowerSeconds * 60) + 2);
        Assert.Equal(0, g.Power);
        g.PutEnemy(EnemyKind.Hawk, h.X, h.CentreY);
        g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
    }

    [Fact]
    public void A_bonus_coin_raises_the_multiplier()
    {
        var g = Game();
        var h = g.Hero;
        g.PutCoin(CoinKind.Bonus, h.X, h.CentreY);
        g.Idle(1);
        Assert.Equal(2, g.Multiplier);
        g.TakeBomb(10);
        Assert.Equal(200, g.Score);
        for (int i = 0; i < 6; i++) { g.PutCoin(CoinKind.Bonus, h.X, h.CentreY); g.Idle(1); }
        Assert.Equal(GameEngine.MaxMultiplier, g.Multiplier);
    }

    [Fact]
    public void A_bonus_coin_comes_out_every_10000_points()
    {
        var g = Game();
        g.SetScore(GameEngine.BonusEvery - 50);
        g.TakeBomb(3);
        Assert.Single(g.Coins, c => c.Kind == CoinKind.Bonus);
    }

    [Fact]
    public void An_extra_coin_gives_a_life_and_every_third_power_coin_brings_one()
    {
        var g = Game();
        var h = g.Hero;
        int lives = g.Lives;
        g.PutCoin(CoinKind.Extra, h.X, h.CentreY);
        g.Idle(1);
        Assert.Equal(lives + 1, g.Lives);
        for (int i = 0; i < 3; i++) { g.PutCoin(CoinKind.Power, h.X, h.CentreY); g.Idle(1); }
        Assert.Single(g.Coins, c => c.Kind == CoinKind.Extra);
    }

    [Fact]
    public void An_extra_life_at_20000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 50);
        g.TakeBomb(3);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void Coins_bounce_about_then_go()
    {
        var g = Game();
        Keep(g);
        g.PutHero(Field.Left + 20, Field.Floor - Hero.Height);
        var c = g.PutCoin(CoinKind.Bonus, 600, 300);
        c.Vx = 150; c.Vy = -150;
        g.Idle(240);
        Assert.InRange(c.X, Field.Left, Field.Right);
        Assert.InRange(c.Y, Field.Top, Field.Floor);
        g.Idle(60 * 12);
        Assert.Empty(g.Coins);
    }

    // ───────────────────────────── the enemies ─────────────────────────────

    [Fact]
    public void Robots_drop_in_walk_off_the_platforms_and_take_to_the_air_on_the_floor()
    {
        var g = Real();
        g.Idle(1);
        g.PutHero(Field.Left + 20, Field.Floor - Hero.Height);
        g.Hero.Safe = 600;
        var robot = g.PutEnemy(EnemyKind.Robot, g.Layout.Platforms[5].CentreX, Field.Top + 20);
        robot.Dir = 1;
        bool landed = false;
        for (int i = 0; i < 60 * 30 && robot.Kind == EnemyKind.Robot; i++)
        {
            g.Tick(default);
            if (robot.OnGround && robot.Y < Field.Floor - 40) landed = true;
        }
        Assert.True(landed);
        Assert.Equal(EnemyKind.Hawk, robot.Kind);
        Assert.True(robot.Flying);
    }

    [Fact]
    public void Each_round_has_its_own_flyer()
    {
        Assert.Equal(new[] { EnemyKind.Hawk, EnemyKind.Orb, EnemyKind.Saucer, EnemyKind.Wisp, EnemyKind.Hawk },
            Enumerable.Range(1, 5).Select(r => Game(r).FlyerKind));
    }

    [Fact]
    public void More_robots_come_until_there_are_enough()
    {
        var g = Real(3);
        g.Hero.Safe = 600;
        g.Idle(60 * 60);
        Assert.Equal(g.MaxEnemies, g.Enemies.Count);
        g.Idle(60 * 20);
        Assert.Equal(g.MaxEnemies, g.Enemies.Count);
    }

    [Fact]
    public void A_wisp_burns_itself_out()
    {
        var g = Game(4);
        Keep(g);
        var w = g.PutEnemy(EnemyKind.Wisp, 200, 200);
        g.Hero.Safe = 600;
        g.Calm = false;
        g.Idle((int)(GameEngine.WispLife * 60) + 30);
        Assert.DoesNotContain(w, g.Enemies);
    }

    [Fact]
    public void Caught_back_to_the_middle_with_the_bombs_kept_and_the_last_life_ends_it()
    {
        var g = Game();
        g.SetLives(2);
        g.TakeBomb(0);
        g.TakeBomb(1);
        g.PutHero(300, Field.Floor - Hero.Height);
        g.PutEnemy(EnemyKind.Orb, 300, Field.Floor - Hero.Height / 2);
        g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("orb", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal(Field.CX, g.Hero.X);
        Assert.Equal(22, g.BombsLeft);
        Assert.Equal(2, g.Lit!.Order);
        Assert.Empty(g.Enemies);
        // Safe for a moment.
        g.PutEnemy(EnemyKind.Orb, g.Hero.X, g.Hero.CentreY);
        g.Idle(1);
        Assert.Equal(Phase.Play, g.Phase);
        g.Enemies.Clear();
        g.Idle((int)(GameEngine.SafeSeconds * 60));
        g.PutEnemy(EnemyKind.Orb, g.Hero.X, g.Hero.CentreY);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── the whole game ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_jumper_clears_the_first_round()
    {
        var g = Real();
        for (int i = 0; i < 60 * 150 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on round {g.Wave}, {g.BombsLeft} bombs left, {g.Cause}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Hero.X:0.00} {g.Hero.Y:0.00} {g.BombsLeft} {g.Phase} {g.Enemies.Count}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Run(30, new Controls(Dx: 1));
        double x = g.Hero.X;
        g.Pause();
        g.Idle(120);
        Assert.Equal(x, g.Hero.X);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
