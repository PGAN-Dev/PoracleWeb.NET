using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Refuses a <c>clean</c> mask carrying the edit-in-place bit (2) on an alarm type that cannot edit.
/// </summary>
/// <remarks>
/// PoracleNG's <c>/api/v2</c> answers 400 "edit is not supported for gym tracking" for quest, invasion, nest,
/// gym and maxbattle (<c>rejectEdit</c>, and <c>v2EditUnsupported</c> for PATCH). Pokemon, raid, egg, lure and
/// pokestop events can carry the bit; fort changes have no <c>clean</c> at all. The set mirrors PoracleNG exactly,
/// so it is the impossible that is refused and nothing a type accepts. Put it beside <c>[Range(0, 7)]</c> on the
/// <c>Clean</c> property of the five types' <c>*Create</c> and <c>*Update</c> DTOs: the quick-pick and profile-import
/// paths validate the Create DTO too, so they are covered without a second check.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NoCleanEditBitAttribute : ValidationAttribute
{
    public const string Message =
        "Edit-in-place isn't available for this alarm type. Clean can include auto-delete (1) and summary (4), not edit (2).";

    public NoCleanEditBitAttribute()
        : base(Message)
    {
    }

    public override bool IsValid(object? value) => value is not int clean || !CleanFlags.IsEdit(clean);
}
