# OpenShelf 📚

OpenShelf is a self-hosted book recommendation and discovery platform. It allows users to track their favorite books, series, and audiobooks, and share recommendations with a curated "Staff Pick" system.

## Features

- **Book & Series Tracking**: Automatically fetches metadata for books and series using Google Books API.
- **Audiobook Support**: Connects with Audible/Amazon for narrator and listening length info.
- **Admin Dashboard**: Secure admin area to manage recommendations.
- **Staff Picks**: Highlight specific books as "Staff Picks".
- **Dynamic Filters**: Filter by Genre, Narrator, or Recommender; fuzzy search by Title/Author.
- **View Options**: Toggle between detailed lists and a mini-grid view.
- **Import/Export**: Bulk import via CSV and database export capabilities.
- **Modern UI**: Clean, responsive interface with Dark Mode support.

## Tech Stack

- **Framework**: ASP.NET Core 10.0 Razor Pages
- **Database**: SQLite (Entity Framework Core)
- **Styling**: Bootstrap 5 + Bootstrap Icons
- **Authentication**: Cookie-based Auth

## Getting Started

1. **Clone the repo**
   ```bash
   git clone https://github.com/yourusername/openshelf.git
   cd openshelf
   ```

2. **Run the application**
   ```bash
   dotnet run
   ```

3. **Login to Admin**
   - Go to `/Admin/Login`
   - Default credentials: `admin` / `admin`

## License

GNU AGPLv3
