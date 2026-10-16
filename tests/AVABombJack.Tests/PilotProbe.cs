using AVABombJack.Engine;

namespace AVABombJack.Tests;

/// <summary>How the computer jumper gets on, round by round (set BOMBJACK_PROBE to run it, for tuning; BOMBJACK_PROBE=lazy for one that just hops about).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("BOMBJACK_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "bombjack-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 6 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Dx: t % 240 < 120 ? -1 : 1, Jump: t % 50 < 25) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"R{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"R{g.Wave} {g.Cause}") + 1;
                if (g.Phase == Phase.Clear && last != Phase.Clear) waves.Add($"R{wave}:{(t - start) / 60:0}s/{g.LitTaken}lit");
                if (g.Wave != wave) { wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: round {g.Wave} after {t / 60:0}s score {g.Score} x{g.Multiplier} | {string.Join(" ", waves)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   bombs left {g.BombsLeft} lives {g.Lives}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
