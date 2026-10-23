using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
var connectionString = "Server=127.0.0.1,1433;Database=master;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;";
var coffeeConnection = connectionString.Replace("Database=master", "Database=CoffeeOS");
var jwtSecret = Environment.GetEnvironmentVariable("COFFEE_JWT_SECRET");
if(string.IsNullOrWhiteSpace(jwtSecret))
{
    var localData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CoffeeOS");
    var secretFile=Path.Combine(localData,"jwt-secret.txt");
    Directory.CreateDirectory(localData);
    if(File.Exists(secretFile))jwtSecret=(await File.ReadAllTextAsync(secretFile)).Trim();
    if(string.IsNullOrWhiteSpace(jwtSecret))
    {
        jwtSecret=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        await File.WriteAllTextAsync(secretFile,jwtSecret);
    }
}
if (args.Length >= 3 && args[0] == "--setup")
{
    var email = args[1].ToLowerInvariant(); var password = args[2]; var name = args.Length > 3 ? string.Join(' ', args.Skip(3)) : "Chủ cửa hàng";
    await using (var master = new SqlConnection(connectionString)) { await master.OpenAsync(); await new SqlCommand("IF DB_ID(N'CoffeeOS') IS NULL CREATE DATABASE CoffeeOS", master).ExecuteNonQueryAsync(); }
    var appConnection = connectionString.Replace("Database=master", "Database=CoffeeOS");
    await using var database = new SqlConnection(appConnection); await database.OpenAsync();
    var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", "001_initial.sql"));
    await new SqlCommand(migration, database).ExecuteNonQueryAsync();
    var advancedMigration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", "002_advanced_operations.sql"));
    await new SqlCommand(advancedMigration, database).ExecuteNonQueryAsync();
    await new SqlCommand("DECLARE @s uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Stores); IF NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE StoreId=@s) INSERT dbo.Areas(StoreId,Name,Position) VALUES(@s,N'Khu vực trong nhà',1); DECLARE @a uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Areas WHERE StoreId=@s); IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables WHERE AreaId=@a) BEGIN INSERT dbo.DiningTables(AreaId,Code,Seats) VALUES(@a,N'Bàn 01',2),(@a,N'Bàn 02',2),(@a,N'Bàn 03',4),(@a,N'Bàn 04',4),(@a,N'Bàn 05',2),(@a,N'Bàn 06',4),(@a,N'Bàn 07',6),(@a,N'Bàn 08',2); END IF NOT EXISTS(SELECT 1 FROM dbo.Categories WHERE StoreId=@s AND Name=N'Cà phê') INSERT dbo.Categories(StoreId,Name) VALUES(@s,N'Cà phê'); DECLARE @c uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Categories WHERE StoreId=@s AND Name=N'Cà phê'); IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE CategoryId=@c) INSERT dbo.Products(CategoryId,Name,BasePrice) VALUES(@c,N'Americano',39000),(@c,N'Latte hạt dẻ',55000),(@c,N'Cold brew cam',59000),(@c,N'Matcha cloud',62000)", database).ExecuteNonQueryAsync();
    var storeId = (Guid)(await new SqlCommand("SELECT TOP 1 Id FROM dbo.Stores", database).ExecuteScalarAsync())!;
    await using var command = new SqlCommand("IF NOT EXISTS(SELECT 1 FROM dbo.Users WHERE Email=@email) INSERT dbo.Users(StoreId,FullName,Email,PasswordHash,Role) VALUES(@storeId,@name,@email,@hash,'owner')", database);
    command.Parameters.AddWithValue("@storeId", storeId); command.Parameters.AddWithValue("@name", name); command.Parameters.AddWithValue("@email", email); command.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password, 12));
    await command.ExecuteNonQueryAsync(); Console.WriteLine($"Admin ready: {email}"); return;
}
// Frontend được mở từ nhiều IP trong LAN; API vẫn bảo vệ bằng JWT và phân quyền role.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer=true,ValidateAudience=true,ValidateLifetime=true,ValidateIssuerSigningKey=true,ValidIssuer="CoffeeOS",ValidAudience="CoffeeOS.Client",IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),ClockSkew=TimeSpan.FromMinutes(1) });
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcDateTimeConverter()));
var app = builder.Build();
var productImageDirectory=Environment.GetEnvironmentVariable("COFFEE_UPLOAD_DIR");
if(string.IsNullOrWhiteSpace(productImageDirectory))productImageDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CoffeeOS","uploads","products");
Directory.CreateDirectory(productImageDirectory);
await using (var schemaConnection=new SqlConnection(coffeeConnection))
{
    await schemaConnection.OpenAsync();
    await new SqlCommand("IF COL_LENGTH('dbo.Products','IsAvailable') IS NULL ALTER TABLE dbo.Products ADD IsAvailable bit NOT NULL CONSTRAINT DF_Products_IsAvailable DEFAULT 1;IF COL_LENGTH('dbo.Products','ImageUrl') IS NULL ALTER TABLE dbo.Products ADD ImageUrl nvarchar(500) NULL;IF COL_LENGTH('dbo.Orders','BillRequestedAt') IS NULL ALTER TABLE dbo.Orders ADD BillRequestedAt datetime2 NULL;IF COL_LENGTH('dbo.Users','AuthVersion') IS NULL ALTER TABLE dbo.Users ADD AuthVersion int NOT NULL CONSTRAINT DF_Users_AuthVersion DEFAULT 1;IF COL_LENGTH('dbo.Users','DeletedAt') IS NULL ALTER TABLE dbo.Users ADD DeletedAt datetime2 NULL;",schemaConnection).ExecuteNonQueryAsync();
    var advancedMigrationPath=Path.Combine(AppContext.BaseDirectory,"migrations","002_advanced_operations.sql");
    if(File.Exists(advancedMigrationPath))await new SqlCommand(await File.ReadAllTextAsync(advancedMigrationPath),schemaConnection).ExecuteNonQueryAsync();
    var layoutMigrationPath=Path.Combine(AppContext.BaseDirectory,"migrations","003_layout_and_license.sql");
    if(File.Exists(layoutMigrationPath))await new SqlCommand(await File.ReadAllTextAsync(layoutMigrationPath),schemaConnection).ExecuteNonQueryAsync();
    var inventoryMigrationPath=Path.Combine(AppContext.BaseDirectory,"migrations","004_inventory.sql");
    if(File.Exists(inventoryMigrationPath))
    {
        var inventorySql=await File.ReadAllTextAsync(inventoryMigrationPath);
        foreach(var batch in Regex.Split(inventorySql,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase).Where(x=>!string.IsNullOrWhiteSpace(x)))
            await new SqlCommand(batch,schemaConnection).ExecuteNonQueryAsync();
    }
    var managementMigrationPath=Path.Combine(AppContext.BaseDirectory,"migrations","005_management_suite.sql");
    if(File.Exists(managementMigrationPath))
    {
        var managementSql=await File.ReadAllTextAsync(managementMigrationPath);
        foreach(var batch in Regex.Split(managementSql,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase).Where(x=>!string.IsNullOrWhiteSpace(x)))
            await new SqlCommand(batch,schemaConnection).ExecuteNonQueryAsync();
    }
}
await LicenseFeatures.EnsureInstallation(coffeeConnection);
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.Use(async (context,next) =>
{
    var path=context.Request.Path.Value??"";
    if(!path.StartsWith("/api")||path.StartsWith("/api/license")||path=="/api/health"){await next();return;}
    var license=await LicenseFeatures.Status(coffeeConnection);
    if(license.Allowed){await next();return;}
    context.Response.StatusCode=402;await context.Response.WriteAsJsonAsync(new{error=license.Message,licenseRequired=true,installationId=license.InstallationId});
});
app.UseAuthentication();
app.Use(async (context,next) =>
{
    if(context.User.Identity?.IsAuthenticated!=true||!context.Request.Path.StartsWithSegments("/api")){await next();return;}
    var idValue=context.User.FindFirstValue(ClaimTypes.NameIdentifier);var versionValue=context.User.FindFirstValue("auth_version");
    if(!Guid.TryParse(idValue,out var userId)||!int.TryParse(versionValue,out var tokenVersion)){context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new{error="Phiên đăng nhập không hợp lệ."});return;}
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("SELECT IsActive,AuthVersion FROM dbo.Users WHERE Id=@id AND DeletedAt IS NULL",connection);command.Parameters.AddWithValue("@id",userId);await using var reader=await command.ExecuteReaderAsync();
    if(!await reader.ReadAsync()||!reader.GetBoolean(0)||reader.GetInt32(1)!=tokenVersion){context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new{error="Tài khoản đã thay đổi hoặc không còn hoạt động. Vui lòng đăng nhập lại."});return;}
    await next();
});
app.UseAuthorization();
app.Use(async (context,next) =>
{
    var path=context.Request.Path.Value??"";var method=context.Request.Method;string? permission=null;
    if(method=="POST"&&(path.EndsWith("/mixed-payment")||path.EndsWith("/refund"))&&context.User.FindFirstValue(ClaimTypes.Role)=="cashier")
    {
        await using var shiftConnection=new SqlConnection(coffeeConnection);await shiftConnection.OpenAsync();
        await using var shiftCommand=new SqlCommand("SELECT COUNT(*) FROM dbo.WorkShifts WHERE OpenedBy=@u AND Status='open'",shiftConnection);shiftCommand.Parameters.AddWithValue("@u",Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if(Convert.ToInt32(await shiftCommand.ExecuteScalarAsync())==0){context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error="Thu ngân phải mở ca trước khi thanh toán hoặc hoàn tiền."});return;}
    }
    if(path.StartsWith("/api/users"))permission="staff.manage";
    else if(path.StartsWith("/api/inventory"))permission="inventory.manage";
    else if(path.StartsWith("/api/products")||path.StartsWith("/api/categories")||path.StartsWith("/api/product-variants")||path.StartsWith("/api/combos"))permission=method=="GET"?null:"menu.manage";
    else if(path.StartsWith("/api/reports"))permission="reports.view";
    else if(path.Contains("/refund"))permission="orders.refund";
    else if(path.Contains("/pay")||path.Contains("/mixed-payment"))permission="orders.pay";
    else if(path.StartsWith("/api/orders")&&method=="POST")permission="orders.create";
    else if(path.StartsWith("/api/order-items")&&method=="DELETE")permission="orders.cancel";
    if(permission is not null&&await ManagementSuite.ExplicitlyDenied(coffeeConnection,context.User,permission)){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Tài khoản đã bị tắt quyền thực hiện thao tác này."});return;}
    await next();
});
app.Use(async (context,next) => { var path=context.Request.Path.Value??"";if(path.StartsWith("/api/auth")||path=="/api/health"){await next();return;}var role=context.User.FindFirstValue(ClaimTypes.Role)??"";var method=context.Request.Method;if(path.StartsWith("/api/users")&&role!="owner"){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Chỉ chủ cửa hàng được quản lý nhân sự."});return;}if(path.StartsWith("/api/products")&&method!="GET"&&!new[]{"owner","manager"}.Contains(role)&&!(role=="barista"&&path.EndsWith("/availability"))){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Bạn không có quyền sửa menu."});return;}if(path.StartsWith("/api/payment-settings")&&method!="GET"&&!new[]{"owner","manager"}.Contains(role)){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Bạn không có quyền cấu hình thanh toán."});return;}if(path.StartsWith("/api/reports")&&!new[]{"owner","manager","cashier"}.Contains(role)){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Bạn không có quyền xem báo cáo."});return;}if(path.Contains("/pay")&&!new[]{"owner","manager","cashier"}.Contains(role)){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Bạn không có quyền thanh toán."});return;}await next();});
app.Use(async (context,next) => { var path=context.Request.Path.Value??"";var role=context.User.FindFirstValue(ClaimTypes.Role)??"";var method=context.Request.Method;if(path=="/api/tables"&&method=="POST"&&!new[]{"owner","manager"}.Contains(role)){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Chỉ chủ quán hoặc quản lý được thêm bàn."});return;}if(path=="/api/orders"&&method=="POST"&&!new[]{"owner","manager","cashier","server"}.Contains(role)){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Tài khoản pha chế không được tạo đơn."});return;}await next();});
app.MapGet("/api/health", async () =>
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new SqlCommand("SELECT @@SERVERNAME", connection);
    return Results.Ok(new { ok = true, server = (string?)await command.ExecuteScalarAsync() });
}).AllowAnonymous();
app.MapGet("/uploads/products/{fileName}",(string fileName)=>
{
    var safeName=Path.GetFileName(fileName);var extension=Path.GetExtension(safeName).ToLowerInvariant();
    if(!string.Equals(fileName,safeName,StringComparison.Ordinal)||!new[]{".jpg",".png",".webp"}.Contains(extension))return Results.NotFound();
    var path=Path.Combine(productImageDirectory,safeName);if(!File.Exists(path))return Results.NotFound();
    var contentType=extension==".png"?"image/png":extension==".webp"?"image/webp":"image/jpeg";return Results.File(path,contentType,enableRangeProcessing:true);
}).AllowAnonymous();
app.MapPost("/api/auth/login", async (LoginRequest login) =>
{
    if(string.IsNullOrWhiteSpace(login.Email)||string.IsNullOrEmpty(login.Password))return Results.Json(new{error="Email hoặc mật khẩu không đúng."},statusCode:401);
    await using var connection = new SqlConnection(coffeeConnection);
    await connection.OpenAsync();
    await using var command = new SqlCommand("SELECT TOP 1 Id,FullName,Role,PasswordHash,AuthVersion FROM dbo.Users WHERE Email=@email AND IsActive=1 AND DeletedAt IS NULL", connection);
    command.Parameters.AddWithValue("@email", login.Email.Trim().ToLowerInvariant());
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || !BCrypt.Net.BCrypt.Verify(login.Password, reader.GetString(3))) return Results.Json(new { error="Email hoặc mật khẩu không đúng." }, statusCode:401);
    var id=reader.GetGuid(0);var name=reader.GetString(1);var role=reader.GetString(2);var authVersion=reader.GetInt32(4);var claims=new[]{new Claim(ClaimTypes.NameIdentifier,id.ToString()),new Claim(ClaimTypes.Name,name),new Claim(ClaimTypes.Role,role),new Claim("auth_version",authVersion.ToString())};var jwt=new JwtSecurityToken("CoffeeOS","CoffeeOS.Client",claims,expires:DateTime.UtcNow.AddHours(8),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),SecurityAlgorithms.HmacSha256));var token=new JwtSecurityTokenHandler().WriteToken(jwt);return Results.Ok(new { token, user = new { id, name, role } });
}).AllowAnonymous();
app.MapGet("/api/tables", async () =>
{
    await using var connection = new SqlConnection(coffeeConnection); await connection.OpenAsync();
    await using var command = new SqlCommand("SELECT t.Id,t.Code,t.Seats,CASE WHEN o.BillRequestedAt IS NOT NULL THEN 'payment_requested' WHEN o.Id IS NOT NULL THEN 'occupied' ELSE t.Status END,o.Total,o.OpenedAt,t.PositionX,t.PositionY,t.AreaId,a.Name,t.Width,t.Height,t.Shape FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId OUTER APPLY(SELECT TOP 1 Id,Total,OpenedAt,BillRequestedAt FROM dbo.Orders WHERE TableId=t.Id AND Status NOT IN ('paid','cancelled') ORDER BY OpenedAt DESC)o ORDER BY a.Position,t.Code", connection);
    await using var reader = await command.ExecuteReaderAsync(); var rows = new List<object>(); while (await reader.ReadAsync()) rows.Add(new { Id=reader.GetGuid(0), name=reader.GetString(1), seats=reader.GetByte(2), status=reader.GetString(3), total=reader.IsDBNull(4)?(int?)null:reader.GetInt32(4), openedAt=reader.IsDBNull(5)?(DateTime?)null:reader.GetDateTime(5),positionX=reader.GetInt32(6),positionY=reader.GetInt32(7),areaId=reader.GetGuid(8),areaName=reader.GetString(9),width=reader.GetInt32(10),height=reader.GetInt32(11),shape=reader.GetString(12) }); return Results.Ok(rows);
});
app.MapGet("/api/tables/{id:guid}/order", async (Guid id) =>
{
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var orderCommand=new SqlCommand("SELECT TOP 1 Id,OrderNumber,Status,Total,OpenedAt,BillRequestedAt FROM dbo.Orders WHERE TableId=@id AND Status NOT IN ('paid','cancelled') ORDER BY OpenedAt DESC",connection);orderCommand.Parameters.AddWithValue("@id",id);await using var reader=await orderCommand.ExecuteReaderAsync();if(!await reader.ReadAsync())return Results.Ok((object?)null);var orderId=reader.GetGuid(0);var order=new{ id=orderId,orderNumber=reader.GetInt64(1),status=reader.GetString(2),total=reader.GetInt32(3),openedAt=reader.GetDateTime(4),billRequestedAt=reader.IsDBNull(5)?(DateTime?)null:reader.GetDateTime(5)};await reader.CloseAsync();await using var itemCommand=new SqlCommand("SELECT i.Id,i.ProductName,i.UnitPrice+ISNULL((SELECT SUM(t.Price*t.Quantity) FROM dbo.OrderItemToppings t WHERE t.OrderItemId=i.Id),0),i.Quantity,i.Status,i.Note,i.CreatedAt,(SELECT STRING_AGG(t.Name,N', ') FROM dbo.OrderItemToppings t WHERE t.OrderItemId=i.Id) FROM dbo.OrderItems i WHERE i.OrderId=@id ORDER BY i.CreatedAt",connection);itemCommand.Parameters.AddWithValue("@id",orderId);await using var itemsReader=await itemCommand.ExecuteReaderAsync();var items=new List<object>();while(await itemsReader.ReadAsync())items.Add(new{id=itemsReader.GetGuid(0),name=itemsReader.GetString(1),price=itemsReader.GetInt32(2),quantity=itemsReader.GetInt16(3),status=itemsReader.GetString(4),note=itemsReader.IsDBNull(5)?null:itemsReader.GetString(5),createdAt=itemsReader.GetDateTime(6),toppings=itemsReader.IsDBNull(7)?null:itemsReader.GetString(7)});return Results.Ok(new{order.id,order.orderNumber,order.status,order.total,order.openedAt,order.billRequestedAt,items});
});
app.MapPost("/api/tables", async (TableInput input) =>
{
    if(string.IsNullOrWhiteSpace(input.Name)||input.Seats<1)return Results.BadRequest(new{error="Tên bàn và số ghế không hợp lệ."});await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await new SqlCommand("DECLARE @s uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Stores);IF NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE StoreId=@s)INSERT dbo.Areas(StoreId,Name,Position)VALUES(@s,N'Khu vực trong nhà',1)",connection).ExecuteNonQueryAsync();var area=input.AreaId??(Guid)(await new SqlCommand("SELECT TOP 1 Id FROM dbo.Areas ORDER BY Position",connection).ExecuteScalarAsync())!;var shape=new[]{"rectangle","round","square"}.Contains(input.Shape)?input.Shape:"rectangle";await using var command=new SqlCommand("INSERT dbo.DiningTables(AreaId,Code,Seats,Shape)OUTPUT INSERTED.Id,INSERTED.Code,INSERTED.Seats,INSERTED.Status VALUES(@a,@n,@s,@shape)",connection);command.Parameters.AddWithValue("@a",area);command.Parameters.AddWithValue("@n",input.Name.Trim());command.Parameters.AddWithValue("@s",input.Seats);command.Parameters.AddWithValue("@shape",shape);await using var reader=await command.ExecuteReaderAsync();await reader.ReadAsync();return Results.Created($"/api/tables/{reader.GetGuid(0)}",new{Id=reader.GetGuid(0),name=reader.GetString(1),seats=reader.GetByte(2),status=reader.GetString(3)});
});
app.MapGet("/api/products", async () =>
{
    await using var connection = new SqlConnection(coffeeConnection); await connection.OpenAsync(); await using var command = new SqlCommand("SELECT Id,Name,BasePrice AS price,IsAvailable,ImageUrl,CategoryId,SKU FROM dbo.Products WHERE IsActive=1 ORDER BY Name", connection); await using var reader = await command.ExecuteReaderAsync(); var rows=new List<object>(); while(await reader.ReadAsync()) rows.Add(new { Id=reader.GetGuid(0), Name=reader.GetString(1), price=reader.GetInt32(2), isAvailable=reader.GetBoolean(3),imageUrl=reader.IsDBNull(4)?null:reader.GetString(4),categoryId=reader.GetGuid(5),sku=reader.IsDBNull(6)?"":reader.GetString(6) }); return Results.Ok(rows);
});
app.MapPost("/api/products", async (ProductInput product) =>
{
    if (string.IsNullOrWhiteSpace(product.Name) || product.Price < 0) return Results.BadRequest(new { error="Tên và giá sản phẩm không hợp lệ." });
    await using var connection=new SqlConnection(coffeeConnection); await connection.OpenAsync(); await new SqlCommand("DECLARE @s uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Stores); IF NOT EXISTS(SELECT 1 FROM dbo.Categories WHERE StoreId=@s) INSERT dbo.Categories(StoreId,Name,Position) VALUES(@s,N'Món khác',99)",connection).ExecuteNonQueryAsync(); var category=(Guid)(await new SqlCommand("SELECT TOP 1 Id FROM dbo.Categories ORDER BY Position",connection).ExecuteScalarAsync())!; await using var command=new SqlCommand("INSERT dbo.Products(CategoryId,Name,BasePrice) OUTPUT INSERTED.Id,INSERTED.Name,INSERTED.BasePrice VALUES(@c,@n,@p)",connection); command.Parameters.AddWithValue("@c",category);command.Parameters.AddWithValue("@n",product.Name.Trim());command.Parameters.AddWithValue("@p",product.Price);await using var reader=await command.ExecuteReaderAsync();await reader.ReadAsync();return Results.Created($"/api/products/{reader.GetGuid(0)}",new { Id=reader.GetGuid(0),Name=reader.GetString(1),price=reader.GetInt32(2)});
});
app.MapDelete("/api/products/{id:guid}", async (Guid id) =>
{
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("UPDATE dbo.Products SET IsActive=0 WHERE Id=@id",connection);command.Parameters.AddWithValue("@id",id);return await command.ExecuteNonQueryAsync()>0?Results.NoContent():Results.NotFound();
});
app.MapPost("/api/products/{id:guid}/image", async (Guid id, HttpRequest request) =>
{
    if(!request.HasFormContentType)return Results.BadRequest(new{error="Dữ liệu tải lên không hợp lệ."});
    var form=await request.ReadFormAsync();var file=form.Files.GetFile("image");
    if(file is null||file.Length==0)return Results.BadRequest(new{error="Hãy chọn một hình ảnh."});
    if(file.Length>5*1024*1024)return Results.BadRequest(new{error="Ảnh không được lớn hơn 5 MB."});
    var extensions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"image/jpeg",".jpg"},{"image/png",".png"},{"image/webp",".webp"}};
    if(!extensions.TryGetValue(file.ContentType,out var extension))return Results.BadRequest(new{error="Chỉ hỗ trợ ảnh JPG, PNG hoặc WebP."});
    if(!await HasValidImageSignature(file,extension))return Results.BadRequest(new{error="Nội dung tệp không phải là hình ảnh hợp lệ."});
    var fileName=$"{id:N}-{Guid.NewGuid():N}{extension}";var filePath=Path.Combine(productImageDirectory,fileName);var imageUrl=$"/uploads/products/{fileName}";
    await using(var output=File.Create(filePath))await file.CopyToAsync(output);
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("UPDATE dbo.Products SET ImageUrl=@url OUTPUT DELETED.ImageUrl WHERE Id=@id AND IsActive=1",connection);command.Parameters.AddWithValue("@url",imageUrl);command.Parameters.AddWithValue("@id",id);var oldUrl=await command.ExecuteScalarAsync();
    if(oldUrl is null){File.Delete(filePath);return Results.NotFound(new{error="Không tìm thấy món."});}
    if(oldUrl is string previous&&previous.StartsWith("/uploads/products/",StringComparison.OrdinalIgnoreCase)){var oldPath=Path.Combine(productImageDirectory,Path.GetFileName(previous));if(File.Exists(oldPath))File.Delete(oldPath);}
    return Results.Ok(new{id,imageUrl});
});
app.MapPatch("/api/products/{id:guid}/availability", async (Guid id, AvailabilityInput input, ClaimsPrincipal user) =>
{
    var role=user.FindFirstValue(ClaimTypes.Role)??"";if(!new[]{"owner","manager","barista"}.Contains(role))return Results.Json(new{error="Bạn không có quyền báo hết món."},statusCode:403);await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("UPDATE dbo.Products SET IsAvailable=@available WHERE Id=@id AND IsActive=1",connection);command.Parameters.AddWithValue("@available",input.IsAvailable);command.Parameters.AddWithValue("@id",id);return await command.ExecuteNonQueryAsync()>0?Results.Ok(new{id,isAvailable=input.IsAvailable}):Results.NotFound(new{error="Không tìm thấy món."});
});
app.MapPost("/api/orders", async (OrderInput input, ClaimsPrincipal user) =>
{
    if(input.Items.Count==0)return Results.BadRequest(new{error="Đơn hàng chưa có món."});
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();
    try
    {
        var total=0;
        var products=new List<(Guid Id,string Name,int Price,int Qty,string? Note,List<(Guid Id,string Name,int Price)> Toppings)>();
        foreach(var item in input.Items)
        {
            if(item.Quantity<1)throw new Exception("Số lượng không hợp lệ.");
            await using var find=new SqlCommand("SELECT Name,BasePrice FROM dbo.Products WHERE Id=@id AND IsActive=1 AND IsAvailable=1",connection,(SqlTransaction)transaction);find.Parameters.AddWithValue("@id",item.ProductId);await using var reader=await find.ExecuteReaderAsync();if(!await reader.ReadAsync())throw new Exception("Món đã hết hoặc không còn bán.");var name=reader.GetString(0);var price=reader.GetInt32(1);await reader.CloseAsync();
            if(item.VariantId.HasValue){await using var variantQuery=new SqlCommand("SELECT Name,PriceDelta FROM dbo.ProductVariants WHERE Id=@id AND ProductId=@product AND IsActive=1",connection,(SqlTransaction)transaction);variantQuery.Parameters.AddWithValue("@id",item.VariantId.Value);variantQuery.Parameters.AddWithValue("@product",item.ProductId);await using var variantReader=await variantQuery.ExecuteReaderAsync();if(!await variantReader.ReadAsync())throw new Exception("Size/biến thể không hợp lệ.");name+=$" · {variantReader.GetString(0)}";price+=variantReader.GetInt32(1);await variantReader.CloseAsync();}
            var toppings=new List<(Guid,string,int)>();
            foreach(var selected in item.Toppings??new List<ToppingSelectionInput>())
            {
                await using var toppingQuery=new SqlCommand("SELECT Name,Price FROM dbo.ProductToppings WHERE Id=@id AND ProductId=@product AND IsActive=1",connection,(SqlTransaction)transaction);toppingQuery.Parameters.AddWithValue("@id",selected.ToppingId);toppingQuery.Parameters.AddWithValue("@product",item.ProductId);await using var toppingReader=await toppingQuery.ExecuteReaderAsync();if(!await toppingReader.ReadAsync())throw new Exception("Topping không hợp lệ.");toppings.Add((selected.ToppingId,toppingReader.GetString(0),toppingReader.GetInt32(1)));await toppingReader.CloseAsync();
            }
            var unitTotal=price+toppings.Sum(t=>t.Item3);total+=unitTotal*item.Quantity;products.Add((item.ProductId,name,price,item.Quantity,string.IsNullOrWhiteSpace(item.Note)?null:item.Note.Trim(),toppings));
        }
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier),out var openedBy);Guid orderId;long number;
        await using(var active=new SqlCommand("SELECT TOP 1 Id,OrderNumber FROM dbo.Orders WHERE TableId=@t AND Status NOT IN ('paid','cancelled') ORDER BY OpenedAt DESC",connection,(SqlTransaction)transaction))
        {
            active.Parameters.AddWithValue("@t",input.TableId);await using var found=await active.ExecuteReaderAsync();
            if(await found.ReadAsync()){orderId=found.GetGuid(0);number=found.GetInt64(1);await found.CloseAsync();await using var update=new SqlCommand("UPDATE dbo.Orders SET Subtotal=Subtotal+@total,Total=Subtotal+@total-Discount-LoyaltyDiscount,Status='sent',BillRequestedAt=NULL WHERE Id=@id",connection,(SqlTransaction)transaction);update.Parameters.AddWithValue("@total",total);update.Parameters.AddWithValue("@id",orderId);await update.ExecuteNonQueryAsync();}
            else{await found.CloseAsync();var store=(Guid)(await new SqlCommand("SELECT TOP 1 Id FROM dbo.Stores",connection,(SqlTransaction)transaction).ExecuteScalarAsync())!;var shift=await new SqlCommand($"SELECT TOP 1 Id FROM dbo.WorkShifts WHERE OpenedBy='{openedBy}' AND Status='open' ORDER BY OpenedAt DESC",connection,(SqlTransaction)transaction).ExecuteScalarAsync();await using var create=new SqlCommand("INSERT dbo.Orders(StoreId,TableId,Status,Subtotal,Total,OpenedBy,ShiftId) OUTPUT INSERTED.Id,INSERTED.OrderNumber VALUES(@s,@t,'sent',@total,@total,@by,@shift)",connection,(SqlTransaction)transaction);create.Parameters.AddWithValue("@s",store);create.Parameters.AddWithValue("@t",input.TableId);create.Parameters.AddWithValue("@total",total);create.Parameters.AddWithValue("@by",openedBy==Guid.Empty?DBNull.Value:openedBy);create.Parameters.AddWithValue("@shift",shift??DBNull.Value);await using var created=await create.ExecuteReaderAsync();await created.ReadAsync();orderId=created.GetGuid(0);number=created.GetInt64(1);await created.CloseAsync();}
        }
        foreach(var p in products)
        {
            await using var add=new SqlCommand("INSERT dbo.OrderItems(OrderId,ProductId,ProductName,UnitPrice,Quantity,Note,Status) OUTPUT INSERTED.Id VALUES(@o,@p,@n,@price,@qty,@note,'sent')",connection,(SqlTransaction)transaction);add.Parameters.AddWithValue("@o",orderId);add.Parameters.AddWithValue("@p",p.Id);add.Parameters.AddWithValue("@n",p.Name);add.Parameters.AddWithValue("@price",p.Price);add.Parameters.AddWithValue("@qty",p.Qty);add.Parameters.AddWithValue("@note",(object?)p.Note??DBNull.Value);var itemId=(Guid)(await add.ExecuteScalarAsync())!;
            foreach(var t in p.Toppings){await using var addTopping=new SqlCommand("INSERT dbo.OrderItemToppings(OrderItemId,ToppingId,Name,Price) VALUES(@item,@id,@name,@price)",connection,(SqlTransaction)transaction);addTopping.Parameters.AddWithValue("@item",itemId);addTopping.Parameters.AddWithValue("@id",t.Id);addTopping.Parameters.AddWithValue("@name",t.Name);addTopping.Parameters.AddWithValue("@price",t.Price);await addTopping.ExecuteNonQueryAsync();}
        }
        await using(var occupy=new SqlCommand("UPDATE dbo.DiningTables SET Status='occupied' WHERE Id=@id",connection,(SqlTransaction)transaction)){occupy.Parameters.AddWithValue("@id",input.TableId);await occupy.ExecuteNonQueryAsync();}await transaction.CommitAsync();return Results.Created($"/api/orders/{orderId}",new{id=orderId,orderNumber=number,total});
    }
    catch(Exception error){await transaction.RollbackAsync();return Results.BadRequest(new{error=error.Message});}
});
app.MapGet("/api/orders/kds", async () =>
{
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("SELECT o.Id,o.OrderNumber,o.Status,o.OpenedAt,ISNULL(t.Code,N'Mang đi'),ISNULL(SUM(CASE WHEN i.Status<>'cancelled' THEN CONVERT(int,i.Quantity) ELSE 0 END),0) FROM dbo.Orders o LEFT JOIN dbo.DiningTables t ON t.Id=o.TableId LEFT JOIN dbo.OrderItems i ON i.OrderId=o.Id WHERE o.Status IN ('sent','preparing','ready') GROUP BY o.Id,o.OrderNumber,o.Status,o.OpenedAt,t.Code ORDER BY o.OpenedAt",connection);await using var reader=await command.ExecuteReaderAsync();var rows=new List<object>();while(await reader.ReadAsync())rows.Add(new{id=reader.GetGuid(0),orderNumber=reader.GetInt64(1),status=reader.GetString(2),openedAt=reader.GetDateTime(3),tableName=reader.GetString(4),itemCount=reader.GetInt32(5)});return Results.Ok(rows);
});
app.MapGet("/api/orders/{id:guid}", async (Guid id) =>
{
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var orderCommand=new SqlCommand("SELECT o.Id,o.OrderNumber,o.Status,o.Total,o.OpenedAt,ISNULL(t.Code,N'Mang đi'),o.BillRequestedAt,o.Subtotal,o.Discount,o.LoyaltyDiscount FROM dbo.Orders o LEFT JOIN dbo.DiningTables t ON t.Id=o.TableId WHERE o.Id=@id",connection);orderCommand.Parameters.AddWithValue("@id",id);await using var reader=await orderCommand.ExecuteReaderAsync();if(!await reader.ReadAsync())return Results.NotFound(new{error="Không tìm thấy đơn hàng."});var order=new{id=reader.GetGuid(0),orderNumber=reader.GetInt64(1),status=reader.GetString(2),total=reader.GetInt32(3),openedAt=reader.GetDateTime(4),tableName=reader.GetString(5),billRequestedAt=reader.IsDBNull(6)?(DateTime?)null:reader.GetDateTime(6),subtotal=reader.GetInt32(7),discount=reader.GetInt32(8),loyaltyDiscount=reader.GetInt32(9)};await reader.CloseAsync();await using var itemCommand=new SqlCommand("SELECT i.Id,i.ProductName,i.UnitPrice+ISNULL((SELECT SUM(t.Price*t.Quantity) FROM dbo.OrderItemToppings t WHERE t.OrderItemId=i.Id),0),i.Quantity,i.Status,i.Note,i.CreatedAt,(SELECT STRING_AGG(t.Name,N', ') FROM dbo.OrderItemToppings t WHERE t.OrderItemId=i.Id) FROM dbo.OrderItems i WHERE i.OrderId=@id ORDER BY i.CreatedAt,i.ProductName",connection);itemCommand.Parameters.AddWithValue("@id",id);await using var itemsReader=await itemCommand.ExecuteReaderAsync();var items=new List<object>();while(await itemsReader.ReadAsync())items.Add(new{id=itemsReader.GetGuid(0),name=itemsReader.GetString(1),price=itemsReader.GetInt32(2),quantity=itemsReader.GetInt16(3),status=itemsReader.GetString(4),note=itemsReader.IsDBNull(5)?null:itemsReader.GetString(5),createdAt=itemsReader.GetDateTime(6),toppings=itemsReader.IsDBNull(7)?null:itemsReader.GetString(7)});return Results.Ok(new{order.id,order.orderNumber,order.status,order.total,order.subtotal,order.discount,order.loyaltyDiscount,order.openedAt,order.tableName,order.billRequestedAt,items});
});
app.MapPost("/api/orders/{id:guid}/request-bill", async (Guid id, ClaimsPrincipal user) =>
{
    var role=user.FindFirstValue(ClaimTypes.Role)??"";if(!new[]{"owner","manager","server"}.Contains(role))return Results.Json(new{error="Chỉ phục vụ hoặc quản lý được gửi yêu cầu in bill."},statusCode:403);await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("UPDATE dbo.Orders SET BillRequestedAt=COALESCE(BillRequestedAt,SYSUTCDATETIME()) OUTPUT INSERTED.BillRequestedAt WHERE Id=@id AND Status NOT IN ('paid','cancelled')",connection);command.Parameters.AddWithValue("@id",id);var requestedAt=await command.ExecuteScalarAsync();if(requestedAt is not DateTime time)return Results.NotFound(new{error="Đơn không còn hoạt động."});var printError=await ReceiptPrinter.TryAutoPrint(coffeeConnection,id);return Results.Ok(new{id,billRequestedAt=time,printError});
});
app.MapDelete("/api/order-items/{id:guid}", async (Guid id, ClaimsPrincipal user) =>
{
    var role=user.FindFirstValue(ClaimTypes.Role)??"";if(!new[]{"owner","manager","cashier","server"}.Contains(role))return Results.Json(new{error="Bạn không có quyền hủy món."},statusCode:403);await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();await using var find=new SqlCommand("SELECT i.OrderId,(i.UnitPrice+ISNULL((SELECT SUM(t.Price*t.Quantity) FROM dbo.OrderItemToppings t WHERE t.OrderItemId=i.Id),0))*i.Quantity,i.Status FROM dbo.OrderItems i WHERE i.Id=@id",connection,(SqlTransaction)transaction);find.Parameters.AddWithValue("@id",id);await using var reader=await find.ExecuteReaderAsync();if(!await reader.ReadAsync()){await transaction.RollbackAsync();return Results.NotFound(new{error="Không tìm thấy món."});}var orderId=reader.GetGuid(0);var amount=reader.GetInt32(1);var status=reader.GetString(2);await reader.CloseAsync();if(status!="sent"){await transaction.RollbackAsync();return Results.Conflict(new{error="Món đã bắt đầu pha nên không thể hủy."});}await using var cancel=new SqlCommand("UPDATE dbo.OrderItems SET Status='cancelled' WHERE Id=@id;UPDATE dbo.Orders SET Subtotal=CASE WHEN Subtotal>=@amount THEN Subtotal-@amount ELSE 0 END,Total=CASE WHEN Subtotal-@amount-Discount-LoyaltyDiscount>=0 THEN Subtotal-@amount-Discount-LoyaltyDiscount ELSE 0 END WHERE Id=@order;INSERT dbo.OrderCancellations(OrderId,OrderItemId,CancelledBy,Reason,Amount)VALUES(@order,@id,@user,N'Hủy trước khi pha',@amount)",connection,(SqlTransaction)transaction);cancel.Parameters.AddWithValue("@id",id);cancel.Parameters.AddWithValue("@order",orderId);cancel.Parameters.AddWithValue("@amount",amount);cancel.Parameters.AddWithValue("@user",Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!));await cancel.ExecuteNonQueryAsync();await using var remaining=new SqlCommand("SELECT COUNT(*) FROM dbo.OrderItems WHERE OrderId=@order AND Status<>'cancelled'",connection,(SqlTransaction)transaction);remaining.Parameters.AddWithValue("@order",orderId);var count=(int)(await remaining.ExecuteScalarAsync())!;if(count==0){await using var close=new SqlCommand("UPDATE dbo.Orders SET Status='cancelled',ClosedAt=SYSUTCDATETIME() WHERE Id=@order;UPDATE dbo.DiningTables SET Status='available' WHERE Id=(SELECT TableId FROM dbo.Orders WHERE Id=@order)",connection,(SqlTransaction)transaction);close.Parameters.AddWithValue("@order",orderId);await close.ExecuteNonQueryAsync();}await transaction.CommitAsync();return Results.Ok(new{id,status="cancelled",orderId});
});
app.MapPatch("/api/order-items/{id:guid}/status", async (Guid id, StatusInput input, ClaimsPrincipal user) =>
{
    var role=user.FindFirstValue(ClaimTypes.Role)??"";var allowed=new[]{"preparing","ready"};if(!new[]{"owner","manager","barista"}.Contains(role))return Results.Json(new{error="Chỉ bộ phận pha chế được cập nhật món."},statusCode:403);if(!allowed.Contains(input.Status))return Results.BadRequest(new{error="Pha chế chỉ được chuyển món sang đang pha hoặc sẵn sàng."});await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();await using var find=new SqlCommand("SELECT OrderId FROM dbo.OrderItems WHERE Id=@id",connection,(SqlTransaction)transaction);find.Parameters.AddWithValue("@id",id);var found=await find.ExecuteScalarAsync();if(found is not Guid orderId){await transaction.RollbackAsync();return Results.NotFound(new{error="Không tìm thấy món trong đơn."});}var userId=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);await InventoryFeatures.DeductOrderItem(connection,(SqlTransaction)transaction,id,userId);await using var updateItem=new SqlCommand("UPDATE dbo.OrderItems SET Status=@status,PreparedBy=COALESCE(PreparedBy,@user),ReadyAt=CASE WHEN @status='ready' THEN SYSUTCDATETIME() ELSE ReadyAt END WHERE Id=@id",connection,(SqlTransaction)transaction);updateItem.Parameters.AddWithValue("@status",input.Status);updateItem.Parameters.AddWithValue("@user",userId);updateItem.Parameters.AddWithValue("@id",id);await updateItem.ExecuteNonQueryAsync();await using var updateOrder=new SqlCommand("UPDATE dbo.Orders SET Status=CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.OrderItems WHERE OrderId=@order AND Status NOT IN ('ready','served','cancelled')) THEN 'ready' WHEN EXISTS(SELECT 1 FROM dbo.OrderItems WHERE OrderId=@order AND Status IN ('preparing','ready','served')) THEN 'preparing' ELSE 'sent' END WHERE Id=@order",connection,(SqlTransaction)transaction);updateOrder.Parameters.AddWithValue("@order",orderId);await updateOrder.ExecuteNonQueryAsync();await transaction.CommitAsync();if(input.Status=="ready")await WebPushFeatures.SendReady(coffeeConnection,orderId);return Results.Ok(new{id,status=input.Status,orderId});
});
app.MapGet("/api/orders", async () =>
{
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("SELECT o.Id,o.OrderNumber,o.Status,o.Total,o.OpenedAt,o.ClosedAt,ISNULL(t.Code,N'Mang đi'),ISNULL(SUM(CASE WHEN i.Status<>'cancelled' THEN CONVERT(int,i.Quantity) ELSE 0 END),0),o.BillRequestedAt,o.OpenedBy FROM dbo.Orders o LEFT JOIN dbo.DiningTables t ON t.Id=o.TableId LEFT JOIN dbo.OrderItems i ON i.OrderId=o.Id GROUP BY o.Id,o.OrderNumber,o.Status,o.Total,o.OpenedAt,o.ClosedAt,t.Code,o.BillRequestedAt,o.OpenedBy ORDER BY o.OpenedAt DESC",connection);await using var reader=await command.ExecuteReaderAsync();var rows=new List<object>();while(await reader.ReadAsync())rows.Add(new{id=reader.GetGuid(0),orderNumber=reader.GetInt64(1),status=reader.GetString(2),total=reader.GetInt32(3),openedAt=reader.GetDateTime(4),closedAt=reader.IsDBNull(5)?(DateTime?)null:reader.GetDateTime(5),tableName=reader.GetString(6),itemCount=reader.GetInt32(7),billRequestedAt=reader.IsDBNull(8)?(DateTime?)null:reader.GetDateTime(8),openedBy=reader.IsDBNull(9)?(Guid?)null:reader.GetGuid(9)});return Results.Ok(rows);
});
app.MapPatch("/api/orders/{id:guid}/status", async (Guid id, StatusInput input, ClaimsPrincipal user) =>
{
    var role=user.FindFirstValue(ClaimTypes.Role)??"";var allowed=new[]{"sent","preparing","ready","served","paid","cancelled"};if(!allowed.Contains(input.Status))return Results.BadRequest(new{error="Trạng thái không hợp lệ."});if(new[]{"preparing","ready"}.Contains(input.Status)&&!new[]{"owner","manager","barista"}.Contains(role))return Results.Json(new{error="Chỉ bộ phận pha chế được cập nhật trạng thái này."},statusCode:403);if(input.Status=="served"&&!new[]{"owner","manager","cashier"}.Contains(role))return Results.Json(new{error="Đơn sẵn sàng phải được thu ngân xác nhận."},statusCode:403);if(new[]{"paid","cancelled"}.Contains(input.Status)&&!new[]{"owner","manager","cashier"}.Contains(role))return Results.Json(new{error="Bạn không có quyền đóng đơn."},statusCode:403);await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();await using var findTable=new SqlCommand("SELECT TableId,Total FROM dbo.Orders WHERE Id=@id",connection,(SqlTransaction)transaction);findTable.Parameters.AddWithValue("@id",id);await using var tableReader=await findTable.ExecuteReaderAsync();if(!await tableReader.ReadAsync()){await transaction.RollbackAsync();return Results.NotFound();}var table=tableReader.IsDBNull(0)?(Guid?)null:tableReader.GetGuid(0);var cancelAmount=tableReader.GetInt32(1);await tableReader.CloseAsync();var uid=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);if(new[]{"preparing","ready"}.Contains(input.Status))await InventoryFeatures.DeductOrder(connection,(SqlTransaction)transaction,id,uid);if(input.Status=="cancelled")await InventoryFeatures.MarkOrderWaste(connection,(SqlTransaction)transaction,id);await using var command=new SqlCommand("IF @status='cancelled' UPDATE c SET Points=Points+o.PointsRedeemed FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId=c.Id WHERE o.Id=@id;UPDATE dbo.Orders SET Status=@status,ClosedAt=CASE WHEN @status IN ('paid','cancelled') THEN SYSUTCDATETIME() ELSE ClosedAt END,ClosedBy=CASE WHEN @status IN ('paid','cancelled') THEN @user ELSE ClosedBy END WHERE Id=@id;UPDATE dbo.OrderItems SET Status=@status,PreparedBy=CASE WHEN @status IN ('preparing','ready') THEN COALESCE(PreparedBy,@user) ELSE PreparedBy END,ReadyAt=CASE WHEN @status='ready' THEN COALESCE(ReadyAt,SYSUTCDATETIME()) ELSE ReadyAt END,ServedAt=CASE WHEN @status='served' THEN COALESCE(ServedAt,SYSUTCDATETIME()) ELSE ServedAt END WHERE OrderId=@id AND Status NOT IN ('paid','cancelled')",connection,(SqlTransaction)transaction);command.Parameters.AddWithValue("@status",input.Status);command.Parameters.AddWithValue("@user",uid);command.Parameters.AddWithValue("@id",id);var changed=await command.ExecuteNonQueryAsync();if(input.Status=="cancelled"){await using var audit=new SqlCommand("INSERT dbo.OrderCancellations(OrderId,CancelledBy,Reason,Amount) VALUES(@id,@user,@reason,@amount)",connection,(SqlTransaction)transaction);audit.Parameters.AddWithValue("@id",id);audit.Parameters.AddWithValue("@user",uid);audit.Parameters.AddWithValue("@reason",string.IsNullOrWhiteSpace(input.Reason)?"Hủy toàn bộ đơn":input.Reason.Trim());audit.Parameters.AddWithValue("@amount",cancelAmount);await audit.ExecuteNonQueryAsync();}if(changed>0&&table.HasValue&&new[]{"paid","cancelled"}.Contains(input.Status)){await using var free=new SqlCommand("UPDATE dbo.DiningTables SET Status='available' WHERE Id=@id",connection,(SqlTransaction)transaction);free.Parameters.AddWithValue("@id",table.Value);await free.ExecuteNonQueryAsync();}await transaction.CommitAsync();if(changed>0&&input.Status=="ready")await WebPushFeatures.SendReady(coffeeConnection,id);return changed>0?Results.Ok(new{id,status=input.Status}):Results.NotFound();
});
app.MapGet("/api/reports/summary", async () =>
{
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("SELECT ISNULL(SUM(CASE WHEN Status='paid' THEN Total ELSE 0 END),0),SUM(CASE WHEN Status='paid' THEN 1 ELSE 0 END),SUM(CASE WHEN Status='cancelled' THEN 1 ELSE 0 END) FROM dbo.Orders WHERE CAST(OpenedAt AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time' AS date)=CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time' AS date)",connection);await using var reader=await command.ExecuteReaderAsync();await reader.ReadAsync();return Results.Ok(new{revenue=reader.GetInt32(0),paidOrders=reader.IsDBNull(1)?0:reader.GetInt32(1),cancelledOrders=reader.IsDBNull(2)?0:reader.GetInt32(2)});
});
app.MapGet("/api/payment-settings", async () =>
{
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await new SqlCommand("IF OBJECT_ID('dbo.PaymentSettings','U') IS NULL CREATE TABLE dbo.PaymentSettings(StoreId uniqueidentifier NOT NULL PRIMARY KEY REFERENCES dbo.Stores(Id),BankId nvarchar(30) NOT NULL,AccountNumber nvarchar(50) NOT NULL,AccountName nvarchar(150) NOT NULL,UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME())",connection).ExecuteNonQueryAsync();await using var command=new SqlCommand("SELECT TOP 1 BankId,AccountNumber,AccountName FROM dbo.PaymentSettings",connection);await using var reader=await command.ExecuteReaderAsync();return await reader.ReadAsync()?Results.Ok(new{bankId=reader.GetString(0),accountNumber=reader.GetString(1),accountName=reader.GetString(2)}):Results.Ok(new{bankId="",accountNumber="",accountName=""});
});
app.MapPut("/api/payment-settings", async (PaymentSettingsInput input) =>
{
    if(string.IsNullOrWhiteSpace(input.BankId)||string.IsNullOrWhiteSpace(input.AccountNumber)||string.IsNullOrWhiteSpace(input.AccountName))return Results.BadRequest(new{error="Vui lòng nhập đủ thông tin tài khoản nhận tiền."});await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await new SqlCommand("IF OBJECT_ID('dbo.PaymentSettings','U') IS NULL CREATE TABLE dbo.PaymentSettings(StoreId uniqueidentifier NOT NULL PRIMARY KEY REFERENCES dbo.Stores(Id),BankId nvarchar(30) NOT NULL,AccountNumber nvarchar(50) NOT NULL,AccountName nvarchar(150) NOT NULL,UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME())",connection).ExecuteNonQueryAsync();await using var command=new SqlCommand("DECLARE @s uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Stores);MERGE dbo.PaymentSettings AS t USING(SELECT @s StoreId)s ON t.StoreId=s.StoreId WHEN MATCHED THEN UPDATE SET BankId=@b,AccountNumber=@a,AccountName=@n,UpdatedAt=SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT(StoreId,BankId,AccountNumber,AccountName)VALUES(@s,@b,@a,@n);",connection);command.Parameters.AddWithValue("@b",input.BankId.Trim());command.Parameters.AddWithValue("@a",input.AccountNumber.Trim());command.Parameters.AddWithValue("@n",input.AccountName.Trim().ToUpperInvariant());await command.ExecuteNonQueryAsync();return Results.Ok(input);
});
app.MapPost("/api/orders/{id:guid}/pay", async (Guid id, PaymentInput input, ClaimsPrincipal user) =>
{
    var methods=new[]{"cash","bank_transfer","card","e_wallet"};if(!methods.Contains(input.Method))return Results.BadRequest(new{error="Phương thức thanh toán không hợp lệ."});var userId=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();if((user.FindFirstValue(ClaimTypes.Role)??"")=="cashier"){await using var shiftCheck=new SqlCommand("SELECT COUNT(*) FROM dbo.WorkShifts WHERE OpenedBy=@user AND Status='open'",connection);shiftCheck.Parameters.AddWithValue("@user",userId);if((int)(await shiftCheck.ExecuteScalarAsync())!<1)return Results.Conflict(new{error="Thu ngân phải mở ca trước khi nhận thanh toán."});}await using var transaction=await connection.BeginTransactionAsync();await using var find=new SqlCommand("SELECT TableId,Total,CustomerId FROM dbo.Orders WHERE Id=@id AND Status NOT IN ('paid','cancelled')",connection,(SqlTransaction)transaction);find.Parameters.AddWithValue("@id",id);await using var reader=await find.ExecuteReaderAsync();if(!await reader.ReadAsync()){await transaction.RollbackAsync();return Results.NotFound(new{error="Đơn đã đóng hoặc không tồn tại."});}var tableId=reader.IsDBNull(0)?(Guid?)null:reader.GetGuid(0);var amount=reader.GetInt32(1);var customerId=reader.IsDBNull(2)?(Guid?)null:reader.GetGuid(2);await reader.CloseAsync();await using var command=new SqlCommand("DECLARE @shift uniqueidentifier=(SELECT TOP 1 Id FROM dbo.WorkShifts WHERE OpenedBy=@user AND Status='open' ORDER BY OpenedAt DESC);INSERT dbo.Payments(OrderId,Amount,Method,Reference,ReceivedBy)VALUES(@id,@amount,@method,@reference,@user);UPDATE dbo.Orders SET Status='paid',ClosedAt=SYSUTCDATETIME(),ClosedBy=@user,ShiftId=@shift WHERE Id=@id;UPDATE dbo.OrderItems SET Status='paid' WHERE OrderId=@id AND Status<>'cancelled';UPDATE dbo.DiningTables SET Status=CASE WHEN EXISTS(SELECT 1 FROM dbo.Orders WHERE TableId=@table AND Status NOT IN ('paid','cancelled') AND Id<>@id) THEN 'occupied' ELSE 'available' END WHERE Id=@table;INSERT dbo.PromotionUsages(PromotionId,OrderId,CustomerId) SELECT PromotionId,Id,CustomerId FROM dbo.Orders WHERE Id=@id AND PromotionId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.PromotionUsages WHERE OrderId=@id);IF @customer IS NOT NULL BEGIN UPDATE dbo.Customers SET TotalSpent=TotalSpent+@amount,Points=Points+(@amount/10000) WHERE Id=@customer;INSERT dbo.CustomerPointTransactions(CustomerId,OrderId,Points,Reason)VALUES(@customer,@id,@amount/10000,N'Tích điểm từ thanh toán') END",connection,(SqlTransaction)transaction);command.Parameters.AddWithValue("@id",id);command.Parameters.AddWithValue("@amount",amount);command.Parameters.AddWithValue("@method",input.Method);command.Parameters.AddWithValue("@reference",(object?)input.Reference??DBNull.Value);command.Parameters.AddWithValue("@table",(object?)tableId??DBNull.Value);command.Parameters.AddWithValue("@user",userId);command.Parameters.AddWithValue("@customer",(object?)customerId??DBNull.Value);await command.ExecuteNonQueryAsync();await transaction.CommitAsync();if(input.Method=="cash")await ReceiptPrinter.TryOpenDrawer(coffeeConnection);return Results.Ok(new{id,amount,earnedPoints=customerId.HasValue?amount/10000:0,method=input.Method,paidAt=DateTime.UtcNow});
});
app.MapGet("/api/users", async () =>
{
    await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("SELECT Id,FullName,Email,Role,IsActive,CreatedAt FROM dbo.Users WHERE DeletedAt IS NULL ORDER BY CASE Role WHEN 'owner' THEN 0 WHEN 'manager' THEN 1 ELSE 2 END,CreatedAt",connection);await using var reader=await command.ExecuteReaderAsync();var rows=new List<object>();while(await reader.ReadAsync())rows.Add(new{id=reader.GetGuid(0),name=reader.GetString(1),email=reader.GetString(2),role=reader.GetString(3),isActive=reader.GetBoolean(4),createdAt=reader.GetDateTime(5)});return Results.Ok(rows);
});
app.MapPost("/api/users", async (UserInput input) =>
{
    var roles=new[]{"manager","cashier","server","barista"};if(!roles.Contains(input.Role??"")||string.IsNullOrWhiteSpace(input.Name)||string.IsNullOrWhiteSpace(input.Email)||!System.Net.Mail.MailAddress.TryCreate(input.Email.Trim(),out _)||string.IsNullOrEmpty(input.Password)||input.Password.Length<8)return Results.BadRequest(new{error="Tên, email, mật khẩu hoặc vai trò không hợp lệ."});await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();var store=(Guid)(await new SqlCommand("SELECT TOP 1 Id FROM dbo.Stores",connection).ExecuteScalarAsync())!;await using var command=new SqlCommand("INSERT dbo.Users(StoreId,FullName,Email,PasswordHash,Role) OUTPUT INSERTED.Id VALUES(@s,@n,@e,@p,@r)",connection);command.Parameters.AddWithValue("@s",store);command.Parameters.AddWithValue("@n",input.Name.Trim());command.Parameters.AddWithValue("@e",input.Email.Trim().ToLowerInvariant());command.Parameters.AddWithValue("@p",BCrypt.Net.BCrypt.HashPassword(input.Password,12));command.Parameters.AddWithValue("@r",input.Role);try{return Results.Ok(new{id=(Guid)(await command.ExecuteScalarAsync())!});}catch(SqlException){return Results.Conflict(new{error="Email đã tồn tại."});}
});
app.MapPut("/api/users/{id:guid}",async(Guid id,UserUpdateInput input,ClaimsPrincipal user)=>{var roles=new[]{"manager","cashier","server","barista"};if(string.IsNullOrWhiteSpace(input.Name)||string.IsNullOrWhiteSpace(input.Email)||string.IsNullOrWhiteSpace(input.Role)||!System.Net.Mail.MailAddress.TryCreate(input.Email.Trim(),out _))return Results.BadRequest(new{error="Tên hoặc email không hợp lệ."});var actor=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var find=new SqlCommand("SELECT Role FROM dbo.Users WHERE Id=@id AND DeletedAt IS NULL",connection);find.Parameters.AddWithValue("@id",id);var oldRole=(string?)await find.ExecuteScalarAsync();if(oldRole is null)return Results.NotFound(new{error="Không tìm thấy tài khoản."});if((oldRole=="owner"&&input.Role!="owner")||(oldRole!="owner"&&!roles.Contains(input.Role)))return Results.BadRequest(new{error="Vai trò không hợp lệ."});if(actor==id&&input.Role!=oldRole)return Results.BadRequest(new{error="Bạn không thể tự thay đổi vai trò của mình."});await using var command=new SqlCommand("UPDATE dbo.Users SET FullName=@n,Email=@e,Role=@r,AuthVersion=AuthVersion+CASE WHEN Role<>@r THEN 1 ELSE 0 END WHERE Id=@id AND DeletedAt IS NULL",connection);command.Parameters.AddWithValue("@n",input.Name.Trim());command.Parameters.AddWithValue("@e",input.Email.Trim().ToLowerInvariant());command.Parameters.AddWithValue("@r",input.Role);command.Parameters.AddWithValue("@id",id);try{await command.ExecuteNonQueryAsync();return Results.Ok(new{id,name=input.Name.Trim(),email=input.Email.Trim().ToLowerInvariant(),role=input.Role});}catch(SqlException){return Results.Conflict(new{error="Email đã được tài khoản khác sử dụng."});}});
app.MapPatch("/api/users/{id:guid}/status",async(Guid id,UserStatusInput input,ClaimsPrincipal user)=>{var actor=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);if(actor==id)return Results.BadRequest(new{error="Bạn không thể tự khóa tài khoản đang đăng nhập."});await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("UPDATE dbo.Users SET IsActive=@active,AuthVersion=AuthVersion+1 WHERE Id=@id AND DeletedAt IS NULL AND Role<>'owner'",connection);command.Parameters.AddWithValue("@active",input.IsActive);command.Parameters.AddWithValue("@id",id);return await command.ExecuteNonQueryAsync()>0?Results.Ok(new{id,isActive=input.IsActive}):Results.BadRequest(new{error="Không thể thay đổi trạng thái tài khoản chủ cửa hàng."});});
app.MapPatch("/api/users/{id:guid}/password",async(Guid id,UserPasswordInput input)=>{if(string.IsNullOrEmpty(input.Password)||input.Password.Length<8)return Results.BadRequest(new{error="Mật khẩu phải có ít nhất 8 ký tự."});await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("UPDATE dbo.Users SET PasswordHash=@p,AuthVersion=AuthVersion+1 WHERE Id=@id AND DeletedAt IS NULL",connection);command.Parameters.AddWithValue("@p",BCrypt.Net.BCrypt.HashPassword(input.Password,12));command.Parameters.AddWithValue("@id",id);return await command.ExecuteNonQueryAsync()>0?Results.Ok(new{id}):Results.NotFound(new{error="Không tìm thấy tài khoản."});});
app.MapDelete("/api/users/{id:guid}",async(Guid id,ClaimsPrincipal user)=>{var actor=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);if(actor==id)return Results.BadRequest(new{error="Bạn không thể tự xóa tài khoản đang đăng nhập."});await using var connection=new SqlConnection(coffeeConnection);await connection.OpenAsync();await using var command=new SqlCommand("UPDATE dbo.Users SET IsActive=0,DeletedAt=SYSUTCDATETIME(),AuthVersion=AuthVersion+1 WHERE Id=@id AND DeletedAt IS NULL AND Role<>'owner'",connection);command.Parameters.AddWithValue("@id",id);return await command.ExecuteNonQueryAsync()>0?Results.Ok(new{id,archived=true}):Results.BadRequest(new{error="Không thể xóa tài khoản chủ cửa hàng."});});
app.MapAdvancedFeatures(coffeeConnection);
app.MapInventoryFeatures(coffeeConnection);
app.MapManagementSuite(coffeeConnection);
app.MapPrinterEndpoints(coffeeConnection);
app.MapWebPushFeatures(coffeeConnection);
app.MapLicenseFeatures(coffeeConnection);
app.MapFallbackToFile("index.html").AllowAnonymous();
app.Run("http://0.0.0.0:3032");

