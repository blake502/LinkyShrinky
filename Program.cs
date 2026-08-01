using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.Internal;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace LinkyShrinky
{
    public class Program
    {
        const string adminPage = "admin";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddAuthentication("Cookies").AddCookie("Cookies", options =>
                {
                    //Path to redirect user if they try to hit a page that requires auth without being authed
                    options.LoginPath = "/" + adminPage;
                });

            // Add services to the container.
            builder.Services.AddAuthorization();

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            app.UseAuthentication();
            app.UseAuthorization();
            app.UseForwardedHeaders();
            
            //Load links from urls.json
            LinkStore linkStore = new LinkStore("urls.json");//app

            //Create authenticator
            Authenticator authenticator = new Authenticator("user.json");

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

            });

            //TODO: Logout

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

            app.MapDelete("api/links/{slug}", (string slug) =>
            {
                bool result = linkStore.Delete(slug);

                if (result)
                    return Results.NoContent();
                else
                    return Results.Conflict();
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

            app.MapGet("api/links", () =>
            {
                return linkStore.shortenedLinks;
            }).RequireAuthorization(policy => policy.RequireRole("Admin"));
            

            app.RunAsync();

            //Save links/hits every 15 seconds (doesn't actually save unless it's marked "dirty")
            while (true)
            {
                Thread.Sleep(1000 * 15);
                Console.WriteLine("Saving...");
                linkStore.Save();
            }
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
