#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugin.TenMicron.Equipment {

    /// <summary>
    /// Decides which mount values to read on each polling tick, so that the single-threaded mount firmware isn't
    /// asked for every value on every tick. Entries with a zero interval are read every tick. Slower entries are
    /// staggered across their interval, jittered on every reschedule, and limited to a per-tick budget so that no
    /// single tick reads them all at once. Invalidated entries are read on the next tick regardless of the budget.
    /// </summary>
    public class MountValuePoller {
        public const int DefaultMaxScheduledReadsPerTick = 1;
        public const double JitterFraction = 0.1;

        private class Entry {
            public string Key;
            public Func<object> Read;
            public TimeSpan Interval;
            public DateTime NextDue;
            public bool Invalidated;
            public long Generation;
        }

        private readonly object lockObj = new object();
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Func<DateTime> clock;
        private readonly Random random;
        private readonly int maxScheduledReadsPerTick;
        private bool initialReadDone = false;

        public MountValuePoller() : this(() => DateTime.UtcNow, new Random(), DefaultMaxScheduledReadsPerTick) {
        }

        public MountValuePoller(Func<DateTime> clock, Random random, int maxScheduledReadsPerTick) {
            if (maxScheduledReadsPerTick < 1) {
                throw new ArgumentOutOfRangeException(nameof(maxScheduledReadsPerTick), "Must be at least 1");
            }
            this.clock = clock;
            this.random = random;
            this.maxScheduledReadsPerTick = maxScheduledReadsPerTick;
        }

        public void Register(string key, Func<object> read, TimeSpan interval) {
            if (interval < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(interval), "Must not be negative");
            }
            lock (lockObj) {
                if (entries.Any(e => e.Key == key)) {
                    throw new ArgumentException($"{key} is already registered", nameof(key));
                }
                entries.Add(new Entry() { Key = key, Read = read, Interval = interval });
            }
        }

        /// <summary>
        /// Marks a value as changed, so it is read on the next tick. A read of that value already in flight is discarded,
        /// since it may have been taken before the change.
        /// </summary>
        public void Invalidate(string key) {
            lock (lockObj) {
                var entry = entries.FirstOrDefault(e => e.Key == key);
                if (entry != null) {
                    entry.Invalidated = true;
                    ++entry.Generation;
                }
            }
        }

        public void InvalidateAll() {
            lock (lockObj) {
                foreach (var entry in entries) {
                    entry.Invalidated = true;
                    ++entry.Generation;
                }
            }
        }

        /// <summary>
        /// Reads the values due this tick and returns only those. The first call reads everything. Exceptions from a
        /// reader propagate to the caller.
        /// </summary>
        public Dictionary<string, object> Poll() {
            List<(Entry Entry, long Generation)> toRead;
            bool isInitialRead;
            lock (lockObj) {
                isInitialRead = !initialReadDone;
                toRead = SelectEntriesToRead(clock())
                    .Select(e => (e, e.Generation))
                    .ToList();
                foreach (var (entry, _) in toRead) {
                    entry.Invalidated = false;
                }
            }

            // Read outside the lock. Each read is a round trip to the mount, and Invalidate must not wait on it
            var values = new List<object>(toRead.Count);
            foreach (var (entry, _) in toRead) {
                values.Add(entry.Read());
            }

            var result = new Dictionary<string, object>();
            lock (lockObj) {
                var now = clock();
                var slowEntries = entries.Where(e => e.Interval > TimeSpan.Zero).ToList();
                for (int i = 0; i < toRead.Count; ++i) {
                    var (entry, generation) = toRead[i];
                    if (entry.Generation != generation) {
                        // Invalidated while the read was in flight. Leave it invalidated so it's read again next tick
                        continue;
                    }

                    result[entry.Key] = values[i];
                    if (isInitialRead && entry.Interval > TimeSpan.Zero) {
                        // Stagger the slow entries evenly across their interval, so they don't stay in lockstep
                        var slot = slowEntries.IndexOf(entry);
                        entry.NextDue = now + TimeSpan.FromTicks(entry.Interval.Ticks * (slot + 1) / slowEntries.Count);
                    } else {
                        var jitter = 1.0 + (random.NextDouble() * 2.0 - 1.0) * JitterFraction;
                        entry.NextDue = now + TimeSpan.FromTicks((long)(entry.Interval.Ticks * jitter));
                    }
                }
                initialReadDone = true;
            }
            return result;
        }

        private IEnumerable<Entry> SelectEntriesToRead(DateTime now) {
            if (!initialReadDone) {
                return entries.ToList();
            }

            var everyTick = entries.Where(e => e.Interval == TimeSpan.Zero);
            var invalidated = entries.Where(e => e.Interval > TimeSpan.Zero && e.Invalidated);
            var scheduled = entries
                .Where(e => e.Interval > TimeSpan.Zero && !e.Invalidated && e.NextDue <= now)
                .OrderBy(e => e.NextDue)
                .Take(maxScheduledReadsPerTick);
            return everyTick.Concat(invalidated).Concat(scheduled).ToList();
        }
    }
}
