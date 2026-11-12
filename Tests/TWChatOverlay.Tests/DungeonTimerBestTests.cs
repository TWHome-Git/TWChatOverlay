using System;
using System.Collections.Generic;
using System.Linq;
using TWChatOverlay.Services;
using Xunit;

namespace TWChatOverlay.Tests
{
    /// <summary>
    /// 던전 타이머의 최고 기록 고르기. 렐릭처럼 건너뛸 수 있는 구간(석상)이 있는 던전은
    /// 가장 빠른 판이 대개 석상을 건너뛴 판이라, 석상 줄은 구간별로 가장 빨랐던 값을 따로 남겨야 한다.
    /// </summary>
    public class DungeonTimerBestTests
    {
        private const string Key = "relic/";
        private static readonly DateTime Day = new(2026, 5, 25, 11, 0, 0);

        private static DungeonRunRecord Relic(int minute, double whole, params (string Name, double Seconds)[] statues)
        {
            var segments = statues.ToDictionary(s => s.Name, s => s.Seconds);
            segments["전체"] = whole;
            return new DungeonRunRecord
            {
                DungeonKey = "relic",
                StartedAt = Day.AddMinutes(minute),
                EndedAt = Day.AddMinutes(minute).AddSeconds(whole),
                Difficulty = string.Empty,
                Segments = segments,
                TotalSeconds = whole,
            };
        }

        [Fact(DisplayName = "렐릭 정의: 석상 넷은 건너뛸 수 있는 구간이고 '전체'는 판 전체 구간이다")]
        public void RelicDefinition_HasOptionalStatuesAndWholeRun()
        {
            var relic = ContentTimerService.RecordDefinitions.Single(d => d.Key == "relic");
            Assert.Equal(new[] { "갑옷의 석상", "보호의 석상", "생명의 석상", "검의 석상" }, relic.Segments.Where(s => s.Optional).Select(s => s.Name).ToArray());
            Assert.Equal("전체", relic.Segments.Single(s => s.WholeRun).Name);
        }

        [Fact(DisplayName = "가장 빠른 판이 석상을 건너뛰어도 석상 줄에는 구간별 최고가 남는다")]
        public void FastestRunSkippedStatues_StatueRowsKeepTheirOwnBest()
        {
            var bests = new Dictionary<string, DungeonRunRecord>();
            DungeonRunRecord withStatues = Relic(0, 240, ("갑옷의 석상", 30), ("보호의 석상", 42), ("생명의 석상", 25), ("검의 석상", 51));
            DungeonRunRecord fastest = Relic(10, 55);
            DungeonRunRecord another = Relic(20, 200, ("갑옷의 석상", 22), ("보호의 석상", 60));

            Assert.True(ContentTimerService.MergeIntoBest(bests, Key, withStatues));
            Assert.True(ContentTimerService.MergeIntoBest(bests, Key, fastest));
            Assert.True(ContentTimerService.MergeIntoBest(bests, Key, another));

            DungeonRunRecord best = bests[Key];
            Assert.Equal(55, best.TotalSeconds);
            Assert.Equal(55, best.Segments["전체"]);
            Assert.Equal(fastest.StartedAt, best.StartedAt);
            Assert.Equal(22, best.Segments["갑옷의 석상"]);
            Assert.Equal(42, best.Segments["보호의 석상"]);
            Assert.Equal(25, best.Segments["생명의 석상"]);
            Assert.Equal(51, best.Segments["검의 석상"]);
        }

