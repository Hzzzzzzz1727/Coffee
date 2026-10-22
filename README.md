# Roast & Root Coffee OS

## Nền tảng dữ liệu

Backend đang sử dụng chính là **ASP.NET Core .NET 10** trong `server-dotnet` và **Microsoft SQL Server** bằng Windows Authentication. Dữ liệu gồm người dùng/phân quyền, bàn và vị trí bàn, menu/topping, đơn hàng, khách hàng thân thiết, khuyến mãi, thanh toán, ca làm, cấu hình máy in, Web Push và báo cáo vận hành.

## Khởi tạo SQL Server

1. Tạo database trống `CoffeeOS` trong SQL Server Management Studio.
2. Tạo `.env` từ `.env.example`, sau đó điền host SQL Server và tên database. Mặc định dự án dùng `DB_AUTH=windows`, không cần SQL username/password. Với Azure SQL, dùng SQL Authentication và đặt `DB_ENCRYPT=true`.
3. Chạy `npm run db:create`, sau đó `npm run db:migrate` để tạo database, bảng và dữ liệu cửa hàng ban đầu.
4. Tạo tài khoản chủ quán: `npm run db:seed-admin -- owner@tenquan.vn MatKhauManh "Tên chủ quán"`.
5. Tạo bàn và menu khởi đầu: `npm run db:seed-operational`.
6. Khởi chạy API bằng `npm run server` và ứng dụng Vue bằng `npm run dev`.

API chạy mặc định ở `http://localhost:3031`; endpoint kiểm tra là `GET /api/health`.

## Khởi chạy bản hiện tại

Chạy `npm run server:dotnet`, sau đó mở terminal thứ hai chạy `npm run dev`. API ở `http://localhost:3032/api`; endpoint kiểm tra là `http://localhost:3032/api/health`. Migration được chạy an toàn lúc backend khởi động và không xóa dữ liệu hiện có.

## Máy in bill, két tiền và thông báo

- Vào **Thu ngân POS → Máy in bill & két tiền**, nhập đúng tên máy in trong Windows (ví dụ `POS-80`), lưu và bấm **In thử**. Máy in cần hỗ trợ lệnh RAW ESC/POS; két tiền nối qua cổng RJ11/RJ12 của máy in.
- Nút **Bật báo món** của tài khoản Phục vụ đăng ký Web Push. `localhost` dùng được khi phát triển; khi cài trên máy khác phải chạy website bằng HTTPS để trình duyệt cho phép Push Notification.
- Khóa VAPID được tự tạo một lần và chỉ lưu trong SQL Server; private key không gửi xuống trình duyệt.

## Chạy nhiều máy trong cùng quán

Máy đang cài SQL Server là **máy chủ trung tâm** và cần được bật trong suốt giờ bán hàng. Bản vận hành phục vụ cả Vue và API trên cùng cổng. Chuẩn bị lại ứng dụng sau mỗi lần cập nhật mã:

```powershell
npm run prepare:server
```

Sau đó chỉ cần chạy một terminal:

```powershell
npm run server:dotnet
```

Các máy Phục vụ, Pha chế và Thu ngân phải dùng cùng Wi-Fi/LAN rồi mở địa chỉ IP của máy chủ, ví dụ hiện tại:

```text
http://192.168.55.105:3032
```

Chạy PowerShell bằng **Run as administrator** một lần để cho phép các máy trong mạng riêng truy cập:

```powershell
New-NetFirewallRule -DisplayName "CoffeeOS Web LAN" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5173 -Profile Private
New-NetFirewallRule -DisplayName "CoffeeOS API LAN" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 3032 -Profile Private
```

Nếu IP máy chủ thay đổi, xem lại bằng `ipconfig`. Nên đặt IP tĩnh/DHCP reservation cho máy chủ để nhân viên luôn sử dụng cùng một địa chỉ. Mỗi máy đăng nhập tài khoản đúng role; toàn bộ order, trạng thái bàn, KDS và yêu cầu thanh toán dùng chung database và tự làm mới khoảng 2 giây một lần.

## Trước khi bán/triển khai

- Không đưa `.env`, mật khẩu database hay `JWT_SECRET` lên Git.
- Dùng một SQL login riêng chỉ có quyền cần thiết với database ứng dụng.
- Bật HTTPS, backup SQL Server tự động, rate limiting/audit log và cơ chế refresh token trước khi mở cho nhiều cửa hàng.
- Kiểm tra thực tế khổ giấy/lệnh cắt/mở két trên đúng model máy in trước khi bàn giao cửa hàng.
