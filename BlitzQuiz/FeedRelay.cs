using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

public class FeedHub : Hub { }

public class FeedRelay : BackgroundService
{
    private readonly IConnectionMultiplexer _mux;
    private readonly IHubContext<FeedHub> _hub;

    public FeedRelay(IConnectionMultiplexer mux, IHubContext<FeedHub> hub)
    {
        _mux = mux;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var sub = _mux.GetSubscriber();
        await sub.SubscribeAsync(RedisChannel.Literal("quiz:events"), async (_, message) =>
        {
            await _hub.Clients.All.SendAsync("gameFinished", message.ToString());
        });
        await Task.Delay(Timeout.Infinite, ct);
    }
}