# Aether Tracker for ACT

作者: `Roxyz0501`

ライセンス: 未設定（既存ライセンスが存在しないため、現時点では再利用・再配布条件を付与していません）

FFXIV_ACT_Plugin が公開している戦闘オブジェクト一覧から、現在読み込まれているプレイヤー、NPC、その他オブジェクトの座標を一覧表示し、選んだ1体への方向を透過オーバーレイで示す ACT プラグインです。

## 任意支援

設定画面の「支援」タブから、[Ko-fiでRoxyz0501の開発を支援](https://ko-fi.com/roxyz0501)できます。支援は完全に任意です。支援しなくてもすべての機能を利用でき、機能差はありません。リンクは支援ボタンを明示的にクリックした場合だけ既定のブラウザで開きます。

## 言語

設定画面の「言語」から英語、日本語、簡体字中国語、韓国語をいつでも切り替えられ、画面とオーバーレイへ即時反映されます。英語が既定・フォールバック言語です。

Configに言語がない初回起動だけWindows UI言語を確認し、`ja`系は日本語、`zh`系は簡体字中国語、`ko`系は韓国語、それ以外は英語を選んで保存します。以後はWindows言語で上書きせず、利用者の選択を維持します。

## 更新機能

「更新」タブには「起動時に更新を確認」と「今すぐ確認」があります。確認は非同期で行い、失敗しても通常機能を継続します。更新が見つかった場合はACT下部のプラグインステータスと更新タブへ現在版、最新版、更新内容を表示します。ダウンロードは利用者が「更新する」を押した場合だけ開始します。

更新パッケージとSHA-256マニフェストを検証し、プラグイン名・バージョン・収録ファイルを確認してから `%LOCALAPPDATA%\AetherTracker\Updates` へ保留します。ロード中のDLLは上書きしません。補助アップデータがACT終了を待ち、現行DLLを `CharacterTracker.dll.backup` に保存してから置換します。置換失敗時はバックアップを復元し、結果を `%LOCALAPPDATA%\AetherTracker\Updater.log` へ記録します。

### 更新元

安定版Releaseは [Roxyz0501/aether-tracker-act](https://github.com/Roxyz0501/aether-tracker-act) から取得します。URLそのものをConfigへ保存する方式ではなく、ビルド時に固定した所有者／リポジトリ名だけからGitHub API URLを構成します。

## 機能

- ACT側は設定のみの軽量画面（全件テーブル更新なし）
- SocialDistance／Target Marker Overlayと共通の設定画面・オーバーレイデザイン
- オーバーレイ一覧での名前／Object ID検索、種類フィルター
- raw座標（ACT取得値）、高さ、プレイヤーからの水平距離
- 2地点校正後のゲーム内マップ座標（テリトリーごとに保存）
- 一覧オーバーレイの行をダブルクリックして追跡
- 選択対象への矢印、距離、高低差
- 矢印・一覧・マップを個別に表示／非表示
- オーバーレイ全体と背景の透明度を個別に調整
- 矢印・一覧・マップすべてのドラッグ移動、リサイズ、位置とサイズの保存
- 現在マップ画像上へのプレイヤー・NPC・その他オブジェクトの点表示
- マップ上でのマウスホイールズーム、点クリック時だけの名前表示
- プレイヤー正面基準／北基準の切替
- FFXIVが非アクティブなとき自動非表示
- 100msの共有スナップショットだけを利用。独自メモリ走査なし

## インストール

1. ACT、`FFXIV_ACT_Plugin.dll` を有効にします。
2. `CharacterTracker.dll` と `CharacterTracker.Updater.exe` を同じフォルダへ配置します。
3. `CharacterTracker.dll` を、ACT の `Plugins > Plugin Listing > Browse...` から追加します。
4. Windowsのファイルプロパティに「ブロックの解除」が表示される場合は、追加前に解除します。
5. 一覧オーバーレイの対象行をダブルクリックします。

OverlayPlugin は不要です。矢印・一覧・マップはこのプラグイン自身の軽量ウィンドウで表示します。ACTタブの「追跡矢印を表示」「キャラクター一覧を表示」「マップと座標点を表示」で個別に切り替えられます。FFXIVはボーダーレスウィンドウ／ウィンドウモードを推奨します。排他的フルスクリーンでは通常のWindowsオーバーレイが見えない場合があります。

初期状態ではすべてのオーバーレイを移動・リサイズできます。各オーバーレイのヘッダーを左ドラッグして移動し、ウィンドウの端をドラッグしてサイズを変更します。配置後に「オーバーレイを固定」をオンにすると誤操作を防げます。位置とサイズは個別に保存されます。固定中も一覧のダブルクリック追跡とマップ操作は利用できます。

「マップと座標点を表示」をオンにすると、ACTのTerritory IDからMap IDを自動解決します。階層切替はChangeMapログも併用し、XIVAPI v2から合成済みマップPNGとMapシートの縮尺・原点補正を取得します。画像は `%APPDATA%\Advanced Combat Tracker\CharacterTracker\Maps` にキャッシュされ、同じマップでは再ダウンロードしません。初回取得にはインターネット接続が必要です。

マップ上にマウスカーソルを置いてホイールを回すと、その位置を中心に100～500%でズームします。通常は点だけを表示し、点を左クリックしたときだけ対象名を表示します。点のない場所をクリックすると名前表示を解除します。「オーバーレイを固定」がオンでも、このマップ操作は利用できます。

### 矢印の基準について

FFXIV_ACT_PluginとOverlayPluginの公開APIにはカメラ角がありません。現在の矢印は「自分の向きを上にする」がオンならプレイヤー正面基準、オフならマップ北基準です。カメラ基準にはパッチ依存の直接メモリ読み取りが必要になるため、安全性と更新耐性を優先して実装していません。

## ゲーム内座標の校正

ACTの座標はワールド座標（yalms）です。一方、ゲーム内マップのX/Yはマップ固有の縮尺と原点オフセットを適用した値なので、そのままでは一致しません。

1. マップを開いて現在のX/Yを読み、「地点Aを記録」に入力します。
2. 同じテリトリー内でX方向・Y方向の両方へそれぞれ5 yalms以上（精度のため20以上を推奨）移動します。
3. 新しいゲーム内X/Yを「地点Bを記録」に入力します。

変換は `mapX = rawX × factorX + offsetX`、`mapY = rawY × factorY + offsetY` です。FFXIV_ACT_PluginではX/Yが水平面、Zが高さです。ゲーム表示が小数1桁に丸められるため、離れた2点を使うほど誤差が小さくなります。インスタンス内のローカル座標など、ゲーム側に通常のマップ座標がない場所ではraw座標を利用してください。

設定・校正値は `%APPDATA%\Advanced Combat Tracker\Config\CharacterTracker.config.xml` に保存されます。

`Language`、`CheckUpdatesOnStartup`、`LastUpdateCheckUtc`、`SkippedVersion` は追加設定です。旧Configに存在しなくても安全な既定値を使うため、従来の表示・校正・位置設定はそのまま読み込めます。

## ビルド

Visual Studio の .NET desktop development workload と .NET Framework 4.8 targeting packが必要です。

```powershell
.\build.ps1
```

PowerShellの実行ポリシーで止まる環境では `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` を使えます。

ACTが標準以外の場所にある場合:

```powershell
.\build.ps1 -ActPath 'D:\Apps\Advanced Combat Tracker'
```

## GitHub Release公開手順

1. `AssemblyInfo.cs` のAssembly/File Versionを公開するSemVerへ更新します。
2. 確認済みのGitHub所有者名・リポジトリ名を `UpdateConfiguration` へ設定します。
3. 次のコマンドでRelease用ZIPとSHA-256マニフェストを生成します。

```powershell
.\release.ps1 -Version 1.6.0
```

4. GitHubでタグ `v1.6.0` の通常Releaseを作成します。draft／prereleaseでは公開しません。
5. `Aether-Tracker-ACT-v1.6.0.zip` と `Aether-Tracker-ACT-v1.6.0.sha256` を両方添付します。ファイル名は変更しません。
6. Release notesへ利用者向け変更点を記載します。プラグインは先頭1200文字を更新タブへ表示します。

更新通信はHTTPSの `api.github.com`、`github.com`、GitHub管理下の `*.githubusercontent.com` だけを許可します。GitHubトークンはプラグインへ埋め込みません。ZIP内は `CharacterTracker.dll`、`CharacterTracker.Updater.exe`、README、共通UI仕様書だけを許可し、絶対パス、`..`、別プラグイン、想定外ファイルを拒否します。

## 注意

FFXIVの利用規約上、ACTを含む第三者ツールの利用は自己責任です。ゲーム内チャット等で第三者ツールの使用を話題にしたり、取得情報を用いて他者を攻撃したりしないでください。
