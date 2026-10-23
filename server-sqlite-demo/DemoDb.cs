using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public static class DemoDb
{
    public static string DataDirectory { get; } = Environment.GetEnvironmentVariable("COFFEE_DEMO_DATA_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoffeeOS Demo");
    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "coffee-demo.db");
    public static string ConnectionString { get; } = new SqliteConnectionStringBuilder { DataSource=DatabasePath,Mode=SqliteOpenMode.ReadWriteCreate,Cache=SqliteCacheMode.Shared,ForeignKeys=true }.ToString();
    public static string Now => DateTime.UtcNow.ToString("O");
    public static string Id() => Guid.NewGuid().ToString();

    public static async Task Initialize()
    {
        Directory.CreateDirectory(DataDirectory);
        await using var c=Open();
        var schemaPath=Path.Combine(AppContext.BaseDirectory,"schema.sql");
        var schema=await File.ReadAllTextAsync(schemaPath);
        var triggerStart=schema.IndexOf("CREATE TRIGGER IF NOT EXISTS TR_Orders_InventoryFallback",StringComparison.Ordinal);
        await new SqliteCommand(triggerStart<0?schema:schema[..triggerStart],c).ExecuteNonQueryAsync();
        await EnsureColumn(c,"OrderItems","InventoryDeducted","INTEGER NOT NULL DEFAULT 0");
        await EnsureColumn(c,"OrderItems","InventoryDeductedAt","TEXT");
        await EnsureColumn(c,"Users","AuthVersion","INTEGER NOT NULL DEFAULT 1");
        await EnsureColumn(c,"Users","DeletedAt","TEXT");
        if(triggerStart>=0)await new SqliteCommand(schema[triggerStart..],c).ExecuteNonQueryAsync();
        if(await ScalarLong(c,"SELECT COUNT(*) FROM Stores")==0)
        {
            await Exec(c,"INSERT INTO Stores(Id,Name,Address,CreatedAt)VALUES(@id,'Roast & Root',NULL,@now)",("@id",Id()),("@now",Now));
            var store=await ScalarString(c,"SELECT Id FROM Stores LIMIT 1")??throw new InvalidOperationException();
            await Exec(c,"INSERT INTO Areas(Id,StoreId,Name,Position)VALUES(@id,@s,'Khu vực chính',1)",("@id",Id()),("@s",store));
        }
        await EnsureSetting(c,"InstallationId",Guid.NewGuid().ToString().ToUpperInvariant());
        await EnsureSetting(c,"TrialStartedAt",Now);
        await EnsureSetting(c,"LicenseLastSeenUtc",Now);
    }

    public static SqliteConnection Open()
    {
        var c=new SqliteConnection(ConnectionString);c.Open();
        using var q=c.CreateCommand();q.CommandText="PRAGMA foreign_keys=ON;PRAGMA busy_timeout=5000;";q.ExecuteNonQuery();
        return c;
    }
    public static SqliteCommand Command(SqliteConnection c,string sql,SqliteTransaction? tx=null,params (string Name,object? Value)[] args)
    {
        var q=c.CreateCommand();q.CommandText=sql;q.Transaction=tx;
        foreach(var (name,value) in args)q.Parameters.AddWithValue(name,value switch { Guid g=>g.ToString(),DateTime d=>d.ToUniversalTime().ToString("O"),bool b=>b?1:0,null=>DBNull.Value,_=>value });
        return q;
    }
    public static async Task<int> Exec(SqliteConnection c,string sql,params (string,object?)[] args)=>await Command(c,sql,null,args).ExecuteNonQueryAsync();
    public static async Task<int> Exec(SqliteConnection c,SqliteTransaction tx,string sql,params (string,object?)[] args)=>await Command(c,sql,tx,args).ExecuteNonQueryAsync();
    public static async Task<object?> Scalar(SqliteConnection c,string sql,params (string,object?)[] args)=>await Command(c,sql,null,args).ExecuteScalarAsync();
    public static async Task<long> ScalarLong(SqliteConnection c,string sql,params (string,object?)[] args)=>Convert.ToInt64(await Scalar(c,sql,args)??0);
    public static async Task<string?> ScalarString(SqliteConnection c,string sql,params (string,object?)[] args)=>(await Scalar(c,sql,args))?.ToString();
    public static async Task<List<Dictionary<string,object?>>> Rows(SqliteConnection c,string sql,params (string,object?)[] args)=>await Rows(c,null,sql,args);
    public static async Task<List<Dictionary<string,object?>>> Rows(SqliteConnection c,SqliteTransaction? tx,string sql,params (string,object?)[] args)
    {
        await using var q=Command(c,sql,tx,args);await using var r=await q.ExecuteReaderAsync();var rows=new List<Dictionary<string,object?>>();
        while(await r.ReadAsync()){var row=new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase);for(var i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);rows.Add(row);}return rows;
    }
    public static string S(this Dictionary<string,object?> row,string key)=>row[key]?.ToString()??"";
    public static long L(this Dictionary<string,object?> row,string key)=>Convert.ToInt64(row[key]??0);
    public static int I(this Dictionary<string,object?> row,string key)=>Convert.ToInt32(row[key]??0);
    public static bool B(this Dictionary<string,object?> row,string key)=>Convert.ToInt32(row[key]??0)!=0;
    public static Guid G(this Dictionary<string,object?> row,string key)=>Guid.Parse(row.S(key));
    public static DateTime D(this Dictionary<string,object?> row,string key)=>DateTime.Parse(row.S(key),null,DateTimeStyles.RoundtripKind);
    public static DateTime? DN(this Dictionary<string,object?> row,string key)=>row[key] is null?null:row.D(key);
    static async Task EnsureSetting(SqliteConnection c,string key,string value)=>await Exec(c,"INSERT OR IGNORE INTO SystemSettings(Key,Value,UpdatedAt)VALUES(@k,@v,@n)",("@k",key),("@v",value),("@n",Now));
    static async Task EnsureColumn(SqliteConnection c,string table,string column,string definition)
    {
        var columns=await Rows(c,$"PRAGMA table_info({table})");
        if(!columns.Any(x=>string.Equals(x.S("name"),column,StringComparison.OrdinalIgnoreCase)))
            await Exec(c,$"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }
}

public record DemoLicenseStatus(bool Allowed,bool Activated,bool TrialExpired,bool ClockInvalid,string InstallationId,DateTime TrialStartedAt,DateTime TrialExpiresAt,int TrialDaysRemaining,string? LicensedTo,DateTime? LicenseExpiresAt,string Message);
public static class DemoLicense
{
    const int TrialDays=15;
    public static async Task<DemoLicenseStatus> Status()
    {
        await using var c=DemoDb.Open();var rows=await DemoDb.Rows(c,"SELECT Key,Value FROM SystemSettings WHERE Key IN ('InstallationId','TrialStartedAt','LicenseLastSeenUtc','LicenseKey')");var values=rows.ToDictionary(x=>x.S("Key"),x=>x.S("Value"));
        var now=DateTime.UtcNow;var machine=values["InstallationId"].ToUpperInvariant();var started=DateTime.Parse(values["TrialStartedAt"],null,DateTimeStyles.RoundtripKind).ToUniversalTime();var last=DateTime.Parse(values["LicenseLastSeenUtc"],null,DateTimeStyles.RoundtripKind).ToUniversalTime();var invalid=now<last.AddMinutes(-5);var end=started.AddDays(TrialDays);var days=Math.Max(0,(int)Math.Ceiling((end-now).TotalDays));
        string? licensedTo=null;DateTime? expiry=null;var activated=values.TryGetValue("LicenseKey",out var key)&&Verify(key,machine,out licensedTo,out expiry)&&(!expiry.HasValue||expiry>=now);var allowed=!invalid&&(activated||now<end);var expired=!activated&&now>=end;
        if(now>=last)await DemoDb.Exec(c,"UPDATE SystemSettings SET Value=@v,UpdatedAt=@n WHERE Key='LicenseLastSeenUtc'",("@v",now.ToString("O")),("@n",DemoDb.Now));
        var message=invalid?"Ngày giờ hệ thống đã bị lùi. Hãy chỉnh đúng thời gian.":activated?(expiry.HasValue?$"Bản quyền của {licensedTo}, hết hạn {expiry:dd/MM/yyyy}.":$"Bản quyền vĩnh viễn của {licensedTo}."):expired?"Thời gian dùng thử 15 ngày đã hết. Hãy nhập key để tiếp tục.":$"Đang dùng thử, còn {days} ngày.";
        return new(allowed,activated,expired,invalid,machine,started,end,days,licensedTo,expiry,message);
    }
    public static async Task<DemoLicenseStatus> Activate(string key)
    {
        var status=await Status();if(!Verify(key.Trim(),status.InstallationId,out _,out _))throw new InvalidOperationException("Key không hợp lệ hoặc không thuộc máy này.");await using var c=DemoDb.Open();await DemoDb.Exec(c,"INSERT INTO SystemSettings(Key,Value,UpdatedAt)VALUES('LicenseKey',@v,@n) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value,UpdatedAt=excluded.UpdatedAt",("@v",key.Trim()),("@n",DemoDb.Now));return await Status();
    }
    static bool Verify(string key,string machine,out string? licensedTo,out DateTime? expires)
    {
        licensedTo=null;expires=null;try{var p=key.Split('.');if(p.Length!=2)return false;var payload=B64(p[0]);using var rsa=RSA.Create();rsa.ImportFromPem(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"license-public.pem")));if(!rsa.VerifyData(payload,B64(p[1]),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))return false;using var doc=JsonDocument.Parse(payload);var root=doc.RootElement;if(!string.Equals(root.GetProperty("installationId").GetString(),machine,StringComparison.OrdinalIgnoreCase))return false;licensedTo=root.GetProperty("licensedTo").GetString();if(root.TryGetProperty("expiresAt",out var e)&&e.ValueKind==JsonValueKind.String)expires=e.GetDateTime().ToUniversalTime();return true;}catch{return false;}
    }
    static byte[] B64(string value){var s=value.Replace('-','+').Replace('_','/');s+=new string('=',(4-s.Length%4)%4);return Convert.FromBase64String(s);}
}
