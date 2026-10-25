using System.Text.Json;
using System.Text.RegularExpressions;

namespace LinkyShrinky
{
    public class LinkStore
    {
        Dictionary<string, shortenedLink> shortenedLinks;
        readonly object linksLock = new();
        string jsonFilePath;
        Random random;
        Regex slugRegex = new Regex(@"^[A-Za-z0-9_-]{1,64}$");

        private volatile bool _dirty;

        public LinkStore(string jsonFilePath)
        {
            random = new Random();
            this.jsonFilePath = jsonFilePath;
            shortenedLinks = new Dictionary<string, shortenedLink>();
            if (File.Exists(jsonFilePath))
            {
                string json = File.ReadAllText(jsonFilePath);
                shortenedLinks = JsonSerializer.Deserialize<Dictionary<string, shortenedLink>>(json) ?? new();
            }
        }

        public Dictionary<string, shortenedLink> Snapshot()
        {
            lock (linksLock)
                return shortenedLinks.ToDictionary(kv => kv.Key, kv => kv.Value with { });
        }

        public string? ResolveRedirect(string slug)
        {
            lock (linksLock)
            {
                if (shortenedLinks.ContainsKey(slug))
                {
                    shortenedLinks[slug].Hits++;
                    shortenedLinks[slug].LastHit = DateTimeOffset.UtcNow;

                    _dirty = true;

                    return shortenedLinks[slug].Redirect;
                }
                else
                    return null;
            }
        }

        public bool Delete(string slug)
        {
            lock (linksLock)
            {
                if (shortenedLinks.ContainsKey(slug))
                {
                    shortenedLinks.Remove(slug);
                    Save();
                    return true;
                }
                return false;
            }
        }

        public void Flush()
        {
            if (!_dirty)
                return;

            lock (linksLock)
            {
                Save();
                _dirty = false;
            }
        }

        public void Save()
        {
            lock (linksLock)
            {
                //Writing to a .tmp file, then moving it over the real file
                //introduces atomic saving
                File.WriteAllText(jsonFilePath + ".tmp", JsonSerializer.Serialize(shortenedLinks, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(jsonFilePath + ".tmp", jsonFilePath, true);
            }
        }

        public AddLinkResult Add(string redirect, string slug = "")
        {
            lock (linksLock)
            {
                if (!slugRegex.IsMatch(slug))
                {
                    Console.WriteLine("[ERROR] Slug uses illegal characters!", slug);
                    return new AddLinkResult() { Success = false, Error = "Slug uses illegal characters!" };
                }

                if (shortenedLinks.ContainsKey(slug))
                {
                    Console.WriteLine("[WARN] Slug already exists!", slug);
                    return new AddLinkResult() { Success = false, Error = "Slug already exists!" };
                }

                Uri? uri;
                if (!Uri.TryCreate(redirect, UriKind.Absolute, out uri) || !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    Console.WriteLine("[WARN] Could not create URI!", slug);
                    return new AddLinkResult() { Success = false, Error = "Could not create URI!" };
                }

                if (string.IsNullOrWhiteSpace(slug))
                {
                    int failCount = 0;
                    int length = 3;
                    do
                    {
                        slug = random.GetString("acdefghjkmnprtuvwxyz23478", length); //Avoid i l 1 and o 0 and 5 s and 9 q and 6 b to avoid look-alikes
                        failCount++;
                        if (failCount >= 32)
                        {
                            failCount = 0;
                            length++;
                        }
                    }
                    while (shortenedLinks.ContainsKey(slug));
                }

                shortenedLinks.Add(slug, new shortenedLink() { Hits = 0, Redirect = uri.AbsoluteUri });
                Save();
                return new AddLinkResult() { Success = true, Slug = slug, Redirect = uri.AbsoluteUri };
            }
        }
    }
    public record shortenedLink
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
