using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Backend.Utilities;

/// <summary>
/// État du circuit breaker
/// </summary>
public enum CircuitState
{
    Closed,     // Fonctionnement normal
    Open,       // Service down, rejette les requêtes
    HalfOpen    // Test si service revenu
}

/// <summary>
/// Implémentation du pattern Circuit Breaker pour les appels FastApi
/// </summary>
public class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _resetTimeout;
    private int _failureCount = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private CircuitState _state = CircuitState.Closed;
    private readonly ILogger<CircuitBreaker> _logger;

    public CircuitState State => _state;
    public int FailureCount => _failureCount;

    public CircuitBreaker(
        int failureThreshold = 5,
        TimeSpan? timeout = null,
        TimeSpan? resetTimeout = null,
        ILogger<CircuitBreaker>? logger = null)
    {
        _failureThreshold = failureThreshold;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _resetTimeout = resetTimeout ?? TimeSpan.FromSeconds(60);
        _logger = logger ?? new NullLogger<CircuitBreaker>();
    }

    /// <summary>
    /// Enregistre un succès
    /// </summary>
    public void RecordSuccess()
    {
        _failureCount = 0;
        _state = CircuitState.Closed;
        _logger?.LogInformation("CircuitBreaker: Success recorded, state = Closed");
    }

    /// <summary>
    /// Enregistre une failure
    /// </summary>
    public void RecordFailure()
    {
        _failureCount++;
        _lastFailureTime = DateTime.UtcNow;
        
        if (_failureCount >= _failureThreshold)
        {
            _state = CircuitState.Open;
            _logger?.LogWarning("CircuitBreaker: Failure threshold reached ({Count}), state = Open", _failureCount);
        }
        else
        {
            _logger?.LogWarning("CircuitBreaker: Failure recorded ({Count}/{Threshold})", _failureCount, _failureThreshold);
        }
    }

    /// <summary>
    /// Vérifie si le circuit peut être fermé (passage de Open à HalfOpen)
    /// </summary>
    public bool CanAttemptReset()
    {
        if (_state != CircuitState.Open)
            return false;

        var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
        if (timeSinceLastFailure >= _resetTimeout)
        {
            _state = CircuitState.HalfOpen;
            _logger?.LogInformation("CircuitBreaker: Reset timeout reached, state = HalfOpen");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Vérifie si une requête peut être envoyée
    /// </summary>
    public bool IsCallAllowed()
    {
        if (_state == CircuitState.Closed)
            return true;

        if (_state == CircuitState.Open)
        {
            CanAttemptReset();
            return _state != CircuitState.Open;
        }

        return _state == CircuitState.HalfOpen;
    }

    /// <summary>
    /// Remet le circuit breaker à zéro
    /// </summary>
    public void Reset()
    {
        _failureCount = 0;
        _state = CircuitState.Closed;
        _logger?.LogInformation("CircuitBreaker: Reset manually");
    }
}

/// <summary>
/// Politique de retry avec exponential backoff
/// </summary>
public class RetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly double _backoffMultiplier;
    private readonly ILogger<RetryPolicy> _logger;

    public RetryPolicy(
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        double backoffMultiplier = 2.0,
        ILogger<RetryPolicy>? logger = null)
    {
        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
        _backoffMultiplier = backoffMultiplier;
        _logger = logger ?? new NullLogger<RetryPolicy>();
    }

    /// <summary>
    /// Exécute une fonction avec retry
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName = "Operation")
    {
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                _logger?.LogInformation("RetryPolicy: Attempting {Operation} (attempt {Attempt}/{Max})", operationName, attempt + 1, _maxRetries + 1);
                return await operation();
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(
                    _initialDelay.TotalMilliseconds * Math.Pow(_backoffMultiplier, attempt));
                
                _logger?.LogWarning(ex, "RetryPolicy: {Operation} failed on attempt {Attempt}, retrying after {Delay}ms", 
                    operationName, attempt + 1, delay.TotalMilliseconds);
                
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "RetryPolicy: {Operation} failed after {MaxRetries} attempts", operationName, _maxRetries + 1);
                throw;
            }
        }

        throw new InvalidOperationException($"Failed to complete operation '{operationName}' after {_maxRetries + 1} attempts");
    }

    /// <summary>
    /// Exécute une fonction avec retry (sans retour de valeur)
    /// </summary>
    public async Task ExecuteAsync(Func<Task> operation, string operationName = "Operation")
    {
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        }, operationName);
    }
}

/// <summary>
/// Logger null pour éviter les null reference exceptions
/// </summary>
public class NullLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
