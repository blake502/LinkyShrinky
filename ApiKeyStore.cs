using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LinkyShrinky
{
    public class ApiKeyStore
    {
        readonly object keysLock = new();
        readonly string jsonFilePath;
        Dictionary<string, ApiKeyRecord> keysByHash;
        volatile bool _dirty;

        public ApiKeyStore(string jsonFilePath)
        {
            this.jsonFilePath = jsonFilePath;
            keysByHash = new Dictionary<string, ApiKeyRecord>();
            if (File.Exists(jsonFilePath))
            {
                string json = File.ReadAllText(jsonFilePath);
                keysByHash = JsonSerializer.Deserialize<Dictionary<string, ApiKeyRecord>>(json) ?? new();
            }
        }

        static string Hash(string key)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        }

        public (string Key, ApiKeyRecord Record) Create(string name)
        {
            string key = "key_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            //TODO: Collision detection on Id and Name
            var record = new ApiKeyRecord { Id = Guid.NewGuid().ToString("N")[..8], Name = name };

            lock (keysLock)
            {
                keysByHash[Hash(key)] = record;
                Save();
            }

            return (key, record);
        }

        public ApiKeyRecord? Validate(string key)
        {
            lock(keysLock)
            {
                var hash = Hash(key);
                if (!keysByHash.TryGetValue(hash, out var record))
                    return null;

                record = record with { LastUsed = DateTimeOffset.UtcNow };
                keysByHash[hash] = record;
                _dirty = true;
                return record;
            }
        }

        public List<ApiKeyRecord> List()
        {
            lock (keysLock)
                return keysByHash.Values.ToList();
        }

        public bool Revoke(string id)
        {
            lock(keysLock)
            {
                var hash = keysByHash.FirstOrDefault(keyvalue => keyvalue.Value.Id == id).Key;
                if (hash == null)
                    return false;
                keysByHash.Remove(hash);
                Save();
                return true;
            }
        }

        public void Flush()
        {
            if (!_dirty)
                return;
            lock(keysLock)
            {
                Save();
                _dirty = false;
            }
        }

        public void Save()
        {
            lock (keysLock)
            {
                File.WriteAllText(jsonFilePath + ".tmp", JsonSerializer.Serialize(keysByHash, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(jsonFilePath + ".tmp", jsonFilePath, true);
            }
        }
    }

    public record ApiKeyRecord
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastUsed { get; init; }
    }
}
