using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Security.Claims;

namespace EvolutTur
{
    public class CustomAuthProvider : AuthenticationStateProvider
    {
        private readonly ProtectedSessionStorage _sessionStorage;
        private ClaimsPrincipal _usuario;

        public CustomAuthProvider(ProtectedSessionStorage sessionStorage)
        {
            _sessionStorage = sessionStorage;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var result = await _sessionStorage.GetAsync<List<KeyValuePair<string, string>>>("authClaims");
                var claimsList = result.Success ? result.Value : null;

                if (claimsList == null || claimsList.Count == 0)
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

                var identity = new ClaimsIdentity(
                    claimsList.Select(c => new Claim(c.Key, c.Value)), "Custom");

                _usuario = new ClaimsPrincipal(identity);

                return new AuthenticationState(_usuario);
            }
            catch
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }

        public async Task LoginAsync(ClaimsPrincipal user)
        {
            // Salve todas as claims relevantes
            var claimsToStore = user.Claims
                .Select(c => new KeyValuePair<string, string>(c.Type, c.Value))
                .ToList();

            await _sessionStorage.SetAsync("authClaims", claimsToStore);

            _usuario = user;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
        }

        public async Task LogoutAsync()
        {
            await _sessionStorage.DeleteAsync("authToken");
            _usuario = new ClaimsPrincipal(new ClaimsIdentity());
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_usuario)));
        }
    }
}
