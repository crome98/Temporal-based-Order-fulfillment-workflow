using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Demo-only: a real service persists this key pair (e.g. in a secret store) instead of
// generating a fresh one on every startup, and the private half never leaves the issuer.
var signingRsa = RSA.Create(2048);
var privateKey = new RsaSecurityKey(signingRsa) { KeyId = "demo-key-1" };

var publicRsa = RSA.Create();
publicRsa.ImportParameters(signingRsa.ExportParameters(includePrivateParameters: false));
var publicKey = new RsaSecurityKey(publicRsa) { KeyId = "demo-key-1" };

var demoUsers = new Dictionary<string, string> { ["alice"] = "password123" };

app.MapPost("/login", (LoginRequest request) =>
{
    if (!demoUsers.TryGetValue(request.Username, out var password) || password != request.Password)
    {
        return Results.Unauthorized();
    }

    var claims = new[] { new Claim(ClaimTypes.Name, request.Username) };
    var credentials = new SigningCredentials(privateKey, SecurityAlgorithms.RsaSha256);
    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(15),
        signingCredentials: credentials);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { token = jwt });
});

app.MapGet("/validate", (HttpRequest request) =>
{
    var header = request.Headers["Authorization"].ToString();
    if (!header.StartsWith("Bearer "))
    {
        return Results.Unauthorized();
    }

    var token = header["Bearer ".Length..];
    var handler = new JwtSecurityTokenHandler();
    try
    {
        handler.ValidateToken(token, new TokenValidationParameters
        {
            IssuerSigningKey = publicKey,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
        }, out _);
        return Results.Ok();
    }
    catch (SecurityTokenException)
    {
        return Results.Unauthorized();
    }
});

app.Run();

record LoginRequest(string Username, string Password);