static async Task<bool> HasValidImageSignature(IFormFile file,string extension)
{
    var header=new byte[12];await using var stream=file.OpenReadStream();var read=await stream.ReadAsync(header);
    return extension switch
    {
        ".jpg"=>read>=3&&header[0]==0xff&&header[1]==0xd8&&header[2]==0xff,
        ".png"=>read>=8&&header.AsSpan(0,8).SequenceEqual(new byte[]{0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a}),
        ".webp"=>read>=12&&header.AsSpan(0,4).SequenceEqual("RIFF"u8)&&header.AsSpan(8,4).SequenceEqual("WEBP"u8),
        _=>false
    };
}

record LoginRequest(string Email, string Password);
record ProductInput(string Name, int Price);
record OrderInput(Guid TableId, List<OrderItemInput> Items);
record OrderItemInput(Guid ProductId, int Quantity, string? Note, List<ToppingSelectionInput>? Toppings, Guid? VariantId=null);
record ToppingSelectionInput(Guid ToppingId);
record TableInput(string Name, int Seats, Guid? AreaId=null, string Shape="rectangle");
record StatusInput(string Status, string? Reason = null);
record UserInput(string Name, string Email, string Password, string Role);
record UserUpdateInput(string Name,string Email,string Role);
record UserStatusInput(bool IsActive);
record UserPasswordInput(string Password);
record PaymentSettingsInput(string BankId, string AccountNumber, string AccountName);
record PaymentInput(string Method, string? Reference);
record AvailabilityInput(bool IsAvailable);
sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DateTime.Parse(reader.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
