using System.Text.Json;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
var mux = await ConnectionMultiplexer.ConnectAsync(builder.Configuration["Redis:Connection"]!);
builder.Services.AddSingleton<IConnectionMultiplexer>(mux);
builder.Services.AddSignalR();
builder.Services.AddHostedService<FeedRelay>();
builder.Services.AddHostedService<BotService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<FeedHub>("/hub/feed");
Console.WriteLine("PING: " + await mux.GetDatabase().PingAsync());
var db = mux.GetDatabase();

// JSON se učitava samo prvi put, posle toga je Redis izvor pitanja
if (!await db.KeyExistsAsync("questions:seeded"))
{
    var json = await File.ReadAllTextAsync(
        Path.Combine(builder.Environment.ContentRootPath, "Data", "questions.json")
    );
    var file = JsonSerializer.Deserialize<QuestionFile>(
        json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    )!;

    foreach (var q in file.Questions)
    {
        await db.HashSetAsync(
            $"question:{q.Id}",
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
        await db.SetAddAsync("questions:all", q.Id);
        await db.SetAddAsync($"questions:d{q.Difficulty}", q.Id);
    }

    await db.StringSetAsync("questions:seeded", 1);
}
Console.WriteLine($"Učitano pitanja: {await db.SetLengthAsync("questions:all")}");
app.Run();

public record Question(
    int Id,
    string Category,
    int Difficulty,
    string Text,
    string[] Options,
    int Correct
);

public record QuestionFile(List<Question> Questions);
