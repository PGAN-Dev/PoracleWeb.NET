using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Refuses a collection that repeats a value.
/// </summary>
/// <remarks>
/// A repeated entry in a set-like field cannot mean anything, and the ones we have reach a column that
/// stores them as JSON text -- so a long enough repeat became a database error rather than a 400.
/// See #612.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class DistinctValuesAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not IEnumerable items)
        {
            return true;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!seen.Add(item?.ToString() ?? string.Empty))
            {
                return false;
            }
        }

        return true;
    }
}
