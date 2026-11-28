using System.Globalization;
using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.Common;

public static partial class DisplayCodeSearch
{
    public const string AppointmentPattern = @"\b(?:APT(?:-[A-Z0-9]+)+|LH\s*[0-9]+)\b";

    public static string Canonical(string value)
    {
        var compact = Compact(value);
        return SequentialCode().IsMatch(compact) &&
               long.TryParse(compact.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? compact[..2] + number.ToString("D4", CultureInfo.InvariantCulture)
            : compact;
    }

    public static string Compact(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

    public static long? LegacyPrescriptionId(string compact) =>
        LegacyPrescriptionCode().IsMatch(compact) &&
        long.TryParse(compact.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    [GeneratedRegex(@"^(?:LH|BN|LK|HD|TT|DT|CL|DK)[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SequentialCode();

    [GeneratedRegex(@"^RX-[0-9]{6}-[0-9]{4,}$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyPrescriptionCode();
}
