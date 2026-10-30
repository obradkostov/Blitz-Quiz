using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

[ApiController]
[Route("api/game")]
public class GameController : ControllerBase
{
    private readonly IDatabase _db;
    private const int QuestionSeconds = 15;
    private static readonly TimeSpan GameTtl = TimeSpan.FromHours(2);
    private readonly IConnectionMultiplexer _mux;

    public GameController(IConnectionMultiplexer mux)
    {
        _mux = mux;
        _db = mux.GetDatabase();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest req)
    {
        var player = req.Player?.Trim();
        if (string.IsNullOrEmpty(player) || player.Length > 20)
            return BadRequest(new { error = "Ime mora imati od 1 do 20 znakova." });

        // najviše 5 novih partija u minuti po igraču
        var rateKey = $"rate:newgame:{player.ToLowerInvariant()}";
        var count = await _db.StringIncrementAsync(rateKey);
        if (count == 1)
            await _db.KeyExpireAsync(rateKey, TimeSpan.FromMinutes(1));
        if (count > 5)
            return StatusCode(
                429,
                new { error = "Previše partija zaredom. Pokušaj ponovo za minut." }
            );
        var id = Guid.NewGuid().ToString("N")[..8];
        foreach (var d in new[] { 1, 2, 3 })
        {
            var key = $"game:{id}:d{d}";
            await _db.SetCombineAndStoreAsync(
                SetOperation.Union,
                key,
                new RedisKey[] { $"questions:d{d}" }
            );
            await _db.KeyExpireAsync(key, GameTtl);
        }
        return Ok(new { gameId = id });
    }

    [HttpGet("{id}/next")]
    public async Task<IActionResult> Next(string id)
    {
        var round = await _db.StringIncrementAsync($"game:{id}:round");
        await _db.KeyExpireAsync($"game:{id}:round", GameTtl);
        if (round > 10)
        {
            await PublishFinishAsync(id);
            return Ok(new { finished = true });
        }

        var difficulty =
            round <= 4 ? 1
            : round <= 8 ? 2
            : 3;
        var qid = await _db.SetPopAsync($"game:{id}:d{difficulty}");
        if (qid.IsNull)
            return NotFound(new { error = "Partija ne postoji ili nema više pitanja" });

        await _db.StringSetAsync($"game:{id}:current", qid, TimeSpan.FromSeconds(QuestionSeconds));
        var q = (await _db.HashGetAllAsync($"question:{qid}")).ToDictionary(
            x => x.Name.ToString(),
            x => x.Value.ToString()
        );

        return Ok(
            new
            {
                round,
                difficulty,
                seconds = QuestionSeconds,
                questionId = (int)qid,
                category = q["category"],
                text = q["text"],
                options = new[] { q["a"], q["b"], q["c"], q["d"] },
            }
        );
    }

    [HttpPost("{id}/answer")]
    public async Task<IActionResult> Answer(string id, [FromBody] AnswerRequest req)
    {
        var current = await _db.StringGetAsync($"game:{id}:current");
        var remaining = await _db.KeyTimeToLiveAsync($"game:{id}:current");
        if (current.IsNull || remaining is null)
            return BadRequest(new { error = "Vreme za odgovor je isteklo" });
        if ((int)current != req.QuestionId)
            return BadRequest(new { error = "To nije trenutno pitanje" });

        var first = await _db.SetAddAsync($"game:{id}:q{req.QuestionId}:answered", req.Player);
        if (!first)
            return Conflict(new { error = "Već si odgovorio na ovo pitanje" });
        await _db.KeyExpireAsync($"game:{id}:q{req.QuestionId}:answered", GameTtl);

        var correct = (int)await _db.HashGetAsync($"question:{req.QuestionId}", "correct");
        var difficulty = (int)await _db.HashGetAsync($"question:{req.QuestionId}", "difficulty");

        var isCorrect = req.Answer == correct;
        var points = isCorrect ? difficulty * 10 + (int)remaining.Value.TotalSeconds : 0;
        var total = await _db.SortedSetIncrementAsync($"game:{id}:leaderboard", req.Player, points);
        await _db.SortedSetAddAsync(
            "leaderboard:best",
            req.Player,
            total,
            SortedSetWhen.GreaterThan
        );
        var dayKey = $"leaderboard:day:{DateTime.UtcNow:yyyyMMdd}";
        await _db.SortedSetAddAsync(dayKey, req.Player, total, SortedSetWhen.GreaterThan);
        await _db.KeyExpireAsync(dayKey, TimeSpan.FromHours(48));
        await _db.KeyExpireAsync($"game:{id}:leaderboard", GameTtl);

        return Ok(
            new
            {
                correct = isCorrect,
                correctAnswer = correct,
                points,
                total,
            }
        );
    }

    [HttpGet("{id}/leaderboard")]
    public async Task<IActionResult> Leaderboard(string id)
    {
        var top = await _db.SortedSetRangeByRankWithScoresAsync(
            $"game:{id}:leaderboard",
            0,
            9,
            Order.Descending
        );
        return Ok(top.Select(e => new { player = e.Element.ToString(), score = e.Score }));
    }

    [HttpGet("~/api/leaderboard")]
    public async Task<IActionResult> GlobalLeaderboard([FromQuery] string period = "all")
    {
        var key =
            period == "day" ? $"leaderboard:day:{DateTime.UtcNow:yyyyMMdd}" : "leaderboard:best";

        var top = await _db.SortedSetRangeByRankWithScoresAsync(key, 0, 9, Order.Descending);
        return Ok(top.Select(e => new { player = e.Element.ToString(), score = e.Score }));
    }

    private async Task PublishFinishAsync(string id)
    {
        // SET NX: poruka se šalje samo jednom po partiji
        var first = await _db.StringSetAsync($"game:{id}:published", 1, GameTtl, When.NotExists);
        if (!first)
            return;

        var top = await _db.SortedSetRangeByRankWithScoresAsync(
            $"game:{id}:leaderboard",
            0,
            0,
            Order.Descending
        );
        if (top.Length == 0)
            return;

        var msg = JsonSerializer.Serialize(
            new { player = top[0].Element.ToString(), score = top[0].Score }
        );
        await _mux.GetSubscriber().PublishAsync(RedisChannel.Literal("quiz:events"), msg);
    }
}

public record AnswerRequest(string Player, int QuestionId, int Answer);

public record CreateRequest(string Player);
