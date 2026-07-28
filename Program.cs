using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.Internal;
using System.ComponentModel.DataAnnotations;

namespace LinkyShrinky
{
    public class Program
    {
        const string adminPage = "admin";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddAuthorization();

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            app.UseAuthorization();
            app.UseForwardedHeaders();
            
            LinkStore linkStore = new LinkStore("urls.json");

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

            app.MapPost("/" + adminPage + "/login", async (HttpContext context) =>
            {
                var form = await context.Request.ReadFormAsync();

                var username = form["username"];
                var password = form["password"];

                //TODO: Auth, issue cookie

                return Results.Redirect("/" + adminPage + "/dashboard");
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
            });

            app.MapGet("api/links", () =>
            {
                return linkStore.shortenedLinks;
            });
            

            app.RunAsync();

            //Save links/hits every 15 seconds
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
