using LiteDB;

namespace SpotHusher;

public enum LogType
{
    Music = 1,
    Advertisement = 2,
    Paused = 3,
    NotRunning = 4,
    Exited = 5
}

public record AudioLog
{
    public int Id { get; init; }
    public long CreatedTimeUtc { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long? EndTimeUtc { get; set; }
    public string? Singer { get; init; }
    public string? Song { get; init; }
    public LogType Type { get; init; }
}

public record Statistics(int PlayCount, long TotalSeconds);

public record SingerStatistics(string Singer, int PlayCount, long TotalSeconds) : Statistics(PlayCount, TotalSeconds);
public record SongStatistics(string Song, int PlayCount, long TotalSeconds) : Statistics(PlayCount, TotalSeconds);

public class SpotifyMusicTracker
{
    private readonly string _dbPath = "SpotTracker.db";

    public void AddAudioLog(string? singer, string? song, LogType type, bool updateLstEndTimeUtc = true)
    {
        try
        {
            var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var db = new LiteDatabase(_dbPath);
            var collection = db.GetCollection<AudioLog>("AudioLogs");

            var lastLog = collection.Query()
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

            if (lastLog != null && lastLog.EndTimeUtc == null && updateLstEndTimeUtc)
            {
                lastLog.EndTimeUtc = nowSeconds;
                collection.Update(lastLog);
            }

            var newLog = new AudioLog
            {
                CreatedTimeUtc = nowSeconds,
                EndTimeUtc = null,
                Singer = singer,
                Song = song,
                Type = type
            };

            collection.Insert(newLog);
        }
        catch (Exception ex)
        {
            Logger.Error($"{nameof(AddAudioLog)} Error: {ex}.");
        }
    }

    public IEnumerable<SingerStatistics> GetTopSingersInMonth(int topCount = 10)
    {
        try
        {
            var oneMonthAgoSeconds = DateTimeOffset.UtcNow.AddMonths(-1).ToUnixTimeSeconds();

            using var db = new LiteDatabase(_dbPath);
            var collection = db.GetCollection<AudioLog>("AudioLogs");

            var stats = collection.Query()
                .Where(x => x.Type == LogType.Music && x.EndTimeUtc != null && x.CreatedTimeUtc >= oneMonthAgoSeconds && x.Singer != null)
                .ToEnumerable()
                .GroupBy(x => x.Singer)
                .Select(g => new SingerStatistics(
                    Singer: g.Key!,
                    PlayCount: g.Count(),
                    TotalSeconds: g.Sum(x => x.EndTimeUtc!.Value - x.CreatedTimeUtc)
                ))
                .OrderByDescending(x => x.TotalSeconds)
                .ThenByDescending(x => x.PlayCount)
                .Take(topCount);

            return stats.ToList();
        }
        catch (Exception ex)
        {
            Logger.Error($"{nameof(GetTopSingersInMonth)} - {nameof(topCount)}({topCount}) Error: {ex}.");
        }

        return [];
    }

    public IEnumerable<SongStatistics> GetTopSongsInMonth(int topCount = 10)
    {
        try
        {
            var oneMonthAgoSeconds = DateTimeOffset.UtcNow.AddMonths(-1).ToUnixTimeSeconds();

            using var db = new LiteDatabase(_dbPath);
            var collection = db.GetCollection<AudioLog>("AudioLogs");

            var stats = collection.Query()
                .Where(x => x.Type == LogType.Music && x.EndTimeUtc != null && x.CreatedTimeUtc >= oneMonthAgoSeconds && x.Singer != null)
                .ToEnumerable()
                .GroupBy(x => x.Song)
                .Select(g => new SongStatistics(
                    Song: g.Key!,
                    PlayCount: g.Count(),
                    TotalSeconds: g.Sum(x => x.EndTimeUtc!.Value - x.CreatedTimeUtc)
                ))
                .OrderByDescending(x => x.TotalSeconds)
                .ThenByDescending(x => x.PlayCount)
                .Take(topCount);

            return stats.ToList();
        }
        catch (Exception ex)
        {
            Logger.Error($"{nameof(GetTopSongsInMonth)} - {nameof(topCount)}({topCount}) Error: {ex}.");
        }

        return [];
    }
}
