namespace TheDiscussion.Core.Discussions;

/// <summary>
/// Collects validation messages keyed by request field name (one of <see cref="RequestFields"/>). It only builds the
/// error response: a field the caller could not read reaches the service on the input record (its <c>…ReadError</c>),
/// not as an entry here.
/// </summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public bool IsEmpty => errors.Count == 0;

    public void Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
        {
            messages = [];
            errors.Add(field, messages);
        }

        messages.Add(message);
    }

    public IDictionary<string, string[]> ToDictionary() =>
        errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);

    /// <summary>Validates required text with a maximum length. Returns the trimmed text when valid.</summary>
    public string? RequireText(string field, string? value, int maxLength, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            Add(field, $"{label} is required.");
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            Add(field, $"{label} must be at most {maxLength} characters (it has {trimmed.Length}).");
            return null;
        }

        return trimmed;
    }
}

/// <summary>A request was rejected; nothing was stored. <see cref="Errors"/> names each offending field.</summary>
public sealed class DiscussionValidationException(IDictionary<string, string[]> errors)
    : Exception("The request is invalid: " + string.Join("; ", errors.Select(error => $"{error.Key}: {string.Join(" ", error.Value)}")))
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
