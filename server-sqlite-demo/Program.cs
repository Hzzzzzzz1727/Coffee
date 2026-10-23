using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var port=int.TryParse(Environment.GetEnvironmentVariable("COFFEE_DEMO_PORT"),out var customPort)?customPort:3077;
using var instance=new Mutex(true,$"CoffeeOS-Demo-SQLite-{port}",out var firstInstance);
if(!firstInstance){TryOpen($"http://localhost:{port}");return;}
await DemoDb.Initialize();
var noBrowser=args.Contains("--no-browser");var noFirewall=args.Contains("--no-firewall");
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=args,ContentRootPath=AppContext.BaseDirectory,WebRootPath=Path.Combine(AppContext.BaseDirectory,"wwwroot")});
// Bản WinExe không có console và không yêu cầu quyền ghi Windows Event Log.
builder.Logging.ClearProviders();
var jwtSecret=await LocalSecret();
builder.Services.AddCors(o=>o.AddDefaultPolicy(p=>p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>o.TokenValidationParameters=new TokenValidationParameters{ValidateIssuer=true,ValidateAudience=true,ValidateLifetime=true,ValidateIssuerSigningKey=true,ValidIssuer="CoffeeOS",ValidAudience="CoffeeOS.Client",IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),ClockSkew=TimeSpan.FromMinutes(1)});
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.Converters.Add(new UtcDateTimeConverter()));
var app=builder.Build();
app.UseCors();
app.Use(async (ctx,next)=>{var p=ctx.Request.Path.Value??"";if(!p.StartsWith("/api")||p.StartsWith("/api/license")||p.StartsWith("/api/setup")||p=="/api/health"){await next();return;}var license=await DemoLicense.Status();if(license.Allowed){await next();return;}ctx.Response.StatusCode=402;await ctx.Response.WriteAsJsonAsync(new{error=license.Message,licenseRequired=true,installationId=license.InstallationId});});
app.UseAuthentication();
app.Use(async(ctx,next)=>{if(ctx.User.Identity?.IsAuthenticated!=true||!ctx.Request.Path.StartsWithSegments("/api")){await next();return;}var id=ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);var versionText=ctx.User.FindFirstValue("auth_version");if(string.IsNullOrWhiteSpace(id)||!int.TryParse(versionText,out var tokenVersion)){ctx.Response.StatusCode=401;await ctx.Response.WriteAsJsonAsync(new{error="Phiên đăng nhập không hợp lệ."});return;}await using var c=DemoDb.Open();var row=(await DemoDb.Rows(c,"SELECT IsActive,AuthVersion FROM Users WHERE Id=@id AND DeletedAt IS NULL LIMIT 1",("@id",id))).FirstOrDefault();if(row is null||!row.B("IsActive")||row.I("AuthVersion")!=tokenVersion){ctx.Response.StatusCode=401;await ctx.Response.WriteAsJsonAsync(new{error="Tài khoản đã thay đổi hoặc không còn hoạt động. Vui lòng đăng nhập lại."});return;}await next();});
app.UseAuthorization();
app.Use(async(ctx,next)=>{var p=ctx.Request.Path.Value??"";if(p.StartsWith("/api/auth")||p.StartsWith("/api/setup")||p.StartsWith("/api/license")||p=="/api/health"){await next();return;}var role=ctx.User.FindFirstValue(ClaimTypes.Role)??"";var method=ctx.Request.Method;if(p.StartsWith("/api/users")&&role!="owner"||p.StartsWith("/api/reports/operations")&&!new[]{"owner","manager"}.Contains(role)||p.Contains("/pay")&&!new[]{"owner","manager","cashier"}.Contains(role)){ctx.Response.StatusCode=403;await ctx.Response.WriteAsJsonAsync(new{error="Tài khoản không có quyền thực hiện thao tác này."});return;}await next();});

