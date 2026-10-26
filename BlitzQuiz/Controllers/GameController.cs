using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

[ApiController]
[Route("api/game")]
public class GameController : ControllerBase
{
    private readonly IDatabase _db;
    private const int QuestionSeconds = 15;
    private static readonly TimeSpan GameTtl = TimeSpan.FromHours(2);

    public GameController(IConnectionMultiplexer mux) => _db = mux.GetDatabase();

    [HttpPost]
    public async Task<IActionResult> Create()
    {
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
            return Ok(new { finished = true });

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
}

public record AnswerRequest(string Player, int QuestionId, int Answer);
