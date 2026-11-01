using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MyWealthV2.Domain.Common;

namespace MyWealthV2.Infrastructure.Data;

public static class OutboxSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { IgnoreDomainEvents, UsePrivateConstructor }
        }
    };

    private static readonly Dictionary<string, Type> EventTypes = typeof(BaseEvent).Assembly
        .GetTypes()
        .Where(type => type is { IsAbstract: false, FullName: not null } && typeof(BaseEvent).IsAssignableFrom(type))
        .ToDictionary(type => type.FullName!);

    public static string Serialize(BaseEvent domainEvent) =>
        JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);

    public static BaseEvent Deserialize(string typeName, string payload)
    {
        if (!EventTypes.TryGetValue(typeName, out var type))
        {
            throw new InvalidOperationException($"Unknown domain event type '{typeName}'.");
        }

        var domainEvent = JsonSerializer.Deserialize(payload, type, Options) as BaseEvent;
        if (domainEvent is null)
        {
            throw new InvalidOperationException($"Payload for '{typeName}' did not deserialize.");
        }

        return domainEvent;
    }

    public static DateTimeOffset OccurredAt(BaseEvent domainEvent, DateTimeOffset fallback)
    {
        var property = domainEvent.GetType().GetProperty("OccurredAt");
        return property?.PropertyType == typeof(DateTimeOffset)
            && property.GetValue(domainEvent) is DateTimeOffset occurredAt
            ? occurredAt
            : fallback;
    }

    public static int? TenantId(BaseEvent domainEvent, int? accessorTenantId)
    {
        var property = domainEvent.GetType().GetProperty("TenantId");
        if (property?.GetMethod?.IsPublic != true)
        {
            return accessorTenantId;
        }

        return property.PropertyType switch
        {
            _ when property.PropertyType == typeof(int) => (int)property.GetValue(domainEvent)!,
            _ when property.PropertyType == typeof(int?) => (int?)property.GetValue(domainEvent),
            _ => accessorTenantId
        };
    }

    private static void IgnoreDomainEvents(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.Name == "DomainEvents")
            {
                property.ShouldSerialize = static (_, _) => false;
            }
        }
    }

    private static void UsePrivateConstructor(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || typeInfo.CreateObject is not null)
        {
            return;
        }

        var constructor = typeInfo.Type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (constructor is null)
        {
            return;
        }

        typeInfo.CreateObject = () => constructor.Invoke(null)!;
    }
}
