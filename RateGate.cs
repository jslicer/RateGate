namespace CVV
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;

    /// <summary>
    /// Used to control the rate of some occurrence per unit of time.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     To control the rate of an action using a <see cref="RateGate" />,
    ///     code should simply call <see cref="WaitToProceed()" /> prior to
    ///     performing the action. <see cref="WaitToProceed()" /> will block
    ///     the current thread until the action is allowed based on the rate
    ///     limit.
    ///     </para>
    ///     <para>
    ///     This class is thread safe. A single <see cref="RateGate" /> instance
    ///     may be used to control the rate of an occurrence across multiple
    ///     threads.
    ///     </para>
    /// </remarks>
    public sealed class RateGate : Disposable, IRateGate
    {
        // Semaphore used to count and limit the number of occurrences per
        // unit time.
#pragma warning disable CA2213
        private readonly SemaphoreSlim _semaphore;
#pragma warning restore CA2213

        // Times (in millisecond ticks) at which the semaphore should be exited.
        private readonly ConcurrentQueue<int> _exitTimes;

        // Timer used to trigger exiting the semaphore.
#pragma warning disable CA2213
        private readonly Timer _exitTimer;
#pragma warning restore CA2213

        /// <summary>
        /// Number of occurrences allowed per unit of time.
        /// </summary>
        public int Occurrences { get; }

        /// <summary>
        /// The length of the time unit, in milliseconds.
        /// </summary>
        public int TimeUnitMilliseconds { get; }

        /// <summary>
        /// Initializes a <see cref="RateGate"/> with a rate of <paramref name="occurrences" />
        /// per <paramref name="timeUnit"/>.
        /// </summary>
        /// <param name="occurrences">Number of occurrences allowed per unit of time.</param>
        /// <param name="timeUnit">Length of the time unit.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// If <paramref name="occurrences"/> or <paramref name="timeUnit"/> is negative.
        /// </exception>
        public RateGate(int occurrences, TimeSpan timeUnit)
        {
            // Check the arguments.
            if (occurrences <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(occurrences),
                    "Number of occurrences must be a positive integer");
            }

            if (timeUnit != timeUnit.Duration())
            {
                throw new ArgumentOutOfRangeException(nameof(timeUnit), "Time unit must be a positive span of time");
            }

            if (timeUnit > TimeSpan.FromMilliseconds(int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeUnit),
                    $"Time unit must be less than {int.MaxValue} milliseconds");
            }

            this.Occurrences = occurrences;
            this.TimeUnitMilliseconds = (int)timeUnit.TotalMilliseconds;

            // Create the semaphore, with the number of occurrences as the maximum count.
            this._semaphore = new SemaphoreSlim(this.Occurrences, this.Occurrences);

            // Create a queue to hold the semaphore exit times.
            this._exitTimes = new ConcurrentQueue<int>();

            // Create a timer to exit the semaphore. Use the time unit as the original interval length because that's
            // the earliest we will need to exit the semaphore.
            this._exitTimer = new Timer(this.ExitTimerCallback, null, this.TimeUnitMilliseconds, Timeout.Infinite);
        }

        // Callback for the exit timer that exits the semaphore based on exit times in the queue and then sets the
        // timer for the next exit time.
        private void ExitTimerCallback(object state)
        {
            lock (this.NoDisposeWhileLocked)
            {
                // While there are exit times that are passed due still in the queue, exit the semaphore and dequeue
                // the exit time.
                int exitTime;

                // ReSharper disable once ComplexConditionExpression
                while (this._exitTimes.TryPeek(out exitTime) && (unchecked(exitTime - Environment.TickCount) <= 0))
                {
                    _ = this._semaphore.Release();
                    _ = this._exitTimes.TryDequeue(out exitTime);
                }

                // Try to get the next exit time from the queue and compute the time until the next check should take
                // place. If the queue is empty, then no exit times will occur until at least one time unit has passed.
                int timeUntilNextCheck = this._exitTimes.TryPeek(out exitTime)
                    ? unchecked(exitTime - Environment.TickCount)
                    : this.TimeUnitMilliseconds;

                // Set the timer.
                _ = this._exitTimer.Change(timeUntilNextCheck, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Blocks the current thread until allowed to proceed or until the specified timeout elapses.
        /// </summary>
        /// <param name="millisecondsTimeout">Number of milliseconds to wait, or Timeout.Infinite to wait
        /// indefinitely.</param>
        /// <returns>true if the thread is allowed to proceed, or false if timed out</returns>
        public bool WaitToProceed(int millisecondsTimeout)
        {
            // Check the arguments.
            // ReSharper disable once ComplexConditionExpression
            if (millisecondsTimeout < 0 && millisecondsTimeout != Timeout.Infinite)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));
            }

            _ = this.AssertSafe();

            // Block until we can enter the semaphore or until the timeout expires.
            bool entered = this._semaphore.Wait(millisecondsTimeout);

            // If we entered the semaphore, compute the corresponding exit time and add it to the queue.
            if (!entered)
            {
                return false;
            }

            int timeToExit = unchecked(Environment.TickCount + this.TimeUnitMilliseconds);

            this._exitTimes.Enqueue(timeToExit);
            return true;
        }

        /// <summary>
        /// Blocks the current thread until allowed to proceed or until the
        /// specified timeout elapses.
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns>true if the thread is allowed to proceed, or false if timed out</returns>
        public bool WaitToProceed(TimeSpan timeout) => this.WaitToProceed((int)timeout.TotalMilliseconds);

        /// <summary>
        /// Blocks the current thread indefinitely until allowed to proceed.
        /// </summary>
        public void WaitToProceed() => this.WaitToProceed(Timeout.Infinite);

        /// <summary>
        /// Releases resources held by an instance of this class.
        /// </summary>
        protected override void CleanUpResources()
        {
            try
            {
                // The semaphore and timer both implement IDisposable and therefore must be disposed.
                this._semaphore.Dispose();
                this._exitTimer.Dispose();
            }
            finally
            {
                base.CleanUpResources();
            }
        }
    }
}
