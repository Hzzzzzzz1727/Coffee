COFFEE OS - HUONG DAN CAI DAT
============================
1. May tinh can Windows 10/11 64-bit va SQL Server default instance (MSSQLSERVER).
2. Giai nen toan bo file ZIP.
3. Mo PowerShell bang Run as administrator.
4. Chay: powershell -ExecutionPolicy Bypass -File .\Install-CoffeeOS.ps1
5. Nhap email, ten chu quan va mat khau quan tri.
6. Mo http://localhost:3032 tren may chu.
7. May khac cung Wi-Fi mo http://IP-MAY-CHU:3032.

Gui email dat lai mat khau chi can cau hinh SMTP mot lan tren may chu trung tam:
- Cai CoffeeOS tren may chu, giai nen goi phat hanh va mo PowerShell bang Run as administrator.
- Chay: powershell -ExecutionPolicy Bypass -File .\Configure-Smtp.ps1
- Nhap SMTP host, port, tai khoan, email gui va mat khau ung dung. Gmail can App Password, khong dung mat khau Gmail thuong.
- Cong cu luu cau hinh vao C:\ProgramData\CoffeeOS\.env, gioi han quyen truy cap file va khoi dong lai API.
- Cac may khach truy cap http://IP-MAY-CHU:3032; khong can cai SMTP hay sao chep mat khau sang may khach.
- Neu sua thu cong, App Password Gmail co the dan theo dang chia nhom co khoang trang; API tu bo khoang trang truoc khi gui.
- Khong dua file .env chua mat khau vao Git hoac chia se trong goi cai dat.

Tu dang ky tai khoan (mac dinh tat):
- De bat, them COFFEE_ALLOW_REGISTRATION=true vao .env hoac Windows System Environment Variables va khoi dong lai CoffeeOS.
- Tai khoan moi se co quyen Phuc vu. Dat COFFEE_ALLOW_REGISTRATION=false hoac xoa bien de tat dang ky.

Ung dung duoc dung thu 15 ngay. Khi het han, gui Ma may hien tren man hinh
cho nha cung cap de nhan key kich hoat.

Khong xoa thu muc C:\ProgramData\CoffeeOS. Database CoffeeOS nam trong SQL Server.
