# OpenShelf 📚

OpenShelf is a self-hosted book recommendation and discovery platform. Track books, series, and audiobooks, share recommendations, and curate "Staff Picks" — all from a clean, modern interface.

## Features

- **Book & Series Tracking** — Auto-fetches metadata from Google Books, Open Library, Audible, and Goodreads.
- **Series Discovery** — Automatically discovers and links all books in a series.
- **Audiobook Support** — Narrator info, listening length, and retail links from Audible/Amazon.
- **Admin Dashboard** — Secure admin area to manage recommendations, users, and site settings.
- **Staff Picks** — Highlight specific books as curated recommendations.
- **Dynamic Filters** — Filter by Genre, Narrator, or Recommender; fuzzy search by Title/Author.
- **View Options** — Toggle between detailed lists and a compact mini-grid view.
- **Read-Together Chat** — Comment threads on individual books.
- **Import/Export** — Bulk import via CSV and full database export.
- **Google SSO** — Optional Google Sign-In for public users (configurable via Admin UI).
- **Require Login** — Optional login gate that requires authentication before accessing the shelf.
- **Feature Flags** — Toggle individual features on/off from Admin Settings (Chat, Public Import, Metadata Refresh, etc.).
- **Modern UI** — Responsive design with Dark Mode, Bootstrap 5, and Bootstrap Icons.

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Framework | ASP.NET Core 10.0 Razor Pages |
| Database | SQLite (Entity Framework Core) |
| Styling | Bootstrap 5 + Bootstrap Icons |
| Auth (Admin) | Cookie-based local authentication (BCrypt) |
| Auth (Public) | Optional Google OAuth 2.0 SSO |
| Scraping | HtmlAgilityPack (Audible, Goodreads) |
| CSV | CsvHelper |

## Getting Started

1. **Clone the repo**
   ```bash
   git clone https://github.com/Skitty4fingers/OpenShelf.git
   cd OpenShelf
   ```

2. **Run the application**
   ```bash
   dotnet run
   ```

3. **Login to Admin**
   - Navigate to `/Admin/Login`
   - Default credentials: `admin` / `admin`
   - **Change these immediately** in Admin → Users.

4. **Configure Settings** (optional)
   - Navigate to Admin → Settings to configure API keys, feature flags, and authentication providers.

## Authentication

OpenShelf supports two independent authentication layers:

| Layer | Purpose | Scheme |
|-------|---------|--------|
| **Admin Auth** | Protects the admin dashboard | Local cookie auth (`CookieAuth`) |
| **Public Auth** | Optional Google SSO for public users | Google OAuth 2.0 (`ExternalAuth`) |

- **Anonymous mode** (default): No sign-in required. Users enter their name manually when recommending books or chatting.
- **Google SSO**: When enabled, users can sign in with their Google account. Their name auto-populates in "Recommended By" and chat author fields.
- **Require Login**: When enabled alongside an auth provider, visitors must sign in before accessing the shelf.

See [DEPLOYMENT.md](DEPLOYMENT.md) for detailed setup instructions including Google OAuth configuration.

## License

GNU AGPLv3
