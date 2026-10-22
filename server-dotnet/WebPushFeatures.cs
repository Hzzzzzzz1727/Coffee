using Microsoft.Data.SqlClient;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using WebPush;

public static class WebPushFeatures
{
    public static void MapWebPushFeatures(this WebApplication app,string db)
    {
        app.MapGet("/api/push/public-key",async ()=>Results.Ok(new{publicKey=(await Keys(db)).PublicKey}));
        app.MapPost("/api/push/subscribe",async (PushSubscriptionInput x,ClaimsPrincipal user)=>{if(string.IsNullOrWhiteSpace(x.Endpoint)||string.IsNullOrWhiteSpace(x.P256dh)||string.IsNullOrWhiteSpace(x.Auth))return Results.BadRequest(new{error="Dữ liệu đăng ký thông báo không hợp lệ."});await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("MERGE dbo.PushSubscriptions t USING(SELECT @endpoint Endpoint)s ON t.Endpoint=s.Endpoint WHEN MATCHED THEN UPDATE SET UserId=@user,P256dh=@p,Auth=@a WHEN NOT MATCHED THEN INSERT(UserId,Endpoint,P256dh,Auth)VALUES(@user,@endpoint,@p,@a);",c);q.Parameters.AddWithValue("@user",Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!));q.Parameters.AddWithValue("@endpoint",x.Endpoint);q.Parameters.AddWithValue("@p",x.P256dh);q.Parameters.AddWithValue("@a",x.Auth);await q.ExecuteNonQueryAsync();return Results.Ok(new{ok=true});});
        app.MapPost("/api/push/unsubscribe",async (PushUnsubscribeInput x,ClaimsPrincipal user)=>{await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("DELETE dbo.PushSubscriptions WHERE Endpoint=@endpoint AND UserId=@user",c);q.Parameters.AddWithValue("@endpoint",x.Endpoint);q.Parameters.AddWithValue("@user",Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!));await q.ExecuteNonQueryAsync();return Results.NoContent();});
    }
    public static async Task SendReady(string db,Guid orderId)
    {
        await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("SELECT o.OrderNumber,ISNULL(t.Code,N'Mang đi') FROM dbo.Orders o LEFT JOIN dbo.DiningTables t ON t.Id=o.TableId WHERE o.Id=@id;SELECT DISTINCT s.Id,s.Endpoint,s.P256dh,s.Auth FROM dbo.PushSubscriptions s JOIN dbo.Users u ON u.Id=s.UserId LEFT JOIN dbo.Orders o ON o.Id=@id WHERE u.IsActive=1 AND (u.Role='server' OR s.UserId=o.OpenedBy)",c);q.Parameters.AddWithValue("@id",orderId);await using var r=await q.ExecuteReaderAsync();if(!await r.ReadAsync())return;var number=r.GetInt64(0);var table=r.GetString(1);await r.NextResultAsync();var subscriptions=new List<(Guid Id,string Endpoint,string P256dh,string Auth)>();while(await r.ReadAsync())subscriptions.Add((r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3)));await r.CloseAsync();var keys=await Keys(db);using var client=new WebPushClient();var payload=JsonSerializer.Serialize(new{title="Món đã sẵn sàng",body=$"{table} · đơn #{number}: đến quầy nhận món",url="/"});foreach(var s in subscriptions){try{await client.SendNotificationAsync(new PushSubscription(s.Endpoint,s.P256dh,s.Auth),payload,new VapidDetails("mailto:admin@roastandroot.local",keys.PublicKey,keys.PrivateKey));}catch(WebPushException e)when(e.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound){await using var d=new SqlCommand("DELETE dbo.PushSubscriptions WHERE Id=@id",c);d.Parameters.AddWithValue("@id",s.Id);await d.ExecuteNonQueryAsync();}catch{}}
    }
    static async Task<(string PublicKey,string PrivateKey)> Keys(string db)
    {
        await using var c=new SqlConnection(db);await c.OpenAsync();await using(var q=new SqlCommand("SELECT [Key],[Value] FROM dbo.SystemSettings WHERE [Key] IN ('VapidPublicKey','VapidPrivateKey')",c)){await using var r=await q.ExecuteReaderAsync();string? pub=null,priv=null;while(await r.ReadAsync()){if(r.GetString(0)=="VapidPublicKey")pub=r.GetString(1);else priv=r.GetString(1);}if(pub!=null&&priv!=null)return(pub,priv);}
        var keys=VapidHelper.GenerateVapidKeys();await using var save=new SqlCommand("MERGE dbo.SystemSettings t USING(VALUES(N'VapidPublicKey',@pub),(N'VapidPrivateKey',@priv))s([Key],[Value]) ON t.[Key]=s.[Key] WHEN MATCHED THEN UPDATE SET [Value]=s.[Value],UpdatedAt=SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT([Key],[Value])VALUES(s.[Key],s.[Value]);",c);save.Parameters.AddWithValue("@pub",keys.PublicKey);save.Parameters.AddWithValue("@priv",keys.PrivateKey);await save.ExecuteNonQueryAsync();return(keys.PublicKey,keys.PrivateKey);
    }
}
public record PushSubscriptionInput(string Endpoint,string P256dh,string Auth);
public record PushUnsubscribeInput(string Endpoint);
