# Block-Update-Windows-Defender

WPF app local cho Windows VPS để quản lý:
- Windows Update (bật/tắt + hardening)
- Microsoft Defender (bật/tắt khi OS hỗ trợ)
- Đồng bộ giờ và múi giờ
- Cài đặt trình duyệt phổ biến
- Kiểm tra/cập nhật chính phần mềm

## Giao diện

![Windows VPS Control Center](./BlockUpdateWindowsDefender_lT1koqpNyj.png)

## Tính năng chính

- Tự nhận diện phiên bản Windows hiện tại (Server 2012 R2 đến bản mới hơn).
- Tab `Điều khiển`: bật/tắt Windows Update và Defender, hiển thị trạng thái màu.
- Tab `Kiểm tra`: đọc trạng thái policy/service (`wuauserv`, `UsoSvc`, `WaaSMedicSvc`) và khả năng manual update.
- Tab `Múi giờ`: đồng bộ time + timezone, tự động khi khởi động (có tùy chọn bật/tắt).
- Tab `Cài trình duyệt`: cài Chrome/Firefox/Edge/Brave/Opera/CentBrowser (silent install tùy chọn).
- Tab `Cập nhật`: kiểm tra bản mới và tự cập nhật app (mặc định bật khi startup).
- Tab `Nhật ký`: theo dõi đầy đủ thao tác, mở nhanh thư mục log.
- Hỗ trợ song ngữ `Tiếng Việt / English`, có auto switch theo kết quả time sync.

## Yêu cầu hệ thống

- Windows Server 2012 R2 hoặc mới hơn.
- `.NET Framework 4.5.1` trở lên.
- Nên chạy với quyền Administrator để thao tác policy/service ổn định.
- Cần internet để:
  - detect IP/timezone
  - tải trình duyệt
  - kiểm tra cập nhật phần mềm

## Build và chạy local

```powershell
dotnet restore
dotnet build
```

Chạy bản Debug:

```powershell
.\bin\Debug\net451\BlockUpdateWindowsDefender.exe
```

Build Release:

```powershell
dotnet build -c Release
```

Output Release:

- `bin\Release\net451\`

## Đóng gói phát hành

File zip phát hành đặt trong thư mục `release\` theo format:

- `Block-Update-Windows-Defender-v<version>.zip`

Ví dụ hiện có:

- `release\Block-Update-Windows-Defender-v1.1.2.zip`

## Cơ chế tự cập nhật

App check update theo thứ tự fallback:

1. `release/latest.json` (raw GitHub)
2. GitHub Releases API
3. GitHub Contents API của thư mục `release`
4. Parse trang `release` trên GitHub

Nếu có bản mới, app tải gói zip, bung file, ghi đè bản cũ và tự khởi động lại.

## Log

Log runtime được ghi tại:

- `C:\ProgramData\BlockUpdateWindowsDefender\activity.log`
- `C:\ProgramData\BlockUpdateWindowsDefender\startup-error.log` (nếu lỗi khởi động)

## Lưu ý

- Trên một số bản Windows, Defender/Tamper Protection có thể chặn thao tác thay đổi.
- Hardening Windows Update là best-effort, tùy mức bảo vệ service của từng OS.
- Tính năng timezone phụ thuộc API bên thứ ba và mapping IANA -> Windows time zone.
