using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.SqlClient;

public static class ReceiptPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    sealed class DocInfo { [MarshalAs(UnmanagedType.LPWStr)] public string DocName="CoffeeOS Receipt"; [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile; [MarshalAs(UnmanagedType.LPWStr)] public string DataType="RAW"; }
    [DllImport("winspool.drv",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool OpenPrinter(string name,out IntPtr printer,IntPtr defaults);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool ClosePrinter(IntPtr printer);
    [DllImport("winspool.drv",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool StartDocPrinter(IntPtr printer,int level,[In] DocInfo info);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool EndDocPrinter(IntPtr printer);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool StartPagePrinter(IntPtr printer);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool EndPagePrinter(IntPtr printer);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool WritePrinter(IntPtr printer,byte[] bytes,int count,out int written);

    public static void SendRaw(string printerName,byte[] bytes)
    {
        if(!OperatingSystem.IsWindows())throw new InvalidOperationException("Máy in ESC/POS hiện chỉ hỗ trợ máy chủ Windows.");
        if(string.IsNullOrWhiteSpace(printerName))throw new InvalidOperationException("Chưa cấu hình tên máy in.");
        if(!OpenPrinter(printerName,out var handle,IntPtr.Zero))throw new InvalidOperationException($"Không mở được máy in '{printerName}'.");
        try { if(!StartDocPrinter(handle,1,new DocInfo()))throw new InvalidOperationException("Không tạo được lệnh in.");try{StartPagePrinter(handle);if(!WritePrinter(handle,bytes,bytes.Length,out var written)||written!=bytes.Length)throw new InvalidOperationException("Máy in không nhận đủ dữ liệu.");EndPagePrinter(handle);}finally{EndDocPrinter(handle);} } finally { ClosePrinter(handle); }
    }
    public static byte[] Receipt(string text,bool drawer)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);var enc=Encoding.GetEncoding(1258);var bytes=new List<byte>{0x1B,0x40};if(drawer)bytes.AddRange(new byte[]{0x1B,0x70,0x00,0x19,0xFA});bytes.AddRange(enc.GetBytes(text.Replace("đ","d")+"\n\n\n"));bytes.AddRange(new byte[]{0x1D,0x56,0x41,0x00});return bytes.ToArray();
    }
    const int PaperColumns=42;
    static string Center(string value)=>value.Length>=PaperColumns?value[..PaperColumns]:new string(' ',(PaperColumns-value.Length)/2)+value;
    static string Clip(string value,int width)=>value.Length<=width?value:value[..Math.Max(1,width-1)]+"~";
    static string AmountLine(string label,int amount)=>label+new string(' ',Math.Max(1,PaperColumns-label.Length-$"{amount:N0}d".Length))+$"{amount:N0}d";
    public static string TextHeader(long number,string table,DateTime opened)
        => $"{new string('=',PaperColumns)}\n{Center("ROAST & ROOT")}\n{Center("COFFEE OPERATING SYSTEM")}\n{new string('=',PaperColumns)}\nHOA DON TAM TINH\nMa HD: HD{number:000000}\nBan: {table}\nNgay: {opened:dd/MM/yyyy HH:mm}\n{new string('-',PaperColumns)}\n{"Ten mon",-18}{"SL",3}{"Don gia",10}{"T.Tien",11}\n{new string('-',PaperColumns)}\n";
    public static string TextItem(short quantity,string name,int unitPrice,string note)
        => $"{Clip(name,18),-18}{quantity,3}{unitPrice,10:N0}{unitPrice*quantity,11:N0}\n"+(string.IsNullOrWhiteSpace(note)?"":$"  > {Clip(note,PaperColumns-4)}\n");
    public static string TextFooter(int subtotal,int discount,int total)
        => $"{new string('-',PaperColumns)}\n{AmountLine("Tam tinh:",subtotal)}\n"+(discount>0?$"{AmountLine("Giam gia / diem:",-discount)}\n":"")+$"{new string('-',PaperColumns)}\n{AmountLine("TONG CONG:",total)}\n{new string('-',PaperColumns)}\n\n{Center("Cam on & hen gap lai Quy khach!")}\n{new string('=',PaperColumns)}";
    public static byte[] DrawerPulse()=>new byte[]{0x1B,0x40,0x1B,0x70,0x00,0x19,0xFA};
    public static async Task<string?> TryAutoPrint(string db,Guid orderId)
    {
        try{await using var c=new SqlConnection(db);await c.OpenAsync();await using var setting=new SqlCommand("SELECT TOP 1 PrinterName FROM dbo.PrinterSettings WHERE AutoPrint=1",c);var printer=await setting.ExecuteScalarAsync() as string;if(string.IsNullOrWhiteSpace(printer))return null;await using var q=new SqlCommand("SELECT o.OrderNumber,ISNULL(t.Code,N'Mang di'),o.OpenedAt,o.Subtotal,o.Discount+o.LoyaltyDiscount,o.Total FROM dbo.Orders o LEFT JOIN dbo.DiningTables t ON t.Id=o.TableId WHERE o.Id=@id;SELECT i.Quantity,i.ProductName,i.UnitPrice+ISNULL((SELECT SUM(t.Price*t.Quantity) FROM dbo.OrderItemToppings t WHERE t.OrderItemId=i.Id),0),ISNULL(i.Note,N'') FROM dbo.OrderItems i WHERE OrderId=@id AND Status<>'cancelled' ORDER BY CreatedAt",c);q.Parameters.AddWithValue("@id",orderId);await using var r=await q.ExecuteReaderAsync();if(!await r.ReadAsync())return "Không tìm thấy đơn";var text=TextHeader(r.GetInt64(0),r.GetString(1),r.GetDateTime(2));var subtotal=r.GetInt32(3);var discount=r.GetInt32(4);var total=r.GetInt32(5);await r.NextResultAsync();while(await r.ReadAsync())text+=TextItem(r.GetInt16(0),r.GetString(1),r.GetInt32(2),r.GetString(3));text+=TextFooter(subtotal,discount,total);SendRaw(printer,Receipt(text,false));return null;}catch(Exception e){return e.Message;}
    }
    public static async Task TryOpenDrawer(string db)
    {
        try{await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("SELECT TOP 1 PrinterName FROM dbo.PrinterSettings WHERE OpenCashDrawer=1",c);var printer=await q.ExecuteScalarAsync() as string;if(!string.IsNullOrWhiteSpace(printer))SendRaw(printer,DrawerPulse());}catch{}
    }
}

public static class PrinterEndpoints
{
    static bool ManagerOrCashier(System.Security.Claims.ClaimsPrincipal u)=>new[]{"owner","manager","cashier"}.Contains(u.FindFirstValue(System.Security.Claims.ClaimTypes.Role)??"");
    public static void MapPrinterEndpoints(this WebApplication app,string db)
    {
        app.MapGet("/api/printer/settings",async (System.Security.Claims.ClaimsPrincipal user)=>{if(!ManagerOrCashier(user))return Results.Forbid();await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("SELECT TOP 1 PrinterName,AutoPrint,OpenCashDrawer FROM dbo.PrinterSettings",c);await using var r=await q.ExecuteReaderAsync();return await r.ReadAsync()?Results.Ok(new{printerName=r.GetString(0),autoPrint=r.GetBoolean(1),openCashDrawer=r.GetBoolean(2)}):Results.Ok(new{printerName="",autoPrint=false,openCashDrawer=false});});
        app.MapPut("/api/printer/settings",async (PrinterSettingsInput x,System.Security.Claims.ClaimsPrincipal user)=>{if(!ManagerOrCashier(user))return Results.Forbid();await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("DECLARE @s uniqueidentifier=(SELECT TOP 1 Id FROM dbo.Stores);MERGE dbo.PrinterSettings t USING(SELECT @s StoreId)s ON t.StoreId=s.StoreId WHEN MATCHED THEN UPDATE SET PrinterName=@name,AutoPrint=@auto,OpenCashDrawer=@drawer,UpdatedAt=SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT(StoreId,PrinterName,AutoPrint,OpenCashDrawer)VALUES(@s,@name,@auto,@drawer);",c);q.Parameters.AddWithValue("@name",x.PrinterName?.Trim()??"");q.Parameters.AddWithValue("@auto",x.AutoPrint);q.Parameters.AddWithValue("@drawer",x.OpenCashDrawer);await q.ExecuteNonQueryAsync();return Results.Ok(x);});
        app.MapPost("/api/printer/test",async (System.Security.Claims.ClaimsPrincipal user)=>{if(!ManagerOrCashier(user))return Results.Forbid();var s=await Settings(db);try{ReceiptPrinter.SendRaw(s.Name,ReceiptPrinter.Receipt("ROAST & ROOT\nIN THU MAY IN THANH CONG\n"+DateTime.Now.ToString("dd/MM/yyyy HH:mm"),s.Drawer));return Results.Ok(new{ok=true});}catch(Exception e){return Results.BadRequest(new{error=e.Message});}});
        app.MapPost("/api/printer/open-drawer",async (System.Security.Claims.ClaimsPrincipal user)=>{if(!ManagerOrCashier(user))return Results.Forbid();var s=await Settings(db);try{ReceiptPrinter.SendRaw(s.Name,ReceiptPrinter.DrawerPulse());return Results.Ok(new{ok=true});}catch(Exception e){return Results.BadRequest(new{error=e.Message});}});
        app.MapPost("/api/orders/{id:guid}/print",async (Guid id,System.Security.Claims.ClaimsPrincipal user)=>{if(!ManagerOrCashier(user))return Results.Forbid();await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("SELECT o.OrderNumber,ISNULL(t.Code,N'Mang di'),o.OpenedAt,o.Subtotal,o.Discount+o.LoyaltyDiscount,o.Total FROM dbo.Orders o LEFT JOIN dbo.DiningTables t ON t.Id=o.TableId WHERE o.Id=@id;SELECT i.Quantity,i.ProductName,i.UnitPrice+ISNULL((SELECT SUM(t.Price*t.Quantity) FROM dbo.OrderItemToppings t WHERE t.OrderItemId=i.Id),0),ISNULL(i.Note,N'') FROM dbo.OrderItems i WHERE OrderId=@id AND Status<>'cancelled' ORDER BY CreatedAt",c);q.Parameters.AddWithValue("@id",id);await using var r=await q.ExecuteReaderAsync();if(!await r.ReadAsync())return Results.NotFound(new{error="Không tìm thấy đơn."});var no=r.GetInt64(0);var table=r.GetString(1);var opened=r.GetDateTime(2);var subtotal=r.GetInt32(3);var discount=r.GetInt32(4);var total=r.GetInt32(5);var text=ReceiptPrinter.TextHeader(no,table,opened);await r.NextResultAsync();while(await r.ReadAsync())text+=ReceiptPrinter.TextItem(r.GetInt16(0),r.GetString(1),r.GetInt32(2),r.GetString(3));text+=ReceiptPrinter.TextFooter(subtotal,discount,total);var s=await Settings(db);try{ReceiptPrinter.SendRaw(s.Name,ReceiptPrinter.Receipt(text,false));return Results.Ok(new{ok=true});}catch(Exception e){return Results.BadRequest(new{error=e.Message});}});
    }
    static async Task<(string Name,bool Drawer)> Settings(string db){await using var c=new SqlConnection(db);await c.OpenAsync();await using var q=new SqlCommand("SELECT TOP 1 PrinterName,OpenCashDrawer FROM dbo.PrinterSettings",c);await using var r=await q.ExecuteReaderAsync();return await r.ReadAsync()?(r.GetString(0),r.GetBoolean(1)):("",false);}
}
public record PrinterSettingsInput(string? PrinterName,bool AutoPrint,bool OpenCashDrawer);
