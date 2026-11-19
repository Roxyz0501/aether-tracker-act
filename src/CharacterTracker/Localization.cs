using System;
using System.Collections.Generic;
using System.Globalization;

namespace CharacterTracker
{
    public static class Localization
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Resources = BuildResources();
        private static readonly Dictionary<string, Dictionary<string, string>> RuntimeOverrides = BuildRuntimeOverrides();
        private static string language = "en";

        public static string Language { get { return language; } }
        public static string UiFontName {
            get {
                if (language == "ja") return "Yu Gothic UI";
                if (language == "zh-Hans") return "Microsoft YaHei UI";
                if (language == "ko") return "Malgun Gothic";
                return "Segoe UI";
            }
        }

        public static void SetLanguage(string value) { language = NormalizeLanguage(value); }

        public static string NormalizeLanguage(string value)
        {
            if (string.Equals(value, "ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (string.Equals(value, "zh-Hans", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "zh-CN", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (string.Equals(value, "ko", StringComparison.OrdinalIgnoreCase)) return "ko";
            return "en";
        }

        public static string DetectLanguage(CultureInfo culture)
        {
            string name = culture == null ? "" : culture.Name;
            if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
            return "en";
        }

        public static string T(string key)
        {
            Dictionary<string, string> selected;
            string value;
            if (RuntimeOverrides.TryGetValue(language, out selected) && selected.TryGetValue(key, out value)) return value;
            if (Resources.TryGetValue(language, out selected) && selected.TryGetValue(key, out value)) return value;
            if (Resources["en"].TryGetValue(key, out value)) return value;
            return key;
        }

        public static string F(string key, params object[] args) { return string.Format(CultureInfo.CurrentCulture, T(key), args); }

        private static Dictionary<string, Dictionary<string, string>> BuildRuntimeOverrides()
        {
            var resources = new Dictionary<string, Dictionary<string, string>> {
                { "en", new Dictionary<string, string> {
                    { "update.ready", "Package verified and staged. The updater will replace the plugin after ACT exits; restart ACT afterward. A timestamped backup is retained." },
                    { "update.safety", "Updates download only after you click Update. A verified helper waits for ACT to exit, then replaces the DLL with backup and rollback protection." },
                    { "update.downloading", "Downloading and validating the update package…" },
                    { "update.packageFailed", "The update package failed validation or could not be staged." },
                    { "settings.saveFailed", "Settings could not be saved. Your changes remain in memory until ACT exits: {0}" }
                } },
                { "ja", new Dictionary<string, string> {
                    { "update.ready", "更新パッケージを検証して準備しました。ACT終了後に補助プログラムが置換します。適用後はACTを再起動してください。バックアップを保持します。" },
                    { "update.safety", "更新ボタンを押した場合だけ取得します。検証済み補助プログラムがACT終了を待ってDLLを置換し、バックアップとロールバックを行います。" },
                    { "update.downloading", "更新パッケージをダウンロードして検証中…" },
                    { "update.packageFailed", "更新パッケージを検証または準備できませんでした。" },
                    { "settings.saveFailed", "設定を保存できませんでした。変更はACT終了までメモリに保持されます: {0}" }
                } },
                { "zh-Hans", new Dictionary<string, string> {
                    { "update.ready", "更新包已验证并准备就绪。ACT退出后辅助程序将替换插件，请随后重启ACT。将保留带时间戳的备份。" },
                    { "update.safety", "仅在点击更新后下载。经过验证的辅助程序会等待ACT退出，再替换DLL，并提供备份和回滚保护。" },
                    { "update.downloading", "正在下载并验证更新包…" },
                    { "update.packageFailed", "更新包验证失败或无法完成准备。" },
                    { "settings.saveFailed", "无法保存设置。更改会保留在内存中，直到ACT退出：{0}" }
                } },
                { "ko", new Dictionary<string, string> {
                    { "update.ready", "업데이트 패키지가 검증 및 준비되었습니다. ACT 종료 후 보조 프로그램이 교체합니다. 적용 후 ACT를 다시 시작하세요. 시간 표시 백업을 보관합니다." },
                    { "update.safety", "업데이트를 클릭한 경우에만 다운로드합니다. 검증된 보조 프로그램이 ACT 종료를 기다린 뒤 DLL을 교체하며 백업 및 롤백을 보호합니다." },
                    { "update.downloading", "업데이트 패키지를 다운로드하고 검증 중…" },
                    { "update.packageFailed", "업데이트 패키지를 검증하거나 준비하지 못했습니다." },
                    { "settings.saveFailed", "설정을 저장하지 못했습니다. 변경 사항은 ACT가 종료될 때까지 메모리에 유지됩니다: {0}" }
                } }
            };
            AddUpdateIdentity(resources["en"], "Author: {0}  •  Installed version: {1}", "Open Aether Tracker on GitHub", "Could not open GitHub: {0}", "Could not identify the installed plugin path: {0}", "ACT did not report a valid installed CharacterTracker.dll path.", "GitHub API rate limit reached. The verified latest stable tag is available; release assets can still be checked when you choose Update.", "Could not reach GitHub. Check your connection and try again later.");
            AddUpdateIdentity(resources["ja"], "作者: {0}  •  導入版: {1}", "GitHubでAether Trackerを開く", "GitHubを開けませんでした: {0}", "導入済みプラグインの場所を確認できません: {0}", "ACTから有効なCharacterTracker.dllの場所を取得できませんでした。", "GitHub APIの利用制限に達しました。最新安定版タグは確認できています。更新操作時に公式配布ファイルを確認できます。", "GitHubへ接続できませんでした。通信を確認して後でもう一度お試しください。");
            AddUpdateIdentity(resources["zh-Hans"], "作者：{0}  •  已安装版本：{1}", "在GitHub打开Aether Tracker", "无法打开GitHub：{0}", "无法识别已安装插件路径：{0}", "ACT未提供有效的CharacterTracker.dll路径。", "已达到GitHub API速率限制。已确认最新稳定版标签；您选择更新时仍可验证官方文件。", "无法连接GitHub。请检查网络并稍后重试。");
            AddUpdateIdentity(resources["ko"], "작성자: {0}  •  설치 버전: {1}", "GitHub에서 Aether Tracker 열기", "GitHub를 열 수 없습니다: {0}", "설치된 플러그인 경로를 확인할 수 없습니다: {0}", "ACT에서 유효한 CharacterTracker.dll 경로를 제공하지 않았습니다.", "GitHub API 요청 한도에 도달했습니다. 최신 안정 태그는 확인했으며 업데이트를 선택하면 공식 파일을 검증할 수 있습니다.", "GitHub에 연결할 수 없습니다. 네트워크를 확인하고 나중에 다시 시도하세요.");
            return resources;
        }

        private static void AddUpdateIdentity(Dictionary<string, string> values, string identity, string repository, string linkError, string targetError, string targetUnavailable, string rateLimited, string networkError)
        {
            values["update.identity"] = identity; values["update.repository"] = repository; values["update.linkFailed"] = linkError;
            values["update.targetFailed"] = targetError; values["update.targetUnavailable"] = targetUnavailable;
            values["update.rateLimited"] = rateLimited; values["update.networkFailed"] = networkError;
        }

        private static Dictionary<string, Dictionary<string, string>> BuildResources()
        {
            var en = new Dictionary<string, string> {
                { "test.fallback", "English fallback" },
                { "tab.display", "Display" }, { "tab.tracking", "Tracking" }, { "tab.map", "Map & Coordinates" }, { "tab.support", "Support" }, { "tab.update", "Updates" },
                { "header.subtitle", "Configure display, tracking, maps, language, and updates" }, { "language", "Language" },
                { "show.players", "Show players" }, { "show.npcs", "Show NPCs" }, { "show.others", "Show other objects" },
                { "show.arrow", "Show tracking arrow" }, { "show.list", "Show actor list" }, { "show.map", "Show map and coordinate markers" },
                { "overlay.lock", "Lock overlays" }, { "facing", "Point arrow relative to player facing" }, { "inactive", "Hide when FFXIV is inactive" },
                { "opacity", "Overlay opacity" }, { "background.opacity", "Background opacity" },
                { "display.hint", "When unlocked, drag headers to move and edges to resize.\r\nTracking and map interactions remain available while locked." },
                { "connection.wait", "●  Waiting for FFXIV_ACT_Plugin" }, { "connected", "●  Connected  •  {0} actors  •  Territory {1}" },
                { "tracking.search", "List search" }, { "tracking.clear", "Clear tracking" }, { "tracking.none", "Tracking: none" }, { "tracking.target", "Tracking: {0}  •  {1}" },
                { "direction.hint", "When off, map north is up. Camera rotation is not available." },
                { "calibration.title", "In-game coordinate calibration" }, { "calibration.a", "Record point A" }, { "calibration.b", "Record point B" },
                { "calibration.none", "In-game coordinates: not calibrated" }, { "calibration.done", "In-game coordinates: calibrated  •  Territory {0}" },
                { "calibration.help", "Record the X/Y shown on the in-game map at two distant points on the same map.\r\nUse the mouse wheel to zoom the map; click a marker to show its name." },
                { "calibration.noPlayer", "Player information is unavailable." }, { "calibration.sameMap", "Calibrate point B on the same map as point A." },
                { "calibration.aSaved", "Point A recorded. Move at least 5 yalms along both X and Y." }, { "calibration.saved", "Calibration saved." },
                { "calibration.dialogA", "Coordinates shown on the in-game map" }, { "calibration.dialogB", "In-game map coordinates after moving" },
                { "calibration.failed", "Calibration failed" }, { "record", "Record" }, { "cancel", "Cancel" },
                { "support.title", "Support Roxyz0501's development" },
                { "support.description", "If you enjoy Aether Tracker, you may optionally support development through Ko-fi.\r\nEvery feature is available without support; there is no functional difference." },
                { "support.button", "Support Roxyz0501 on Ko-fi" }, { "support.note", "This page is informational only. There are no startup popups, automatic redirects, repeated prompts, or feature restrictions." },
                { "support.opened", "Ko-fi opened in your default browser." }, { "support.failed", "Could not open the link. Paste the URL into your browser. ({0})" },
                { "update.startup", "Check for updates at startup" }, { "update.checkNow", "Check now" }, { "update.install", "Update" }, { "update.later", "Later" },
                { "update.title", "Aether Tracker updates" }, { "update.idle", "No update check has run yet." }, { "update.checking", "Checking GitHub Releases…" },
                { "update.unconfigured", "Update source is not configured. Set the verified GitHub owner and repository in UpdateConfiguration.cs, then rebuild." },
                { "update.current", "Current version: {0}" }, { "update.latest", "Latest version: {0}" }, { "update.available", "An update is available." },
                { "update.none", "You are using the latest stable version." }, { "update.failed", "Update check failed: {0}" }, { "update.skipped", "Version {0} was deferred." },
                { "update.downloading", "Downloading and validating the update package…" },
                { "update.ready", "Update staged. Close ACT to apply it, then restart ACT." }, { "update.installFailed", "Update preparation failed: {0}" },
                { "update.notes", "Release notes" }, { "update.safety", "Updates only download after you click Update. The loaded DLL is never overwritten directly." },
                { "plugin.started", "Aether Tracker: running" }, { "plugin.stopped", "Aether Tracker: stopped" },
                { "map.checkingTerritory", "Checking map for Territory {0}…" }, { "map.loading", "Loading Map {0}…" }, { "map.failed", "Download failed: {0}" },
                { "map.waiting", "Checking area information…" }, { "actors.waiting", "Waiting for actors" }, { "coordinates.raw", "Coordinates: raw" }, { "coordinates.game", "Coordinates: in-game" },
                { "column.name", "NAME" }, { "column.type", "TYPE" }, { "column.distance", "DIST" },
                { "overlay.unlocked", "UNLOCKED  •  DRAG / RESIZE" }, { "overlay.waiting", "WAITING" }, { "overlay.tracking", "TRACKING" }, { "overlay.lost", "LOST" },
                { "tracking.lost", "Target lost" }, { "tracking.placeholder", "Tracked target" }, { "height", "height {0:+0.0;-0.0;0.0}" },
                { "error.ffxivDisabled", "FFXIV_ACT_Plugin.dll is not enabled." }, { "error.repository", "FFXIV DataRepository is unavailable." }, { "error.api", "FFXIV_ACT_Plugin public API was not found. Check for an update." },
                { "error.move", "Move at least 5 yalms along both X and Y before recording point B." }, { "error.scale", "The calculated scale is outside the normal range. Check the in-game X/Y values." },
                { "error.noMap", "This area has no available map." }, { "error.mapField", "The map response does not contain {0}." },
                { "kind.1", "Player" }, { "kind.2", "Battle NPC" }, { "kind.3", "NPC" }, { "kind.4", "Aetheryte" }, { "kind.5", "Gathering point" }, { "kind.6", "Event object" }, { "kind.7", "Mount" }, { "kind.8", "Minion" }, { "kind.9", "Retainer" }, { "kind.10", "Area object" }, { "kind.11", "Housing" }, { "kind.12", "Cutscene" }, { "kind.13", "Card stand" }, { "kind.14", "Fashion accessory" }, { "kind.other", "Other ({0})" }
            };
            return new Dictionary<string, Dictionary<string, string>> {
                { "en", en },
                { "ja", Overlay(en, new Dictionary<string, string> {
                    { "tab.display", "表示" }, { "tab.tracking", "一覧・追跡" }, { "tab.map", "マップ・座標" }, { "tab.support", "支援" }, { "tab.update", "更新" }, { "header.subtitle", "表示・追跡・マップ・言語・更新をここから調整できます" }, { "language", "言語" },
                    { "show.players", "プレイヤーを表示する" }, { "show.npcs", "NPCを表示する" }, { "show.others", "その他のオブジェクトを表示する" }, { "show.arrow", "追跡矢印を表示する" }, { "show.list", "キャラクター一覧を表示する" }, { "show.map", "マップと座標点を表示する" }, { "overlay.lock", "オーバーレイを固定" }, { "facing", "矢印はプレイヤー正面を上にする" }, { "inactive", "FFXIVが非アクティブなら隠す" }, { "opacity", "オーバーレイの透明度" }, { "background.opacity", "背景の透明度" }, { "display.hint", "固定OFF中はヘッダーをドラッグして移動、縁をドラッグしてリサイズできます。\r\n固定ON中も一覧の追跡操作とマップのズーム・点選択は利用できます。" },
                    { "connection.wait", "●  FFXIV_ACT_Plugin 接続待機中" }, { "connected", "●  接続中  •  {0}体  •  Territory {1}" }, { "tracking.search", "一覧検索" }, { "tracking.clear", "追跡を解除" }, { "tracking.none", "追跡対象: なし" }, { "tracking.target", "追跡対象: {0}  •  {1}" }, { "direction.hint", "OFFではマップ北が上になります。カメラ角には追従しません。" },
                    { "calibration.title", "ゲーム内座標の校正" }, { "calibration.a", "地点Aを記録" }, { "calibration.b", "地点Bを記録" }, { "calibration.none", "ゲーム内座標: 未校正" }, { "calibration.done", "ゲーム内座標: 校正済み  •  Territory {0}" }, { "calibration.help", "同じマップ内の離れた2地点で、ゲーム内マップに表示されるX/Yを記録します。\r\nマップ上ではホイールでズームし、点をクリックすると対象名を表示します。" }, { "calibration.noPlayer", "プレイヤー情報を取得できません。" }, { "calibration.sameMap", "地点Aと同じマップで校正してください。" }, { "calibration.aSaved", "地点Aを記録しました。X/Y両方向へ5 yalms以上移動してください。" }, { "calibration.saved", "校正を保存しました。" }, { "calibration.dialogA", "ゲーム内マップに表示中の座標" }, { "calibration.dialogB", "移動後のゲーム内マップ座標" }, { "calibration.failed", "校正できません" }, { "record", "記録" }, { "cancel", "キャンセル" },
                    { "support.title", "Roxyz0501の開発を支援" }, { "support.description", "Aether Trackerを気に入っていただけた場合は、Ko-fiから任意で開発を支援できます。\r\n支援しなくてもすべての機能を利用でき、機能差はありません。" }, { "support.button", "Ko-fiでRoxyz0501を支援する" }, { "support.note", "このページは案内専用です。起動時のポップアップ、自動遷移、繰り返し通知、機能制限はありません。" }, { "support.opened", "既定のブラウザでKo-fiを開きました。" }, { "support.failed", "リンクを開けませんでした。ブラウザへURLを貼り付けてください。({0})" },
                    { "update.startup", "起動時に更新を確認" }, { "update.checkNow", "今すぐ確認" }, { "update.install", "更新する" }, { "update.later", "後で" }, { "update.title", "Aether Trackerの更新" }, { "update.idle", "まだ更新を確認していません。" }, { "update.checking", "GitHub Releasesを確認中…" }, { "update.unconfigured", "更新元が未設定です。確認済みのGitHub所有者名とリポジトリ名をUpdateConfiguration.csへ設定し、再ビルドしてください。" }, { "update.current", "現在版: {0}" }, { "update.latest", "最新版: {0}" }, { "update.available", "利用可能な更新があります。" }, { "update.none", "最新の安定版を使用しています。" }, { "update.failed", "更新確認に失敗しました: {0}" }, { "update.skipped", "バージョン {0} を後回しにしました。" }, { "update.downloading", "更新パッケージをダウンロードして検証中…" }, { "update.ready", "更新を準備しました。ACTを終了すると適用されます。その後ACTを再起動してください。" }, { "update.installFailed", "更新準備に失敗しました: {0}" }, { "update.notes", "更新内容" }, { "update.safety", "更新ボタンを押した場合だけダウンロードします。ロード中のDLLを直接上書きしません。" },
                    { "plugin.started", "Aether Tracker: 起動済み" }, { "plugin.stopped", "Aether Tracker: 停止" }, { "map.checkingTerritory", "Territory {0} のマップを確認中…" }, { "map.loading", "Map {0} を取得中…" }, { "map.failed", "取得失敗: {0}" }, { "map.waiting", "エリア情報を確認しています…" }, { "actors.waiting", "キャラクター待機中" }, { "coordinates.raw", "座標: raw" }, { "coordinates.game", "座標: ゲーム内" }, { "tracking.lost", "対象を見失いました" }, { "tracking.placeholder", "追跡対象" }, { "height", "高低差 {0:+0.0;-0.0;0.0}" },
                    { "column.name", "名前" }, { "column.type", "種類" }, { "column.distance", "距離" }, { "overlay.unlocked", "固定解除  •  ドラッグ / サイズ変更" }, { "overlay.waiting", "待機中" }, { "overlay.tracking", "追跡中" }, { "overlay.lost", "見失い" },
                    { "error.ffxivDisabled", "FFXIV_ACT_Plugin.dll が有効ではありません。" }, { "error.repository", "FFXIV DataRepository を取得できません。" }, { "error.api", "FFXIV_ACT_Plugin の公開APIが見つかりません。更新を確認してください。" }, { "error.move", "X方向とY方向の両方へ5 yalms以上移動してから地点Bを記録してください。" }, { "error.scale", "入力座標から得た縮尺が通常範囲外です。ゲーム内X/Yを確認してください。" },
                    { "error.noMap", "このエリアには利用可能なマップがありません。" }, { "error.mapField", "Map応答に {0} がありません。" },
                    { "kind.1", "プレイヤー" }, { "kind.2", "バトルNPC" }, { "kind.3", "NPC" }, { "kind.4", "エーテライト" }, { "kind.5", "採集ポイント" }, { "kind.6", "イベントオブジェクト" }, { "kind.7", "マウント" }, { "kind.8", "ミニオン" }, { "kind.9", "リテイナー" }, { "kind.10", "範囲オブジェクト" }, { "kind.11", "ハウジング" }, { "kind.12", "カットシーン" }, { "kind.13", "カード台" }, { "kind.14", "ファッションアクセサリー" }, { "kind.other", "その他({0})" }
                }) },
                { "zh-Hans", Overlay(en, new Dictionary<string, string> {
                    { "tab.display", "显示" }, { "tab.tracking", "列表与追踪" }, { "tab.map", "地图与坐标" }, { "tab.support", "支持" }, { "tab.update", "更新" }, { "header.subtitle", "在此调整显示、追踪、地图、语言和更新" }, { "language", "语言" },
                    { "show.players", "显示玩家" }, { "show.npcs", "显示NPC" }, { "show.others", "显示其他对象" }, { "show.arrow", "显示追踪箭头" }, { "show.list", "显示角色列表" }, { "show.map", "显示地图和坐标点" }, { "overlay.lock", "锁定悬浮窗" }, { "facing", "箭头以玩家朝向为上" }, { "inactive", "FFXIV不活动时隐藏" }, { "opacity", "悬浮窗透明度" }, { "background.opacity", "背景透明度" }, { "display.hint", "解锁时拖动标题栏可移动，拖动边缘可调整大小。\r\n锁定后仍可使用追踪和地图交互。" }, { "connection.wait", "●  正在等待FFXIV_ACT_Plugin" }, { "connected", "●  已连接  •  {0}个对象  •  Territory {1}" }, { "tracking.search", "列表搜索" }, { "tracking.clear", "取消追踪" }, { "tracking.none", "追踪目标：无" }, { "tracking.target", "追踪目标：{0}  •  {1}" }, { "direction.hint", "关闭时地图北方朝上，不支持相机旋转。" },
                    { "calibration.title", "游戏内坐标校准" }, { "calibration.a", "记录A点" }, { "calibration.b", "记录B点" }, { "calibration.none", "游戏内坐标：未校准" }, { "calibration.done", "游戏内坐标：已校准  •  Territory {0}" }, { "calibration.help", "在同一地图两个相距较远的位置记录游戏地图显示的X/Y。\r\n滚轮缩放地图，点击坐标点显示名称。" }, { "calibration.noPlayer", "无法取得玩家信息。" }, { "calibration.sameMap", "请在与A点相同的地图校准。" }, { "calibration.aSaved", "已记录A点。请沿X和Y方向各移动至少5 yalms。" }, { "calibration.saved", "校准已保存。" }, { "calibration.dialogA", "游戏地图上显示的坐标" }, { "calibration.dialogB", "移动后的游戏地图坐标" }, { "calibration.failed", "校准失败" }, { "record", "记录" }, { "cancel", "取消" },
                    { "support.title", "支持Roxyz0501的开发" }, { "support.description", "如果您喜欢Aether Tracker，可以自愿通过Ko-fi支持开发。\r\n不支持也能使用全部功能，不存在功能差异。" }, { "support.button", "在Ko-fi支持Roxyz0501" }, { "support.note", "此页面仅用于说明。没有启动弹窗、自动跳转、重复提醒或功能限制。" }, { "support.opened", "已在默认浏览器中打开Ko-fi。" }, { "support.failed", "无法打开链接。请将URL粘贴到浏览器。({0})" },
                    { "update.startup", "启动时检查更新" }, { "update.checkNow", "立即检查" }, { "update.install", "更新" }, { "update.later", "稍后" }, { "update.title", "Aether Tracker更新" }, { "update.idle", "尚未检查更新。" }, { "update.checking", "正在检查GitHub Releases…" }, { "update.unconfigured", "尚未配置更新源。请在UpdateConfiguration.cs中设置已验证的GitHub所有者和仓库名，然后重新构建。" }, { "update.current", "当前版本：{0}" }, { "update.latest", "最新版本：{0}" }, { "update.available", "有可用更新。" }, { "update.none", "您正在使用最新稳定版。" }, { "update.failed", "检查更新失败：{0}" }, { "update.skipped", "已暂缓版本 {0}。" }, { "update.downloading", "正在下载并验证签名更新包…" }, { "update.ready", "更新已准备。关闭ACT后将应用更新，然后请重启ACT。" }, { "update.installFailed", "准备更新失败：{0}" }, { "update.notes", "发行说明" }, { "update.safety", "仅在点击更新后下载，不会直接覆盖已加载的DLL。" },
                    { "plugin.started", "Aether Tracker：运行中" }, { "plugin.stopped", "Aether Tracker：已停止" }, { "map.checkingTerritory", "正在检查Territory {0}的地图…" }, { "map.loading", "正在加载Map {0}…" }, { "map.failed", "获取失败：{0}" }, { "map.waiting", "正在检查区域信息…" }, { "actors.waiting", "正在等待角色" }, { "coordinates.raw", "坐标：raw" }, { "coordinates.game", "坐标：游戏内" }, { "column.name", "名称" }, { "column.type", "类型" }, { "column.distance", "距离" }, { "overlay.unlocked", "已解锁  •  拖动 / 调整大小" }, { "overlay.waiting", "等待中" }, { "overlay.tracking", "追踪中" }, { "overlay.lost", "已丢失" }, { "tracking.lost", "目标已丢失" }, { "tracking.placeholder", "追踪目标" }, { "height", "高度差 {0:+0.0;-0.0;0.0}" },
                    { "error.ffxivDisabled", "FFXIV_ACT_Plugin.dll未启用。" }, { "error.repository", "无法取得FFXIV DataRepository。" }, { "error.api", "找不到FFXIV_ACT_Plugin公共API，请检查更新。" }, { "error.move", "记录B点前，请沿X和Y方向各移动至少5 yalms。" }, { "error.scale", "计算出的比例超出正常范围，请检查游戏内X/Y。" }, { "error.noMap", "此区域没有可用地图。" }, { "error.mapField", "地图响应中没有 {0}。" },
                    { "kind.1", "玩家" }, { "kind.2", "战斗NPC" }, { "kind.3", "NPC" }, { "kind.4", "以太之光" }, { "kind.5", "采集点" }, { "kind.6", "事件对象" }, { "kind.7", "坐骑" }, { "kind.8", "宠物" }, { "kind.9", "雇员" }, { "kind.10", "区域对象" }, { "kind.11", "房屋" }, { "kind.12", "过场动画" }, { "kind.13", "卡牌台" }, { "kind.14", "时尚配饰" }, { "kind.other", "其他（{0}）" }
                }) },
                { "ko", Overlay(en, new Dictionary<string, string> {
                    { "tab.display", "표시" }, { "tab.tracking", "목록 및 추적" }, { "tab.map", "지도 및 좌표" }, { "tab.support", "후원" }, { "tab.update", "업데이트" }, { "header.subtitle", "표시, 추적, 지도, 언어 및 업데이트를 설정합니다" }, { "language", "언어" },
                    { "show.players", "플레이어 표시" }, { "show.npcs", "NPC 표시" }, { "show.others", "기타 오브젝트 표시" }, { "show.arrow", "추적 화살표 표시" }, { "show.list", "캐릭터 목록 표시" }, { "show.map", "지도와 좌표 점 표시" }, { "overlay.lock", "오버레이 잠금" }, { "facing", "플레이어 방향을 화살표 위쪽으로" }, { "inactive", "FFXIV가 비활성일 때 숨기기" }, { "opacity", "오버레이 불투명도" }, { "background.opacity", "배경 불투명도" }, { "display.hint", "잠금 해제 중에는 헤더를 끌어 이동하고 가장자리를 끌어 크기를 조절합니다.\r\n잠금 중에도 추적 및 지도 조작을 사용할 수 있습니다." }, { "connection.wait", "●  FFXIV_ACT_Plugin 연결 대기 중" }, { "connected", "●  연결됨  •  {0}개  •  Territory {1}" }, { "tracking.search", "목록 검색" }, { "tracking.clear", "추적 해제" }, { "tracking.none", "추적 대상: 없음" }, { "tracking.target", "추적 대상: {0}  •  {1}" }, { "direction.hint", "끄면 지도 북쪽이 위로 표시됩니다. 카메라 회전은 지원하지 않습니다." },
                    { "calibration.title", "게임 내 좌표 보정" }, { "calibration.a", "지점 A 기록" }, { "calibration.b", "지점 B 기록" }, { "calibration.none", "게임 내 좌표: 미보정" }, { "calibration.done", "게임 내 좌표: 보정됨  •  Territory {0}" }, { "calibration.help", "같은 지도에서 멀리 떨어진 두 지점의 게임 지도 X/Y를 기록합니다.\r\n휠로 지도를 확대하고 점을 클릭하면 이름이 표시됩니다." }, { "calibration.noPlayer", "플레이어 정보를 가져올 수 없습니다." }, { "calibration.sameMap", "지점 A와 같은 지도에서 보정하세요." }, { "calibration.aSaved", "지점 A를 기록했습니다. X와 Y 방향으로 각각 5 yalms 이상 이동하세요." }, { "calibration.saved", "보정을 저장했습니다." }, { "calibration.dialogA", "게임 지도에 표시된 좌표" }, { "calibration.dialogB", "이동 후 게임 지도 좌표" }, { "calibration.failed", "보정 실패" }, { "record", "기록" }, { "cancel", "취소" },
                    { "support.title", "Roxyz0501의 개발 후원" }, { "support.description", "Aether Tracker가 마음에 드시면 Ko-fi에서 선택적으로 개발을 후원할 수 있습니다.\r\n후원하지 않아도 모든 기능을 사용할 수 있으며 기능 차이는 없습니다." }, { "support.button", "Ko-fi에서 Roxyz0501 후원하기" }, { "support.note", "이 페이지는 안내 전용입니다. 시작 팝업, 자동 이동, 반복 알림, 기능 제한이 없습니다." }, { "support.opened", "기본 브라우저에서 Ko-fi를 열었습니다." }, { "support.failed", "링크를 열 수 없습니다. URL을 브라우저에 붙여 넣으세요. ({0})" },
                    { "update.startup", "시작할 때 업데이트 확인" }, { "update.checkNow", "지금 확인" }, { "update.install", "업데이트" }, { "update.later", "나중에" }, { "update.title", "Aether Tracker 업데이트" }, { "update.idle", "아직 업데이트를 확인하지 않았습니다." }, { "update.checking", "GitHub Releases 확인 중…" }, { "update.unconfigured", "업데이트 원본이 설정되지 않았습니다. 확인된 GitHub 소유자와 저장소 이름을 UpdateConfiguration.cs에 설정한 후 다시 빌드하세요." }, { "update.current", "현재 버전: {0}" }, { "update.latest", "최신 버전: {0}" }, { "update.available", "사용 가능한 업데이트가 있습니다." }, { "update.none", "최신 안정 버전을 사용 중입니다." }, { "update.failed", "업데이트 확인 실패: {0}" }, { "update.skipped", "버전 {0}을 나중으로 미뤘습니다." }, { "update.downloading", "서명된 업데이트 패키지를 다운로드하고 검증하는 중…" }, { "update.ready", "업데이트가 준비되었습니다. ACT를 종료하면 적용됩니다. 이후 ACT를 다시 시작하세요." }, { "update.installFailed", "업데이트 준비 실패: {0}" }, { "update.notes", "릴리스 노트" }, { "update.safety", "업데이트를 클릭한 경우에만 다운로드하며 로드된 DLL을 직접 덮어쓰지 않습니다." },
                    { "plugin.started", "Aether Tracker: 실행 중" }, { "plugin.stopped", "Aether Tracker: 중지됨" }, { "map.checkingTerritory", "Territory {0} 지도 확인 중…" }, { "map.loading", "Map {0} 불러오는 중…" }, { "map.failed", "가져오기 실패: {0}" }, { "map.waiting", "지역 정보 확인 중…" }, { "actors.waiting", "캐릭터 대기 중" }, { "coordinates.raw", "좌표: raw" }, { "coordinates.game", "좌표: 게임 내" }, { "column.name", "이름" }, { "column.type", "종류" }, { "column.distance", "거리" }, { "overlay.unlocked", "잠금 해제  •  드래그 / 크기 조절" }, { "overlay.waiting", "대기 중" }, { "overlay.tracking", "추적 중" }, { "overlay.lost", "놓침" }, { "tracking.lost", "대상을 놓쳤습니다" }, { "tracking.placeholder", "추적 대상" }, { "height", "높이 차이 {0:+0.0;-0.0;0.0}" },
                    { "error.ffxivDisabled", "FFXIV_ACT_Plugin.dll이 활성화되지 않았습니다." }, { "error.repository", "FFXIV DataRepository를 가져올 수 없습니다." }, { "error.api", "FFXIV_ACT_Plugin 공개 API를 찾을 수 없습니다. 업데이트를 확인하세요." }, { "error.move", "지점 B를 기록하기 전에 X와 Y 방향으로 각각 5 yalms 이상 이동하세요." }, { "error.scale", "계산된 배율이 정상 범위를 벗어났습니다. 게임 내 X/Y를 확인하세요." }, { "error.noMap", "이 지역에는 사용 가능한 지도가 없습니다." }, { "error.mapField", "지도 응답에 {0}이(가) 없습니다." },
                    { "kind.1", "플레이어" }, { "kind.2", "전투 NPC" }, { "kind.3", "NPC" }, { "kind.4", "에테라이트" }, { "kind.5", "채집 지점" }, { "kind.6", "이벤트 오브젝트" }, { "kind.7", "탈것" }, { "kind.8", "꼬마 친구" }, { "kind.9", "집사" }, { "kind.10", "범위 오브젝트" }, { "kind.11", "하우징" }, { "kind.12", "컷신" }, { "kind.13", "카드 테이블" }, { "kind.14", "패션 액세서리" }, { "kind.other", "기타({0})" }
                }) }
            };
        }

        private static Dictionary<string, string> Overlay(Dictionary<string, string> fallback, Dictionary<string, string> values)
        {
            var result = new Dictionary<string, string>(fallback);
            foreach (var pair in values) result[pair.Key] = pair.Value;
            return result;
        }
    }
}
