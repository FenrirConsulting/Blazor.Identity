# Blazor.Identity

## Overview
Blazor.Identity serves as the centralized authentication service for the Blazor Enterprise Architecture. It provides Single Sign-On (SSO) capabilities across all applications within the same domain, ensuring a seamless authentication experience for users.

## Features
- 🔐 Single Sign-On (SSO) implementation
- 🔑 Centralized authentication management
- 🌐 Cross-application session handling
- 🔒 Secure cookie management
- 📝 Authentication audit logging
- ⚡ High-performance token management

## Technical Stack
- ASP.NET Core 7.0+
- Entity Framework Core
- Azure AD Integration
- SQL Server
- JWT Token Authentication

## Architecture
This service is part of a larger Blazor Enterprise Architecture:
- Provides authentication endpoints for all applications
- Manages token lifecycle and validation
- Handles Azure AD integration
- Maintains session state across applications

## Setup
1. Configure Azure AD:
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "yourdomain.com",
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "CallbackPath": "/signin-oidc"
  }
}
```

2. Configure Database:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "your-connection-string"
  }
}
```

3. Configure CORS for your domain:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("SameDomain",
        builder => builder
            .WithOrigins("https://*.yourdomain.com")
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader());
});
```

## API Endpoints

### Authentication
```
POST /api/auth/login
POST /api/auth/logout
GET  /api/auth/user
POST /api/auth/refresh
```

### Session Management
```
GET  /api/session/validate
POST /api/session/extend
```

## Security Considerations
- Implements secure cookie policies
- Uses HTTP-only cookies
- Implements CSRF protection
- Requires HTTPS
- Implements rate limiting
- Logs security events

## Integration
Other applications in the ecosystem integrate with Blazor.Identity through the Blazor.RCL package, which provides:
- Pre-configured authentication services
- HTTP client configuration
- Token management
- Session handling

## Deployment
1. Deploy to IIS
2. Configure SSL certificate
3. Set up Azure AD application
4. Configure authentication settings
5. Set up database
6. Configure logging

## Monitoring
- Application Insights integration
- Authentication attempt logging
- Session tracking
- Performance metrics
- Error logging

## Contributing
1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Author
Christopher Olson - Senior Security Engineer