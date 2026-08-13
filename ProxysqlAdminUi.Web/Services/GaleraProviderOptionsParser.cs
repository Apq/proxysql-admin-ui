namespace ProxysqlAdminUi.Web.Services;

public static class GaleraProviderOptionsParser
{
    public static (int Weight, bool IsDefault) ParseWeight(string? providerOptions)
    {
        if (string.IsNullOrWhiteSpace(providerOptions))
        {
            return (1, true);
        }

        int? weight = null;
        foreach (var segment in providerOptions.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            if (!string.Equals(key, "pc.weight", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (weight.HasValue)
            {
                throw new InvalidOperationException("The Galera provider options contain duplicate pc.weight values.");
            }

            var value = segment[(separatorIndex + 1)..].Trim();
            if (!int.TryParse(value, out var parsedWeight) || parsedWeight is < 0 or > 255)
            {
                throw new InvalidOperationException("The Galera provider option pc.weight is not a valid value from 0 to 255.");
            }

            weight = parsedWeight;
        }

        return weight.HasValue ? (weight.Value, false) : (1, true);
    }
}