app.MapGet("/api/health",()=>Results.Ok(new{ok=true,server="SQLite",database=DemoDb.DatabasePath})).AllowAnonymous();
app.MapGet("/api/setup/status",async()=>{await using var c=DemoDb.Open();return Results.Ok(new{required=await DemoDb.ScalarLong(c,"SELECT COUNT(*) FROM Users")==0});}).AllowAnonymous();
app.MapPost("/api/setup",async(SetupInput x)=>{await using var c=DemoDb.Open();if(await DemoDb.ScalarLong(c,"SELECT COUNT(*) FROM Users")>0)return Results.Conflict(new{error="Hệ thống đã được thiết lập."});if(string.IsNullOrWhiteSpace(x.Name))return Results.BadRequest(new{error="Vui lòng nhập tên chủ quán."});if(string.IsNullOrWhiteSpace(x.Email)||!System.Net.Mail.MailAddress.TryCreate(x.Email.Trim(),out _))return Results.BadRequest(new{error="Email không đúng định dạng."});if(string.IsNullOrEmpty(x.Password)||x.Password.Length<8)return Results.BadRequest(new{error="Mật khẩu phải có ít nhất 8 ký tự."});var store=await DemoDb.ScalarString(c,"SELECT Id FROM Stores LIMIT 1");await DemoDb.Exec(c,"INSERT INTO Users(Id,StoreId,FullName,Email,PasswordHash,Role,CreatedAt)VALUES(@id,@s,@n,@e,@p,'owner',@now)",("@id",DemoDb.Id()),("@s",store),("@n",x.Name.Trim()),("@e",x.Email.Trim().ToLowerInvariant()),("@p",BCrypt.Net.BCrypt.HashPassword(x.Password,12)),("@now",DemoDb.Now));return Results.Ok(new{ok=true});}).AllowAnonymous();
app.MapGet("/first-run",async ctx=>{ctx.Response.ContentType="text/html; charset=utf-8";await ctx.Response.WriteAsync(FirstRun.Page);}).AllowAnonymous();
app.MapPost("/api/auth/login",async(LoginRequest x)=>{if(string.IsNullOrWhiteSpace(x.Email)||string.IsNullOrEmpty(x.Password))return Results.Json(new{error="Email hoặc mật khẩu không đúng."},statusCode:401);await using var c=DemoDb.Open();var row=(await DemoDb.Rows(c,"SELECT Id,FullName,Role,PasswordHash,AuthVersion FROM Users WHERE Email=@e AND IsActive=1 AND DeletedAt IS NULL LIMIT 1",("@e",x.Email.Trim().ToLowerInvariant()))).FirstOrDefault();if(row is null||!BCrypt.Net.BCrypt.Verify(x.Password,row.S("PasswordHash")))return Results.Json(new{error="Email hoặc mật khẩu không đúng."},statusCode:401);var claims=new[]{new Claim(ClaimTypes.NameIdentifier,row.S("Id")),new Claim(ClaimTypes.Name,row.S("FullName")),new Claim(ClaimTypes.Role,row.S("Role")),new Claim("auth_version",row.I("AuthVersion").ToString())};var jwt=new JwtSecurityToken("CoffeeOS","CoffeeOS.Client",claims,expires:DateTime.UtcNow.AddHours(12),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),SecurityAlgorithms.HmacSha256));return Results.Ok(new{token=new JwtSecurityTokenHandler().WriteToken(jwt),user=new{id=Guid.Parse(row.S("Id")),name=row.S("FullName"),role=row.S("Role")}});}).AllowAnonymous();
app.MapGet("/api/license/status",async()=>Results.Ok(await DemoLicense.Status())).AllowAnonymous();
app.MapPost("/api/license/activate",async(ActivateLicenseInput x)=>{try{return Results.Ok(await DemoLicense.Activate(x.Key));}catch(Exception e){return Results.BadRequest(new{error=e.Message});}}).AllowAnonymous();
app.MapCoreEndpoints();app.MapAdvancedEndpoints();app.MapInventoryEndpoints();app.MapDeviceEndpoints();
app.MapGet("/uploads/products/{fileName}",(string fileName)=>{var safeName=Path.GetFileName(fileName);var extension=Path.GetExtension(safeName).ToLowerInvariant();if(!string.Equals(fileName,safeName,StringComparison.Ordinal)||!new[]{".jpg",".png",".webp"}.Contains(extension))return Results.NotFound();var path=Path.Combine(DemoDb.DataDirectory,"uploads","products",safeName);if(!File.Exists(path))return Results.NotFound();var contentType=extension==".png"?"image/png":extension==".webp"?"image/webp":"image/jpeg";return Results.File(path,contentType,enableRangeProcessing:true);}).AllowAnonymous();
app.MapGet("/{**path}",(string? path)=>EmbeddedWeb.File(path)).AllowAnonymous();
app.Lifetime.ApplicationStarted.Register(()=>_ = Task.Run(async()=>{await Task.Delay(700);if(!noFirewall)EnsureFirewall(port);if(!noBrowser){await using var c=DemoDb.Open();var setup=await DemoDb.ScalarLong(c,"SELECT COUNT(*) FROM Users")==0;TryOpen(setup?$"http://localhost:{port}/first-run":$"http://localhost:{port}");}}));
app.Run($"http://0.0.0.0:{port}");

