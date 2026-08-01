# LinkyShrinky
A very lightweight URL shortener, made with ASP.NET Core.

**LinkyShrinky** uses approximately 20 MB of RAM and negligible CPU usage, making it a great choice for lightweight single-core VPSs.

# Purpose
I created this project to replace my existing URL shortener, which used a MySQL database requiring too much RAM (over 100 MB) on my VPS.

**LinkyShrinky** uses a simple json file in place of a typical database.

## Usage
**LinkyShrinky** provides a simple admin dashboard. Log in at `/admin`

**Note**: You may use the environment variable `ADMIN_PAGE` to define another path for `/admin` (for example `administration` or `dashboard`)

During the first login, any credentials used will used to create the admin account.

Alternatively, you may use the API directly by collecting an API key from the admin dashboard. The API provides 3 methods:
- GET `/api/links`
- POST `/api/links`
- DELETE `/api/links/{slug}`

## GET `/api/links`
Returns a list of all shortened links.

### Response
```
{
    "yt": {
        "redirect": "https://www.youtube.com",
        "hits": 5,
        "created": "2026-07-28T02:24:22.4084883+00:00",
        "lastHit": "2026-07-31T02:19:24.2379011+00:00"
    },
    "ttv": {
        "redirect": "https://www.twitch.tv",
        "hits": 2,
        "created": "2026-07-28T02:24:22.6733277+00:00",
        "lastHit": "2026-07-31T02:19:29.1706539+00:00"
    }
}
```

## POST `/api/links`
Creates a new shortened link.

### Request Payload
```
{
    "redirect": "https://example.com",
    "slug": "ex"
}
```
**Note**: `slug` is optional. A random slug will be generated automatically if it is not provided.

### Response
```
{
    "success": true,
    "slug": "ca6",
    "redirect": "https://google.com/",
    "error": null
}
```

## DELETE `/api/links/{slug}`
Deletes an existing shortened link.

### Response
`204 No Content`

# Docker
This service is designed to be used with Docker.

There are several things you should consider configuring.

## Environment Variable
`ADMIN_PAGE` will change the path for the admin dashboard. This value defaults to `admin` so the default admin dashboard is accessible at `example.com/admin`

## Example docker-compose.yml
```
services:
  linkyshrinky:
    image: ghcr.io/blake502/linkyshrinky:latest
    container_name: linkyshrinky
    ports:
      - 8080:8080
    volumes:
      - ./config:/app/config
      - ./keys:/home/app/.aspnet/DataProtection-Keys
    environment:
      - ADMIN_PAGE=admin #The admin dashboard path
```

# TODO
- Clean up API
- Clean up first time setup admin account
- Add logout path
- Fix admin path reservation
- File locking
- Make the admin dashboard prettier
