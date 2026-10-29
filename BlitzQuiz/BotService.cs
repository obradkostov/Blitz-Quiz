using System.Text.Json;
using StackExchange.Redis;

public class BotService : BackgroundService
{
    private readonly IConnectionMultiplexer _mux;
    private readonly Random _rnd = new();

    // ime, verovatnoća tačnog odgovora, najduže "razmišljanje" u sekundama
    private static readonly (string Name, double Skill, int MaxThink)[] Bots =
    {
        ("Bot Mika", 0.9, 6),
        ("Bot Nina", 0.7, 10),
        ("Bot Laza", 0.5, 13),
    };

    public BotService(IConnectionMultiplexer mux) => _mux = mux;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_rnd.Next(20, 45)), ct);
            var bot = Bots[_rnd.Next(Bots.Length)];
            await PlayGameAsync(bot.Name, bot.Skill, bot.MaxThink, ct);
        }
    }

    private async Task PlayGameAsync(string name, double skill, int maxThink, CancellationToken ct)
    {
        var score = 0;

        for (var round = 1; round <= 10; round++)
        {
            var difficulty = round <= 4 ? 1 : round <= 8 ? 2 : 3;
            var think = _rnd.Next(1, maxThink + 1);
            await Task.Delay(TimeSpan.FromSeconds(think), ct);

            // isti obrazac poena kao kod igrača: težina * 10 + preostale sekunde
            if (_rnd.NextDouble() < skill)
                score += difficulty * 10 + (15 - think);
        }

        var db = _mux.GetDatabase();
await db.SortedSetAddAsync("leaderboard:best", name, score, SortedSetWhen.GreaterThan);
        var msg = JsonSerializer.Serialize(new { player = name, score });
        await _mux.GetSubscriber().PublishAsync(RedisChannel.Literal("quiz:events"), msg);
    }
}