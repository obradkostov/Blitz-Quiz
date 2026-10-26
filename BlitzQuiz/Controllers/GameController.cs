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
                SetOperation.Union, key, new RedisKey[] { $"questions:d{d}" });
            await _db.KeyExpireAsync(key, GameTtl);
        }
        return Ok(new { gameId = id });
    }

    [HttpGet("{id}/next")]
    public async Task<IActionResult> Next(string id)
    {
        var round = await _db.StringIncrementAsync($"game:{id}:round");
        await _db.KeyExpireAsync($"game:{id}:round", GameTtl);
        if (round > 10) return Ok(new { finished = true });

        var difficulty = round <= 4 ? 1 : round <= 8 ? 2 : 3;
        var qid = await _db.SetPopAsync($"game:{id}:d{difficulty}");
        if (qid.IsNull)
            return NotFound(new { error = "Partija ne postoji ili nema više pitanja" });

        await _db.StringSetAsync($"game:{id}:current", qid, TimeSpan.FromSeconds(QuestionSeconds));
        var q = (await _db.HashGetAllAsync($"question:{qid}"))
            .ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

        return Ok(new
        {
            round,
            difficulty,
            seconds = QuestionSeconds,
            questionId = (int)qid,
            category = q["category"],
            text = q["text"],
            options = new[] { q["a"], q["b"], q["c"], q["d"] }
        });
    }
}