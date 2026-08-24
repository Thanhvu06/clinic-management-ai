using ClinicManagement.Api.Middlewares;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Patients.Interfaces;
using ClinicManagement.Application.Specialties.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Schedules.Interfaces;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Infrastructure.Authentication;
using ClinicManagement.Infrastructure.Patients;
using ClinicManagement.Infrastructure.Specialties;
using ClinicManagement.Infrastructure.Doctors;
using ClinicManagement.Infrastructure.Schedules;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ClinicManagementDb")
    ?? throw new InvalidOperationException("Connection string 'ClinicManagementDb' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add Identity
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager<SignInManager<ApplicationUser>>()
.AddDefaultTokenProviders();

// Add JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                success = false,
                message = "Chưa đăng nhập",
                errorCode = "UNAUTHORIZED"
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            return context.Response.WriteAsync(result);
        },
        OnForbidden = context =>
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                success = false,
                message = "Không có quyền",
                errorCode = "FORBIDDEN"
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            return context.Response.WriteAsync(result);
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<ISpecialtyService, SpecialtyService>();
builder.Services.AddScoped<IDoctorService, DoctorService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IChangeRequestService, ChangeRequestService>();
builder.Services.AddScoped<IReceptionService, ReceptionService>();
builder.Services.AddScoped<IDoctorAppointmentService, DoctorAppointmentService>();
builder.Services.AddScoped<IRevisitService, RevisitService>();
builder.Services.AddScoped<ClinicManagement.Application.Admin.Interfaces.IAdminUserService, ClinicManagement.Infrastructure.Admin.AdminUserService>();
builder.Services.AddScoped<ClinicManagement.Application.Admin.Interfaces.IAdminSpecialtyService, ClinicManagement.Infrastructure.Admin.AdminSpecialtyService>();
builder.Services.AddScoped<ClinicManagement.Application.Admin.Interfaces.IAdminDoctorService, ClinicManagement.Infrastructure.Admin.AdminDoctorService>();
builder.Services.AddScoped<ClinicManagement.Application.Leaves.Interfaces.IDoctorLeaveService, ClinicManagement.Infrastructure.Leaves.DoctorLeaveService>();
builder.Services.AddScoped<ClinicManagement.Application.Leaves.Interfaces.IAdminLeaveService, ClinicManagement.Infrastructure.Leaves.AdminLeaveService>();
builder.Services.Configure<ClinicManagement.Infrastructure.AI.AiProviderOptions>(builder.Configuration.GetSection(ClinicManagement.Infrastructure.AI.AiProviderOptions.SectionName));
builder.Services.AddHttpClient<ClinicManagement.Application.AI.Interfaces.IAiSpecialtySuggestionProvider, ClinicManagement.Infrastructure.AI.GeminiAiProvider>();
builder.Services.AddScoped<ClinicManagement.Application.AI.Interfaces.IAiSpecialtyService, ClinicManagement.Infrastructure.AI.AiSpecialtyService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync("{\"success\":false,\"message\":\"Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau 1 phút.\",\"data\":null}", token);
    };

    options.AddPolicy("AiChatPolicy", httpContext =>
    {
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
            new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.AddFixedWindowLimiter("ai_endpoint", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
    });
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCors();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await RoleSeeder.SeedRolesAsync(scope.ServiceProvider);

    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("DemoSeed:Enabled"))
    {
        await ClinicManagement.Infrastructure.Persistence.DevelopmentDataSeeder.SeedAsync(scope.ServiceProvider);
    }
}

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(corsBuilder => 
{
    if (app.Environment.IsDevelopment())
    {
        corsBuilder.SetIsOriginAllowed(origin => 
        {
            var uri = new Uri(origin);
            return uri.Host == "localhost" || uri.Host == "127.0.0.1";
        })
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    }
    else
    {
        var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        corsBuilder.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    }
});

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
