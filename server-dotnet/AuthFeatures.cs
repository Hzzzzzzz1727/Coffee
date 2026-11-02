using Google.Apis.Auth;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.IdentityModel.Tokens.Jwt;
using System.Data;
using Microsoft.IdentityModel.Tokens;

static class AuthFeatures
{
    private const string GenericResetMessage = "Nếu email đã đăng ký, mã xác thực sẽ được gửi đến hộp thư.";
    public static async Task InitializeAsync(string connectionString)
    {
        const string addGoogleSubject = """
            IF COL_LENGTH('dbo.Users', 'GoogleSubject') IS NULL
                ALTER TABLE dbo.Users ADD GoogleSubject nvarchar(255) NULL;
            """;
        const string createGoogleSubjectIndex = """
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Users_GoogleSubject' AND object_id=OBJECT_ID('dbo.Users'))
                CREATE UNIQUE INDEX UX_Users_GoogleSubject ON dbo.Users(GoogleSubject) WHERE GoogleSubject IS NOT NULL;
            """;
        const string createPasswordResetSchema = """
            IF OBJECT_ID('dbo.PasswordResetCodes', 'U') IS NULL
                CREATE TABLE dbo.PasswordResetCodes(
                    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    UserId uniqueidentifier NOT NULL REFERENCES dbo.Users(Id),
                    CodeHash char(64) NOT NULL,
                    ExpiresAt datetime2 NOT NULL,
                    Attempts tinyint NOT NULL DEFAULT 0,
                    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_PasswordResetCodes_User' AND object_id=OBJECT_ID('dbo.PasswordResetCodes'))
                CREATE INDEX IX_PasswordResetCodes_User ON dbo.PasswordResetCodes(UserId, CreatedAt DESC);
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
            await new SqlCommand(addGoogleSubject, connection).ExecuteNonQueryAsync();
            await new SqlCommand(createGoogleSubjectIndex, connection).ExecuteNonQueryAsync();
            await new SqlCommand(createPasswordResetSchema, connection).ExecuteNonQueryAsync();
    }

    public static void Map(WebApplication app, string connectionString, string jwtSecret, string googleClientId, bool allowRegistration)
    {
        app.MapGet("/api/auth/config", () => Results.Ok(new { googleClientId, allowRegistration }))
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        app.MapPost("/api/auth/register", async (RegistrationRequest request) =>
        {
            if (!allowRegistration)
                return Results.NotFound(new { error = "Đăng ký tài khoản hiện đang tắt." });

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 ||
                string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 255 ||
                !MailAddress.TryCreate(request.Email.Trim(), out _) ||
                string.IsNullOrEmpty(request.Password) || request.Password.Length < 8 ||
                Encoding.UTF8.GetByteCount(request.Password) > 72)
                return Results.BadRequest(new { error = "Vui lòng nhập họ tên, email hợp lệ và mật khẩu từ 8 đến 72 byte." });

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            var storeIdValue = await new SqlCommand("SELECT TOP 1 Id FROM dbo.Stores ORDER BY CreatedAt", connection).ExecuteScalarAsync();
            if (storeIdValue is not Guid storeId)
                return Results.Conflict(new { error = "Chưa có cửa hàng để đăng ký tài khoản." });

            await using var command = new SqlCommand(
                "INSERT dbo.Users(StoreId,FullName,Email,PasswordHash,Role) OUTPUT INSERTED.Id VALUES(@storeId,@name,@email,@passwordHash,'server')",
                connection);
            command.Parameters.AddWithValue("@storeId", storeId);
            command.Parameters.AddWithValue("@name", request.Name.Trim());
            command.Parameters.AddWithValue("@email", NormalizeEmail(request.Email));
            command.Parameters.AddWithValue("@passwordHash", BCrypt.Net.BCrypt.HashPassword(request.Password, 12));

            try
            {
                var userId = (Guid)(await command.ExecuteScalarAsync())!;
                return Results.Ok(CreateSession(userId, request.Name.Trim(), "server", 1, jwtSecret));
            }
            catch (SqlException exception) when (exception.Number is 2601 or 2627)
            {
                return Results.Conflict(new { error = "Email này đã được đăng ký." });
            }
        }).AllowAnonymous().RequireRateLimiting("registration");

        app.MapPost("/api/auth/login", async (LoginRequest login) =>
        {
            if (string.IsNullOrWhiteSpace(login.Email) || string.IsNullOrEmpty(login.Password))
                return Results.Json(new { error = "Email hoặc mật khẩu không đúng." }, statusCode: 401);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT TOP 1 Id,FullName,Role,PasswordHash,AuthVersion FROM dbo.Users WHERE Email=@email AND IsActive=1 AND DeletedAt IS NULL", connection);
            command.Parameters.AddWithValue("@email", NormalizeEmail(login.Email));
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || !BCrypt.Net.BCrypt.Verify(login.Password, reader.GetString(3)))
                return Results.Json(new { error = "Email hoặc mật khẩu không đúng." }, statusCode: 401);
            return Results.Ok(CreateSession(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(4), jwtSecret));
        }).AllowAnonymous().RequireRateLimiting("auth");

        app.MapPost("/api/auth/google", async (GoogleLoginRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Credential) || string.IsNullOrWhiteSpace(googleClientId))
                return Results.Json(new { error = "Đăng nhập Google chưa được cấu hình." }, statusCode: 503);

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(
                    request.Credential,
                    new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { googleClientId } });
            }
            catch (InvalidJwtException)
            {
                return Results.Json(new { error = "Thông tin xác thực Google không hợp lệ hoặc đã hết hạn." }, statusCode: 401);
            }
            catch (HttpRequestException exception)
            {
                app.Logger.LogError(exception, "Google token validation service is unavailable.");
                return Results.Json(new { error = "Không thể xác minh tài khoản Google lúc này." }, statusCode: 503);
            }

            if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
                return Results.Json(new { error = "Tài khoản Google chưa xác minh địa chỉ email." }, statusCode: 401);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new SqlCommand("SELECT TOP 1 Id,FullName,Role,AuthVersion,GoogleSubject FROM dbo.Users WITH (UPDLOCK, ROWLOCK) WHERE Email=@email AND IsActive=1 AND DeletedAt IS NULL", connection, (SqlTransaction)transaction);
            command.Parameters.AddWithValue("@email", NormalizeEmail(payload.Email));
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await reader.CloseAsync();
                await transaction.RollbackAsync();
                return Results.Json(new { error = "Email Google này chưa được tạo tài khoản trong Coffee OS. Hãy nhờ chủ quán cấp tài khoản." }, statusCode: 403);
            }

            var userId = reader.GetGuid(0);
            var name = reader.GetString(1);
            var role = reader.GetString(2);
            var authVersion = reader.GetInt32(3);
            var linkedSubject = reader.IsDBNull(4) ? null : reader.GetString(4);
            await reader.CloseAsync();
            if (linkedSubject is not null && !string.Equals(linkedSubject, payload.Subject, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync();
                return Results.Json(new { error = "Tài khoản Coffee OS đã được liên kết với một tài khoản Google khác." }, statusCode: 403);
            }

            if (linkedSubject is null)
            {
                await using var linkCommand = new SqlCommand("UPDATE dbo.Users SET GoogleSubject=@subject WHERE Id=@id AND GoogleSubject IS NULL", connection, (SqlTransaction)transaction);
                linkCommand.Parameters.AddWithValue("@subject", payload.Subject);
                linkCommand.Parameters.AddWithValue("@id", userId);
                try
                {
                    if (await linkCommand.ExecuteNonQueryAsync() != 1)
                    {
                        await transaction.RollbackAsync();
                        return Results.Json(new { error = "Không thể liên kết tài khoản Google. Hãy thử lại." }, statusCode: 409);
                    }
                }
                catch (SqlException exception) when (exception.Number is 2601 or 2627)
                {
                    await transaction.RollbackAsync();
                    return Results.Json(new { error = "Tài khoản Google này đã được liên kết với người dùng khác." }, statusCode: 409);
                }
            }

            await transaction.CommitAsync();
            return Results.Ok(CreateSession(userId, name, role, authVersion, jwtSecret));
        }).AllowAnonymous().RequireRateLimiting("auth");

        app.MapPost("/api/auth/password-reset/request", async (PasswordResetRequest request, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 255 ||
                !Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant))
                return Results.Ok(new { message = GenericResetMessage });

            var mailSettings = MailSettings.FromEnvironment();
            if (mailSettings is null)
            {
                app.Logger.LogError("Password reset email is not configured. Set COFFEE_SMTP_HOST, COFFEE_SMTP_USER, COFFEE_SMTP_PASSWORD, and COFFEE_SMTP_FROM.");
                return Results.Json(new { error = "Gửi email chưa được cấu hình trên máy chủ." }, statusCode: 503);
            }

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            await using var userCommand = new SqlCommand("SELECT TOP 1 Id,FullName FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Email=@email AND IsActive=1 AND DeletedAt IS NULL", connection, (SqlTransaction)transaction);
            userCommand.Parameters.AddWithValue("@email", NormalizeEmail(request.Email));
            Guid? userId = null;
            string? name = null;
            await using (var reader = await userCommand.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    userId = reader.GetGuid(0);
                    name = reader.GetString(1);
                }
            }

            if (userId is null)
            {
                await transaction.RollbackAsync();
                app.Logger.LogInformation("Password reset request was not sent because no active account matched.");
                return Results.Ok(new { message = GenericResetMessage });
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            await using (var invalidate = new SqlCommand("DELETE FROM dbo.PasswordResetCodes WHERE UserId=@userId", connection, (SqlTransaction)transaction))
            {
                invalidate.Parameters.AddWithValue("@userId", userId.Value);
                await invalidate.ExecuteNonQueryAsync();
            }
            await using (var insert = new SqlCommand("INSERT dbo.PasswordResetCodes(UserId,CodeHash,ExpiresAt) VALUES(@userId,@hash,DATEADD(MINUTE,10,SYSUTCDATETIME()))", connection, (SqlTransaction)transaction))
            {
                insert.Parameters.AddWithValue("@userId", userId.Value);
                insert.Parameters.AddWithValue("@hash", HashCode(userId.Value, code, jwtSecret));
                await insert.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();

            try
            {
                await mailSettings.SendResetCodeAsync(request.Email.Trim(), name!, code, context.RequestAborted);
                app.Logger.LogInformation("Password reset message was accepted by the configured SMTP server.");
            }
            catch (Exception exception) when (exception is SmtpException or FormatException)
            {
                app.Logger.LogError(exception, "Could not send a password reset code.");
                await using var invalidate = new SqlCommand("DELETE FROM dbo.PasswordResetCodes WHERE UserId=@userId", connection);
                invalidate.Parameters.AddWithValue("@userId", userId.Value);
                await invalidate.ExecuteNonQueryAsync();
                return Results.Json(new { error = "Không thể gửi email xác thực. Hãy thử lại sau." }, statusCode: 503);
            }

            return Results.Ok(new { message = GenericResetMessage });
        }).AllowAnonymous().RequireRateLimiting("auth").RequireRateLimiting("password-reset");

        app.MapPost("/api/auth/password-reset/confirm", async (PasswordResetConfirmRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 255 ||
                string.IsNullOrWhiteSpace(request.Code) || !Regex.IsMatch(request.Code, @"^\d{6}$", RegexOptions.CultureInvariant) ||
                string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 8 ||
                Encoding.UTF8.GetByteCount(request.NewPassword) > 72)
                return Results.BadRequest(new { error = "Email, mã OTP 6 số và mật khẩu mới (8-72 byte) là bắt buộc." });

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var lookup = new SqlCommand("SELECT TOP 1 c.Id,c.UserId,c.CodeHash,c.Attempts FROM dbo.PasswordResetCodes c WITH (UPDLOCK, ROWLOCK) JOIN dbo.Users u ON u.Id=c.UserId WHERE u.Email=@email AND u.IsActive=1 AND u.DeletedAt IS NULL AND c.ExpiresAt>SYSUTCDATETIME() ORDER BY c.CreatedAt DESC", connection, (SqlTransaction)transaction);
            lookup.Parameters.AddWithValue("@email", NormalizeEmail(request.Email));
            await using var reader = await lookup.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await reader.CloseAsync();
                await transaction.RollbackAsync();
                return Results.BadRequest(new { error = "Mã xác thực không hợp lệ hoặc đã hết hạn." });
            }
            var codeId = reader.GetInt64(0);
            var userId = reader.GetGuid(1);
            var storedHash = reader.GetString(2);
            var attempts = reader.GetByte(3);
            await reader.CloseAsync();

            if (attempts >= 5 || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(storedHash),
                    Convert.FromHexString(HashCode(userId, request.Code, jwtSecret))))
            {
                await using var update = new SqlCommand("UPDATE dbo.PasswordResetCodes SET Attempts=Attempts+1 WHERE Id=@id AND Attempts<5", connection, (SqlTransaction)transaction);
                update.Parameters.AddWithValue("@id", codeId);
                await update.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
                return Results.BadRequest(new { error = "Mã xác thực không hợp lệ hoặc đã hết hạn." });
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 12);
            await using var updatePassword = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@hash,AuthVersion=AuthVersion+1 WHERE Id=@id; DELETE FROM dbo.PasswordResetCodes WHERE UserId=@id;", connection, (SqlTransaction)transaction);
            updatePassword.Parameters.AddWithValue("@hash", passwordHash);
            updatePassword.Parameters.AddWithValue("@id", userId);
            await updatePassword.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { message = "Đặt lại mật khẩu thành công. Hãy đăng nhập bằng mật khẩu mới." });
        }).AllowAnonymous().RequireRateLimiting("auth");
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string HashCode(Guid userId, string code, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{userId:N}:{code}")));
    }

    private static object CreateSession(Guid id, string name, string role, int authVersion, string jwtSecret)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role),
            new Claim("auth_version", authVersion.ToString())
        };
        var jwt = new JwtSecurityToken("CoffeeOS", "CoffeeOS.Client", claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                SecurityAlgorithms.HmacSha256));
        return new { token = new JwtSecurityTokenHandler().WriteToken(jwt), user = new { id, name, role } };
    }

    private sealed record MailSettings(string Host, int Port, string User, string Password, string From)
    {
        public static MailSettings? FromEnvironment()
        {
            var host = Environment.GetEnvironmentVariable("COFFEE_SMTP_HOST")?.Trim();
            var user = Environment.GetEnvironmentVariable("COFFEE_SMTP_USER");
            var password = Environment.GetEnvironmentVariable("COFFEE_SMTP_PASSWORD");
            if (string.Equals(host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase) && password is not null)
                password = Regex.Replace(password, @"\s+", string.Empty);
            var from = Environment.GetEnvironmentVariable("COFFEE_SMTP_FROM") ?? user;
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) ||
                string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(from))
                return null;
            if (!int.TryParse(Environment.GetEnvironmentVariable("COFFEE_SMTP_PORT") ?? "587", out var port) ||
                port is < 1 or > 65535)
                return null;
            try
            {
                _ = new MailAddress(from);
            }
            catch (FormatException)
            {
                return null;
            }
            return new MailSettings(host, port, user, password, from);
        }

        public async Task SendResetCodeAsync(string email, string name, string code, CancellationToken cancellationToken)
        {
            using var message = new MailMessage(From, email)
            {
                Subject = "Mã xác thực đặt lại mật khẩu Coffee OS",
                Body = $"Xin chào {name},\n\nMã xác thực đặt lại mật khẩu của bạn là: {code}\nMã có hiệu lực trong 10 phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.",
                IsBodyHtml = false
            };
            using var client = new SmtpClient(Host, Port)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(User, Password),
                Timeout = 15000
            };
            await client.SendMailAsync(message, cancellationToken);
        }
    }

    private sealed record LoginRequest(string Email, string Password);
    private sealed record RegistrationRequest(string Name, string Email, string Password);
    private sealed record GoogleLoginRequest(string Credential);
    private sealed record PasswordResetRequest(string Email);
    private sealed record PasswordResetConfirmRequest(string Email, string Code, string NewPassword);
}
