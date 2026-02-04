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

```bash
docker run -d \
  --name openshelf \
  --restart unless-stopped \
  -p 80:80 \
  -v /opt/openshelf/data:/app/data \
  openshelf
```

Your app is now running at `http://your-server-ip`.

---

## Option 2: Windows IIS 🪟

Use this method to host on a Windows Server.

### 1. Prerequisites
- **Windows Server** with IIS enabled.
- **.NET Core Hosting Bundle**: Download and install the [.NET 9.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/9.0) on the server.

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
Open a browser and navigate to `http://localhost` (or your servers IP).

---

## Database Note 💾
By default, the app uses SQLite.
- **Docker**: The database is stored in `/app/data/openshelf.db` (mapped to your host volume).
- **IIS**: The database is stored in the application root folder by default. Ensure backups are included in your regular server backup routine.

---



