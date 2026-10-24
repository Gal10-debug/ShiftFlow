using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Domain;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
 builder.Configuration.GetConnectionString("Database") ?? "Host=localhost;Database=shiftmanager;Username=shiftmanager"));
builder.Services.AddDataProtection();
builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<Guid>>()
 .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<RequestLocalizationOptions>(options => options
 .SetDefaultCulture("he-IL").AddSupportedCultures("he-IL").AddSupportedUICultures("he-IL"));
var app = builder.Build();
app.UseRequestLocalization();
app.Run();
