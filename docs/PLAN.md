# Workspace Manager – Plan nâng cấp thành Git client + công cụ PM

> Mục tiêu: viết lại Workspace Manager thành ứng dụng desktop **Avalonia + .NET 10**, có **toàn bộ tính năng của Fork / SourceTree**,
> thêm các tính năng **trả phí** của GitKraken Pro / Tower / SmartGit, giữ lợi thế **quản lý nhiều repo cùng lúc**,
> phục vụ trước hết vai trò **PM**, sau đó mở rộng cho các vai trò khác. **Windows trước, macOS sau.**
> Lập: 2026-11-23 · Cập nhật: 2026-11-23 (đổi sang Avalonia, repo public) · Trạng thái: **chờ duyệt lần cuối, rồi bắt đầu Phase 0**

---

## 0. Quyết định đã chốt

| # | Câu hỏi | Quyết định | Hệ quả |
|---|---|---|---|
| 1 | Công nghệ | **Avalonia UI + .NET 10 LTS** (C#) | Đa nền tảng. .NET 10 hỗ trợ đến 11/2028 (.NET 8 hết hỗ trợ 11/2026) |
| 2 | Nền tảng | **Windows trước**, **macOS ở Phase 7**, Linux nếu cần | Code đa nền tảng ngay từ đầu, nhưng đóng gói / test Mac để sau |
| 3 | Bản PowerShell | **Bỏ**, viết lại source cho đàng hoàng | Phase 0 phải đạt đủ tính năng bản PowerShell rồi xoá toàn bộ `*.ps1` / `*.bat` |
| 4 | Người dùng | Hiện **1 người, vai trò PM**; sau này thêm nhiều vai trò | Kiến trúc module theo vai trò ngay từ đầu (mục 3) |
| 5 | Đa repo hay AI trước | **Làm cả hai**, xen kẽ | Phase 3 |
| 6 | Hosting | **GitHub là chính**, không tích hợp Git server khác | PR / issue / CI chỉ với GitHub. Fetch / pull / push vẫn chạy với mọi remote |
| 7 | Repo source | **Public** trên GitHub | Không có dữ liệu nội bộ trong source (mục 6.1); auto-update đọc GitHub Releases không cần token |
| 8 | License | **MIT** | File `LICENSE` ở gốc repo |
| 9 | Ticket | **GitHub Issues** (+ GitHub Projects làm kanban) | Ticket view gom theo số issue; cần đăng nhập GitHub sớm hơn (Phase 3) |

## 1. Hiện trạng (bản PowerShell, sẽ bị thay thế)

| Có rồi | Chưa có |
|---|---|
| Grid nhiều repo: branch, thay đổi, ahead/behind, last fetch | Commit graph, diff, staging từng file / hunk / dòng |
| Fetch / Checkout + Pull (fast-forward) / Pull current trên nhiều repo | Commit, push, merge, rebase, cherry-pick, stash |
| PM Report, AI Report (Gemini), Bug Hunt PCL | Giải conflict, blame, file history, tìm kiếm |
| Installer Windows, auto-update qua `git pull` | Tích hợp GitHub, phân vai trò, chạy trên macOS |

## 2. Kiến trúc

### 2.1 Công nghệ

| Lớp | Chọn | Ghi chú |
|---|---|---|
| Runtime | .NET 10 LTS, C# 14, `Nullable` bật | |
| UI | **Avalonia 12** + **CommunityToolkit.Mvvm** | XAML + MVVM gần giống WPF; chạy Windows / macOS / Linux |
| Giao diện | Theme Fluent của Avalonia, sáng / tối, tự thiết kế | Không copy UI của Fork / SourceTree |
| Git engine | **git CLI** của máy, parse định dạng máy (`--porcelain=v2 -z`, `--format` + NUL) | Đúng 100% hành vi git: credential manager, hooks, LFS, worktree |
| Đọc nhanh (tuỳ chọn) | LibGit2Sharp, chỉ đọc khi cần tốc độ | Không dùng để ghi |
| Diff / editor / conflict | **AvaloniaEdit** (bản port chính thức của AvalonEdit) | Syntax highlight, đánh dấu dòng, 3 khung |
| Commit graph | Tự vẽ bằng custom control + ảo hoá (chỉ vẽ dòng đang hiện) | Mượt với hàng chục nghìn commit |
| GitHub | **Octokit.NET** (REST) + GraphQL (Projects, review) | |
| AI | `IAiProvider`; bản đầu dùng Gemini | Đổi model / nhà cung cấp không phải sửa tính năng |
| DI, cấu hình, log | Microsoft.Extensions.Hosting / DI / Logging, Serilog ghi file | |
| Lưu bí mật | `ISecretStore`: **Windows = DPAPI** (Phase 0); macOS = Keychain (Phase 7) | Đọc lại được key bản PowerShell đã lưu |
| Tích hợp hệ điều hành | `IPlatformService`: mở thư mục, terminal, editor, đường dẫn dữ liệu, thông báo | Windows làm trước; macOS ở Phase 7 |
| Dữ liệu local | JSON settings + **SQLite** (cache commit, ticket, lịch sử report) | |
| Đóng gói + auto-update | **Velopack** + **GitHub Releases** (repo public nên không cần token) | Windows `Setup.exe` trước; macOS `.dmg` ở Phase 7 |
| Test | xUnit v3 (giữ bản 3.x cho khớp Avalonia.Headless.XUnit) trên Microsoft.Testing.Platform, **AwesomeAssertions** (FluentAssertions 8 không còn miễn phí cho thương mại), **Avalonia.Headless** (test UI không cần màn hình) | Chạy được trên CI mọi hệ điều hành |
| CI | GitHub Actions | Phase 0: `windows-latest` (build, test, đóng gói) + `ubuntu-latest` (build + unit test, để bắt sớm code chỉ chạy được trên Windows). Phase 7: thêm `macos-latest` |

### 2.2 Cấu trúc solution

```
workspace_manager/
  src/
    WorkspaceManager.App/            Avalonia: shell, điều hướng, theme, khởi động, DI
    WorkspaceManager.Core/           domain: Repo, Commit, Branch, Ticket, Role, Module; interfaces; không phụ thuộc UI / OS
    WorkspaceManager.Git/            git CLI runner, parsers, operations, hàng đợi lệnh theo repo, backup ref / undo
    WorkspaceManager.GitHub/         PR, issue, review, checks, Projects
    WorkspaceManager.Ai/             IAiProvider, Gemini, prompt templates
    WorkspaceManager.Reports/        PM report, AI report, bug hunt rules, xuất HTML / PCL
    WorkspaceManager.Data/           SQLite, settings, migrate cấu hình bản PowerShell
    WorkspaceManager.Platform/       IPlatformService, ISecretStore: Windows/ (Phase 0), MacOS/ (Phase 7)
    Modules/                         (tên `Modules` số nhiều: `Module` là từ khoá của VB, analyzer CA1716 chặn)
      WorkspaceManager.Modules.Pm/          vai trò PM (Phase 0)
      WorkspaceManager.Modules.GitClient/   Git client (Phase 1–2), dùng chung cho mọi vai trò
      WorkspaceManager.Modules.Developer/   Phase 6
      WorkspaceManager.Modules.Reviewer/    Phase 6
  tests/
    *.Tests (unit), WorkspaceManager.IntegrationTests (repo git tạm), WorkspaceManager.UiTests (Avalonia.Headless)
  samples/workspace.sample.json      cấu hình mẫu, chỉ có giá trị giả (mục 6.1)
  docs/  PLAN.md, adr/, user-guide/
  .github/workflows/  ci.yml, release.yml
  Directory.Build.props, Directory.Packages.props, .editorconfig, global.json, WorkspaceManager.slnx
```

### 2.3 Nguyên tắc

- **Không bao giờ mất code**: trước mỗi thao tác phá huỷ (reset, rebase, force push, xoá branch, drop stash, discard) tự tạo
  ref backup `refs/wm-backup/<thời gian>/<tên>`; có **Undo**; force push luôn dùng `--force-with-lease`.
- **Đọc song song, ghi tuần tự**: mỗi repo có một hàng đợi lệnh ghi; UI không bao giờ bị khoá.
- **Mọi lệnh git đều ghi log** (lệnh, thời gian, exit code), xem lại được trong app.
- **Đa nền tảng từ thiết kế**: `Core`, `Git`, `GitHub`, `Ai`, `Reports`, `Data` target `net10.0` (không phải `net10.0-windows`);
  mọi thứ riêng của hệ điều hành nằm sau `IPlatformService` / `ISecretStore`; đường dẫn dùng `Path.Combine`, không viết cứng `\`;
  so sánh tên file theo hệ điều hành (macOS / Linux phân biệt hoa thường). CI Ubuntu bắt lỗi này ngay từ Phase 0.
- **Không copy** UI, icon, tên của Fork / SourceTree / GitKraken: chỉ tham khảo tính năng.

## 3. Vai trò (role)

### 3.1 Cơ chế

- **Module** (`IModule`): đăng ký màn hình, lệnh, menu, widget dashboard, job nền.
- **Role** = danh sách module + dashboard mặc định + quyền.
- **Profile**: người dùng chọn một hoặc nhiều role, đổi ngay trên thanh tiêu đề; mỗi profile có identity git, tài khoản GitHub, bộ repo riêng.
- **Quyền**: hiện một người nên mọi quyền đều bật; nhưng mỗi lệnh đã khai báo quyền cần (vd `git.push.force`, `release.tag`),
  để sau này giới hạn theo role mà không sửa code tính năng.
- **Nhiều người dùng (sau này)**: dùng **GitHub làm backend** (Issues, Projects, PR, Discussions), không dựng server riêng.

### 3.2 Lộ trình vai trò

| Role | Khi nào | Tính năng chính |
|---|---|---|
| **PM** (hiện tại) | Phase 0 → | PM Dashboard; trạng thái mọi repo; ticket / feature view; tiến độ branch và PR; rủi ro, việc cần làm; AI report, daily / weekly report; hoạt động team; sẵn sàng release; bug hunt PCL |
| **Developer** | Phase 6 | Git client làm màn hình chính; PR của tôi; AI commit message, AI review trước push; cảnh báo conflict sớm |
| **Reviewer / Tech lead** | Phase 6 | Hàng đợi review, review diff có comment, kiểm tra quy tắc, duyệt PR |
| **QA / Tester** | Sau Phase 7 | PCL theo ticket, chạy test E2E trên staging, ghi bug sang GitHub Issues, so sánh staging với production |
| **Release manager / DevOps** | Sau Phase 7 | Checklist release, tag / changelog, trạng thái CI/CD, so sánh branch release giữa các repo |
| **Team lead / Manager** | Sau Phase 7 | Insights: commit / PR, thời gian review, phân bổ công việc theo người |

## 4. Bảng tính năng mục tiêu

Nguồn: **F** = Fork, **S** = SourceTree, **$** = chỉ có ở bản trả phí (GitKraken Pro/Advanced, Tower Pro, SmartGit, license Fork),
**WM** = riêng của Workspace Manager. Ưu tiên: **P0** bắt buộc · P1 quan trọng · P2 nên có · P3 để sau.

### 4.1 Repo & điều hướng
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| Danh sách repo, nhóm repo, tab nhiều repo | F S | P0 | 0–1 |
| Clone / init / thêm repo, quét thư mục tìm repo | F S | P0 | 0 |
| Quick Launch (Ctrl+P): repo, branch, commit, lệnh | F | P1 | 2 |
| Mở trong VS Code / thư mục / terminal | F S | P0 | 0 |
| Theme sáng / tối, phím tắt tuỳ chỉnh | F S | P1 | 1 |

### 4.2 Thay đổi & commit
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| Staging theo file / hunk / **từng dòng**, discard theo hunk / dòng | F S | P0 | 1 |
| Commit, amend, sign-off, template message, kiểm tra chính tả | F S | P0 | 1 |
| Diff inline + side-by-side, word diff, bỏ qua whitespace, syntax highlight | F S | P0 | 1 |
| Image diff | F | P2 | 2 |
| Tạo / áp dụng patch | S | P2 | 2 |
| Ký commit GPG / SSH | F S | P2 | 6 |
| Commit composer (chia lại thay đổi thành nhiều commit) | $ (Tower) | P2 | 6 |

### 4.3 Lịch sử
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| Commit graph có lane màu, ảo hoá cho repo lớn | F S | P0 | 1 |
| Chi tiết commit: file, diff, parent, ref | F S | P0 | 1 |
| Tìm commit theo message / tác giả / nội dung (`-S`, `-G`) / file | F S | P0 | 1 |
| File history, blame | F S | P0 | 1 |
| Reflog có giao diện, khôi phục branch / commit bị mất | F | P1 | 2 |
| So sánh 2 branch / 2 commit bất kỳ | F S | P1 | 2 |
| Bisect có giao diện | — | P3 | 2 |

### 4.4 Branch, remote & đồng bộ
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| Sidebar branch / tag / remote: tạo, đổi tên, xoá, checkout, track | F S | P0 | 1 |
| Fetch / pull (merge hoặc rebase) / push có tiến trình | F S | P0 | 1 |
| Stash: tạo (cả từng phần), apply, pop, xem diff | F S | P0 | 1 |
| Merge, rebase, cherry-pick, revert, reset bằng kéo thả | F S | P0 | 2 |
| **Interactive rebase** kéo thả: squash, fixup, reword, edit, drop | F | P1 | 2 |
| **Giải conflict 3 chiều** (ours / base / theirs + kết quả) | F | P0 | 2 |
| **Undo** thao tác vừa làm | $ (Tower, GitKraken) | P1 | 2 |
| Worktree, submodule, Git LFS | F S | P2 | 2 |
| Git-flow (feature / release / hotfix) | F S | P2 | 2 |
| Custom commands / custom actions | F S | P2 | 2 |
| Stacked branches | $ (Tower) | P3 | 6 |

### 4.5 Đa repo & PM
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| Grid trạng thái mọi repo, fetch / checkout / pull hàng loạt | WM | P0 | 0 |
| PM report, AI report, bug hunt PCL (viết lại bằng C#) | WM | P0 | 0 |
| **PM Dashboard**: KPI, việc cần làm, rủi ro, hoạt động, lịch sử theo ngày | WM | P0 | 0 |
| Tạo branch / commit / push / tạo PR **cùng lúc trên nhiều repo** | WM, $ (GitKraken Workspaces) | P0 | 3 |
| **Ticket view**: gom branch, commit, PR theo **GitHub Issue** xuyên repo. Liên kết qua tên branch có số issue (`feature/42-...`) và `#42` / `owner/repo#42` trong commit message; repo không ở GitHub thì cấu hình một repo GitHub chứa issue | WM | P1 | 3 |
| Tìm commit / code xuyên nhiều repo | $ (GitKraken) | P1 | 3 |
| Workspace profiles (bộ repo + branch mặc định theo dự án) | $ (GitKraken) | P2 | 3 |
| Báo cáo tuần / tháng, xu hướng | WM | P2 | 5 |

### 4.6 GitHub (bản trả phí)
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| Đăng nhập GitHub (device flow), lưu token an toàn | S, $ | P1 | 4 |
| Xem / tạo / review PR, comment trên diff, merge PR | $ (GitKraken, Tower) | P1 | 4 |
| Trạng thái GitHub Actions / checks trên commit và PR | $ | P1 | 4 |
| **Launchpad**: PR của tôi, PR cần tôi review, issue được giao, gom mọi repo | $ (GitKraken) | P1 | 4 |
| GitHub Issues + Projects: **nơi quản lý ticket chính**, tạo / sửa / gán issue, kanban cho PM | $ | P0 | 3–4 |
| Avatar tác giả | F S | P3 | 4 |

### 4.7 AI (bản trả phí)
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| Sinh commit message từ diff, đúng format của team | $ (GitKraken AI, Tower) | P1 | 3 |
| Sinh mô tả PR, changelog, release note | $ | P1 | 3 |
| Giải thích commit / diff / branch | $ (GitKraken AI) | P2 | 3 |
| AI review diff trước khi push (luật bug hunt + AI) | WM, $ | P1 | 3 |
| Gợi ý giải conflict | $ | P2 | 3 |
| Hỏi đáp về workspace ("tuần này ticket X tiến độ thế nào?") | WM | P2 | 3 |

### 4.8 Nhóm (bản trả phí)
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| **Cảnh báo conflict sớm**: branch của tôi và branch người khác cùng sửa một file | $ (GitKraken) | P1 | 6 |
| Team view: ai đang làm branch nào, file nào | $ (GitKraken) | P2 | 6 |
| Insights: thống kê commit, PR, thời gian review | $ (GitKraken Insights) | P2 | 6 |
| Chia sẻ patch nhanh (gist / PR draft) | $ (GitKraken Cloud Patch) | P3 | 6 |
| Nhiều identity theo repo / profile | $ (GitKraken) | P2 | 6 |

### 4.9 Đa nền tảng
| Tính năng | Nguồn | Ưu tiên | Phase |
|---|---|---|---|
| Windows: bộ cài, auto-update, shortcut, DPAPI | F S | P0 | 0 |
| **macOS**: `.dmg`, auto-update, Keychain, menu bar, phím Cmd, Finder / Terminal | F S | P1 | 7 |
| Linux (AppImage / deb) | — | P3 | sau 7 |

## 5. Các phase

Ước lượng cho **1 dev có AI hỗ trợ**, tính bằng tuần. Mỗi phase kết thúc bằng một bản release trên GitHub Releases, app tự cập nhật.

| Phase | Nội dung | Tuần | Nghiệm thu |
|---|---|---|---|
| **0. Nền móng + thay bản PowerShell** | Solution, CI (Windows + Ubuntu), chuẩn source (mục 6); git engine + parser; shell Avalonia, cơ chế module / role / profile; `Platform` cho Windows; **module PM**: grid repo, fetch / checkout / pull hàng loạt, PM report, AI report, bug hunt PCL, PM Dashboard; màn hình "Thiết lập lần đầu" (clone repo còn thiếu, git identity); migrate cấu hình + Gemini key bản PowerShell; bộ cài + auto-update Velopack; **xoá `*.ps1`, `*.bat`** | 5 | Làm được mọi việc bản PowerShell làm; số liệu grid khớp bản cũ; cài trên máy Windows sạch < 2 phút |
| **1. Git client cơ bản** | 4.1, 4.2 (trừ GPG / composer / patch / image), 4.3 (trừ reflog / bisect), 4.4 phần P0 trừ merge / rebase | 4–5 | Làm việc cả ngày bằng app: graph, stage từng dòng, commit, push, stash, blame. Graph repo lớn nhất cuộn mượt |
| **2. Git nâng cao** | Merge / rebase / cherry-pick / reset kéo thả, interactive rebase, conflict 3 chiều, Undo + backup ref, reflog, so sánh, worktree, submodule, LFS, git-flow, patch, image diff, custom command, Quick Launch | 4–5 | Ngang Fork. Rebase có conflict trên repo mẫu chạy đúng, Undo khôi phục được |
| **3. Đa repo + AI (xen kẽ)** | Tuần lẻ: đa repo. Tuần chẵn: AI. Thứ tự: thao tác hàng loạt → AI commit message → đăng nhập GitHub + đọc Issues (làm sớm một phần Phase 4) → ticket view → AI review trước push → tìm xuyên repo → mô tả PR / changelog → profiles → hỏi đáp workspace | 5 | Tạo branch cùng tên trên 4 repo bằng 1 thao tác; AI commit message đúng format; ticket view khớp dữ liệu thật |
| **4. GitHub** | 4.6 | 3–4 | Tạo PR và review từ app; Launchpad hiện đúng PR đang mở |
| **5. Hoàn thiện cho PM** | Dashboard tuỳ biến (widget), báo cáo tuần / tháng, xuất báo cáo, thông báo hệ điều hành | 2 | Báo cáo tuần tự sinh mỗi thứ Hai |
| **6. Nhóm + vai trò mới** | 4.8; bật cơ chế quyền; module **Developer**, **Reviewer**; GPG / SSH signing; commit composer; stacked branches | 4 | Đổi role Developer / Reviewer được; cảnh báo conflict đúng |
| **7. macOS** | `Platform/MacOS` (Finder, Terminal, đường dẫn `~/Library/Application Support`, thông báo); **Keychain**; menu bar macOS, phím Cmd; CI `macos-latest` (Intel + Apple Silicon); `.dmg` + auto-update Velopack; ký + notarize (cần Apple Developer account ~99 USD/năm nếu phát cho người khác); test trên Mac thật | 2–3 | Cài trên Mac sạch, mở được không cảnh báo (nếu đã notarize), mọi tính năng Phase 0–6 chạy đúng |
| | **Tổng** | **~29–34 tuần** | Thay bản PowerShell sau ~5 tuần; thay Fork trên Windows sau ~10 tuần; macOS sau ~32 tuần |

Sau Phase 7: module QA, Release manager, Team lead (mỗi module ~2–3 tuần); Linux nếu cần.
Nếu cần Mac sớm hơn, Phase 7 có thể chuyển lên ngay sau Phase 2 (vì code đã đa nền tảng từ đầu).

## 6. Chuẩn source

### 6.1 Repo public: không có dữ liệu nội bộ
- **Không commit**: URL / host nội bộ, URL staging, danh sách repo của công ty, mã ticket thật, email, token, key, đường dẫn máy cá nhân.
- Mọi thứ riêng của workspace (danh sách repo + URL clone, URL staging cho test, quy tắc commit message của team, prompt riêng)
  nằm trong **file cấu hình local** `%APPDATA%\WorkspaceManager\workspace.json` (macOS: `~/Library/Application Support/WorkspaceManager/`),
  có thể **nhập / xuất** để chia sẻ riêng trong team. Repo chỉ có `samples/workspace.sample.json` với giá trị giả.
- CI chạy **secret scanning** + kiểm tra chuỗi cấm (danh sách pattern host nội bộ lưu trong GitHub Secrets, không nằm trong repo).
- Commit dùng email **GitHub noreply** (`<id>+<user>@users.noreply.github.com`) cho repo này.

### 6.2 Quy trình & chất lượng
- **Branch**: `main` luôn release được; mỗi việc một branch `feature/…` / `fix/…`; merge qua **Pull Request**, CI phải xanh; squash merge.
- **Commit message**: Conventional Commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`) → CHANGELOG tự động.
- **Phiên bản**: SemVer (`0.x` khi phát triển, `1.0.0` sau Phase 1); tag `v*` → workflow release.
- **Code**: `.editorconfig`; `Directory.Build.props` bật `Nullable`, `TreatWarningsAsErrors`, .NET analyzers; `dotnet format` trong CI;
  không có logic trong code-behind (MVVM); mọi service có interface, inject qua DI.
- **Test**: bắt buộc cho Git, Core, Reports; coverage ≥ 70% các project không phải UI.
- **Tài liệu**: `docs/adr/` (001-avalonia-dotnet10, 002-git-cli-engine, 003-module-role, 004-no-internal-data-in-public-repo,
  005-windows-first-macos-later); `docs/user-guide/`; README hướng dẫn build.
- **License**: MIT (file `LICENSE`); chỉ dùng thư viện có license tương thích (MIT / Apache-2.0 / BSD), không dùng GPL.

## 7. Kiểm thử

- **Unit**: parser trên output git thật đã lưu (fixture): tên file Unicode, có dấu cách, CRLF, rename, submodule, merge commit.
- **Integration**: mỗi thao tác (commit, merge có conflict, rebase -i, cherry-pick, stash, reset + Undo) chạy trên repo mẫu tạo mới
  trong thư mục tạm, push lên bare repo cục bộ; kiểm tra bằng git. Chạy trên Windows + Ubuntu (Phase 7: + macOS).
- **UI**: Avalonia.Headless cho kịch bản chính (stage dòng → commit → push; checkout hàng loạt; AI report với AI giả lập).
- **Hiệu năng**: trên repo lớn nhất của workspace: mở repo < 2 giây, cuộn graph mượt, diff 2.000+ file không treo UI.
- **Không chạm repo thật trong test**; GitHub và Gemini dùng bản giả lập.

## 8. Chuyển từ bản PowerShell

| Bản cũ | Thay bằng | Ghi chú |
|---|---|---|
| `workspace_manager.ps1` (GUI) | `App` + `Module.Pm` | |
| `pull_latest.ps1` | `Git` (thao tác hàng loạt) | Giữ quy tắc: pull hàng loạt chỉ fast-forward, không merge / reset |
| `workspace_report.ps1` | `Reports.PmReport` | Lưu lịch sử vào SQLite |
| `workspace_ai_report.ps1` | `Reports.AiReport` + `Ai.Gemini` | Đọc được ghi chú `_reports\ai_notes.txt` |
| `branch_bughunt.ps1` | `Reports.BugHunt` | Xuất PCL VI + EN như cũ; URL staging lấy từ cấu hình local |
| `install.ps1` / `install.bat` | Bộ cài Velopack + màn hình "Thiết lập lần đầu" | |
| `update_tools.ps1` | Velopack auto-update | |
| `repos.txt` | Danh sách repo trong `workspace.json` local | Không còn trong repo public |
| `%LOCALAPPDATA%\workspace_manager.json`, `workspace_manager_gemini.key` | Tự import khi chạy lần đầu | Key DPAPI đọc lại được |

Bản PowerShell **không nằm trong repo public này** (đã gỡ ngày 2026-11-23 vì có cấu hình riêng của công ty); nó chỉ còn
ở máy cá nhân và được bỏ hẳn khi Phase 0 xong.

## 9. Rủi ro & cách giảm

| Rủi ro | Mức | Cách giảm |
|---|---|---|
| Mất code do thao tác phá huỷ | Cao | Backup ref, Undo, hỏi xác nhận, `--force-with-lease` |
| **Lộ dữ liệu nội bộ qua repo public** | Cao | Mục 6.1; kiểm tra trong CI; lịch sử repo đã được làm mới (2026-11-23) |
| Phạm vi quá lớn (~30 tuần) | Cao | Phase nào cũng ra bản dùng được; P2 / P3 có thể bỏ; xem lại plan cuối mỗi phase |
| Parse sai output git | Trung bình | Chỉ dùng định dạng máy, `LC_ALL=C`, kiểm tra phiên bản git tối thiểu |
| Chậm trên repo lớn | Trung bình | Ảo hoá, đọc theo trang, `git commit-graph`, diff lười, chạy nền |
| Avalonia khác WPF ở vài chỗ (control, thư viện bên thứ ba ít hơn) | Thấp | Prototype graph + diff trong Phase 0 trước khi làm Phase 1 |
| Code vô tình chỉ chạy trên Windows | Trung bình | CI Ubuntu từ Phase 0; `IPlatformService`; review |
| macOS cần ký / notarize, cần máy Mac để test | Trung bình | Phase 7; chỉ dùng cá nhân thì mở bằng chuột phải → Open; phát rộng cần Apple Developer account |
| Repo code của team có thể nằm trên Git server khác GitHub | Thấp | Git chạy bình thường với mọi remote; chỉ PR / issue / CI là của GitHub |
| Lộ token / dữ liệu khi dùng AI | Trung bình | `ISecretStore`; AI có nút tắt; chỉ gửi diff khi người dùng bấm; xem trước dữ liệu gửi đi |

## 10. Tiến độ

### Phase 0 – tuần 1 (2026-11-23, branch `feature/phase0-foundation`)

| Việc | Trạng thái |
|---|---|
| Cài .NET 10 SDK (10.0.404) + template Avalonia | ✅ |
| Solution `.slnx`, central package management, analyzers = lỗi, `.editorconfig`, `dotnet format` sạch | ✅ |
| `Git`: GitRunner, parser `status --porcelain=v2 -z`, remote branches, RepoStatusReader, WorkspaceScanner | ✅ |
| So số liệu với bản PowerShell trên workspace thật (25 repo) | ✅ khớp 100%, đọc hết trong ~3,6 giây |
| Core: module / role / quyền, `IPlatformService`, `ISecretStore`, settings | ✅ |
| Platform Windows (Explorer, Git Bash / Windows Terminal, VS Code, DPAPI, đọc key bản PowerShell) | ✅ |
| Data: settings JSON, import lựa chọn repo từ bản PowerShell | ✅ |
| Shell Avalonia + module PM: grid repo, lọc, chọn nhanh, mở thư mục / terminal / VS Code | ✅ |
| Test: 39 test (unit, integration git thật, ViewModel, UI headless) | ✅ |
| ADR 001–005, CI GitHub Actions (Windows + Ubuntu, format, kiểm tra dữ liệu nội bộ) | ✅ |
| Prototype commit graph + diff (AvaloniaEdit) | ⏳ tuần 2 |

### Phase 0 – tuần 2 (2026-11-23)

| Việc | Trạng thái |
|---|---|
| Fetch / Checkout + Pull / Pull current hàng loạt (chỉ fast-forward, stash, giữ branch hiện tại, khoá ghi theo repo, xác nhận, log màu) | ✅ 11 test git thật |
| Bộ cài Windows + auto-update (Velopack, GitHub Releases, workflow `release.yml` khi push tag `v*`), icon riêng | ✅ đóng gói thử được Setup.exe 60 MB |
| PM report trong app: KPI, việc cần làm, theo dõi branch, daily report, team, xuất HTML | ✅ khớp bản PowerShell (25 repo, 12 High) |
| AI report (Gemini): key DPAPI / biến môi trường / import .env, xem trước dữ liệu gửi đi, nút tắt AI, Markdown -> HTML | ✅ gọi Gemini thật OK |
| Hiệu năng: git không chạy trên luồng UI, tải dữ liệu sau khi mở cửa sổ, ReadyToRun | ✅ |
| Test | ✅ 99 test |

### Phase 0 – còn lại
PM Dashboard (trang chủ) + thiết lập lần đầu (chọn workspace, clone repo còn thiếu từ danh sách URL lưu local, git identity)
→ bug hunt (luật chung, cấu hình được) → release `v0.1.0` → bỏ hẳn bản PowerShell.

## 10b. Việc làm ngay (Phase 0, tuần 1) – kế hoạch ban đầu

1. Cài .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`) và template Avalonia (`dotnet new install Avalonia.Templates`).
2. Tạo solution, `Directory.Build.props`, `.editorconfig`, `global.json`, `LICENSE`, CI GitHub Actions (Windows + Ubuntu).
3. Viết ADR 001–005.
4. `Git`: runner + parser `status --porcelain=v2`, `for-each-ref`, `rev-list --left-right` + unit test.
5. Prototype commit graph + diff bằng Avalonia / AvaloniaEdit (giảm rủi ro sớm).
6. Shell Avalonia + module PM với grid repo, so sánh số liệu với bản PowerShell.

## 11. Còn cần chốt

Không còn câu hỏi mở. Các quyết định nằm ở mục 0; quyết định mới phát sinh sẽ ghi vào `docs/adr/`.
