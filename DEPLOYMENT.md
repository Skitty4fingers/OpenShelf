# Deployment Guide 🚀

This guide covers how to deploy OpenShelf to production environments.

## Option 1: Linux Container (Docker) 🐳

This is the recommended way to run OpenShelf on Linux, NAS (Synology/QNAP), or cloud platforms.

### 1. Build the Image
Navigate to the project root and run:
```bash
docker build -t openshelf .
```

### 2. Prepare Data Directory
Create a folder on your host machine to store the persistent database (so you don't lose data when the container restarts).
```bash
mkdir -p /opt/openshelf/data
```

### 3. Run the Container
Map port 80 and the data volume.

**Option A: Named Volume (Easiest)**
```bash
docker run -d --name openshelf -p 80:80 -v openshelf_data:/app/data openshelf
```

**Option B: Path Mapping (Linux/Pro)**
```bash
docker run -d \
  --name openshelf \
  --restart unless-stopped \
  -p 80:80 \
  -v /opt/openshelf/data:/app/data \
  openshelf
```

Your app is now running at `http://localhost` (or your-server-ip).

> [!NOTE]
> The volume stores your database (`openshelf.db`) and security keys (`data/keys`). Keeping the keys persistent ensures that users aren't logged out when the container restarts.

---

## Option 2: Windows IIS 🪟

Use this method to host on a Windows Server.

### 1. Prerequisites
- **Windows Server** with IIS enabled.
- **.NET Core Hosting Bundle**: Download and install the [.NET 10.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) on the server.

### 2. Publish the App
On your development machine, run:
```powershell
dotnet publish -c Release -o c:\deploy\openshelf
```
Copy the contents of `c:\deploy\openshelf` to a folder on your server (e.g., `C:\inetpub\wwwroot\openshelf`).

### 3. Configure IIS
1. Open **IIS Manager**.
2. Right-click **Sites** -> **Add Website**.
   - **Site name**: OpenShelf
   - **Physical path**: `C:\inetpub\wwwroot\openshelf`
   - **Port**: 80 (or your preferred port)
3. Click **OK**.

### 4. Application Pool Settings
1. Go to **Application Pools**.
2. Find the pool created for your site (e.g., `OpenShelf`).
3. Double-click it.
   - **.NET CLR version**: `No Managed Code` (Core handles this internally).
   - **Managed pipeline mode**: `Integrated`.

### 5. File Permissions (CRITICAL) ⚠️
Since OpenShelf uses SQLite, the application needs **Write** access to the database file.

1. Navigate to `C:\inetpub\wwwroot\openshelf` in File Explorer.
2. Right-click the folder -> **Properties** -> **Security**.
3. Click **Edit** -> **Add**.
4. Important: Enter `IIS AppPool\OpenShelf` (replace `OpenShelf` with your actual App Pool name) and click Check Names.
5. Grant **Modify** and **Write** permissions.
6. Click OK.

### 6. Verify
Open a browser and navigate to `http://localhost` (or your server's IP).

---

## Post-Deployment Configuration ⚙️

### First Login
1. Navigate to `/Admin/Login`.
2. Default credentials: `admin` / `admin`.
3. **Change the default password immediately** in Admin → Users.

### Site Settings
Navigate to **Admin → Settings** to configure:

| Setting | Description |
|---------|-------------|
| Google Books API Key | Optional. Increases your Google Books API quota. |
| Data Sources | Toggle Google Books, Open Library, Audible, and Goodreads on/off. |
| Feature Flags | Enable/disable Chat, Public Import, Metadata Refresh, "Get This Book" links. |
| Authentication | Enable Google SSO, configure credentials, and optionally require login. |

---

## Google SSO Setup (Optional) 🔐

Enable Google Sign-In so public users can authenticate with their Google accounts. When signed in, user names are automatically populated in "Recommended By" and chat author fields.

### 1. Create a Google Cloud Project
1. Go to the [Google Cloud Console](https://console.cloud.google.com/).
2. Click the project dropdown at the top → **New Project**.
3. Name it (e.g., "OpenShelf") and click **Create**.
4. Select the new project from the dropdown.

### 2. Configure the OAuth Consent Screen
1. Navigate to **APIs & Services → OAuth consent screen**.
2. Select **External** (unless you have a Google Workspace domain) and click **Create**.
3. Fill in:
   - **App name**: OpenShelf
   - **User support email**: your email
   - **Developer contact**: your email
4. Click **Save and Continue** through Scopes and Test Users (defaults are fine).

### 3. Create OAuth 2.0 Credentials
1. Go to **APIs & Services → Credentials**.
2. Click **+ Create Credentials → OAuth client ID**.
3. Set **Application type** to **Web application**.
4. Name it (e.g., "OpenShelf Web Client").
5. Under **Authorized redirect URIs**, add:
   ```
   https://your-domain.com/signin-google
   ```
   For local testing, also add `http://localhost/signin-google`.
6. Click **Create**.
7. Copy the **Client ID** and **Client Secret**.

### 4. Configure in OpenShelf
1. Go to **Admin → Settings → Authentication Providers**.
2. Toggle **Enable Google Sign-In** on.
3. Paste your **Client ID** and **Client Secret**.
4. Click **Save Settings**.

### 5. Publish the App (Optional)
- While in "Testing" mode, only users you add as test users can sign in.
- To allow anyone with a Google account: go to **OAuth consent screen → Publish App**.
- For private deployments, stay in Testing mode and just add user emails manually.

### 6. Require Login (Optional)
If you want to gate the entire app behind authentication:
1. Enable Google Sign-In first (as above).
2. Toggle **Require Sign-In to Access App** on.
3. Click **Save Settings**.

> [!WARNING]
> If you enable "Require Login" without properly configured Google credentials, users will be unable to access the app. Admin login at `/Admin/Login` is always available as a fallback.

---

## Database Note 💾
By default, the app uses SQLite.
- **Docker**: The database is stored in `/app/data/openshelf.db` (mapped to your host volume).
- **IIS**: The database is stored in the application root folder by default. Ensure backups are included in your regular server backup routine.

---

## Reverse Proxy Notes 🔄

If running behind a reverse proxy (nginx, Caddy, Traefik, etc.):
- OpenShelf automatically handles `X-Forwarded-For` and `X-Forwarded-Proto` headers.
- Ensure your proxy passes these headers so that redirect URIs (especially for Google OAuth) use the correct scheme (`https`).
- Set your Google OAuth redirect URI to match the **public-facing** URL, not the internal container URL.