        [Fact(DisplayName = "판이 들어오는 순서가 달라도 결과가 같다")]
        public void OrderDoesNotMatter()
        {
            DungeonRunRecord[] runs =
            {
                Relic(0, 240, ("갑옷의 석상", 30), ("생명의 석상", 25)),
                Relic(10, 55),
                Relic(20, 200, ("갑옷의 석상", 22), ("보호의 석상", 60)),
                Relic(30, 180, ("검의 석상", 40), ("보호의 석상", 35)),
            };

            string Describe(IEnumerable<DungeonRunRecord> order)
            {
                var bests = new Dictionary<string, DungeonRunRecord>();
                foreach (DungeonRunRecord run in order)
                    ContentTimerService.MergeIntoBest(bests, Key, run);
                return string.Join(", ", bests[Key].Segments.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
            }

            string forward = Describe(runs);
            Assert.Equal(forward, Describe(runs.Reverse()));
            Assert.Equal(forward, Describe(new[] { runs[2], runs[0], runs[3], runs[1] }));
            Assert.Contains("갑옷의 석상=22", forward);
            Assert.Contains("보호의 석상=35", forward);
            Assert.Contains("전체=55", forward);
        }

        [Fact(DisplayName = "최고 기록에 구간 값을 채워도 판 이력에 든 원래 기록은 바뀌지 않는다")]
        public void MergingDoesNotChangeTheOriginalRunRecords()
        {
            var bests = new Dictionary<string, DungeonRunRecord>();
            DungeonRunRecord fastest = Relic(10, 55);
            DungeonRunRecord withStatues = Relic(0, 240, ("갑옷의 석상", 30));

            ContentTimerService.MergeIntoBest(bests, Key, fastest);
            ContentTimerService.MergeIntoBest(bests, Key, withStatues);

            Assert.NotSame(fastest, bests[Key]);
            Assert.Equal(new[] { "전체" }, fastest.Segments.Keys.ToArray());
            Assert.Equal(30, bests[Key].Segments["갑옷의 석상"]);
        }

        [Fact(DisplayName = "상한 기록(\"30초 이하\")과 시간이 0인 판은 최고 기록에 반영하지 않는다")]
        public void CappedAndEmptyRunsAreIgnored()
        {
            var bests = new Dictionary<string, DungeonRunRecord>();
            DungeonRunRecord capped = Relic(0, 30, ("갑옷의 석상", 5));
            capped.Capped = true;
            DungeonRunRecord empty = Relic(5, 0, ("갑옷의 석상", 5));

            Assert.False(ContentTimerService.MergeIntoBest(bests, Key, capped));
            Assert.False(ContentTimerService.MergeIntoBest(bests, Key, empty));
            Assert.Empty(bests);
        }

        [Fact(DisplayName = "건너뛸 수 없는 구간은 가장 빠른 판의 값만 쓴다 (다른 판의 구간이 섞이지 않는다)")]
        public void NonOptionalSegmentsComeOnlyFromTheFastestRun()
        {
            // 구간이 둘 이상이고 건너뛸 수 있는 구간이 없는 던전 하나
            var def = ContentTimerService.RecordDefinitions.First(d => d.Segments.Length >= 2 && !d.Segments.Any(s => s.Optional) && !d.Segments.Any(s => s.WholeRun));
            string first = def.Segments[0].Name, second = def.Segments[1].Name;
            DungeonRunRecord Run(int minute, double a, double b) => new()
            {
                DungeonKey = def.Key,
                StartedAt = Day.AddMinutes(minute),
                EndedAt = Day.AddMinutes(minute).AddSeconds(a + b),
                Segments = new Dictionary<string, double> { [first] = a, [second] = b },
                TotalSeconds = a + b,
            };

            var bests = new Dictionary<string, DungeonRunRecord>();
            string key = def.Key + "/";
            ContentTimerService.MergeIntoBest(bests, key, Run(0, 60, 40));   // 합 100
            Assert.False(ContentTimerService.MergeIntoBest(bests, key, Run(10, 20, 100))); // 합 120 — 첫 구간은 더 빠르지만 판은 느리다

            Assert.Equal(100, bests[key].TotalSeconds);
            Assert.Equal(60, bests[key].Segments[first]);
            Assert.Equal(40, bests[key].Segments[second]);
        }
    }
}
