using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Net;
using System.Threading.RateLimiting;
using IPNetwork = System.Net.IPNetwork;
using System.Linq.Expressions;

namespace LinkyShrinky
{
    public class Program
    {
        static string? adminPage = Environment.GetEnvironmentVariable("ADMIN_PAGE");
        static string? trustedProxies = Environment.GetEnvironmentVariable("TRUSTED_PROXIES");

        public static void Main(string[] args)
        {
            if (adminPage == null)
                adminPage = "admin";

            if (trustedProxies == null)
                trustedProxies = "";

            var builder = WebApplication.CreateBuilder(args);

            //Cookies
            builder.Services.AddAuthentication("Cookies").AddCookie("Cookies", options =>
            {
                //Path to redirect user if they try to hit a page that requires auth without being authed
                options.LoginPath = "/" + adminPage;
            });

            //Forwarded headers
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                string[] entries = trustedProxies.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if(entries.Length == 0)
                    Console.WriteLine("[WARN] No trusted proxies configured. Please consider configuring the \"TRUSTED_PROXIES\" environment variable.");

                foreach (string entry in entries)
                {
                    if(entry == "*")
                    {
                        options.KnownProxies.Clear();
                        options.KnownIPNetworks.Clear();

                        Console.WriteLine("[WARN] Trusting forwarded headers from ANY source. This is discouraged.");

                        break;
                    }

                    if (entry.Contains("/"))
                    {
                        if (IPNetwork.TryParse(entry.Trim(), out IPNetwork ipNetwork))
                            options.KnownIPNetworks.Add(ipNetwork);
                        else
                            Console.WriteLine("[ERROR] Could not parse trusted proxy {0}", entry);
                    }
                    else
                    {
                        if (IPAddress.TryParse(entry.Trim(), out IPAddress? ipAddress))
                            options.KnownProxies.Add(ipAddress);
                        else
                            Console.WriteLine("[ERROR] Could not parse trusted proxy {0}", entry);
                    }
                }

                foreach (var network in options.KnownIPNetworks)
                    Console.WriteLine("[INFO] Trusted proxy range {0}/{1}", network.BaseAddress, network.PrefixLength);

                foreach (var address in options.KnownProxies)
                    Console.WriteLine("[INFO] Trusted proxy address {0}", address);
            });

            //Rate limiting
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, CancellationToken) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                    context.HttpContext.Response.Redirect("/" + adminPage + "?error=2");
                    return ValueTask.CompletedTask;
                };

                options.AddPolicy("login", context =>
                    RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = 5,
                            Window = TimeSpan.FromMinutes(5),
                            SegmentsPerWindow = 5,
                            QueueLimit = 0
                        }));
            });

            // Add services to the container.
            builder.Services.AddAuthorization();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.UseForwardedHeaders();
            app.UseRateLimiter();
            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();

            if (!Directory.Exists("config"))
                Directory.CreateDirectory("config");

            //Load links from urls.json
            LinkStore linkStore = new LinkStore("config/urls.json");//app

            //Create authenticator
            Authenticator authenticator = new Authenticator("config/user.json");

            //Require authentication for admin dashboard
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path;

                if (path.StartsWithSegments("/" + adminPage + "/dashboard", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await context.AuthenticateAsync("Cookies");

                    if(!result.Succeeded || !result.Principal.IsInRole("Admin"))
                    {
                        context.Response.Redirect("/" + adminPage);
                        return;
                    }

                    context.User = result.Principal;

                    context.Response.Headers.CacheControl = "no-store";
                }

                await next();
            });

            //Serve wwwroot/admin to path defined by adminPage variable
            //This allows wwwroot/admin to hold the dashboard files, even if the adminPage variable is something else
            //such as "administration"
            app.UseDefaultFiles(new DefaultFilesOptions()
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.WebRootPath, "admin")),
                RequestPath = "/" + adminPage
            });

            app.UseStaticFiles(new StaticFileOptions()
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.WebRootPath, "admin")),
                RequestPath = "/" + adminPage
            });

            //Login attempt
            app.MapPost("/" + adminPage + "/login", async (HttpContext context) =>
            {
                var form = await context.Request.ReadFormAsync();

                string? username = form["username"];
                string? password = form["password"];

                if (username == null || password == null)
                    return Results.BadRequest();

                var results = authenticator.Verify(username, password);
                
                if (results)
                {
                    //Issue cookie
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, username),
                        new Claim(ClaimTypes.Role, "Admin")
                    };

                    var identity = new ClaimsIdentity(claims, "Cookies");

                    var principal = new ClaimsPrincipal(identity);

                    await context.SignInAsync("Cookies", principal);

                    return Results.Redirect("/" + adminPage + "/dashboard");
                }
                else
                {
                    return Results.Redirect("/" + adminPage + "?error=1");
                }
            }).RequireRateLimiting("login");

            app.MapPost("/" + adminPage + "/logout", async (HttpContext context) =>
            {
                await context.SignOutAsync("Cookies");
                return Results.Redirect("/" + adminPage);
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            //TODO: Probably not regex this. Find another way to reserve admin path
            app.MapGet("/{slug:regex(^(?!" + adminPage + "$).+)}", (string slug, HttpContext context) =>
            {
                string clientIP = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                string? redirect = linkStore.ResolveRedirect(slug);

                if (redirect != null)
                {
                    Console.WriteLine("Resolved slug \"{0}\" to {1} for {2}", slug, redirect, clientIP);
                    return Results.Redirect(redirect);
                }
                else
                {
                    Console.WriteLine("Unable to resolve slug \"{0}\" for {1}", slug, clientIP);
                    return Results.Redirect("/");
                }
            });

            app.MapGet("api/links", () =>
            {
                return linkStore.Snapshot();
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            app.MapPost("api/links", (AddLinkRequest addLinkRequest) =>
            {
                AddLinkResult result = linkStore.Add(addLinkRequest.Redirect, addLinkRequest.Slug ?? "");
                if (result.Success)
                {
                    Console.WriteLine("Successfully added {0} to {1}", result.Slug, result.Redirect);
                    return Results.Ok(result);
                }
                else
                {
                    Console.WriteLine("Failed to add!");
                    return Results.Conflict(result.Error);
                }
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            app.MapDelete("api/links/{slug}", (string slug) =>
            {
                bool result = linkStore.Delete(slug);

                if (result)
                    return Results.NoContent();
                else
                    return Results.NotFound();
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));

            PeriodicTimer saveTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            Task.Run(async () =>
            {
                while (await saveTimer.WaitForNextTickAsync())
                    try
                    {
                        linkStore.Flush();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ERROR] Error while flushing cache to disk!");
                        Console.WriteLine(ex.Message);
                    }
            });

            app.Lifetime.ApplicationStopping.Register(() =>
            {
                linkStore.Save();
            });

            app.Run();
        }
    }
    public class AddLinkRequest
    {
        [Required]
        [Url]
        public string Redirect { get; init; } = "";

        public string? Slug { get; init; }
    }
}
