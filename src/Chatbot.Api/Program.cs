using Microsoft.AspNetCore.Authentication;
using Chatbot.Api.Errors;
using Chatbot.Api.Identity;
using Chatbot.Api.Logging;
using Chatbot.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddScoped<IApplicationErrorLogger, SqlApplicationErrorLogger>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<Chatbot.Api.Identity.LockoutOptions>(
    builder.Configuration.GetSection("Authentication:Lockout"));
builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.PasswordHasher<Chatbot.Api.Identity.IdentityUser>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<IIdentityStore, SqlIdentityStore>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserAdminService>();
builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>("Bearer", _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
