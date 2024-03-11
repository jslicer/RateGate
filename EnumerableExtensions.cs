namespace CVV
{
    using System;
    using System.Collections.Generic;

    public static class EnumerableExtensions
    {
        /// <summary>
        /// Limits the rate at which the sequence is enumerated.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}"/> whose enumeration is to be rate limited.</param>
        /// <param name="count">The number of items in the sequence that are allowed to be processed per time
        /// unit.</param>
        /// <param name="timeUnit">Length of the time unit.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> containing the elements of the source sequence.</returns>
        public static IEnumerable<T> LimitRate<T>(this IEnumerable<T> source, int count, TimeSpan timeUnit)
        {
            return source is null ? throw new ArgumentNullException(nameof(source)) : _(source, count, timeUnit);

            static IEnumerable<T> _(IEnumerable<T> source2, int count2, TimeSpan timeUnit2)
            {
                using RateGate rateGate = new RateGate(count2, timeUnit2);
                foreach (T item in source2)
                {
                    rateGate.WaitToProceed();
                    yield return item;
                }
            }
        }
    }
}
