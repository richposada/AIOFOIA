using System.Text.RegularExpressions;
using FoiaProcessor.Api.Contracts;

namespace FoiaProcessor.Api.Validation;

public static class FoiaRequestValidator
{
    // Pragmatic RFC-5322-ish email regex; intentionally permissive for the PoC.
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    public static IDictionary<string, string[]> Validate(SubmitFoiaRequestDto dto)
    {
        var errors = new Dictionary<string, List<string>>();

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                list = new List<string>();
                errors[field] = list;
            }
            list.Add(message);
        }

        if (string.IsNullOrWhiteSpace(dto.Subject))
        {
            Add(nameof(dto.Subject).ToCamel(), "Subject is required.");
        }
        else if (dto.Subject.Length > 200)
        {
            Add(nameof(dto.Subject).ToCamel(), "Subject must be 200 characters or fewer.");
        }

        if (dto.RequestedStartDate == default)
        {
            Add(nameof(dto.RequestedStartDate).ToCamel(), "Requested start date is required.");
        }

        if (dto.RequestedEndDate == default)
        {
            Add(nameof(dto.RequestedEndDate).ToCamel(), "Requested end date is required.");
        }
        else if (dto.RequestedStartDate != default && dto.RequestedEndDate < dto.RequestedStartDate)
        {
            Add(nameof(dto.RequestedEndDate).ToCamel(), "Requested end date must be on or after the start date.");
        }

        if (string.IsNullOrWhiteSpace(dto.RequestorFullName))
        {
            Add(nameof(dto.RequestorFullName).ToCamel(), "Requestor full name is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.RequestorEmail))
        {
            Add(nameof(dto.RequestorEmail).ToCamel(), "A valid email address is required.");
        }
        else if (!EmailRegex.IsMatch(dto.RequestorEmail))
        {
            Add(nameof(dto.RequestorEmail).ToCamel(), "A valid email address is required.");
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static string ToCamel(this string s) =>
        string.IsNullOrEmpty(s) || char.IsLower(s[0]) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
