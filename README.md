# Identity-Service

A pure gRPC service responsible for storing and managing user data. It does not talk to the frontend directly — it is called internally by Auth-Api and Profile-Api.

---

## What it does

- Stores user accounts in Azure SQL Database
- Hashes passwords with BCrypt
- Manages email verification status
- Assigns and returns user roles (Student, Admin)
- Publishes to Azure Service Bus on signup so Verification-Service can send a confirmation email

---

## Tech stack

- .NET 10
- ASP.NET Core gRPC
- Entity Framework Core + Azure SQL
- Azure Service Bus
- BCrypt.Net

---

## Architecture

```
Auth-Api  ──gRPC──►  Identity-Service  ◄──gRPC──  Profile-Api
                           │
                      Azure SQL DB
                           │
                      Service Bus  ──►  Verification-Service
```

Identity-Service does not make authentication decisions — it answers questions. Auth-Api orchestrates the login flow and coordinates with Token-Service for JWT generation.

---

## gRPC methods

| Method | Called by | Description |
|--------|-----------|-------------|
| `SignUp` | Auth-Api | Creates user, assigns Student role, publishes to Service Bus |
| `Authenticate` | Auth-Api | Verifies email + password, returns user info |
| `CheckEmailConfirmStatus` | Auth-Api | Returns whether a user exists and if email is confirmed |
| `ChangeEmailConfirmStatus` | Auth-Api | Sets email confirmed true or false |
| `GetRoles` | Auth-Api | Returns list of roles for a user |
| `GetUser` | Profile-Api | Returns full user profile by ID |
| `UpdateUser` | Profile-Api | Updates name, email, phone, description, profile picture URL |
| `DeleteUser` | Auth-Api | Deletes a user account |

---

## Database

Three tables managed by Entity Framework Core migrations:

- `Users` — stores credentials and profile fields
- `Roles` — Student and Admin (seeded on migration)
- `UserRoles` — join table for the many-to-many relationship

---

## User fields

| Field | Type | Notes |
|-------|------|-------|
| `Id` | Guid | Auto-generated |
| `Email` | string | Unique, required |
| `PasswordHash` | string | BCrypt hashed, never exposed |
| `FirstName` | string | Optional at signup |
| `LastName` | string | Optional at signup |
| `PhoneNumber` | string | Optional |
| `Description` | string | Optional, max 500 chars |
| `ProfilePictureUrl` | string | URL from Image-Service |
| `IsEmailVerified` | bool | False until email confirmed |
| `CreatedAt` | DateTime | Auto-set on creation |
| `UpdatedAt` | DateTime | Updated on every change |

---

## Setup

### 1. Clone the repo

```bash
git clone https://github.com/cms25-aspnet2-grupp-3/IdentityService
```

### 2. Configure appsettings

Copy `appsettings.example.json` to `appsettings.json` and fill in:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "YOUR_AZURE_SQL_CONNECTION_STRING",
    "ServiceBus": "YOUR_AZURE_SERVICE_BUS_CONNECTION_STRING"
  },
  "ServiceBus": {
    "VerificationQueueName": "verification-queue"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Grpc": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

### 3. Run migrations

```bash
Add-Migration InitialCreate
Update-Database
```

### 4. Run the service

```bash
dotnet run
```

The service listens on `http://0.0.0.0:8080` (HTTP/2 only).

---

## Connecting from another service

Add `identity.proto` to your project as a gRPC client:

```xml
<Protobuf Include="Protos\identity.proto" GrpcServices="Client" />
```

Register the client in `Program.cs`:

```csharp
builder.Services.AddGrpcClient<IdentityGrpcService.IdentityGrpcServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["IdentityService:Url"]!);
});
```

Add the URL to `appsettings.json`:

```json
"IdentityService": {
  "Url": "https://identityservice.politewave-9d97f858.spaincentral.azurecontainerapps.io"
}
```

---

## Service Bus message on signup

When a user signs up, this message is published to `verification-queue`:

```json
{
  "UserId": "some-guid",
  "Email": "user@example.com",
  "CreatedAt": "2026-05-12T10:00:00Z"
}
```

Verification-Service subscribes to this queue and sends the confirmation email.
