using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVABombJack.Engine;
using AVABombJack.Views;
using Controls = AVABombJack.Engine.Controls;

namespace AVABombJack.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set BOMBJACK_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avabombjack-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("BOMBJACK_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVABombJack-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Jump = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 98650, 7);
        g.HighScores.Insert("AVA", 72400, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>A round, and the computer at work on it until something worth showing.</summary>
    private static void Round(int round, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 90)
    {
        var (w, c, g) = Open(seed);
        g.Go(round);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Hero.Alive && g.Hero.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_Dunes() => Round(1, "dunes", g => g.BombsLeft <= 20 && g.Lit != null && !g.Hero.OnGround && g.Enemies.Count >= 2);

    [AvaloniaFact]
    public void The_Ruins() => Round(2, "ruins", g => g.Enemies.Count(e => e.Kind == EnemyKind.Orb) >= 1 && g.Enemies.Any(e => e.Kind == EnemyKind.Robot) && g.BombsLeft <= 18);

    [AvaloniaFact]
    public void The_Frost_Peaks() => Round(3, "frost", g => g.Hero.Gliding && g.Enemies.Any(e => e.Kind == EnemyKind.Saucer));

    [AvaloniaFact]
    public void The_Neon_City() => Round(4, "city", g => g.Enemies.Any(e => e.Kind == EnemyKind.Wisp) && g.BombsLeft <= 18 && !g.Hero.OnGround);

    [AvaloniaFact]
    public void The_Lagoon() => Round(5, "lagoon", g => g.Coins.Count > 0 && g.Enemies.Count >= 2);

    [AvaloniaFact]
    public void Power() => Round(2, "power", g => g.Power > 2 && g.Enemies.Count >= 2, seed: 5, seconds: 150);

    [AvaloniaFact]
    public void A_round_cleared()
    {
        var (w, c, g) = Open(4);
        g.Go(1);
        g.SetLives(20);
        // Twenty taken in order (all but the first lit), and the computer to finish the round.
        for (int i = 0; i <= 20; i++) g.TakeBomb(i);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 1.3, 90));
        Assert.True(g.Bonus > 0);
        Shot(w, c, "clear");
    }
}
