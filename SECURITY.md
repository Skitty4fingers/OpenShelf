# Security Policy

## Authentication Architecture

OpenShelf uses two independent authentication layers:

| Layer | Scheme | Purpose | Storage |
|-------|--------|---------|---------|
| **Admin Auth** | `CookieAuth` | Protects admin dashboard and settings | Local credentials (BCrypt hashed) in SQLite |
| **Public Auth** | `ExternalAuth` | Optional Google SSO for public users | Google OAuth 2.0 tokens via cookie |

### Admin Authentication
- Credentials are stored locally in the SQLite database with BCrypt password hashing.
- Default credentials (`admin`/`admin`) should be changed immediately after first deployment.
- Admin sessions are managed via the `OpenShelf.Auth` cookie.
- Admin login is always accessible at `/Admin/Login`, regardless of the "Require Login" setting.

### Public Authentication (Google SSO)
- Google OAuth Client ID and Secret are stored in the `SiteSettings` table in the database.
- Auth tokens are not stored server-side — Google handles the OAuth flow and OpenShelf stores only the session cookie (`OpenShelf.External`).
- The `ExternalAuth` cookie expires after 30 days.
- Google credentials are read from the database at challenge time, not from configuration files.

## Data Protection
- ASP.NET Core Data Protection keys are persisted to disk (`data/keys/`) to prevent session invalidation across container restarts.
- The `SetApplicationName("OpenShelf")` ensures key isolation.

## Feature Gating
- The **Require Login** flag gates public access behind authentication. When enabled, unauthenticated users are redirected to the sign-in page.
- Admin routes (`/Admin/*`) are exempt from the login gate and protected by their own authorization policy.
- Static assets, login pages, and OAuth callback paths are always accessible.

## Reporting a Vulnerability

If you discover a security vulnerability in OpenShelf, please report it responsibly:

1. **Do not** open a public GitHub issue for security vulnerabilities.
2. Email the maintainer directly or use GitHub's [private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability) feature.
3. Include steps to reproduce and any potential impact.
4. You can expect an initial response within 72 hours.
