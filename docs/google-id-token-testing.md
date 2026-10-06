# Testing the API with a real Google ID token

Step-by-step guide for the owner (issue #65). It covers:

1. creating the Google OAuth client;
2. getting a real Google ID token for your own account;
3. finding your `sub` without pasting the token anywhere;
4. putting the client ID and `sub` into `dotnet user-secrets`;
5. the manual checks, recorded as status codes only.

The automated tests never use real Google tokens (ADR-005). This guide is the
one-time manual check before the first deployment.

> **Public repository.** Never put a token, `sub` value, email address, client ID
> or client secret in this repository, in an issue, in a commit or in a chat.
> Report results as status codes only. Never paste a token into an online JWT
> decoder: until it expires, it lets anyone call the API as you.

Pages are cited as [n]; the list is at the end. Anything marked **UNVERIFIED**
is not stated on an official page I could read.

## Summary

- **Client type:** *Web application*, with `http://localhost` and
  `http://localhost:8765` as authorized JavaScript origins and **no** redirect
  URIs [1]. This is the type Google requires for "Sign in with Google" in a
  browser, so a later web UI can use the same client.
- **Getting a token:** a small local web page with the official "Sign in with
  Google" button [1][2][3]. Its callback receives "the ID token as a
  base64-encoded JSON Web Token (JWT) string" [3]. This flow does **not** use
  the client secret.
- **Client secret:** not needed. Google still generates one when you create a
  Web application client and shows it only once [6]. Do not download, copy or
  store it.
- **`sub`:** the local page shows it; a PowerShell command reads it from the
  token on your own computer and stores it in `dotnet user-secrets`.

## 1. Create the Google OAuth client

Use your **personal** Google account, not a Euromaster account. Console names
below are from Google's pages accessed on 2026-10-06 [1][5][7]; Google renames
menus from time to time.

1. Open the Google Cloud Console and create a new project for this API, for
   example `rdw-tools`. (Whether a billing account is needed for OAuth only:
   **UNVERIFIED**; Google's pages I read do not mention billing for this.)
2. Open **Google Auth Platform** and click **Get started** [7]. Fill in:
   - **App name**: a distinctive name, for example `RDW Tools`. Google asks to
     avoid names that could be confused with Google products [7].
   - **User support email**: your own address. It is "displayed to users on the
     consent screen" [7]. It stays in Google; do not write it anywhere else.
   - **Audience**: **External**. "Internal" is only for "Projects associated
     with a Google Cloud Organization" [7]; a personal project has none.
     External does **not** open the API: Google lets any Google account sign in,
     but our API still answers `403` to every account that is not in its
     allow-list (ADR-005).
   - **Contact information**: your own address (Google sends project
     notifications there [7]).
   - Agree to the policy and click **Create**.
3. **Audience** page: leave the publishing status on **Testing**. Our page asks
   only for the basic sign-in scopes. For apps that request only "name, email
   address, and user profile (through the `userinfo.email, userinfo.profile,
   openid` scopes or their OpenID Connect equivalents)", Google says "your
   users do not need to be in the trusted user list, they will not see a
   warning message, and their authorizations will not expire after 7 days"
   [5]. You do not need to add test users. (Whether the "Sign in with Google"
   button requests exactly these scopes: **UNVERIFIED**; if Google shows an
   "app not verified" warning or refuses the second account, add both accounts
   as test users on this page.)
4. **Data access** page: add nothing.
5. **Clients** page: click **Create client** [1].
   - **Application type**: **Web application** [1].
   - **Name**: for example `rdw-api` (only visible to you).
   - **Authorized JavaScript origins**: add both `http://localhost` and
     `http://localhost:8765`. Google: "For local tests or development add both
     `http://localhost` and `http://localhost:<port_number>`" [1]. `8765` is the
     port of the test page below.
   - **Authorized redirect URIs**: leave empty. They are only needed when
     credentials are returned "using a redirect to an endpoint you host rather
     than through a JavaScript callback" [1]; we use the callback.
   - Click **Create**.
6. Google shows the **client ID** (ends in `.apps.googleusercontent.com`) and a
   client secret. Copy only the client ID. **Ignore the client secret**: the
   flow below never uses it. Google shows it only "at the time of their
   creation" and afterwards only the last four characters [6].

Good to know:

- "OAuth 2.0 clients that have been inactive for six months are automatically
  deleted", with a notice 30 days before [6]. If that happens, create a new
  client and update the configuration.
- The client ID is not a secret (it is visible in every token), but it is still
  kept out of the repository, like all real values.

## 2. Get a real Google ID token

### Requirements for our case

- The token's `aud` must be **our** client ID; the API rejects anything else
  with `401` (ADR-005, `GoogleAuthentication.cs`).
- No client secret.
- Works on your Windows computer, and the token must not pass through any site
  other than Google.

### Options compared

| Option | Client secret? | `aud` is our client? | Pros | Cons |
| --- | --- | --- | --- | --- |
| **A. Local page with the "Sign in with Google" button** [1][2][3] | **No** [1][3] | Yes, the page uses our client ID | Official, documented browser flow; the same client and flow a later web UI will use; no extra software (PowerShell and a browser) | Needs a browser and a small local page; the token is moved by copy and paste |
| B. OpenID Connect implicit flow in the browser (`response_type=id_token`) [4] | No, per the OIDC page [4] | Yes | No JavaScript library | Needs a registered redirect URI and a page to read the token from the URL `#fragment` [4]; the token ends up in the browser address bar and history; the exact accepted `response_type` values for an ID token only: **UNVERIFIED** |
| C. Desktop app with loopback redirect and PKCE [8] | **Yes**: the token exchange lists `client_secret`, "obtained from the Cloud Console Clients page" [8] | No: a *Desktop app* is a second client with its own client ID [8] | Works from a script | Needs a secret; the API would have to accept a second audience (a design change) |
| D. Device flow ("TVs and Limited Input devices") [9] | **Yes**: polling requires `client_id`, `client_secret`, `device_code`, `grant_type` [9] | No: a separate client type [9] | Works in a terminal | Needs a secret; the documented token response lists no `id_token` [9]; second audience |
| E. OAuth 2.0 Playground with your own credentials [10] | **Yes**: you enter the client ID **and** secret into the Playground [10] | Yes | No local files | Needs a secret and an extra redirect URI; the token is shown on a Google web page |
| F. `gcloud auth print-identity-token` [11] | No | **No** for a user account (**UNVERIFIED**: the page does not say which `aud` a user token gets or whether `--audiences` works for user accounts [11]) | One command | Needs the Google Cloud CLI; the page documents `--audiences` for service accounts only in its examples [11] |

### Recommendation: option A

It is the only option that is documented by Google for exactly our client type,
needs no client secret and gives a token with our client ID as `aud`. Options
C, D and E need a client secret, so they are ruled out by the rules of this
task. B works in theory but leaves the token in browser history.

What the official documentation **does** cover: creating a Web application
client with localhost origins [1], showing the button [2], and that the callback
receives the ID token [3]. What it **does not** cover: using that ID token as a
credential for your own API with `curl` (ADR-005 already records this choice
and its trade-off), how long the token is valid (**UNVERIFIED**; the OIDC page
only says to check `exp` [4]), and a supported way to get an ID token for our
client from a script without a browser and without a secret (none found). The
last point stays open for a future CLI or script mode; it does not block this
issue.

### Set up the test page (once)

Keep these files **outside** the repository, so the client ID can never be
committed. Example folder: `%USERPROFILE%\rdw-token-page`.

`index.html` (replace `YOUR_CLIENT_ID.apps.googleusercontent.com` with your
client ID). It follows Google's button example [2] and JavaScript reference
[3]:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Google ID token for local API tests</title>
<script src="https://accounts.google.com/gsi/client" async></script>
</head>
<body>
<h1>Google ID token for local API tests</h1>
<p>Sign in with the Google account you want to test. The token stays in this page and your clipboard.</p>
<div id="g_id_onload"
     data-client_id="YOUR_CLIENT_ID.apps.googleusercontent.com"
     data-callback="onGoogleCredential"
     data-auto_prompt="false"></div>
<div class="g_id_signin" data-type="standard"></div>
<div id="result" hidden>
  <p>Account <code>sub</code>: <code id="sub"></code></p>
  <p><code>hd</code> claim: <code id="hd"></code></p>
  <p>Token expires at: <span id="exp"></span> (local time)</p>
  <p><button id="copy" type="button">Copy token to clipboard</button> <span id="copied"></span></p>
</div>
<script>
  let token = '';
  function onGoogleCredential(response) {
    token = response.credential;
    const part = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const bytes = Uint8Array.from(atob(part), c => c.charCodeAt(0));
    const claims = JSON.parse(new TextDecoder().decode(bytes));
    document.getElementById('sub').textContent = claims.sub;
    document.getElementById('hd').textContent = claims.hd || '(none: personal Google account)';
    document.getElementById('exp').textContent = new Date(claims.exp * 1000).toLocaleString();
    document.getElementById('copied').textContent = '';
    document.getElementById('result').hidden = false;
  }
  document.getElementById('copy').addEventListener('click', async () => {
    await navigator.clipboard.writeText(token);
    document.getElementById('copied').textContent = 'Copied.';
  });
</script>
</body>
</html>
```

`serve.ps1`. The page must come from `http://localhost`, not from a file
(`HTTP is only allowed when using localhost during development` [2]), and
Google asks for `Referrer-Policy: no-referrer-when-downgrade` when testing on
`http` and localhost [1]. This script serves the page with that header, only on
your own computer:

```powershell
# Serves index.html from this folder on http://localhost:8765/ until you press Ctrl+C.
# Only reachable from this computer. Sets the Referrer-Policy that Google asks for on http://localhost.
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add('http://localhost:8765/')
$listener.Start()
Write-Host 'Open http://localhost:8765/ in your browser. Press Ctrl+C to stop.'
$page = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'index.html'))
try {
    while ($listener.IsListening) {
        $pending = $listener.GetContextAsync()
        while (-not $pending.Wait(500)) { }
        $context = $pending.Result
        $response = $context.Response
        if ($context.Request.Url.AbsolutePath -eq '/') {
            $response.ContentType = 'text/html; charset=utf-8'
            $response.Headers.Add('Referrer-Policy', 'no-referrer-when-downgrade')
            $response.OutputStream.Write($page, 0, $page.Length)
        } else {
            $response.StatusCode = 404
        }
        $response.Close()
    }
} finally {
    $listener.Stop()
}
```

Checked on 2026-10-06 without Google: the script serves the page with that
header and answers `404` for anything else. The sign-in itself could not be
tried, because that needs your client and account.

### Get a token

1. In a PowerShell window: `powershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\rdw-token-page\serve.ps1"`
   (`Bypass` applies only to this one run).
2. Open `http://localhost:8765/` and click **Sign in with Google**. Choose the
   account to test.
3. The page shows the `sub`, whether there is an `hd` claim (a personal account
   has none [4]), and when the token expires. Click **Copy token to clipboard**.

For a second account, use **Use another account** in the Google window, or open
the page in a private window (**UNVERIFIED** whether the button works in a
private window with third-party cookies blocked; if not, use another browser
profile).

## 3. Find your `sub` without pasting the token anywhere

The token is three base64url parts separated by dots; the middle part holds the
claims. Both ways below decode it **on your own computer**:

- **On the test page**: it already shows the `sub` (step 2.3).
- **In PowerShell**, straight from the clipboard (in the PowerShell window
  where you will run the checks, in the repository folder):

  ```powershell
  $token = Get-Clipboard
  $payload = $token.Split('.')[1].Replace('-', '+').Replace('_', '/')
  $payload += '=' * ((4 - $payload.Length % 4) % 4)
  $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
  $claims | Select-Object iss, aud, sub, hd, @{ n = 'expires'; e = { [DateTimeOffset]::FromUnixTimeSeconds($_.exp).LocalDateTime } }
  ```

  Check that `iss` is `https://accounts.google.com` or `accounts.google.com`, and
  that `aud` is your client ID [4]. Checked on 2026-10-06 with a made-up token.

Decoding does not check the signature; the API does that. Here it is only used
to read your own `sub`. PowerShell history saves the commands, not the token.
Windows clipboard history (Win+V), if turned on, may keep the token until it
expires; clear it there afterwards.

## 4. Configure `dotnet user-secrets`

Run in the repository folder, in the same PowerShell window (so `$claims` is
still there). User secrets live in your user profile, never in the repository;
they are for development only and not encrypted [12].

```powershell
# The client ID (the expected "aud").
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_CLIENT_ID.apps.googleusercontent.com" --project src/Rdw.Api

# Euromaster stays denied for now (ADR-005 rollout order): no hosted domains.
dotnet user-secrets remove "Authorization:AllowedHostedDomains:0" --project src/Rdw.Api

# Check: only the client ID, no AllowedSubjects and no AllowedHostedDomains yet.
dotnet user-secrets list --project src/Rdw.Api
```

`list` prints the values; do not copy its output anywhere. If the domain was
never set, `remove` prints `Cannot find '...' in the secret store`; that is
fine.

Add your `sub` only **after** check 3 below, directly from the decoded token so
you never type or copy it:

```powershell
dotnet user-secrets set "Authorization:AllowedSubjects:0" $claims.sub --project src/Rdw.Api
```

## 5. Manual checks

Start the API in a second PowerShell window:

```powershell
dotnet run --project src/Rdw.Api --launch-profile http
```

Restart it (Ctrl+C, then the same command) after every user-secrets change.

In Windows PowerShell, `curl` is a different command; always type
**`curl.exe`**. The commands below print only the status code, so the output can
be reported as is. `$token` must hold the token of the account in that row
(`$token = Get-Clipboard` after copying it on the test page).

```powershell
$url = 'http://localhost:5064/api/v1/vehicles/X998ZG'

# Status code only, with a token:
curl.exe -s -o NUL -w "%{http_code}`n" -H "Authorization: Bearer $token" $url

# Status code only, without a token:
curl.exe -s -o NUL -w "%{http_code}`n" $url

# Status code and the WWW-Authenticate header, without a token:
curl.exe -s -o NUL -D - $url | Select-String 'HTTP/|WWW-Authenticate'

# Health, without a token:
curl.exe -s -o NUL -w "%{http_code}`n" http://localhost:5064/health
```

| # | Check | Setup | Expected |
| --- | --- | --- | --- |
| 1 | `/health`, no token | — | `200` |
| 2 | Vehicle, no token | — | `401`, header `WWW-Authenticate: Bearer` |
| 3 | Vehicle, your token | `AllowedSubjects` empty | `403` |
| 4 | Vehicle, your token | your `sub` added (section 4), API restarted | `200` |
| 5 | Vehicle, token of **another** personal Google account | your `sub` still the only one | `403` |
| 6 | Vehicle, your token after it expired | wait until the expiry time shown, **plus 5 minutes** | `401` |

Check 6: the API accepts a token up to 5 minutes after `exp`, because the
default clock skew of the token validation is "300 seconds (5 minutes)" [13]
and our code does not change it. Get a fresh token for any further tests.

If check 4 gives `401` instead of `200`, the `aud` probably does not match: compare
the decoded `aud` with the configured client ID. Successful lookups count against
the rate limit (10 per user per minute, README); `401` and `403` do not.

### Reporting

Report in issue #65 as status codes only, for example:

```text
1: 200
2: 401 (WWW-Authenticate: Bearer present)
3: 403
4: 200
5: 403
6: 401
```

No tokens, `sub` values, client IDs, email addresses, screenshots of the test
page, or `user-secrets list` output.

### Afterwards

- Stop `serve.ps1` with Ctrl+C. You can keep the folder for later tests.
- The second account's `sub` was never stored, so there is nothing to remove.
- Your own `sub` stays in user secrets for local testing. The production
  configuration is set during deployment, not from this guide.

## Sources

Accessed 2026-10-06.

1. Google for Developers, "Get your Google API client ID" (updated 2026-04-06): <https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid>
2. Google for Developers, "Display the Sign in with Google button" (updated 2025-05-19): <https://developers.google.com/identity/gsi/web/guides/display-button>
3. Google for Developers, "Sign in with Google JavaScript API reference" (updated 2026-09-01): <https://developers.google.com/identity/gsi/web/reference/js-reference>
4. Google for Developers, "OpenID Connect | Sign in with Google" (updated 2026-06-15): <https://developers.google.com/identity/openid-connect/openid-connect>
5. Google Cloud Platform Console Help, "Manage App Audience": <https://support.google.com/cloud/answer/15549945>
6. Google Cloud Platform Console Help, "Manage OAuth Clients": <https://support.google.com/cloud/answer/15549257>
7. Google Cloud Platform Console Help, "Get started with the Google Auth Platform": <https://support.google.com/cloud/answer/15544987>
8. Google for Developers, "OAuth 2.0 for iOS & Desktop Apps" (updated 2026-09-14): <https://developers.google.com/identity/protocols/oauth2/native-app>
9. Google for Developers, "OAuth 2.0 for TV and Limited-Input Device Applications" (updated 2026-09-14): <https://developers.google.com/identity/protocols/oauth2/limited-input-device>
10. Google for Developers, AdMob API, "OAuth Playground" (updated 2026-10-05): <https://developers.google.com/admob/api/v1/how-tos/playground>
11. Google Cloud SDK reference, "gcloud auth print-identity-token" (updated 2026-05-27): <https://docs.cloud.google.com/sdk/gcloud/reference/auth/print-identity-token>
12. Microsoft Learn, "Safe storage of app secrets in development in ASP.NET Core": <https://learn.microsoft.com/aspnet/core/security/app-secrets>
13. Microsoft Learn, "TokenValidationParameters.DefaultClockSkew Field": <https://learn.microsoft.com/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters.defaultclockskew>
