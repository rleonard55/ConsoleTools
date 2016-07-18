using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

#region Header
// Developer(s) : rgleonard
//                        Rob Leonard
// Modified : 04-21-2016 7:50 AM
// Created : 04-21-2016 7:50 AM
// Solution : Console Tools
// Project : Console Tools
// File : SpeedProfiler.cs
#endregion
namespace Console_Tools
{
    public static class SpeedProfile
    {
        private static double StdDevLogic(this IEnumerable<double> source, int buffer = 1)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            var data = source.ToList();
            var average = data.Average();
            var differences = data.Select(u => Math.Pow(average - u, 2.0)).ToList();
            return Math.Sqrt(differences.Sum()/(differences.Count() - buffer));
        }

        public class TimeTest
        {
            private TimeTest(Action action, TimeSpan timeSpan)
            {
                Action = action;
                TimeSpan = timeSpan;
            }

            internal TimeTest(Action action)
            {
                Action = action;
            }

            public Action Action { get; internal set; }
            public TimeSpan TimeSpan { get; internal set; }
            public Exception Exception { get; internal set; }

            public bool HasError
            {
                get { return Exception != null; }
            }

            /// <summary>
            /// Returns a string that represents the current object.
            /// </summary>
            /// <returns>
            /// A string that represents the current object.
            /// </returns>
            public override string ToString()
            {
                return TimeSpan.ToString();
            }
        }

        public static TimeSpan SpeedTest(Action action)
        {
            var sw = new Stopwatch();
            sw.Start();
            try
            {
                action();
                sw.Stop();
            }
            catch (Exception)
            {
                sw.Stop();
                throw;
            }
            return sw.Elapsed;
        }

        public static TimeTestResults GetResults(this IEnumerable<TimeTest> timeTests)
        {
            return new TimeTestResults(timeTests.ToList());
        }

        public class TimeTestResults
        {
            internal TimeTestResults(List<TimeTest> results)
            {
                RawResults = results;
                MaxMilliseconds = RawResults.Max(d => d.TimeSpan.TotalMilliseconds);
                MinMilliseconds = RawResults.Min(d => d.TimeSpan.TotalMilliseconds);
                AverageMilliseconds = RawResults.Average(d => d.TimeSpan.TotalMilliseconds);
                StandardDeviation = RawResults.Select(d => d.TimeSpan.TotalMilliseconds).StdDevLogic();
            }

            public List<TimeTest> RawResults { get; private set; }
            public double MaxMilliseconds { get; private set; }

            public double MinMilliseconds { get; private set; }
            public double AverageMilliseconds { get; private set; }

            public double? StandardDeviation { get; private set; }

            /// <summary>
            /// Returns a string that represents the current object.
            /// </summary>
            /// <returns>
            /// A string that represents the current object.
            /// </returns>
            public override string ToString()
            {
                return AverageMilliseconds.ToString();
            }
        }

        public static IEnumerable<TimeTest> SpeedTest(Action action, int iterations)
        {
            var tests = new TimeTest[iterations];
            for (var i = 0; i < iterations; i++)
            {
                var timeTest = new TimeTest(action);
                try
                {
                    timeTest.TimeSpan = SpeedTest(action);
                }
                catch (Exception e)
                {
                    timeTest.Exception = e;
                }
                tests[i] = timeTest;
            }
            return tests;
        }
    }
}
