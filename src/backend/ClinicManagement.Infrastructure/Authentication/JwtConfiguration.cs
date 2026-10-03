using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ClinicManagement.Infrastructure.Authentication;

public sealed record JwtConfiguration(string Key, string Issuer, string Audience, int ExpiryMinutes)
{
    public const string DevelopmentKey = "ClinicCareDevelopmentSecretKey2026MustBeAtLeast32BytesLong!";

    public static JwtConfiguration Read(IConfiguration configuration, IHostEnvironment environment)
    {
        var allowDevelopmentKey = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) && allowDevelopmentKey) key = DevelopmentKey;
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32 ||
            (!allowDevelopmentKey && key == DevelopmentKey))
            throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes and must not use the development key outside Development/Testing.");

        if (!int.TryParse(configuration["Jwt:ExpiryMinutes"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiryMinutes) || expiryMinutes <= 0)
            throw new InvalidOperationException("Jwt:ExpiryMinutes is required and must be a positive integer.");

        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        return new JwtConfiguration(key,
            string.IsNullOrWhiteSpace(issuer) ? "ClinicCareServer" : issuer,
            string.IsNullOrWhiteSpace(audience) ? "ClinicCareClient" : audience,
            expiryMinutes);
    }
}
