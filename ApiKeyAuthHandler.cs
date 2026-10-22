using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace LinkyShrinky
{
    public class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        readonly ApiKeyStore store;

        public ApiKeyAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ApiKeyStore store) : base (options, logger, encoder)
        {
            this.store = store;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            if (!authHeader.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(AuthenticateResult.NoResult());

            if (string.IsNullOrEmpty(authHeader.Parameter))
                return Task.FromResult(AuthenticateResult.NoResult());

            var record = store.Validate(authHeader.Parameter);
            if (record == null)
                return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, record.Id),
                new Claim(ClaimTypes.Name, "apikey:" + record.Name),
                new Claim(ClaimTypes.Role, "Admin")
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
