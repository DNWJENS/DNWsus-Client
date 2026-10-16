using System.Net;
using System.Security.Principal;
using DnwSus.Server;
using DnwSus.Server.Api;
using DnwSus.Server.Data;
using DnwSus.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory   // als Dienst ist das Arbeitsverzeichnis System32
});

builder.Host.UseWindowsService(o => o.ServiceName = "DnwSusServer");

var options = builder.Configuration.GetSection("DnwSus").Get<DnwSusOptions>() ?? new DnwSusOptions();
options.Normalize();
builder.Services.Configure<DnwSusOptions>(builder.Configuration.GetSection("DnwSus"));
builder.Services.PostConfigure<DnwSusOptions>(o => o.Normalize());

var certificate = ServerCertificate.LoadOrCreate(out string certStore);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Listen(IPAddress.IPv6Any, options.Port, listen =>
    {
        // Windows-Anmeldung (Kerberos/NTLM) funktioniert nur mit HTTP/1.1.
        listen.Protocols = HttpProtocols.Http1;
        listen.UseHttps(certificate);
    });
});

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<AdService>();
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton(new ServerIdentity(certificate, options.Port));
builder.Services.AddSingleton<AgentMsiService>();
builder.Services.AddHostedService<ScheduleService>();
builder.Services.AddRazorPages();
// Agent (.NET 4.8, JavaScriptSerializer) schreibt Eigenschaftsnamen in PascalCase
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = null;
    json.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

app.Logger.LogInformation("DNWsus-Server lauscht auf https://{Fqdn}:{Port} – Zertifikat {Thumbprint} aus {Store}",
    ServerCertificate.GetFqdn(), options.Port, certificate.Thumbprint, certStore);

app.UseExceptionHandler("/Error");
app.UseHsts();
app.UseRouting();
app.UseAuthentication();

// Zugriffsschutz: Das Dashboard ist nur auf dem Server selbst und nur für Admins erreichbar.
// Agent-Schnittstelle und MSI-Download sind auch von Clients erreichbar und haben eigene Regeln (AgentApi).
app.Use(async (context, next) =>
{
    if (LocalAccess.IsRemoteAllowedPath(context.Request.Path))
    {
        await next();
        return;
    }

    if (!LocalAccess.IsLocal(context))
    {
        await Deny(context, "Das DNWsus-Dashboard ist nur direkt auf dem Server erreichbar, auf dem der Dienst läuft.");
        return;
    }

    if (context.User.Identity is not WindowsIdentity { IsAuthenticated: true } identity)
    {
        await context.ChallengeAsync();
        return;
    }

    var ad = context.RequestServices.GetRequiredService<AdService>();
    if (!ad.IsAdmin(identity, out string? problem))
    {
        await Deny(context, problem ?? $"Kein Zugriff für {identity.Name}. Das Dashboard ist nur für Mitglieder der Gruppe „{ad.AdminGroupDisplayName}“ freigegeben.");
        return;
    }

    await next();
});

app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapAgentApi();

app.Run();

static Task Deny(HttpContext context, string message)
{
    context.Response.StatusCode = StatusCodes.Status403Forbidden;
    context.Response.ContentType = "text/html; charset=utf-8";
    return context.Response.WriteAsync(
        "<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>DNWsus – Kein Zugriff</title></head>" +
        "<body style=\"font-family:Segoe UI,sans-serif;background:#1e1e2e;color:#cdd6f4;padding:40px\">" +
        "<h2 style=\"color:#f38ba8\">Kein Zugriff</h2><p>" + WebUtility.HtmlEncode(message) + "</p></body></html>");
}
