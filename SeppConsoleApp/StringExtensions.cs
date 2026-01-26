using System.Text.RegularExpressions;

namespace SeppConsoleApp;

/// <summary>
/// Provides extension methods for string manipulation.
/// </summary>
public static partial class StringExtensions
{
    /// <summary>
    /// Removes all double or multiple spaces in the input string and replaces them with a single space.
    /// </summary>
    /// <param name="input">The input string that may contain multiple spaces.</param>
    /// <returns>A string with all multiple spaces replaced by a single space.</returns>
    public static string RemoveExtraSpaces(this string input)
    {
        return WhitespaceReducerRegex().Replace(input, " ");
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceReducerRegex();
}