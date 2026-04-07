using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Validates that a string or string-collection property contains only values from a fixed allowed set.
/// Null values pass validation (use <see cref="RequiredAttribute"/> for required fields).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AllowedStringValuesAttribute(params string[] allowedValues) : ValidationAttribute
{
    private readonly HashSet<string> _allowed = new(allowedValues, StringComparer.Ordinal);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is string s)
        {
            return this._allowed.Contains(s)
                ? ValidationResult.Success
                : new ValidationResult($"{validationContext.DisplayName} must be one of: {string.Join(", ", this._allowed)}.");
        }

        if (value is IEnumerable<string> items)
        {
            var invalidItem = items.FirstOrDefault(item => !this._allowed.Contains(item));
            if (invalidItem is not null)
            {
                return new ValidationResult($"{validationContext.DisplayName} contains invalid value '{invalidItem}'. Allowed: {string.Join(", ", this._allowed)}.");
            }

            return ValidationResult.Success;
        }

        return new ValidationResult($"{validationContext.DisplayName} has an unsupported type for {nameof(AllowedStringValuesAttribute)}.");
    }
}
