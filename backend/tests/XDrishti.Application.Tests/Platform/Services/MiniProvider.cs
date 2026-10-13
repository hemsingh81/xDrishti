using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// A small service provider for tests (the unit-test project has the DI abstractions only): singletons and scoped services built by <see cref="ActivatorUtilities"/>, one scope per
/// <see cref="CreateScope"/>, loggers that log nothing.
/// </summary>
internal sealed class MiniProvider : IServiceProvider, IServiceScopeFactory, IDisposable
{
    private readonly Dictionary<Type, Registration> _registrations = [];
    private readonly Dictionary<Type, object> _singletons = [];
    private readonly object _gate = new();

    public MiniProvider Singleton<TService>(TService instance)
        where TService : class
    {
        _registrations[typeof(TService)] = new Registration(false, _ => instance);
        return this;
    }

    public MiniProvider Singleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        _registrations[typeof(TService)] = new Registration(false, sp => ActivatorUtilities.CreateInstance<TImplementation>(sp));
        return this;
    }

    public MiniProvider Scoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        _registrations[typeof(TService)] = new Registration(true, sp => ActivatorUtilities.CreateInstance<TImplementation>(sp));
        return this;
    }

    public MiniProvider Scoped<TService>()
        where TService : class =>
        Scoped<TService, TService>();

    public object? GetService(Type serviceType) => Resolve(serviceType, this, scope: null);

    public IServiceScope CreateScope() => new Scope(this);

    public void Dispose()
    {
        // nothing to release: the fakes hold no resources
    }

    private object? Resolve(Type type, IServiceProvider requester, Scope? scope)
    {
        if (type == typeof(IServiceScopeFactory) || type == typeof(IServiceProvider))
        {
            return this;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            return Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()));
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var item = type.GetGenericArguments()[0];
            var all = _registrations.Where(r => r.Key == item).Select(r => Resolve(item, requester, scope)).ToArray();
            var array = Array.CreateInstance(item, all.Length);
            Array.Copy(all, array, all.Length);
            return array;
        }

        if (!_registrations.TryGetValue(type, out var registration))
        {
            return null;
        }

        lock (_gate)
        {
            if (registration.Scoped)
            {
                var cache = scope?.Cache ?? throw new InvalidOperationException($"{type.Name} is scoped; ask a scope for it.");
                if (!cache.TryGetValue(type, out var made))
                {
                    cache[type] = made = registration.Factory(scope);
                }

                return made;
            }

            if (!_singletons.TryGetValue(type, out var single))
            {
                _singletons[type] = single = registration.Factory(this);
            }

            return single;
        }
    }

    private sealed record Registration(bool Scoped, Func<IServiceProvider, object> Factory);

    private sealed class Scope(MiniProvider root) : IServiceScope, IServiceProvider
    {
        public Dictionary<Type, object> Cache { get; } = [];

        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) => root.Resolve(serviceType, this, this);

        public void Dispose()
        {
            // nothing to release
        }
    }
}
