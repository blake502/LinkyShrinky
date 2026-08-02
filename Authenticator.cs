using Microsoft.AspNetCore.Identity;
using System.Text.Json;
using System.Xml.Linq;

namespace LinkyShrinky
{
    public class Authenticator
    {
        PasswordHasher<string> passwordHasher;
        string jsonFilePath;

        UserCreds userCreds;

        public Authenticator(string jsonFilePath)
        {
            passwordHasher = new PasswordHasher<string>();
            userCreds = new UserCreds();
            this.jsonFilePath = jsonFilePath;
            this.Load();
        }

        public void Load()
        {
            if (!File.Exists(jsonFilePath))
                return;

            string json = File.ReadAllText(jsonFilePath);
            UserCreds? deserialized = JsonSerializer.Deserialize<UserCreds>(json);

            if (deserialized != null)
                userCreds = deserialized;
        }

        public void Save()
        {
            File.WriteAllText(jsonFilePath + ".tmp", JsonSerializer.Serialize(userCreds, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(jsonFilePath + ".tmp", jsonFilePath, true);
        }

        public bool Verify(string username, string password)
        {
            username = username.ToLower();

            if (userCreds.username == null || userCreds.hashedPassword == null)
            {
                userCreds.hashedPassword = passwordHasher.HashPassword(username, password);
                userCreds.username = username;
                Save();
            }

            if (username != userCreds.username)
                return false;

            return passwordHasher.VerifyHashedPassword(username, userCreds.hashedPassword, password) == PasswordVerificationResult.Success;
        }
    }

    public class UserCreds
    {
        public string? hashedPassword { get; set; }
        public string? username { get; set; }
    }
}
