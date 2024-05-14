// The MIT License(MIT)
// 
// Copyright (c) 2017 Hangfire OÜ
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cronos
{
    /// <summary>
    /// Provides a parser and scheduler for cron expressions.
    /// </summary>
    public sealed class CronExpression: IEquatable<CronExpression>
    {
        private const long NotFound = 0;
        private const int MaxYear = 2499;

        /// <summary>
        /// Represents a cron expression that fires on Jan 1st every year at midnight.
        /// Equals to "0 0 1 1 *".
        /// </summary>
        public static readonly CronExpression Yearly = Parse("0 0 1 1 *", CronFormat.Standard);

        /// <summary>
        /// Represents a cron expression that fires every Sunday at midnight.
        /// Equals to "0 0 * * 0".
        /// </summary>
        public static readonly CronExpression Weekly = Parse("0 0 * * 0", CronFormat.Standard);

        /// <summary>
        /// Represents a cron expression that fires on 1st day of every month at midnight.
        /// Equals to "0 0 1 * *".
        /// </summary>
        public static readonly CronExpression Monthly = Parse("0 0 1 * *", CronFormat.Standard);

        /// <summary>
        /// Represents a cron expression that fires every day at midnight.
        /// Equals to "0 0 * * *".
        /// </summary>
        public static readonly CronExpression Daily = Parse("0 0 * * *", CronFormat.Standard);

        /// <summary>
        /// Represents a cron expression that fires every hour at the beginning of the hour.
        /// Equals to "0 * * * *".
        /// </summary>
        public static readonly CronExpression Hourly = Parse("0 * * * *", CronFormat.Standard);

        /// <summary>
        /// Represents a cron expression that fires every minute.
        /// Equals to "* * * * *".
        /// </summary>
        public static readonly CronExpression EveryMinute = Parse("* * * * *", CronFormat.Standard);

        /// <summary>
        /// Represents a cron expression that fires every second.
        /// Equals to "* * * * * *". 
        /// </summary>
        public static readonly CronExpression EverySecond = Parse("* * * * * *", CronFormat.IncludeSeconds);

        private static readonly TimeZoneInfo UtcTimeZone = TimeZoneInfo.Utc;

        private static readonly int[] DeBruijnPositions =
        {
            0, 1, 2, 53, 3, 7, 54, 27,
            4, 38, 41, 8, 34, 55, 48, 28,
            62, 5, 39, 46, 44, 42, 22, 9,
            24, 35, 59, 56, 49, 18, 29, 11,
            63, 52, 6, 26, 37, 40, 33, 47,
            61, 45, 43, 21, 23, 58, 17, 10,
            51, 25, 36, 32, 60, 20, 57, 16,
            50, 31, 19, 15, 30, 14, 13, 12
        };

        public long  Second { get; }     // 60 bits -> from 0 bit to 59 bit
        public long  Minute { get; }      // 60 bits -> from 0 bit to 59 bit
        public int   Hour { get; }        // 24 bits -> from 0 bit to 23 bit
        public int   DayOfMonth { get; }  // 31 bits -> from 1 bit to 31 bit
        public short Month { get; }       // 12 bits -> from 1 bit to 12 bit
        public byte  DayOfWeek { get; }   // 8 bits  -> from 0 bit to 7 bit

        public byte  NthDayOfWeek { get; } 
        public byte  LastMonthOffset { get; }

        private readonly CronExpressionFlag _flags;

        internal CronExpression(
            long second,
            long minute,
            int hour,
            int dayOfMonth,
            short month,
            byte dayOfWeek,
            byte nthDayOfWeek,
            byte lastMonthOffset,
            CronExpressionFlag flags)
        {
            Second = second;
            Minute = minute;
            Hour = hour;
            DayOfMonth = dayOfMonth;
            Month = month;
            DayOfWeek = dayOfWeek;
            NthDayOfWeek = nthDayOfWeek;
            LastMonthOffset = lastMonthOffset;
            _flags = flags;
        }

        ///<summary>
        /// Constructs a new <see cref="CronExpression"/> based on the specified
        /// cron expression. It's supported expressions consisting of 5 fields:
        /// minute, hour, day of month, month, day of week. 
        /// If you want to parse non-standard cron expressions use <see cref="Parse(string, CronFormat)"/> with specified CronFields argument.
        /// See more: <a href="https://github.com/HangfireIO/Cronos">https://github.com/HangfireIO/Cronos</a>
        /// </summary>
        public static CronExpression Parse(string expression)
        {
            return Parse(expression, CronFormat.Standard);
        }

        ///<summary>
        /// Constructs a new <see cref="CronExpression"/> based on the specified
        /// cron expression. It's supported expressions consisting of 5 or 6 fields:
        /// second (optional), minute, hour, day of month, month, day of week. 
        /// See more: <a href="https://github.com/HangfireIO/Cronos">https://github.com/HangfireIO/Cronos</a>
        /// </summary>
        public static CronExpression Parse(string expression, CronFormat format)
        {
            if (string.IsNullOrEmpty(expression)) throw new ArgumentNullException(nameof(expression));

            return CronParser.Parse(expression, format);
        }

        /// <summary>
        /// Constructs a new <see cref="CronExpression"/> based on the specified cron expression with the
        /// <see cref="CronFormat.Standard"/> format.
        /// A return value indicates whether the operation succeeded.
        /// </summary>
        public static bool TryParse(string expression, out CronExpression cronExpression)
        {
            return TryParse(expression, CronFormat.Standard, out cronExpression);
        }

        /// <summary>
        /// Constructs a new <see cref="CronExpression"/> based on the specified cron expression with the specified
        /// <paramref name="format"/>.
        /// A return value indicates whether the operation succeeded.
        /// </summary>
        public static bool TryParse(string expression, CronFormat format, out CronExpression cronExpression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            try
            {
                cronExpression = Parse(expression, format);
                return true;
            }
            catch (CronFormatException)
            {
                cronExpression = null;
                return false;
            }
        }

        /// <summary>
        /// Calculates next occurrence starting with <paramref name="fromUtc"/> (optionally <paramref name="inclusive"/>) in UTC time zone.
        /// </summary>
        /// <exception cref="ArgumentException"/>
        public DateTime? GetNextOccurrence(DateTime fromUtc, bool inclusive = false)
        {
            if (fromUtc.Kind != DateTimeKind.Utc) ThrowWrongDateTimeKindException(nameof(fromUtc));
            if (fromUtc.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(fromUtc));

            var found = FindOccurrence(fromUtc.Ticks, inclusive);
            if (found == NotFound) return null;

            return new DateTime(found, DateTimeKind.Utc);
        }

        /// <summary>
        /// Returns the list of next occurrences within the given date/time range,
        /// including <paramref name="fromUtc"/> and excluding <paramref name="toUtc"/>
        /// by default, and UTC time zone. When none of the occurrences found, an 
        /// empty list is returned.
        /// </summary>
        /// <exception cref="ArgumentException"/>
        public IEnumerable<DateTime> GetOccurrences(
            DateTime fromUtc,
            DateTime toUtc,
            bool fromInclusive = true,
            bool toInclusive = false)
        {
            if (fromUtc > toUtc) ThrowFromShouldBeLessThanToException(nameof(fromUtc), nameof(toUtc));
            if (fromUtc.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(fromUtc));
            if (toUtc.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(toUtc));

            for (var occurrence = GetNextOccurrence(fromUtc, fromInclusive);
                occurrence < toUtc || occurrence == toUtc && toInclusive;
                // ReSharper disable once RedundantArgumentDefaultValue
                // ReSharper disable once ArgumentsStyleLiteral
                occurrence = GetNextOccurrence(occurrence.Value, inclusive: false))
            {
                yield return occurrence.Value;
            }
        }

        /// <summary>
        /// Calculates next occurrence starting with <paramref name="fromUtc"/> (optionally <paramref name="inclusive"/>) in given <paramref name="zone"/>
        /// </summary>
        /// <exception cref="ArgumentException"/>
        public DateTime? GetNextOccurrence(DateTime fromUtc, TimeZoneInfo zone, bool inclusive = false)
        {
            if (fromUtc.Kind != DateTimeKind.Utc) ThrowWrongDateTimeKindException(nameof(fromUtc));
            if (fromUtc.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(fromUtc));
            if (zone == null) ThrowArgumentNullException(nameof(zone));

            if (ReferenceEquals(zone, UtcTimeZone))
            {
                var found = FindOccurrence(fromUtc.Ticks, inclusive);
                if (found == NotFound) return null;

                return new DateTime(found, DateTimeKind.Utc);
            }

            var fromOffset = new DateTimeOffset(fromUtc);

#pragma warning disable CA1062
            var occurrence = GetOccurrenceConsideringTimeZone(fromOffset, zone, inclusive);
#pragma warning restore CA1062

            return occurrence?.UtcDateTime;
        }

        /// <summary>
        /// Returns the list of next occurrences within the given date/time range, including
        /// <paramref name="fromUtc"/> and excluding <paramref name="toUtc"/> by default, and 
        /// specified time zone. When none of the occurrences found, an empty list is returned.
        /// </summary>
        /// <exception cref="ArgumentException"/>
        public IEnumerable<DateTime> GetOccurrences(
            DateTime fromUtc,
            DateTime toUtc,
            TimeZoneInfo zone,
            bool fromInclusive = true,
            bool toInclusive = false)
        {
            if (fromUtc > toUtc) ThrowFromShouldBeLessThanToException(nameof(fromUtc), nameof(toUtc));
            if (fromUtc.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(fromUtc));
            if (toUtc.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(toUtc));

            for (var occurrence = GetNextOccurrence(fromUtc, zone, fromInclusive);
                occurrence < toUtc || occurrence == toUtc && toInclusive;
                // ReSharper disable once RedundantArgumentDefaultValue
                // ReSharper disable once ArgumentsStyleLiteral
                occurrence = GetNextOccurrence(occurrence.Value, zone, inclusive: false))
            {
                yield return occurrence.Value;
            }
        }

        /// <summary>
        /// Calculates next occurrence starting with <paramref name="from"/> (optionally <paramref name="inclusive"/>) in given <paramref name="zone"/>
        /// </summary>
        /// <exception cref="ArgumentException"/>
        public DateTimeOffset? GetNextOccurrence(DateTimeOffset from, TimeZoneInfo zone, bool inclusive = false)
        {
            if (from.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(from));
            if (zone == null) ThrowArgumentNullException(nameof(zone));

            if (ReferenceEquals(zone, UtcTimeZone))
            {
                var found = FindOccurrence(from.UtcTicks, inclusive);
                if (found == NotFound) return null;

                return new DateTimeOffset(found, TimeSpan.Zero);
            }

#pragma warning disable CA1062
            return GetOccurrenceConsideringTimeZone(from, zone, inclusive);
#pragma warning restore CA1062
        }

        /// <summary>
        /// Returns the list of occurrences within the given date/time offset range,
        /// including <paramref name="from"/> and excluding <paramref name="to"/> by
        /// default. When none of the occurrences found, an empty list is returned.
        /// </summary>
        /// <exception cref="ArgumentException"/>
        public IEnumerable<DateTimeOffset> GetOccurrences(
            DateTimeOffset from,
            DateTimeOffset to,
            TimeZoneInfo zone,
            bool fromInclusive = true,
            bool toInclusive = false)
        {
            if (from > to) ThrowFromShouldBeLessThanToException(nameof(from), nameof(to));
            if (from.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(from));
            if (to.Year > MaxYear) ThrowDateTimeExceedsMaxException(nameof(to));

            for (var occurrence = GetNextOccurrence(from, zone, fromInclusive);
                occurrence < to || occurrence == to && toInclusive;
                // ReSharper disable once RedundantArgumentDefaultValue
                // ReSharper disable once ArgumentsStyleLiteral
                occurrence = GetNextOccurrence(occurrence.Value, zone, inclusive: false))
            {
                yield return occurrence.Value;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var expressionBuilder = new StringBuilder();

            if (Second != 1L)
            {
                AppendFieldValue(expressionBuilder, CronField.Seconds, Second).Append(' ');
            }

            AppendFieldValue(expressionBuilder, CronField.Minutes, Minute).Append(' ');
            AppendFieldValue(expressionBuilder, CronField.Hours, Hour).Append(' ');
            AppendDayOfMonth(expressionBuilder, DayOfMonth).Append(' ');
            AppendFieldValue(expressionBuilder, CronField.Months, Month).Append(' ');
            AppendDayOfWeek(expressionBuilder, DayOfWeek);

            return expressionBuilder.ToString();
        }

        /// <summary>
        /// Determines whether the specified <see cref="Object"/> is equal to the current <see cref="Object"/>.
        /// </summary>
        /// <param name="other">The <see cref="Object"/> to compare with the current <see cref="Object"/>.</param>
        /// <returns>
        /// <c>true</c> if the specified <see cref="Object"/> is equal to the current <see cref="Object"/>; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(CronExpression other)
        {
            if (other == null) return false;

            return Second == other.Second &&
                   Minute == other.Minute &&
                   Hour == other.Hour &&
                   DayOfMonth == other.DayOfMonth &&
                   Month == other.Month &&
                   DayOfWeek == other.DayOfWeek &&
                   NthDayOfWeek == other.NthDayOfWeek &&
                   LastMonthOffset == other.LastMonthOffset &&
                   _flags == other._flags;
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" /> is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        /// <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance;
        /// otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj) => Equals(obj as CronExpression);

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data
        /// structures like a hash table. 
        /// </returns>
        [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Second.GetHashCode();
                hashCode = (hashCode * 397) ^ Minute.GetHashCode();
                hashCode = (hashCode * 397) ^ Hour;
                hashCode = (hashCode * 397) ^ DayOfMonth;
                hashCode = (hashCode * 397) ^ Month.GetHashCode();
                hashCode = (hashCode * 397) ^ DayOfWeek.GetHashCode();
                hashCode = (hashCode * 397) ^ NthDayOfWeek.GetHashCode();
                hashCode = (hashCode * 397) ^ LastMonthOffset.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)_flags;

                return hashCode;
            }
        }

        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        public static bool operator ==(CronExpression left, CronExpression right) => Equals(left, right);

        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        public static bool operator !=(CronExpression left, CronExpression right) => !Equals(left, right);

        private DateTimeOffset? GetOccurrenceConsideringTimeZone(DateTimeOffset fromUtc, TimeZoneInfo zone, bool inclusive)
        {
            if (!DateTimeHelper.IsRound(fromUtc))
            {
                // Rarely, if fromUtc is very close to DST transition, `TimeZoneInfo.ConvertTime` may not convert it correctly on Windows.
                // E.g., In Jordan Time DST started 2017-03-31 00:00 local time. Clocks jump forward from `2017-03-31 00:00 +02:00` to `2017-03-31 01:00 +3:00`.
                // But `2017-03-30 23:59:59.9999000 +02:00` will be converted to `2017-03-31 00:59:59.9999000 +03:00` instead of `2017-03-30 23:59:59.9999000 +02:00` on Windows.
                // It can lead to skipped occurrences. To avoid such errors we floor fromUtc to seconds:
                // `2017-03-30 23:59:59.9999000 +02:00` will be floored to `2017-03-30 23:59:59.0000000 +02:00` and will be converted to `2017-03-30 23:59:59.0000000 +02:00`.
                fromUtc = DateTimeHelper.FloorToSeconds(fromUtc);
                inclusive = false;
            }

            var from = TimeZoneInfo.ConvertTime(fromUtc, zone);

            var fromLocal = from.DateTime;

            if (TimeZoneHelper.IsAmbiguousTime(zone, fromLocal))
            {
                var currentOffset = from.Offset;
                var standardOffset = zone.GetUtcOffset(fromLocal);
               
                if (standardOffset != currentOffset)
                {
                    var daylightOffset = TimeZoneHelper.GetDaylightOffset(zone, fromLocal);
                    var daylightTimeLocalEnd = TimeZoneHelper.GetDaylightTimeEnd(zone, fromLocal, daylightOffset).DateTime;

                    // Early period, try to find anything here.
                    var foundInDaylightOffset = FindOccurrence(fromLocal.Ticks, daylightTimeLocalEnd.Ticks, inclusive);
                    if (foundInDaylightOffset != NotFound) return new DateTimeOffset(foundInDaylightOffset, daylightOffset);

                    fromLocal = TimeZoneHelper.GetStandardTimeStart(zone, fromLocal, daylightOffset).DateTime;
                    inclusive = true;
                }

                // Skip late ambiguous interval.
                var ambiguousIntervalLocalEnd = TimeZoneHelper.GetAmbiguousIntervalEnd(zone, fromLocal).DateTime;

                if (HasFlag(CronExpressionFlag.Interval))
                {
                    var foundInStandardOffset = FindOccurrence(fromLocal.Ticks, ambiguousIntervalLocalEnd.Ticks - 1, inclusive);
                    if (foundInStandardOffset != NotFound) return new DateTimeOffset(foundInStandardOffset, standardOffset);
                }

                fromLocal = ambiguousIntervalLocalEnd;
                inclusive = true;
            }

            var occurrenceTicks = FindOccurrence(fromLocal.Ticks, inclusive);
            if (occurrenceTicks == NotFound) return null;

            var occurrence = new DateTime(occurrenceTicks, DateTimeKind.Unspecified);

            if (zone.IsInvalidTime(occurrence))
            {
                var nextValidTime = TimeZoneHelper.GetDaylightTimeStart(zone, occurrence);
                return nextValidTime;
            }

            if (TimeZoneHelper.IsAmbiguousTime(zone, occurrence))
            {
                var daylightOffset = TimeZoneHelper.GetDaylightOffset(zone, occurrence);
                return new DateTimeOffset(occurrence, daylightOffset);
            }

            return new DateTimeOffset(occurrence, zone.GetUtcOffset(occurrence));
        }

        private long FindOccurrence(long startTimeTicks, long endTimeTicks, bool startInclusive)
        {
            var found = FindOccurrence(startTimeTicks, startInclusive);

            if (found == NotFound || found > endTimeTicks) return NotFound;
            return found;
        }

        private long FindOccurrence(long ticks, bool startInclusive)
        {
            if (!startInclusive) ticks++;

            CalendarHelper.FillDateTimeParts(
                ticks,
                out int startSecond,
                out int startMinute,
                out int startHour,
                out int startDay,
                out int startMonth,
                out int startYear);

            var minMatchedDay = GetFirstSet(DayOfMonth);

            var second = startSecond;
            var minute = startMinute;
            var hour = startHour;
            var day = startDay;
            var month = startMonth;
            var year = startYear;

            if (!GetBit(Second, second) && !Move(Second, ref second)) minute++;
            if (!GetBit(Minute, minute) && !Move(Minute, ref minute)) hour++;
            if (!GetBit(Hour, hour) && !Move(Hour, ref hour)) day++;

            // If NearestWeekday flag is set it's possible forward shift.
            if (HasFlag(CronExpressionFlag.NearestWeekday)) day = CronField.DaysOfMonth.First;

            if (!GetBit(DayOfMonth, day) && !Move(DayOfMonth, ref day)) goto RetryMonth;
            if (!GetBit(Month, month)) goto RetryMonth;

            Retry:

            if (day > GetLastDayOfMonth(year, month)) goto RetryMonth;

            if (HasFlag(CronExpressionFlag.DayOfMonthLast)) day = GetLastDayOfMonth(year, month);

            var lastCheckedDay = day;

            if (HasFlag(CronExpressionFlag.NearestWeekday)) day = CalendarHelper.MoveToNearestWeekDay(year, month, day);

            if (IsDayOfWeekMatch(year, month, day))
            {
                if (CalendarHelper.IsGreaterThan(year, month, day, startYear, startMonth, startDay)) goto RolloverDay;
                if (hour > startHour) goto RolloverHour;
                if (minute > startMinute) goto RolloverMinute;
                goto ReturnResult;

                RolloverDay: hour = GetFirstSet(Hour);
                RolloverHour: minute = GetFirstSet(Minute);
                RolloverMinute: second = GetFirstSet(Second);

                ReturnResult:

                var found = CalendarHelper.DateTimeToTicks(year, month, day, hour, minute, second);
                if (found >= ticks) return found;
            }

            day = lastCheckedDay;
            if (Move(DayOfMonth, ref day)) goto Retry;

            RetryMonth:

            if (!Move(Month, ref month) && ++year >= MaxYear) return NotFound;
            day = minMatchedDay;

            goto Retry;
        }

        private static bool Move(long fieldBits, ref int fieldValue)
        {
            if (fieldBits >> ++fieldValue == 0)
            {
                fieldValue = GetFirstSet(fieldBits);
                return false;
            }

            fieldValue += GetFirstSet(fieldBits >> fieldValue);
            return true;
        }

        private int GetLastDayOfMonth(int year, int month)
        {
            return CalendarHelper.GetDaysInMonth(year, month) - LastMonthOffset;
        }

        private bool IsDayOfWeekMatch(int year, int month, int day)
        {
            if (HasFlag(CronExpressionFlag.DayOfWeekLast) && !CalendarHelper.IsLastDayOfWeek(year, month, day) ||
                HasFlag(CronExpressionFlag.NthDayOfWeek) && !CalendarHelper.IsNthDayOfWeek(day, NthDayOfWeek))
            {
                return false;
            }

            if (DayOfWeek == CronField.DaysOfWeek.AllBits) return true;

            var dayOfWeek = CalendarHelper.GetDayOfWeek(year, month, day);

            return ((DayOfWeek >> (int)dayOfWeek) & 1) != 0;
        }

        private static int GetFirstSet(long value)
        {
            // TODO: Add description and source
            ulong res = unchecked((ulong)(value & -value) * 0x022fdd63cc95386d) >> 58;
            return DeBruijnPositions[res];
        }

        private bool HasFlag(CronExpressionFlag value)
        {
            return (_flags & value) != 0;
        }

        private static StringBuilder AppendFieldValue(StringBuilder expressionBuilder, CronField field, long fieldValue)
        {
            if (field.AllBits == fieldValue) return expressionBuilder.Append('*');

            // Unset 7 bit for Day of week field because both 0 and 7 stand for Sunday.
            if (field == CronField.DaysOfWeek) fieldValue &= ~(1 << field.Last);

            for (var i = GetFirstSet(fieldValue);; i = GetFirstSet(fieldValue >> i << i))
            {
                expressionBuilder.Append(i);
                if (fieldValue >> ++i == 0) break;
                expressionBuilder.Append(',');
            }

            return expressionBuilder;
        }

        private StringBuilder AppendDayOfMonth(StringBuilder expressionBuilder, int domValue)
        {
            if (HasFlag(CronExpressionFlag.DayOfMonthLast))
            {
                expressionBuilder.Append('L');
                if (LastMonthOffset != 0) expressionBuilder.Append($"-{LastMonthOffset}");
            }
            else
            {
                AppendFieldValue(expressionBuilder, CronField.DaysOfMonth, (uint)domValue);
            }

            if (HasFlag(CronExpressionFlag.NearestWeekday)) expressionBuilder.Append('W');

            return expressionBuilder;
        }

        private void AppendDayOfWeek(StringBuilder expressionBuilder, int dowValue)
        {
            AppendFieldValue(expressionBuilder, CronField.DaysOfWeek, dowValue);

            if (HasFlag(CronExpressionFlag.DayOfWeekLast)) expressionBuilder.Append('L');
            else if (HasFlag(CronExpressionFlag.NthDayOfWeek)) expressionBuilder.Append($"#{NthDayOfWeek}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowFromShouldBeLessThanToException(string fromName, string toName)
        {
            throw new ArgumentException($"The value of the {fromName} argument should be less than the value of the {toName} argument.", fromName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowWrongDateTimeKindException(string paramName)
        {
            throw new ArgumentException("The supplied DateTime must have the Kind property set to Utc", paramName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowDateTimeExceedsMaxException(string paramName)
        {
            throw new ArgumentException($"The supplied DateTime is after the supported year of {MaxYear}.", paramName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentNullException(string paramName)
        {
            throw new ArgumentNullException(paramName);
        }

        private static bool GetBit(long value, int index)
        {
            return (value & (1L << index)) != 0;
        }
    }
}