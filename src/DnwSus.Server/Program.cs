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
builder.Services.AddSingleton<AgentDistribution>();
builder.Services.AddHostedService<ScheduleService>();
builder.Services.AddHostedService<AgentShareService>();
builder.Services.AddRazorPages();
// Agent (.NET Framework 4.6.2, JavaScriptSerializer) schreibt Eigenschaftsnamen in PascalCase
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

// Zugriffsschutz fürs Dashboard: von überall erreichbar (soweit die Firewall es zulässt), aber nur für Domänen-Admins.
// Auf dem Server selbst meldet Windows den angemeldeten Benutzer per SSO an (keine Abfrage); von fremden Rechnern
// erscheint eine Anmeldeabfrage. Wer angemeldet, aber kein Admin ist, bekommt erneut die Abfrage (anderes Konto).
// Agent-Schnittstelle und MSI-Download sind auch von Clients erreichbar und haben eigene Regeln (AgentApi).
app.Use(async (context, next) =>
{
    if (LocalAccess.IsRemoteAllowedPath(context.Request.Path))
    {
        await next();
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
        app.Logger.LogInformation("Dashboard-Zugriff abgelehnt für {User}: {Problem}", identity.Name, problem);
        // Angemeldet, aber nicht in der Admin-Gruppe → erneute Anmeldeabfrage, damit ein Domänen-Admin-Konto genutzt werden kann.
        await context.ChallengeAsync();
        return;
    }

    await next();
});

app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapAgentApi();

app.Run();
