# Admin API Smoke Tests

Use the Cloudflare Workers HTTP test tab or any HTTP client.

All admin requests need:

```http
X-Admin-Token: MINI_666
Content-Type: application/json
```

## List Privilege Codes

```http
GET /api/v1/admin/privilege-codes?includeInactive=true
X-Admin-Token: MINI_666
```

The response hides `CodeHash` and returns safe management fields:

- `CodeId`
- `Label`
- `OwnerName`
- `GroupName`
- `Role`
- `Status`: `active`, `paused`, or `revoked`
- `Scopes`
- `LastUsedAt`
- `UsageCount`
- `Notes`

## Get One Privilege Code

```http
GET /api/v1/admin/privilege-codes/{codeId}
X-Admin-Token: MINI_666
```

## Pause a Code

```http
POST /api/v1/admin/privilege-codes/{codeId}/pause
X-Admin-Token: MINI_666
Content-Type: application/json

{
  "reason": "Temporary suspension"
}
```

Paused codes cannot upload or use admin APIs, but can be resumed.

## Resume a Code

```http
POST /api/v1/admin/privilege-codes/{codeId}/resume
X-Admin-Token: MINI_666
Content-Type: application/json

{
  "reason": "Issue resolved"
}
```

Revoked codes cannot be resumed.

## Revoke a Code

```http
POST /api/v1/admin/privilege-codes/{codeId}/revoke
X-Admin-Token: MINI_666
Content-Type: application/json

{
  "reason": "Code leaked"
}
```

Revocation is the recommended block action. The row stays in D1 for audit.

## Update Code Metadata

```http
PATCH /api/v1/admin/privilege-codes/{codeId}
X-Admin-Token: MINI_666
Content-Type: application/json

{
  "label": "CN Official Team",
  "ownerName": "Translator Name",
  "groupName": "CN Group",
  "notes": "Trusted group"
}
```

You can also update:

- `role`
- `scopes`
- `expiresAt`

## Code Events

```http
GET /api/v1/admin/privilege-codes/{codeId}/events?limit=200
X-Admin-Token: MINI_666
```

Events include create, update, pause, resume, revoke, and use.

## Recent Usage

```http
GET /api/v1/admin/privilege-code-usage?limit=200
X-Admin-Token: MINI_666
```

This is the endpoint a future web admin panel can use for "who is using which
privilege code recently".

## Create a Reviewer Code

```http
POST /api/v1/admin/privilege-codes
X-Admin-Token: MINI_666
Content-Type: application/json

{
  "label": "Feedback Reviewer",
  "ownerName": "Reviewer Name",
  "groupName": "Community Review",
  "role": "reviewer",
  "scopes": ["record:verify", "audit:read", "feedback:moderate", "feedback:read_private"],
  "notes": "Can triage player feedback reports"
}
```

The raw code is shown once.

## Create a Player Feedback Report

```http
POST /api/v1/feedback
Content-Type: application/json

{
  "title": "Simplified Chinese package has broken labels",
  "category": "translation_wrong",
  "severity": "normal",
  "body": "Several menu labels show old English text after loading the package.",
  "packageId": "author.examplemod",
  "modName": "Example Mod",
  "language": "ChineseSimplified",
  "gameVersion": "1.6",
  "reporterName": "Player"
}
```

## List Public Feedback

```http
GET /api/v1/feedback?status=open&category=translation_wrong&limit=50
```

## Vote on a Feedback Report

```http
POST /api/v1/feedback/{feedbackId}/vote
Content-Type: application/json

{}
```

## List Feedback as Reviewer

```http
GET /api/v1/admin/feedback?includeHidden=true&limit=100
X-Admin-Token: MINI_666
```

Reviewer privilege codes with `feedback:read_private` can also call this route.

## Moderate Feedback

```http
PATCH /api/v1/admin/feedback/{feedbackId}
X-Admin-Token: MINI_666
Content-Type: application/json

{
  "status": "triage",
  "category": "translation_wrong",
  "severity": "normal",
  "isPublic": true,
  "isPinned": false,
  "assignedTo": "CN reviewer",
  "publicNote": "已收到，正在確認翻譯包內容。"
}
```

Reviewer privilege codes with `feedback:moderate` can also call this route.
