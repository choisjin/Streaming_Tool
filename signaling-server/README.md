# Streaming_tool — Signaling Server

WebRTC SDP/ICE 중계만 담당하는 가벼운 WebSocket 서버. Mac mini에서 항시 운영.

## Run locally

```bash
cd signaling-server
npm install
npm run dev          # http://localhost:8080/ws
```

Health check: `GET http://localhost:8080/health` -> `ok`

## Deploy to Mac mini

### 1. Install Node 20+

```bash
# Homebrew
brew install node@20
```

### 2. Run as launchd service

`/Library/LaunchDaemons/com.streamingtool.signaling.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>com.streamingtool.signaling</string>
  <key>ProgramArguments</key>
  <array>
    <string>/opt/homebrew/bin/node</string>
    <string>/Users/YOUR_USER/streaming-tool/signaling-server/src/server.js</string>
  </array>
  <key>WorkingDirectory</key>
  <string>/Users/YOUR_USER/streaming-tool/signaling-server</string>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>EnvironmentVariables</key>
  <dict><key>PORT</key><string>8080</string></dict>
  <key>StandardOutPath</key><string>/var/log/streamingtool-signaling.log</string>
  <key>StandardErrorPath</key><string>/var/log/streamingtool-signaling.log</string>
</dict>
</plist>
```

```bash
sudo launchctl load -w /Library/LaunchDaemons/com.streamingtool.signaling.plist
```

### 3. Reverse proxy with TLS (nginx + Let's Encrypt)

`/opt/homebrew/etc/nginx/servers/signal.conf`:

```nginx
server {
  listen 443 ssl http2;
  server_name signal.YOUR-DOMAIN.com;

  ssl_certificate     /etc/letsencrypt/live/signal.YOUR-DOMAIN.com/fullchain.pem;
  ssl_certificate_key /etc/letsencrypt/live/signal.YOUR-DOMAIN.com/privkey.pem;

  location /ws {
    proxy_pass http://127.0.0.1:8080;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_read_timeout 86400;
  }

  location /health {
    proxy_pass http://127.0.0.1:8080;
  }
}

server {
  listen 80;
  server_name signal.YOUR-DOMAIN.com;
  return 301 https://$host$request_uri;
}
```

```bash
brew install nginx certbot
sudo certbot certonly --standalone -d signal.YOUR-DOMAIN.com
brew services start nginx
```

### 4. DNS

A record: `signal.YOUR-DOMAIN.com -> Mac mini public IP`

ISP가 IPv4를 매번 갱신하면 dynamic DNS 사용 (e.g. duckdns, cloudflare DDNS).
