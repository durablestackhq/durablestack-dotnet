using System;
using System.Collections.Generic;

namespace DurableStack.Tests.TestSupport;

internal sealed class InlineServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services;

    public InlineServiceProvider(params object[] services)
    {
        _services = new Dictionary<Type, object>();
        foreach (var service in services)
        {
            _services[service.GetType()] = service;
        }
    }

    public object? GetService(Type serviceType)
    {
        if (_services.TryGetValue(serviceType, out var service))
        {
            return service;
        }

        foreach (var pair in _services)
        {
            if (serviceType.IsAssignableFrom(pair.Key))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
