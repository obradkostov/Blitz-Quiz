using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

[ApiController]
[Route("api/questions")]
public class QuestionsController : ControllerBase
{
    private readonly IDatabase _db;

    public QuestionsController(IConnectionMultiplexer mux) => _db = mux.GetDatabase();

    // READ: spisak pitanja jedne težine (id i tekst)
    [HttpGet]
    public async Task<IActionResult> List(int difficulty = 1)
    {
        var ids = (await _db.SetMembersAsync($"questions:d{difficulty}"))
            .Select(x => (int)x)
            .OrderBy(x => x)
            .ToList();
        var texts = await Task.WhenAll(
            ids.Select(id => _db.HashGetAsync($"question:{id}", "text"))
        );
        return Ok(ids.Select((id, i) => new { id, text = (string?)texts[i] }));
    }

    // READ: jedno pitanje
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var h = await _db.HashGetAllAsync($"question:{id}");
        if (h.Length == 0)
            return NotFound();
        var d = h.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
        return Ok(
            new
            {
                id,
                text = d["text"],
                options = new[] { d["a"], d["b"], d["c"], d["d"] },
                correct = int.Parse(d["correct"]),
                category = d["category"],
                difficulty = int.Parse(d["difficulty"]),
            }
        );
    }

    // CREATE: novi id = najveći postojeći + 1
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] QuestionRequest q)
    {
        if (!Valid(q))
            return BadRequest(new { error = "Popuni tekst, 4 odgovora i kategoriju." });
        var id =
            (await _db.SetMembersAsync("questions:all")).Select(x => (int)x).DefaultIfEmpty(0).Max()
            + 1;
        await Save(id, q);
        return Ok(new { id });
    }

    // UPDATE
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] QuestionRequest q)
    {
        if (!Valid(q))
            return BadRequest(new { error = "Popuni tekst, 4 odgovora i kategoriju." });
        if (!await _db.KeyExistsAsync($"question:{id}"))
            return NotFound(new { error = "Pitanje ne postoji." });
        await Save(id, q);
        return Ok();
    }

    // DELETE
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await _db.KeyDeleteAsync($"question:{id}"))
            return NotFound(new { error = "Pitanje ne postoji." });
        await RemoveFromSets(id);
        return Ok();
    }

    // Izbaci id iz svih skupova (pitanja se čuvaju u skupu po težini)
    private async Task RemoveFromSets(int id)
    {
        await _db.SetRemoveAsync("questions:all", id);
        for (var d = 1; d <= 3; d++)
            await _db.SetRemoveAsync($"questions:d{d}", id);
    }

    // Upis hash-a i ubacivanje u skupove
    private async Task Save(int id, QuestionRequest q)
    {
        await RemoveFromSets(id); // da izmena težine ne ostavi pitanje u starom skupu
        await _db.HashSetAsync(
            $"question:{id}",
            new HashEntry[]
            {
                new("text", q.Text),
                new("a", q.Options[0]),
                new("b", q.Options[1]),
                new("c", q.Options[2]),
                new("d", q.Options[3]),
                new("correct", q.Correct),
                new("category", q.Category),
                new("difficulty", q.Difficulty),
            }
        );
        await _db.SetAddAsync("questions:all", id);
        await _db.SetAddAsync($"questions:d{q.Difficulty}", id);
    }

    private static bool Valid(QuestionRequest q) =>
        !string.IsNullOrWhiteSpace(q.Text)
        && !string.IsNullOrWhiteSpace(q.Category)
        && q.Options is { Length: 4 }
        && q.Options.All(o => !string.IsNullOrWhiteSpace(o))
        && q.Correct is >= 0 and <= 3
        && q.Difficulty is >= 1 and <= 3;
}

public record QuestionRequest(
    string Text,
    string[] Options,
    int Correct,
    string Category,
    int Difficulty
);
