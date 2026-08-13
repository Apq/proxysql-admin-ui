using System.Security.Cryptography;
using System.Text.Json;
using ProxysqlAdminUi.Web.Data;

namespace ProxysqlAdminUi.Web.Services;

public sealed record InitialCredential(string Username, string Password);

public sealed class InitialCredentialService
{
    private const string PasswordCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_%";
    private const string UppercaseCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseCharacters = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitCharacters = "0123456789";
    private const int InitialPasswordLength = 14;

    private readonly string credentialPath = Path.Combine(
        Path.GetDirectoryName(SqliteDbExtensions.GetAppDbPath())!,
        "initial-login.json");

    public string GeneratePassword()
    {
        var characters = new char[InitialPasswordLength];
        characters[0] = Pick(UppercaseCharacters);
        characters[1] = Pick(LowercaseCharacters);
        characters[2] = Pick(DigitCharacters);

        for (var index = 3; index < characters.Length; index++)
        {
            characters[index] = Pick(PasswordCharacters);
        }

        for (var index = characters.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swapIndex]) = (characters[swapIndex], characters[index]);
        }

        return new string(characters);
    }

    public async Task<IReadOnlyList<InitialCredential>> GetCredentialsAsync()
    {
        if (!File.Exists(credentialPath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(credentialPath);
            return await JsonSerializer.DeserializeAsync<List<InitialCredential>>(stream) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task AddCredentialAsync(InitialCredential credential)
    {
        var credentials = (await GetCredentialsAsync())
            .Where(item => !string.Equals(item.Username, credential.Username, StringComparison.OrdinalIgnoreCase))
            .Append(credential)
            .ToList();

        await SaveCredentialsAsync(credentials);
    }

    public async Task RemoveCredentialAsync(string username)
    {
        var credentials = (await GetCredentialsAsync())
            .Where(item => !string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (credentials.Count == 0)
        {
            if (File.Exists(credentialPath))
            {
                File.Delete(credentialPath);
            }

            return;
        }

        await SaveCredentialsAsync(credentials);
    }

    private async Task SaveCredentialsAsync(IReadOnlyList<InitialCredential> credentials)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(credentialPath)!);
        await using var stream = File.Create(credentialPath);
        await JsonSerializer.SerializeAsync(stream, credentials, new JsonSerializerOptions { WriteIndented = true });

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(credentialPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static char Pick(string characters) =>
        characters[RandomNumberGenerator.GetInt32(characters.Length)];
}
