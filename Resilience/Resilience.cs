// Resilience.cs
// One-file resilience helpers: Retry (exp backoff + jitter), Timeout, CircuitBreaker.
// Author: Asim Faiaz
// License: MIT
// #nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Demo.Resilience
{
    public sealed class RetryOptions
    {
        public int MaxRetries { get; init; } = 5;
        public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);
        public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);
        public double JitterRatio { get; init; } = 0.25;
        public Func<Exception, bool>? ShouldRetryOn { get; init; } = null;
        public Action<int, TimeSpan, Exception>? OnRetry { get; init; } = null;
    }

    public sealed class TimeoutOptions
    {
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
        public Action<TimeSpan>? OnTimeout { get; init; } = null;
    }

    public sealed class CircuitBreakerOptions
    {
        public int FailuresBeforeOpen { get; init; } = 5;
        public TimeSpan OpenInterval { get; init; } = TimeSpan.FromSeconds(30);
        public TimeSpan HalfOpenProbeInterval { get; init; } = TimeSpan.FromSeconds(5);
        public Action<string>? OnStateChange { get; init; } = null;
    }

    internal enum CircuitState { Closed, Open, HalfOpen }

    public sealed class CircuitBreaker
    {
        private readonly CircuitBreakerOptions _opt;
        private int _consecutiveFailures = 0;
        private volatile CircuitState _state = CircuitState.Closed;
        private long _nextTransitionTicks = 0;

        public CircuitBreaker(CircuitBreakerOptions? options = null)
        {
            _opt = options ?? new CircuitBreakerOptions();
        }

        public bool IsClosed => _state == CircuitState.Closed;

        private void Transition(CircuitState newState)
        {
            var old = _state;
            _state = newState;
            if (newState == CircuitState.Open)
                _nextTransitionTicks = DateTime.UtcNow.Add(_opt.OpenInterval).Ticks;
            else if (newState == CircuitState.HalfOpen)
                _nextTransitionTicks = DateTime.UtcNow.Add(_opt.HalfOpenProbeInterval).Ticks;

            _opt.OnStateChange?.Invoke($"{old} -> {newState}");
        }

        public void OnSuccess()
        {
            _consecutiveFailures = 0;
            if (_state != CircuitState.Closed)
                Transition(CircuitState.Closed);
        }

        public void OnFailure()
        {
            var fails = Interlocked.Increment(ref _consecutiveFailures);
            if (_state == CircuitState.Closed && fails >= _opt.FailuresBeforeOpen)
                Transition(CircuitState.Open);
        }

        private bool CanProbeHalfOpen()
            => DateTime.UtcNow.Ticks >= Interlocked.Read(ref _nextTransitionTicks);

        public void ThrowIfOpen()
        {
            var s = _state;
            if (s == CircuitState.Open && !CanProbeHalfOpen())
                throw new CircuitOpenException("Circuit is open; calls are short-circuited.");

            if (s == CircuitState.Open && CanProbeHalfOpen())
                Transition(CircuitState.HalfOpen);
        }
    }

    public sealed class CircuitOpenException : Exception
    {
        public CircuitOpenException(string message) : base(message) { }
    }

    public static class Resilience
    {
        private static readonly Random _rng = new();

        public static async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            RetryOptions? retry = null,
            TimeoutOptions? timeout = null,
            CircuitBreaker? circuit = null,
            CancellationToken cancellationToken = default)
        {
            retry ??= new RetryOptions();
            timeout ??= new TimeoutOptions();

            int attempt = 0;
            Exception? last = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                circuit?.ThrowIfOpen();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout.Timeout);
                var sw = Stopwatch.StartNew();

                try
                {
                    var result = await action(cts.Token).ConfigureAwait(false);
                    sw.Stop();
                    circuit?.OnSuccess();
                    return result;
                }
                catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested && cts.IsCancellationRequested)
                {
                    sw.Stop();
                    timeout.OnTimeout?.Invoke(timeout.Timeout);
                    last = new TimeoutException($"Operation exceeded timeout of {timeout.Timeout}.", oce);
                    circuit?.OnFailure();
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    last = ex;
                    circuit?.OnFailure();
                }
				
                attempt++;
                if (attempt > retry.MaxRetries)
                    throw last!;

                if (retry.ShouldRetryOn is not null && last is not null && !retry.ShouldRetryOn(last))
                    throw last;

                var delay = ComputeBackoff(attempt, retry.BaseDelay, retry.MaxDelay, retry.JitterRatio);
                retry.OnRetry?.Invoke(attempt, delay, last!);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            RetryOptions? retry = null,
            TimeoutOptions? timeout = null,
            CircuitBreaker? circuit = null,
            CancellationToken cancellationToken = default)
            => await ExecuteAsync(async ct =>
            {
                await action(ct).ConfigureAwait(false);
                return true;
            },
                                  retry, timeout, circuit, cancellationToken).ConfigureAwait(false);

        private static TimeSpan ComputeBackoff(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, double jitterRatio)
        {
            var expMs = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            var capped = Math.Min(expMs, maxDelay.TotalMilliseconds);
            var jitter = jitterRatio <= 0 ? 0 : _rng.NextDouble() * jitterRatio * capped;
            return TimeSpan.FromMilliseconds(Math.Max(0, capped + jitter));
        }
    }
}
