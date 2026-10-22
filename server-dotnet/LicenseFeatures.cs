using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

public record LicenseStatus(bool Allowed,bool Activated,bool TrialExpired,bool ClockInvalid,string InstallationId,DateTime TrialStartedAt,DateTime TrialExpiresAt,int TrialDaysRemaining,string? LicensedTo,DateTime? LicenseExpiresAt,string Message);

public static class LicenseFeatures
{
    const int TrialDays=15;
    static readonly SemaphoreSlim Gate=new(1,1);
    static LicenseStatus? cached;
    static DateTime cacheUntil;

    public static async Task EnsureInstallation(string db)
    {
        await using var c=new SqlConnection(db);await c.OpenAsync();
        await using var q=new SqlCommand(@"IF NOT EXISTS(SELECT 1 FROM dbo.SystemSettings WHERE [Key]=N'InstallationId') INSERT dbo.SystemSettings([Key],[Value])VALUES(N'InstallationId',UPPER(CONVERT(nvarchar(36),NEWID())));
IF NOT EXISTS(SELECT 1 FROM dbo.SystemSettings WHERE [Key]=N'TrialStartedAt') INSERT dbo.SystemSettings([Key],[Value])VALUES(N'TrialStartedAt',CONVERT(nvarchar(40),SYSUTCDATETIME(),127));
IF NOT EXISTS(SELECT 1 FROM dbo.SystemSettings WHERE [Key]=N'LicenseLastSeenUtc') INSERT dbo.SystemSettings([Key],[Value])VALUES(N'LicenseLastSeenUtc',CONVERT(nvarchar(40),SYSUTCDATETIME(),127));",c);
        await q.ExecuteNonQueryAsync();
    }

    public static async Task<LicenseStatus> Status(string db,bool force=false)
    {
        if(!force&&cached!=null&&DateTime.UtcNow<cacheUntil)return cached;
        await Gate.WaitAsync();
        try
        {
            if(!force&&cached!=null&&DateTime.UtcNow<cacheUntil)return cached;
            await EnsureInstallation(db);await using var c=new SqlConnection(db);await c.OpenAsync();
            await using var q=new SqlCommand("SELECT [Key],[Value] FROM dbo.SystemSettings WHERE [Key] IN (N'InstallationId',N'TrialStartedAt',N'LicenseLastSeenUtc',N'LicenseKey')",c);
            await using var r=await q.ExecuteReaderAsync();var values=new Dictionary<string,string>();while(await r.ReadAsync())values[r.GetString(0)]=r.GetString(1);await r.CloseAsync();
            var now=DateTime.UtcNow;var installationId=values["InstallationId"].ToUpperInvariant();var started=DateTime.Parse(values["TrialStartedAt"],null,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();var lastSeen=DateTime.Parse(values["LicenseLastSeenUtc"],null,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();var clockInvalid=now<lastSeen.AddMinutes(-5);var trialExpires=started.AddDays(TrialDays);var days=Math.Max(0,(int)Math.Ceiling((trialExpires-now).TotalDays));
            string? licensedTo=null;DateTime? licenseExpires=null;var activated=false;
            if(values.TryGetValue("LicenseKey",out var key)&&Verify(key,installationId,out licensedTo,out licenseExpires))activated=!licenseExpires.HasValue||licenseExpires.Value>=now;
            var allowed=!clockInvalid&&(activated||now<trialExpires);var expired=now>=trialExpires&&!activated;
            var message=clockInvalid?"Ngày giờ hệ thống đã bị lùi. Hãy chỉnh đúng thời gian rồi khởi động lại.":activated?(licenseExpires.HasValue?$"Bản quyền của {licensedTo}, hết hạn {licenseExpires:dd/MM/yyyy}.":$"Bản quyền vĩnh viễn của {licensedTo}."):expired?"Thời gian dùng thử 15 ngày đã hết. Hãy nhập key để tiếp tục.":$"Đang dùng thử, còn {days} ngày.";
            if(now>=lastSeen){await using var u=new SqlCommand("UPDATE dbo.SystemSettings SET [Value]=@now,UpdatedAt=SYSUTCDATETIME() WHERE [Key]=N'LicenseLastSeenUtc'",c);u.Parameters.AddWithValue("@now",now.ToString("O"));await u.ExecuteNonQueryAsync();}
            cached=new LicenseStatus(allowed,activated,expired,clockInvalid,installationId,started,trialExpires,days,licensedTo,licenseExpires,message);cacheUntil=now.AddSeconds(15);return cached;
        }
        finally{Gate.Release();}
    }

    public static async Task<LicenseStatus> Activate(string db,string key)
    {
        var status=await Status(db,true);if(!Verify(key.Trim(),status.InstallationId,out _,out _))throw new InvalidOperationException("Key không hợp lệ hoặc không thuộc máy này.");
        await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("MERGE dbo.SystemSettings t USING(SELECT N'LicenseKey' [Key])s ON t.[Key]=s.[Key] WHEN MATCHED THEN UPDATE SET [Value]=@key,UpdatedAt=SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT([Key],[Value])VALUES(N'LicenseKey',@key);",c);q.Parameters.AddWithValue("@key",key.Trim());await q.ExecuteNonQueryAsync();cached=null;return await Status(db,true);
    }

    static bool Verify(string key,string installationId,out string? licensedTo,out DateTime? expiresAt)
    {
        licensedTo=null;expiresAt=null;
        try
        {
            var parts=key.Split('.');if(parts.Length!=2)return false;var payload=Base64Url(parts[0]);var signature=Base64Url(parts[1]);
            var publicPath=Path.Combine(AppContext.BaseDirectory,"license-public.pem");if(!File.Exists(publicPath))return false;using var rsa=RSA.Create();rsa.ImportFromPem(File.ReadAllText(publicPath));if(!rsa.VerifyData(payload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))return false;
            using var doc=JsonDocument.Parse(payload);var root=doc.RootElement;if(!string.Equals(root.GetProperty("installationId").GetString(),installationId,StringComparison.OrdinalIgnoreCase))return false;licensedTo=root.GetProperty("licensedTo").GetString();if(root.TryGetProperty("expiresAt",out var expiry)&&expiry.ValueKind==JsonValueKind.String)expiresAt=expiry.GetDateTime().ToUniversalTime();return true;
        }
        catch{return false;}
    }
    static byte[] Base64Url(string value){var s=value.Replace('-','+').Replace('_','/');s+=new string('=',(4-s.Length%4)%4);return Convert.FromBase64String(s);}

    public static void MapLicenseFeatures(this WebApplication app,string db)
    {
        app.MapGet("/api/license/status",async ()=>Results.Ok(await Status(db,true))).AllowAnonymous();
        app.MapPost("/api/license/activate",async (ActivateLicenseInput x)=>{try{return Results.Ok(await Activate(db,x.Key));}catch(InvalidOperationException e){return Results.BadRequest(new{error=e.Message});}}).AllowAnonymous();
    }
}
public record ActivateLicenseInput(string Key);