async Task<string> LocalSecret(){await using var c=DemoDb.Open();var value=await DemoDb.ScalarString(c,"SELECT Value FROM SystemSettings WHERE Key='JwtSecret'");if(value is not null)return value;value=Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));await DemoDb.Exec(c,"INSERT INTO SystemSettings(Key,Value,UpdatedAt)VALUES('JwtSecret',@v,@n)",( "@v",value),("@n",DemoDb.Now));return value;}
static void TryOpen(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch{}}
static void EnsureFirewall(int port){if(!OperatingSystem.IsWindows())return;var marker=Path.Combine(DemoDb.DataDirectory,$"firewall-{port}-ready.txt");if(File.Exists(marker))return;try{var p=Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall add rule name=\"CoffeeOS Demo SQLite\" dir=in action=allow protocol=TCP localport={port} profile=private"){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});p?.WaitForExit();if(p?.ExitCode==0)File.WriteAllText(marker,DateTime.Now.ToString("O"));}catch{}}

static class FirstRun
{
public const string Page="""
<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Thiết lập CoffeeOS</title><style>body{margin:0;background:#17382d;color:#17382d;font:16px system-ui;display:grid;place-items:center;min-height:100vh}.box{background:#fffdf8;padding:32px;border-radius:18px;width:min(420px,calc(100% - 48px));box-shadow:0 20px 70px #0005}h1{margin:0 0 8px}p{color:#6b756f}label{display:block;margin:16px 0 6px;font-weight:650}input{box-sizing:border-box;width:100%;padding:13px;border:1px solid #ccd3cf;border-radius:9px;font-size:16px}button{width:100%;margin-top:22px;padding:14px;border:0;border-radius:9px;background:#215342;color:white;font-weight:750;font-size:16px}.error{color:#b43f36;margin-top:12px}</style><div class="box"><h1>Thiết lập CoffeeOS</h1><p>Lần chạy đầu tiên · SQLite đã được tạo tự động. Hãy tạo tài khoản chủ quán.</p><form id="setup-form"><label for="owner-name">Tên chủ quán</label><input id="owner-name" autocomplete="name" required><label for="owner-email">Email đăng nhập</label><input id="owner-email" type="email" autocomplete="email" required><label for="owner-password">Mật khẩu</label><input id="owner-password" type="password" autocomplete="new-password" minlength="8" required><div id="setup-error" class="error"></div><button id="setup-submit">Tạo tài khoản và bắt đầu</button></form></div><script>const form=document.getElementById('setup-form'),nameInput=document.getElementById('owner-name'),emailInput=document.getElementById('owner-email'),passwordInput=document.getElementById('owner-password'),errorBox=document.getElementById('setup-error'),submitButton=document.getElementById('setup-submit');form.addEventListener('submit',async event=>{event.preventDefault();errorBox.textContent='';submitButton.disabled=true;try{const response=await fetch('/api/setup',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:nameInput.value.trim(),email:emailInput.value.trim(),password:passwordInput.value})});const data=await response.json();if(!response.ok){errorBox.textContent=data.error||'Không thể tạo tài khoản.';return}location.replace('/')}catch{errorBox.textContent='Không kết nối được CoffeeOS.'}finally{submitButton.disabled=false}});</script></html>
""";
}

record LoginRequest(string Email,string Password);
record SetupInput(string Name,string Email,string Password);
record ActivateLicenseInput(string Key);
sealed class UtcDateTimeConverter:JsonConverter<DateTime>{public override DateTime Read(ref Utf8JsonReader r,Type t,JsonSerializerOptions o)=>DateTime.Parse(r.GetString()!,null,System.Globalization.DateTimeStyles.RoundtripKind);public override void Write(Utf8JsonWriter w,DateTime v,JsonSerializerOptions o)=>w.WriteStringValue(DateTime.SpecifyKind(v,DateTimeKind.Utc));}
static class EmbeddedWeb
{
    static readonly Assembly App=typeof(EmbeddedWeb).Assembly;
    static readonly Dictionary<string,string> Files=App.GetManifestResourceNames().Where(n=>n.StartsWith("DemoWeb/",StringComparison.Ordinal)).ToDictionary(n=>n["DemoWeb/".Length..].Replace('\\','/'),n=>n,StringComparer.OrdinalIgnoreCase);
    public static IResult File(string? path){var requested=string.IsNullOrWhiteSpace(path)?"index.html":path.TrimStart('/');if(!Files.TryGetValue(requested,out var resource)){requested="index.html";if(!Files.TryGetValue(requested,out resource))return Results.NotFound();}var ext=Path.GetExtension(requested).ToLowerInvariant();var type=ext switch{".html"=>"text/html; charset=utf-8",".js"=>"text/javascript; charset=utf-8",".css"=>"text/css; charset=utf-8",".json"=>"application/json",".svg"=>"image/svg+xml",".png"=>"image/png",".ico"=>"image/x-icon",_=>"application/octet-stream"};return Results.Stream(App.GetManifestResourceStream(resource)!,type);}
}
