# Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore (preview)

Hosts the BPMN MCP server (`Hasseware.AspNetCore.Components.Bpmn.Mcp`) over streamable HTTP in an ASP.NET Core app.

```csharp
builder.Services.AddBpmnFileDocumentStore("/data/diagrams");       // or any IBpmnDocumentStore
builder.Services
    .AddBpmnMcpHttp(
        http => http.TenantClaimType = "tid",                       // who may see which documents: from the caller's claims
        mcp => mcp.AllowWrites = true);                             // optional; read-only by default. Also offers the writing tools

builder.Services.AddAuthentication(/* your scheme, e.g. JwtBearer */);
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapBpmnMcp("/mcp");
```

- **Stateless.** Every request stands alone (`HttpServerSessionMode.Stateless`, set explicitly), so the server scales out
  behind any load balancer; nothing is kept between calls and there is no session to pin. Concurrent writers are
  arbitrated by the store's revision check.
- **Authenticated.** The endpoint requires an authenticated caller; use the SDK's `AddMcp` OAuth resource-metadata handler
  or any scheme you like, and `AuthorizationPolicy` to require a scope or role. The tenant is the caller's
  `TenantClaimType` claim and a caller without it gets 403. Set `SingleTenant` instead when everyone shares one space;
  you must choose one, or `MapBpmnMcp` throws at startup. `AllowAnonymous` exists for a loopback developer endpoint and
  needs `SingleTenant`.
- **Per-document control.** Register an `IBpmnDocumentAuthorizer` (it can read the caller from `IHttpContextAccessor`) for
  owners and ACLs; a refusal looks like a missing document.
- **DNS rebinding and cross-origin pages.** `Host` must be a loopback name unless you list yours in `AllowedHosts`, and a
  request that carries an `Origin` must come from the endpoint's own origin or one in `AllowedOrigins`; anything else is 403.
- **Size and rate.** Bodies over `MaxRequestBodyBytes` (1 MB) are 413. `RateLimitPolicy` names a policy from
  `AddRateLimiter`.
- **Transport security.** The library never serves plain HTTP off the machine on its own: bind to loopback, or terminate
  TLS in front (a reverse proxy, or Kestrel with a certificate), and set `AllowedHosts` to the public name.
- **Audit.** Each call is logged with the caller's id, the tool, the document id and the revisions before and after,
  never the content.
