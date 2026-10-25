using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Stored properties that ride along with one alarm model on its next write, because the typed model has no
/// field for them. A move builds a create from the model, and a create serialises the model, so anything the
/// source row held beyond it would be dropped; the mover attaches those properties here and
/// <see cref="PoracleJsonHelper.SerializeToElement{T}"/> merges them back into the body.
/// </summary>
/// <remarks>
/// Held against the model instance and never serialised with it, so a read, a response body and every
/// write that attached nothing are unchanged.
/// </remarks>
internal static class CarriedFields
{
    private static readonly ConditionalWeakTable<object, Box> Table = new();

    public static void Attach(object model, JsonElement properties)
    {
        if (properties.ValueKind == JsonValueKind.Object && properties.EnumerateObject().Any())
        {
            Table.AddOrUpdate(model, new Box(properties.Clone()));
        }
    }

    public static bool TryGet(object model, out JsonElement properties)
    {
        if (Table.TryGetValue(model, out var box))
        {
            properties = box.Value;
            return true;
        }

        properties = default;
        return false;
    }

    private sealed class Box(JsonElement value)
    {
        public JsonElement Value { get; } = value;
    }
}
