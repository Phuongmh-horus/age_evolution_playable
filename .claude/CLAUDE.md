# Project rules for Claude Code

Tài liệu này gom các quy tắc cần tuân thủ khi làm việc trên project playable ad này, dựa trên Luna / Unity Playworks docs.

## 1) Môi trường được hỗ trợ
- Unity **2021.3 LTS**, **2022.3 LTS**, hoặc **6000.0 LTS**.
- Plugin chạy trên **Windows** và **macOS**.
- Cần **.NET 4.7+** và **MSBuild** để build C# gameplay thành JavaScript.
- Nếu gặp lỗi build liên quan assembly .NET Framework, hãy kiểm tra targeting pack / developer pack trước.

## 2) Quy trình build / export
- Cài Playworks Plugin **ngoài** thư mục project, rồi add vào Unity bằng **Package Manager > Add Package from Disk**.
- Chọn scene export và startup scene trước khi build.
- Chạy **Build Develop** trước.
- Sau đó mở **Open In Browser** để kiểm tra playable local.
- Chỉ khi dev build ổn mới tiếp tục **Upload to Creative Library** hoặc **Download / Publish**.
- Nếu cache đã bật và không có thay đổi mới, **Open in Browser** có thể thay cho Build Develop.

## 3) Quy tắc thiết kế playable
- Tập trung vào đoạn **onboarding/tutorial** để giữ người chơi.
- Phải là trải nghiệm **có thể chơi được**, không chỉ 1 tap rồi đi thẳng tới install.
- Ưu tiên input đơn giản, thường là **một control chính** như tap-to-jump hoặc tap-to-shoot.
- Thời lượng mục tiêu khoảng **30 giây**; vùng chấp nhận thường là **10–40 giây**.
- Chỉ dùng **1–2 lớp hướng dẫn**; không biến ad thành tutorial dài.
- Trải nghiệm phải **nhất quán với game thật**; không được gây hiểu sai.
- Có **CTA rõ ràng** và cảm giác mời gọi người chơi vào store.
- Trải nghiệm nên **winnable nhưng không quá dễ**.
- **Localization** theo ngôn ngữ người dùng.
- Thiết kế nên được **A/B test** và tinh chỉnh theo kết quả.

## 4) API / lifecycle bắt buộc
- Dùng **single C# API** của Unity Playworks để map sang network lúc export.
- Khi user bấm CTA / end card để rời ad, gọi `Luna.Unity.Playable.InstallFullGame();`.
- Subscribe `Luna.Unity.LifeCycle.OnPause` và `Luna.Unity.LifeCycle.OnResume`.
- Khi playable kết thúc, gọi `Luna.Unity.LifeCycle.GameEnded();`.
- `GameEnded()` là bắt buộc cho một số network như **Mintegral** và **Vungle**.
- Detect orientation bằng `Screen.width` và `Screen.height`; không dùng `Input.deviceOrientation` trong context này.
- Với coroutine lồng nhau trong playable build, ưu tiên `StartCoroutine(...)` cho các nhánh cần resume rõ ràng sau khi coroutine con kết thúc; tránh giả định `yield return <IEnumerator>` luôn resume ổn định trong JS export.

## 5) Project Diagnostics / PHC
- Kiểm tra **Project Diagnostics** trong Build & Upload tab để xem lỗi, warning, suggestion.
- Ưu tiên sửa theo thứ tự:
  1. **Red**: lỗi build / compiler / broken playable / reserved keyword.
  2. **Amber**: vấn đề runtime hoặc khuyến nghị chưa đủ tốt.
  3. **Green**: không có vấn đề ảnh hưởng playable.
- Luôn xử lý các yêu cầu API bắt buộc của ad network.
- Kiểm tra **FPS** và các vấn đề hiệu năng vì có thể ảnh hưởng rejection.
- Ưu tiên dùng **LunaPlaygroundFields** và **Custom Events API** khi phù hợp.

## 6) Thói quen kiểm tra
- Khi thay đổi gameplay/UI/playable flow, phải kiểm tra trong Unity và browser.
- Test theo network-specific spec nếu build cho ad network cụ thể.
- Nếu thay đổi asset/config, hãy kiểm tra diagnostics và build log trước khi suy đoán.
- `Open in Browser` là bước xác nhận bắt buộc sau dev build.

## 7) Ghi chú cho repository này
- Repo đã có `CLAUDE.md` ở root để mô tả kiến trúc và luồng game của project.
- `.claude/settings.local.json` hiện đã cho phép `WebFetch(domain:docs.lunalabs.io)`.

## 8) Lưu ý khi viết mã cho project này
- Viết mã **đơn giản**, tránh tạo thêm tầng trừu tượng nếu không thật sự cần.
- Ưu tiên chia hệ thống game theo các khối nhỏ, rõ trách nhiệm hơn là gom logic vào một lớp lớn.

### 8.1) Ba hệ thống chính
#### Player Control
- Chuyên về tương tác và xử lý hành vi liên quan tới player.
- Gồm 2 phần chính:
  - **Player Control**: điều khiển user.
  - **Player Interaction**: tương tác và chỉ số của user.

#### Map & Environment
- Chuyên về tài nguyên, môi trường và các vật cản.
- Gồm 2 phần chính:
  - **Maps**: load các asset tĩnh, không tương tác với user, chủ yếu về mặt hình ảnh.
  - **Environment**: xử lý vật cản, độ khó, mục tiêu và các yếu tố user thường xuyên tương tác.

#### UI Virusal
- Chuyên về xử lý UI.
- Với game đơn giản như project này, chỉ cần một canvas hiển thị là đủ.
- Hệ thống này được phép tương tác với 2 hệ thống còn lại.
- Không bị giới hạn khi giao tiếp với các hệ thống đó, miễn là không vượt quyền xử lý lên các cấp con của chúng.

### 8.2) Mối liên hệ giữa các hệ thống
- Các hệ thống có thể tương tác qua lại, nhưng **không tương tác trực tiếp qua hệ thống con**.
- Nếu A là con của B, và C muốn liên kết với A thì C phải liên kết qua B.
- Hệ thống con không nên biết về nhau.
- Logic điều phối phải nằm ở hệ thống cha.
- Hệ thống con chỉ nên:
  - chứa dữ liệu,
  - xử lý đầu cuối,
  - hoặc thu thập thông tin từ môi trường.
