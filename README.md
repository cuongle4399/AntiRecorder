# AntiRecorder

Trình duyệt web ẩn 100% trước phần mềm quay màn hình (OBS, Zoom, Discord, Bandicam...) trên Windows.

## Chức năng chính

**Bảo mật & Tàng hình**
- Ẩn hoàn toàn cửa sổ khỏi mọi phần mềm quay/chụp màn hình
- Con trỏ chuột hiển thị bình thường trên màn hình, tàng hình trên máy quay
- Ẩn khỏi Alt+Tab, Win+Tab, Taskbar
- Nút chụp màn hình tích hợp, ảnh lưu thẳng vào Clipboard, không ghi đĩa

**Trình duyệt**
- Hỗ trợ đa tab, proxy riêng từng tab (HTTP/SOCKS5 + kiểm tra ping)
- Cài đặt Chrome Extension (kéo thả thư mục vào)
- Tài khoản Google riêng biệt theo profile
- Bookmark, lịch sử, tải file, zoom trang web

**Tiện ích**
- Luôn nổi trên cùng (Topmost) khi làm việc với app khác
- Tắt/bật âm thanh trang web
- Giao diện Dark / Light, điều chỉnh độ trong suốt
- Portable 100%, không cần cài đặt

## Phím tắt

| Phím | Chức năng |
|------|-----------|
| F4 / Ctrl+Shift+Space | Ẩn/hiện app |
| Ctrl+Shift+S | Chụp màn hình |
| Ctrl+T | Tab mới |
| Ctrl+W | Đóng tab |

## Build

Yêu cầu: Windows 10/11 x64, .NET 9 SDK

```bash
BuildScripts\Build_Windows.bat
```

Output: `BuildOutput\AntiRecorder.exe` (Portable, không cần cài thêm gì)

## License

MIT © [Cuong Le](https://github.com/cuongle4399)
