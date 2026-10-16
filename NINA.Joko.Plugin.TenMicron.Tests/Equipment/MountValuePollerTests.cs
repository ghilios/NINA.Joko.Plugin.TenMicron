using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugin.TenMicron.Tests.Equipment {

    [TestFixture]
    public class MountValuePollerTests {
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(60);
        private static readonly string[] SlowKeys = { "A", "B", "C", "D", "E", "F", "G", "H" };

        private DateTime now;
        private Dictionary<string, int> readCounts;
        private MountValuePoller sut;

        [SetUp]
        public void SetUp() {
            now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            readCounts = new Dictionary<string, int>();
            sut = new MountValuePoller(() => now, new Random(1234), MountValuePoller.DefaultMaxScheduledReadsPerTick);
        }

        private void Register(string key, TimeSpan interval, Func<object> read = null) {
            readCounts[key] = 0;
            sut.Register(key, () => {
                ++readCounts[key];
                return read != null ? read() : key;
            }, interval);
        }

        private void RegisterTypicalSet() {
            Register("Status", TimeSpan.Zero);
            foreach (var key in SlowKeys) {
                Register(key, SlowInterval);
            }
        }

        [Test]
        public void FirstPoll_ReadsEverything() {
            RegisterTypicalSet();

            var result = sut.Poll();

            result.Keys.Should().BeEquivalentTo(new[] { "Status" }.Concat(SlowKeys));
            result["A"].Should().Be("A");
        }

        [Test]
        public void SecondPoll_ReadsOnlyEveryTickEntries() {
            RegisterTypicalSet();
            sut.Poll();

            now += Tick;
            var result = sut.Poll();

            result.Keys.Should().BeEquivalentTo(new[] { "Status" });
        }

        [Test]
        public void Invalidate_ForcesReadOnNextPoll() {
            RegisterTypicalSet();
            sut.Poll();

            sut.Invalidate("C");
            now += Tick;
            var result = sut.Poll();

            result.Keys.Should().BeEquivalentTo(new[] { "Status", "C" });
        }

        [Test]
        public void Invalidate_UnknownKey_IsIgnored() {
            RegisterTypicalSet();

            sut.Invalidate("NotRegistered");

            sut.Poll().Should().HaveCount(1 + SlowKeys.Length);
        }

        [Test]
        public void InvalidatedEntries_BypassTheBudget() {
            RegisterTypicalSet();
            sut.Poll();

            sut.Invalidate("A");
            sut.Invalidate("B");
            sut.Invalidate("C");
            now += Tick;
            var result = sut.Poll();

            result.Keys.Should().BeEquivalentTo(new[] { "Status", "A", "B", "C" });
        }

        [Test]
        public void InvalidateAll_ReadsEverythingOnNextPoll() {
            RegisterTypicalSet();
            sut.Poll();

            sut.InvalidateAll();
            now += Tick;

            sut.Poll().Should().HaveCount(1 + SlowKeys.Length);
        }

        [Test]
        public void ValueReadWhileInvalidated_IsDiscarded_AndReadAgainNextPoll() {
            var value = "old";
            Register("Status", TimeSpan.Zero);
            Register("A", SlowInterval, () => {
                // Simulates a write landing while this read is in flight
                var observed = value;
                if (readCounts["A"] == 1) {
                    value = "new";
                    sut.Invalidate("A");
                }
                return observed;
            });

            var first = sut.Poll();
            first.Should().NotContainKey("A");
            first.Should().ContainKey("Status");

            now += Tick;
            var second = sut.Poll();
            second["A"].Should().Be("new");
        }

        [Test]
        public void ReaderException_Propagates() {
            Register("Status", TimeSpan.Zero, () => throw new InvalidOperationException("mount gone"));

            Action act = () => sut.Poll();

            act.Should().Throw<InvalidOperationException>().WithMessage("mount gone");
        }

        [Test]
        public void SlowEntries_AreStaggered_SoNoLaterTickReadsMoreThanTheBudget() {
            RegisterTypicalSet();
            var lastRead = SlowKeys.ToDictionary(k => k, _ => now);
            var maxGap = TimeSpan.Zero;

            sut.Poll();
            for (int tick = 0; tick < 300; ++tick) {
                now += Tick;
                var result = sut.Poll();
                var slowRead = result.Keys.Where(k => k != "Status").ToList();

                result.Should().ContainKey("Status");
                slowRead.Should().HaveCountLessThanOrEqualTo(MountValuePoller.DefaultMaxScheduledReadsPerTick, $"tick {tick} read {string.Join(",", slowRead)}");
                foreach (var key in slowRead) {
                    var gap = now - lastRead[key];
                    if (gap > maxGap) {
                        maxGap = gap;
                    }
                    lastRead[key] = now;
                }
            }

            // Every slow entry is read about once per interval: jitter plus waiting behind the other slow entries
            var bound = TimeSpan.FromTicks((long)(SlowInterval.Ticks * (1 + MountValuePoller.JitterFraction))) + TimeSpan.FromTicks(Tick.Ticks * SlowKeys.Length);
            maxGap.Should().BeLessThanOrEqualTo(bound);
            foreach (var key in SlowKeys) {
                readCounts[key].Should().BeInRange(9, 12, $"{key} should be read about once a minute over 10 minutes");
            }
        }

        [Test]
        public void InitialStagger_SpreadsFirstScheduledReadsAcrossTheInterval() {
            RegisterTypicalSet();
            var start = now;
            sut.Poll();

            var firstReadAt = new Dictionary<string, TimeSpan>();
            while (firstReadAt.Count < SlowKeys.Length) {
                now += Tick;
                foreach (var key in sut.Poll().Keys.Where(k => k != "Status" && !firstReadAt.ContainsKey(k))) {
                    firstReadAt[key] = now - start;
                }
            }

            // 8 entries over 60 s => one due every 7.5 s
            firstReadAt.Values.Min().Should().BeLessThan(TimeSpan.FromSeconds(10));
            firstReadAt.Values.Max().Should().BeGreaterThan(TimeSpan.FromSeconds(55));
        }

        [Test]
        public void Budget_RollsOverdueEntriesForward_MostOverdueFirst() {
            RegisterTypicalSet();
            sut.Poll();

            // Skip well past every entry's due time, so all of them are overdue at once
            now += TimeSpan.FromMinutes(5);
            var order = new List<string>();
            for (int i = 0; i < SlowKeys.Length; ++i) {
                var slowRead = sut.Poll().Keys.Where(k => k != "Status").ToList();
                slowRead.Should().HaveCount(1);
                order.Add(slowRead.Single());
                now += TimeSpan.FromMilliseconds(1);
            }

            // Staggered in registration order, so they are overdue in registration order
            order.Should().Equal(SlowKeys);
        }

        [Test]
        public void Jitter_StaysWithinTenPercentOfTheInterval() {
            Register("A", SlowInterval);
            sut.Poll();

            // The single slow entry is staggered to a full interval, then jittered on every later read
            var lastRead = now;
            var gaps = new List<TimeSpan>();
            var step = TimeSpan.FromMilliseconds(100);
            while (gaps.Count < 50) {
                now += step;
                if (sut.Poll().ContainsKey("A")) {
                    gaps.Add(now - lastRead);
                    lastRead = now;
                }
            }

            gaps.Skip(1).Should().OnlyContain(g =>
                g >= TimeSpan.FromSeconds(54) && g <= TimeSpan.FromSeconds(66) + step);
            gaps.Skip(1).Distinct().Count().Should().BeGreaterThan(1, "the interval should vary between reads");
        }

        [Test]
        public void Register_DuplicateKey_Throws() {
            Register("A", SlowInterval);

            Action act = () => sut.Register("A", () => 1, SlowInterval);

            act.Should().Throw<ArgumentException>();
        }
    }
}
