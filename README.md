<section id="resilience-overview">
  <h1>Resilience</h1>

  ![Status](https://img.shields.io/badge/status-stable-blue)
  ![Build](https://img.shields.io/badge/build-passing-brightgreen)
  ![License](https://img.shields.io/badge/license-MIT-lightgrey)
  
  <h2>Overview</h2>
  <p>
    <strong>Resilience</strong> is a single-file C# utility that combines three powerful reliability
    patterns -- <em>Retry</em>, <em>Timeout</em>, and <em>Circuit Breaker</em> -- into one easy-to-use helper.
    It lets you wrap any async operation with automatic transient failure handling, exponential backoff,
    jitter, and per-call timeouts.
  </p>

  <p>
    Inspired by the resilience policies seen in production frameworks like <strong>Polly</strong>,
    this lightweight version is completely dependency-free and easy to drop into any .NET project.
    It’s ideal for API calls, database queries, or external service integrations that need fault tolerance.
  </p>

  <h2>Developer Note</h2>
  <p>
    This project is part of a broader cleanup of my personal playground -- where I’m 
    organizing standalone mini-projects that demonstrate core programming concepts, 
    clean design, and practical problem-solving in small, focused doses.
  </p>

  <h2>Key Features</h2>
  <ul>
    <li><strong>Retry with Exponential Backoff:</strong> customizable max attempts, base delay, and jitter ratio</li>
    <li><strong>Timeout Control:</strong> automatic cancellation of long-running tasks per attempt</li>
    <li><strong>Circuit Breaker:</strong> temporarily halts execution after repeated failures, with automatic recovery</li>
    <li><strong>Async-First:</strong> designed for modern <code>Task</code> and <code>async/await</code> workflows</li>
    <li><strong>Configurable Callbacks:</strong> hooks for <code>OnRetry</code>, <code>OnTimeout</code>, and state change events</li>
    <li><strong>Dependency-Free:</strong> no NuGet packages, no setup -- just copy and use</li>
  </ul>

  <h2>Example Usage</h2>
  <pre>
using Demo.Resilience;

  var breaker = new CircuitBreaker(new CircuitBreakerOptions {
      FailuresBeforeOpen = 3,
      OpenInterval = TimeSpan.FromSeconds(20),
      OnStateChange = s => Console.WriteLine($"[CB] {s}")
  });

  var result = await Resilience.ExecuteAsync&lt;string&gt;(
      async ct =&gt;
      {
          // Simulate unstable I/O
          await Task.Delay(200, ct);
          throw new HttpRequestException("Transient 503");
      },
      retry: new RetryOptions {
          MaxRetries = 4,
          BaseDelay = TimeSpan.FromMilliseconds(150),
          MaxDelay = TimeSpan.FromSeconds(2),
          JitterRatio = 0.3,
          ShouldRetryOn = ex =&gt; ex is HttpRequestException || ex is TimeoutException,
          OnRetry = (i, d, ex) =&gt; Console.WriteLine($"Retry {i} in {d.TotalMilliseconds:n0} ms: {ex.Message}")
      },
      timeout: new TimeoutOptions {
          Timeout = TimeSpan.FromSeconds(1),
          OnTimeout = t =&gt; Console.WriteLine($"Timed out after {t}.")
      },
      circuit: breaker
  );
  </pre>

  <p><em>Approximate Console Output:</em></p>
  <pre>
  Retry 1 in 200 ms: Transient 503
  Retry 2 in 450 ms: Transient 503
  [CB] Closed -> Open
  Timed out after 00:00:01.
  Retry 3 in 850 ms: Operation exceeded timeout of 00:00:01.
  [CB] Open -> HalfOpen
  [CB] HalfOpen -> Closed
  </pre>

  <h2>Why Resilience?</h2>
  <p>
    Network instability, API throttling, and transient I/O failures are inevitable in real systems.  
    Instead of littering your code with nested try-catch blocks and manual delays,
    <strong>Resilience</strong> centralizes retry, timeout, and circuit-breaker logic into one clean utility.
  </p>

  <p><strong>Without Resilience:</strong></p>
  <pre>
  try
  {
      await DoHttpCallAsync();
  }
  catch (HttpRequestException)
  {
      await Task.Delay(500);
      await DoHttpCallAsync(); // try again...
  }
  // Repeat with timeouts and fail counters everywhere
  </pre>

  <p><strong>With Resilience:</strong></p>
  <pre>
  await Resilience.ExecuteAsync(ct =&gt; DoHttpCallAsync(ct),
      retry: new RetryOptions { MaxRetries = 5 },
      timeout: new TimeoutOptions { Timeout = TimeSpan.FromSeconds(2) },
      circuit: new CircuitBreaker()
  );
  </pre>

  <p>
    This approach results in cleaner, safer, and far more maintainable code -- all while providing 
    the same robustness you’d expect from enterprise-grade frameworks.
  </p>

  <section id="tech-stack">
    <h2>Tech Stack</h2>
    <pre>☑ C# (.NET 8 or newer)</pre>
    <pre>☑ Async / Await based architecture</pre>
    <pre>☑ No external dependencies</pre>
  </section>

  <h2>Build Status</h2>
  <p>
    This is a single-file demonstration repository and does not include a build pipeline.  
    Future updates may introduce unit tests and a CI workflow via GitHub Actions.
  </p>

  <h2>License</h2>
  <p>
    Licensed under the <a href="LICENSE">MIT License</a>.<br>
  </p>
</section>
