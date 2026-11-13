using System.Text.RegularExpressions;

namespace PotatoLauncher.App.ViewModels;

public sealed class NaturalVersionComparer : IComparer<string>
{
    public static NaturalVersionComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var leftParts = Regex.Matches(left, @"\d+|[A-Za-z]+")
            .Select(match => match.Value)
            .ToArray();
        var rightParts = Regex.Matches(right, @"\d+|[A-Za-z]+")
            .Select(match => match.Value)
            .ToArray();
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < length; index++)
        {
            var leftPart = index < leftParts.Length ? leftParts[index] : "0";
            var rightPart = index < rightParts.Length ? rightParts[index] : "0";
            var leftIsNumber = int.TryParse(leftPart, out var leftNumber);
            var rightIsNumber = int.TryParse(rightPart, out var rightNumber);
            var comparison = leftIsNumber && rightIsNumber
                ? leftNumber.CompareTo(rightNumber)
                : string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
