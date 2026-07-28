using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LinkyShrinky
{
    public class LinkStore
    {
        public Dictionary<string, shortenedLink> shortenedLinks;
        readonly object linksLock = new();
        string jsonFilePath;
        Random random;
        bool dirty;

        public LinkStore(string jsonFilePath)
        {
            random = new Random();
            this.jsonFilePath = jsonFilePath;
            shortenedLinks = this.Load();
            dirty = false;
        }

        public string? ResolveRedirect(string slug)
        {
            lock (linksLock)
            {
                if (shortenedLinks.ContainsKey(slug))
                {
                    dirty = true;
                    shortenedLinks[slug].Hits++;
                    shortenedLinks[slug].LastHit = DateTimeOffset.UtcNow;
                    return shortenedLinks[slug].Redirect;
                }
                else
                    return null;
            }
        }

        Dictionary<string, shortenedLink> Load()
        {
            lock (linksLock)
            {
                if (!File.Exists(jsonFilePath))
                    File.WriteAllText(jsonFilePath, "{}");

                string json = File.ReadAllText(jsonFilePath);
                Dictionary<string, shortenedLink>? serializedJson = JsonSerializer.Deserialize<Dictionary<string, shortenedLink>>(json);

                if (serializedJson != null)
                    return serializedJson;
                else
                    return new Dictionary<string, shortenedLink>();
            }
        }

        public void Save()
        {
            lock (linksLock)
            {
                if (dirty)
                {
                    //Writing to a .tmp file, then moving it over the real file
                    //introduces atomic saving
                    File.WriteAllText(jsonFilePath + ".tmp", JsonSerializer.Serialize(shortenedLinks, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(jsonFilePath + ".tmp", jsonFilePath, true);
                    dirty = false;
                }
            }
        }

        public AddLinkResult Add(string redirect, string slug = "")
        {
            lock (linksLock)
            {
                if (shortenedLinks.ContainsKey(slug))
                {
                    Console.WriteLine("[WARN]: Tried to add slug {0}, but it already exists!", slug);
                    return new AddLinkResult() { Success = false, Error = "Slug already exists!" };
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        int failCount = 0;
                        int length = 3;
                        do
                        {
                            slug = random.GetString("abcdefghjkmnpqrstuvwxyz23456789", length);//Avoid i l 1 and o 0 to avoid look-alikes
                            failCount++;
                            if (failCount >= 32)
                            {
                                failCount = 0;
                                length++;
                            }
                        }
                        while (shortenedLinks.ContainsKey(slug));
                    }

                    dirty = true;
                    shortenedLinks.Add(slug, new shortenedLink() { Hits = 0, Redirect = redirect });
                    Save();
                    return new AddLinkResult() { Success = true, Slug = slug, Redirect = redirect };
                }
            }
        }
    }
    public class shortenedLink
    {
        public string Redirect { get; set; }
        public int Hits { get; set; }
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset? LastHit { get; set; }

        public shortenedLink()
        {
            Redirect = "/";
            Hits = 0;
            Created = DateTimeOffset.UtcNow;
            LastHit = null;
        }
    }
    public class AddLinkResult
    {
        public bool Success { get; init; }
        public string? Slug { get; init; }
        public string? Redirect { get; init; }
        public string? Error { get; init; }
    }
}
